#!/usr/bin/env python3
"""Unity Script Library (USL) — manage scripts and packages in Unity projects."""

import argparse
import difflib
import json
import logging
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple

logging.basicConfig(level=logging.INFO, format='%(message)s')
logger = logging.getLogger(__name__)

LIBRARY_DIR = Path(__file__).parent.resolve()
SCRIPTS_DIR = LIBRARY_DIR / "scripts"
UNITY_PACKAGES_MANIFEST = "Packages/manifest.json"
SKIP_FILES = frozenset(['dependencies.txt', '.DS_Store', 'Thumbs.db', 'desktop.ini'])
DEP_NAME_PATTERN = re.compile(r"^[a-zA-Z][a-zA-Z0-9_-]*$")
PACKAGE_ID_PATTERN = re.compile(r"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*){2,}$")


# --- Exceptions ---

class USLError(Exception):
    """Base exception for USL errors."""

class DependencyNotFoundError(USLError):
    """Required dependency not found."""

class InvalidDependencyFileError(USLError):
    """Malformed dependencies.txt file."""

class ManifestError(USLError):
    """Issue with the Unity manifest."""


# --- Transaction ---

class InstallationTransaction:
    """Context manager for safe installations with rollback."""

    def __init__(self, manifest_path: Path):
        self.manifest_path = manifest_path
        self.backup_path = manifest_path.with_suffix('.backup')
        self.tracked_files: List[Tuple[Path, Optional[Path]]] = []
        self.tracked_dirs: List[Path] = []
        self.committed = False

    def __enter__(self):
        if self.manifest_path.exists():
            shutil.copy2(self.manifest_path, self.backup_path)
        return self

    def track_file_operation(self, dest_path: Path, original_backup: Optional[Path] = None) -> None:
        self.tracked_files.append((dest_path, original_backup))

    def track_directory_creation(self, dir_path: Path) -> None:
        if dir_path not in self.tracked_dirs:
            self.tracked_dirs.append(dir_path)

    def commit(self):
        """Mark transaction as successful — no rollback needed."""
        self.committed = True
        if self.backup_path.exists():
            self.backup_path.unlink()
        for _dest, backup in self.tracked_files:
            if backup and backup.exists():
                backup.unlink()
        self.tracked_files.clear()
        self.tracked_dirs.clear()

    def __exit__(self, exc_type, exc_val, exc_tb):
        if exc_type is not None and not self.committed:
            logger.error("\n✗ Error occurred! Rolling back changes...")

            if self.backup_path.exists():
                if self.manifest_path.exists():
                    self.manifest_path.unlink()
                self.backup_path.replace(self.manifest_path)
                logger.info("  Restored manifest backup")

            for dest_path, backup_path in reversed(self.tracked_files):
                try:
                    if backup_path and backup_path.exists():
                        backup_path.replace(dest_path)
                        logger.info(f"  Restored: {self._display(dest_path)}")
                    elif dest_path.exists():
                        dest_path.unlink()
                        logger.info(f"  Removed: {self._display(dest_path)}")
                except Exception as e:
                    logger.warning(f"  Could not roll back {dest_path.name}: {e}")

            for dir_path in reversed(self.tracked_dirs):
                try:
                    if dir_path.exists() and not any(dir_path.iterdir()):
                        dir_path.rmdir()
                except OSError:
                    pass

            logger.info("Rollback complete.")
        return False

    def _display(self, file_path: Path) -> str:
        """Return a path relative to the Assets folder, or the full path."""
        for p in file_path.parents:
            if p.name == "Assets":
                try:
                    return str(file_path.relative_to(p))
                except ValueError:
                    pass
        return str(file_path)


# --- Helpers ---

def is_unity_project(path: Path) -> bool:
    return (path / "Assets").is_dir() and (path / "ProjectSettings").is_dir()

def is_git_repo(path: Path) -> bool:
    return (path / ".git").is_dir()

def prompt_yes_no(question: str, assume_yes: bool = False) -> bool:
    if assume_yes:
        return True
    while True:
        try:
            answer = input(f"{question} (y/n): ").strip().lower()
        except (EOFError, KeyboardInterrupt):
            logger.info("\nCancelled.")
            return False
        if answer in ("y", "yes"):
            return True
        if answer in ("n", "no"):
            return False
        print("Please answer y or n.")

def validate_dependency_name(name: str) -> bool:
    return bool(DEP_NAME_PATTERN.fullmatch(name))

def validate_package_id(package_id: str) -> bool:
    return bool(PACKAGE_ID_PATTERN.fullmatch(package_id))

def parse_dependencies_file(package_path: Path) -> Dict[str, List[str]]:
    """Parse dependencies.txt and return {'scripts': [...], 'packages': [...]}."""
    deps_path = package_path / "dependencies.txt"
    if not deps_path.is_file():
        return {"scripts": [], "packages": []}

    results: Dict[str, List[str]] = {"scripts": [], "packages": []}
    validators = {
        "scripts": (validate_dependency_name, "script dependency name"),
        "packages": (validate_package_id, "Unity package ID"),
    }
    sections_seen: Set[str] = set()
    current_section: Optional[str] = None
    seen_in_section: Set[str] = set()

    try:
        with open(deps_path, "r", encoding="utf-8") as f:
            for line_num, line in enumerate(f, 1):
                stripped = line.strip()
                if not stripped or stripped.startswith("#"):
                    continue

                lower = stripped.lower()
                if lower in ("scripts:", "packages:"):
                    section = lower[:-1]  # strip the trailing ":"
                    if section in sections_seen:
                        raise InvalidDependencyFileError(
                            f"Duplicate '{section}:' section at line {line_num} in {deps_path}"
                        )
                    sections_seen.add(section)
                    current_section = section
                    seen_in_section = set()
                    continue

                if current_section and stripped.startswith("-"):
                    name = stripped[1:].strip()
                    validate_fn, label = validators[current_section]
                    if not validate_fn(name):
                        logger.warning(
                            f"Invalid {label} '{name}' in {deps_path.name} line {line_num}. Skipping."
                        )
                    elif name in seen_in_section:
                        logger.warning(
                            f"Duplicate {current_section} dependency '{name}' "
                            f"in {deps_path.name} line {line_num}. Skipping."
                        )
                    else:
                        seen_in_section.add(name)
                        results[current_section].append(name)
    except IOError as e:
        raise InvalidDependencyFileError(
            f"Cannot read dependencies file for '{package_path.name}': {e}"
        )

    return results

def write_manifest_atomic(manifest_path: Path, manifest_data: dict) -> None:
    """Write the Unity manifest via a temp file for safety (atomic on POSIX, best-effort on Windows)."""
    tmp = manifest_path.with_suffix(".tmp")
    try:
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(manifest_data, f, indent=2)
        with open(tmp, "r", encoding="utf-8") as f:
            json.load(f)  # validate before committing
        os.replace(tmp, manifest_path)
    except (IOError, OSError) as e:
        if tmp.exists():
            tmp.unlink()
        raise ManifestError(f"Failed to write manifest '{manifest_path}': {e}")
    except json.JSONDecodeError as e:
        if tmp.exists():
            tmp.unlink()
        raise ManifestError(f"Generated invalid JSON for manifest '{manifest_path}': {e}")

def load_manifest(manifest_path: Path) -> Dict:
    if not manifest_path.exists():
        raise ManifestError(f"Unity package manifest not found at: {manifest_path}")
    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (IOError, OSError) as e:
        raise ManifestError(f"Error reading manifest '{manifest_path}': {e}")
    except json.JSONDecodeError as e:
        raise ManifestError(f"Malformed JSON in manifest '{manifest_path}': {e}")


# --- Core Logic ---

def scan_scripts(scripts_dir: Path) -> List[Path]:
    if not scripts_dir.exists():
        return []
    return sorted([f for f in scripts_dir.iterdir() if f.is_dir()], key=lambda f: f.name.lower())

def resolve_script_dependencies(
    script_path: Path,
    name_map: Dict[str, Path],
    visited: Optional[Set[Path]] = None
) -> Tuple[Set[Path], Set[str]]:
    """Recursively resolve all script and package dependencies."""
    if visited is None:
        visited = set()
    if script_path in visited:
        return set(), set()

    visited.add(script_path)
    scripts = {script_path}
    packages: Set[str] = set()

    deps = parse_dependencies_file(script_path)
    packages.update(deps["packages"])

    for dep_name in deps["scripts"]:
        if dep_name not in name_map:
            suggestions = difflib.get_close_matches(dep_name, name_map.keys(), n=3, cutoff=0.6)
            msg = f"Script '{dep_name}' (required by '{script_path.name}') not found."
            if suggestions:
                msg += "\nDid you mean one of these?\n" + "\n".join(f"  - {s}" for s in suggestions)
            raise DependencyNotFoundError(msg)

        dep_scripts, dep_packages = resolve_script_dependencies(name_map[dep_name], name_map, visited)
        scripts.update(dep_scripts)
        packages.update(dep_packages)

    return scripts, packages

def copy_script_package(
    package_path: Path,
    target_dir: Path,
    assume_yes: bool,
    txn: InstallationTransaction
) -> List[Path]:
    """Copy a script package to target_dir, tracking all operations for rollback."""
    logger.info(f"Installing script package: {package_path.name}")
    successfully_copied = []

    for src in package_path.rglob('*'):
        if src.is_dir() or src.name in SKIP_FILES:
            continue

        dest = target_dir / src.relative_to(package_path)
        rel = dest.relative_to(target_dir)

        # Track and create any missing parent directories
        for parent in reversed(list(dest.parents)):
            try:
                parent.relative_to(target_dir)
            except ValueError:
                continue
            if parent != target_dir and not parent.exists():
                txn.track_directory_creation(parent)
        dest.parent.mkdir(parents=True, exist_ok=True)

        if dest.exists() and not assume_yes:
            if not prompt_yes_no(f"'{rel}' already exists. Overwrite?"):
                logger.info(f"Skipped: {rel}")
                continue

        # Back up any pre-existing file so rollback can restore it
        backup = None
        if dest.exists():
            backup = dest.with_suffix(dest.suffix + ".usl_backup")
            shutil.copy2(dest, backup)

        txn.track_file_operation(dest, original_backup=backup)
        shutil.copy2(src, dest)
        successfully_copied.append(dest)
        logger.info(f"Copied: {rel}")

    logger.info(f"Successfully installed script package: {package_path.name}")
    return successfully_copied

def init_git_repo(path: Path, assume_yes: bool) -> None:
    if is_git_repo(path):
        logger.info("Git repository already exists.")
        return
    if not prompt_yes_no("No git repository found. Initialize one?", assume_yes):
        return
    try:
        result = subprocess.run(
            ["git", "init"], cwd=path, check=True, capture_output=True, text=True
        )
        logger.info("Initialized a new git repository.")
        if result.stdout:
            logger.debug(f"Git output: {result.stdout.strip()}")
    except subprocess.CalledProcessError as e:
        logger.error(f"Failed to initialize git repository: {e}")
        if e.stderr:
            logger.error(f"Git error output: {e.stderr.strip()}")
        logger.error("Please ensure Git is installed and configured correctly.")
    except FileNotFoundError:
        logger.error("Failed to initialize git repository: 'git' command not found.")
        logger.error("Please ensure Git is installed and in your system's PATH.")

def add_package_to_manifest(package_id: str, manifest: Dict) -> bool:
    """Add package_id to the manifest dict. Returns True if newly added."""
    manifest.setdefault("dependencies", {})
    if package_id in manifest["dependencies"]:
        logger.info(f"Package '{package_id}' already present in manifest.")
        return False
    manifest["dependencies"][package_id] = "*"
    logger.info(f"Added '{package_id}' to manifest.")
    return True


# --- Command Handlers ---

def cmd_list_scripts(scripts: List[Path]) -> None:
    logger.info("Available USL scripts:")
    if not scripts:
        logger.info("  (No scripts found)")
        return
    for s in scripts:
        logger.info(f"  - {s.name}")

def cmd_add_scripts(
    all_available_scripts: List[Path],
    script_names: List[str],
    project_assets: Path,
    project_path: Path,
    assume_yes: bool
) -> None:
    """Add script packages with full dependency resolution."""
    name_map = {s.name: s for s in all_available_scripts}
    scripts_to_copy: Set[Path] = set()
    packages_to_install: Set[str] = set()

    for name in script_names:
        if name not in name_map:
            suggestions = difflib.get_close_matches(name, name_map.keys(), n=3, cutoff=0.6)
            msg = f"Primary script not found: {name}"
            if suggestions:
                msg += "\nDid you mean one of these?\n" + "\n".join(f"  - {s}" for s in suggestions)
            raise DependencyNotFoundError(msg)
        scripts, packages = resolve_script_dependencies(name_map[name], name_map)
        scripts_to_copy.update(scripts)
        packages_to_install.update(packages)

    if not scripts_to_copy and not packages_to_install:
        logger.info("No scripts or packages to add after dependency resolution.")
        return

    logger.info("\n--- Installation Plan ---")
    if scripts_to_copy:
        logger.info("The following script packages will be copied:")
        for p in sorted(scripts_to_copy, key=lambda p: p.name):
            logger.info(f"  - {p.name}")
    if packages_to_install:
        logger.info("The following Unity packages will be added to manifest.json:")
        for pkg in sorted(packages_to_install):
            logger.info(f"  - {pkg}")
    logger.info("-------------------------\n")

    if not prompt_yes_no("Proceed with installation?", assume_yes):
        logger.info("Installation cancelled by user.")
        return

    logger.info("\nStarting installation...")
    manifest_path = project_path / UNITY_PACKAGES_MANIFEST

    with InstallationTransaction(manifest_path) as txn:
        if manifest_path.exists():
            manifest_data = load_manifest(manifest_path)
        else:
            logger.info(f"Unity package manifest not found at '{manifest_path}'. Creating a new one.")
            manifest_data = {"dependencies": {}}

        for pkg_id in sorted(packages_to_install):
            add_package_to_manifest(pkg_id, manifest_data)

        if packages_to_install:
            write_manifest_atomic(manifest_path, manifest_data)
            logger.info(f"Updated Unity package manifest at '{manifest_path}'.")

        for script_path in sorted(scripts_to_copy, key=lambda p: p.name):
            copy_script_package(script_path, project_assets, assume_yes, txn)

        txn.commit()
        logger.info("\n✓ Installation complete!")

def cmd_install_package(package_id: str, project_path: Path) -> None:
    """Validate and install a Unity package by ID."""
    if not validate_package_id(package_id):
        raise ValueError(
            f"Invalid Unity package ID format: '{package_id}'\n"
            "Expected format: com.company.package (lowercase, at least 2 dots)"
        )
    manifest_path = project_path / UNITY_PACKAGES_MANIFEST
    manifest = load_manifest(manifest_path)
    if not add_package_to_manifest(package_id, manifest):
        return
    write_manifest_atomic(manifest_path, manifest)
    logger.info(f"Added '{package_id}' to the project's package manifest.")
    logger.info("Note: '*' was used for version. Unity will resolve to the latest compatible version.")

def cmd_interactive_mode(project_assets: Path, project_path: Path) -> None:
    """Run interactive script selection mode (incompatible with --yes; enforced by run_command)."""
    scripts = scan_scripts(SCRIPTS_DIR)
    if not scripts:
        logger.info("No local scripts available to install.")
        return

    logger.info("Available scripts:")
    for idx, s in enumerate(scripts, 1):
        logger.info(f"{idx}. {s.name}")

    selection = ""
    try:
        selection = input("Enter numbers to add (e.g., 1 3 5 or 1,2,3), or leave blank to cancel: ").strip()
        if not selection:
            logger.info("Operation cancelled.")
            return

        tokens = [t for t in re.split(r'[,\s]+', selection) if t]
        indexes = [int(i) - 1 for i in tokens]
        selected = [scripts[i] for i in indexes if 0 <= i < len(scripts)]

        if not selected:
            raise ValueError("No valid scripts selected.")

        cmd_add_scripts(scripts, [s.name for s in selected], project_assets, project_path, assume_yes=False)

    except (ValueError, IndexError):
        raise ValueError(
            f"Invalid input: '{selection}'\n"
            "Please enter numbers (e.g., '1 3 5' or '1,2,3')"
        )


# --- CLI ---

def parse_args():
    parser = argparse.ArgumentParser(
        prog="usl",
        description="Unity Script Library - manage scripts and packages in Unity projects."
    )
    parser.add_argument("--init-git", action="store_true", help="Initialize a git repo if one doesn't exist.")

    # Shared flags on a parent parser — the standard argparse pattern for
    # flags that belong to every subcommand. add_help=False prevents argparse
    # from adding a duplicate -h when the parent is inherited.
    shared = argparse.ArgumentParser(add_help=False)
    shared.add_argument("-y", "--yes", action="store_true", help="Automatically answer yes to all prompts.")
    shared.add_argument("-v", "--verbose", action="store_true", help="Enable verbose logging.")

    subparsers = parser.add_subparsers(dest="command", help="Available commands")
    subparsers.add_parser("list", parents=[shared], help="List available local script packages.")

    add_parser = subparsers.add_parser("add", parents=[shared], help="Add local script packages to the Unity project.")
    add_parser.add_argument("scripts", nargs="+", metavar="SCRIPT", help="Script package names to add.")

    install_parser = subparsers.add_parser("install", parents=[shared], help="Install a Unity package by its ID.")
    install_parser.add_argument("package_id", metavar="PACKAGE_ID", help="Unity package ID (e.g., com.unity.inputsystem).")

    return parser.parse_args()

def run_command(args, cwd: Path) -> int:
    if args.verbose:
        logger.setLevel(logging.DEBUG)

    if not SCRIPTS_DIR.exists():
        logger.error(f"Error: Scripts directory not found at: {SCRIPTS_DIR}")
        logger.error("Please create it or run from the correct location.")
        return 1

    assume_yes = getattr(args, "yes", False)

    if args.command == "list":
        cmd_list_scripts(scan_scripts(SCRIPTS_DIR))
        return 0

    if not is_unity_project(cwd):
        logger.error("Error: This command must be run from a Unity project directory.")
        logger.error("       (Expected to find 'Assets' and 'ProjectSettings' subdirectories.)")
        return 1

    if args.init_git:
        init_git_repo(cwd, assume_yes)

    try:
        if args.command == "add":
            cmd_add_scripts(scan_scripts(SCRIPTS_DIR), args.scripts, cwd / "Assets", cwd, assume_yes)
        elif args.command == "install":
            cmd_install_package(args.package_id, cwd)
        else:  # interactive mode
            if assume_yes:
                logger.error("Error: Interactive mode cannot be used with --yes flag.")
                logger.error("Please specify scripts to add or remove --yes flag.")
                return 1
            logger.info("No command specified. Entering interactive mode...")
            cmd_interactive_mode(cwd / "Assets", cwd)
        return 0
    except (DependencyNotFoundError, InvalidDependencyFileError, ManifestError, ValueError) as e:
        logger.error(f"Error: {e}")
        return 1
    except KeyboardInterrupt:
        logger.info("\nCancelled by user.")
        return 130
    except Exception as e:
        logger.error(f"Unexpected error: {e}")
        if args.verbose:
            import traceback
            traceback.print_exc()
        return 2

def main():
    return run_command(parse_args(), Path.cwd())

if __name__ == "__main__":
    sys.exit(main())
