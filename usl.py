#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Unity Script Library (USL)
A tool for managing scripts and packages in Unity projects.
"""

import argparse
import json
import os
import shutil
import subprocess
import difflib
import re
import logging
import sys
from pathlib import Path
from typing import Dict, List, Set, Tuple, Optional

# --- Logging Setup ---

logging.basicConfig(
    level=logging.INFO,
    format='%(message)s'  # Simple format for CLI tool
)
logger = logging.getLogger(__name__)

# --- Constants ---

LIBRARY_DIR = Path(__file__).parent.resolve()
SCRIPTS_DIR = LIBRARY_DIR / "scripts"
UNITY_PACKAGES_MANIFEST = "Packages/manifest.json"

# Files to skip during copy (USL metadata, OS junk, etc.)
SKIP_FILES = frozenset([
    'dependencies.txt',  # USL metadata
    '.DS_Store',         # macOS
    'Thumbs.db',         # Windows
    'desktop.ini',       # Windows
])

# Validation patterns
DEP_NAME_PATTERN = re.compile(r"^[a-zA-Z][a-zA-Z0-9_-]*$")
PACKAGE_ID_PATTERN = re.compile(r"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*){2,}$")


# --- Custom Exceptions ---

class USLError(Exception):
    """Base exception for USL errors."""
    pass


class DependencyNotFoundError(USLError):
    """Raised when a required dependency is not found."""
    pass


class InvalidDependencyFileError(USLError):
    """Raised when a dependencies.txt file is malformed."""
    pass


class ManifestError(USLError):
    """Raised when there's an issue with the Unity manifest."""
    pass


# --- Helper Classes ---

class InstallationTransaction:
    """Context manager for safe installations with rollback.

    Files and directories are tracked BEFORE operations,
    ensuring rollback can clean up even partial failures.

    For files that already existed before the operation, the original
    is backed up so rollback can restore it rather than simply delete it.
    """

    def __init__(self, manifest_path: Path):
        self.manifest_path = manifest_path
        self.backup_path = manifest_path.with_suffix('.backup')
        # FIX 1: store (dest_path, backup_path_or_None) tuples so rollback
        # can restore originals instead of deleting them.
        self.tracked_files: List[Tuple[Path, Optional[Path]]] = []
        self.tracked_dirs: List[Path] = []
        self.committed = False

    def __enter__(self):
        # Backup manifest if it exists
        if self.manifest_path.exists():
            shutil.copy2(self.manifest_path, self.backup_path)
        return self

    def track_file_operation(
        self, dest_path: Path, original_backup: Optional[Path] = None
    ) -> None:
        """Track a file operation BEFORE it happens.

        Args:
            dest_path: The destination path that will be written.
            original_backup: If the destination already existed, the path
                where the original has been backed up. On rollback the backup
                will be restored; without it the file would simply be deleted.
        """
        self.tracked_files.append((dest_path, original_backup))

    def track_directory_creation(self, dir_path: Path) -> None:
        """Track a directory creation BEFORE it happens."""
        if dir_path not in self.tracked_dirs:
            self.tracked_dirs.append(dir_path)

    def commit(self):
        """Mark transaction as successful - no rollback needed."""
        self.committed = True
        if self.backup_path.exists():
            self.backup_path.unlink()
        # Clean up any lingering per-file backups
        for _dest, backup in self.tracked_files:
            if backup and backup.exists():
                backup.unlink()
        self.tracked_files.clear()
        self.tracked_dirs.clear()

    def __exit__(self, exc_type, exc_val, exc_tb):
        if exc_type is not None and not self.committed:
            # Rollback on error
            logger.error("\n✗ Error occurred! Rolling back changes...")

            # Restore manifest backup
            if self.backup_path.exists():
                if self.manifest_path.exists():
                    self.manifest_path.unlink()
                self.backup_path.replace(self.manifest_path)
                logger.info("  Restored manifest backup")

            # FIX 1: Restore or remove tracked files
            for dest_path, backup_path in reversed(self.tracked_files):
                try:
                    if backup_path and backup_path.exists():
                        # File existed before — restore the original
                        backup_path.replace(dest_path)
                        rel_display = self._get_display_path(dest_path)
                        logger.info(f"  Restored: {rel_display}")
                    elif dest_path.exists():
                        # File is new — simply remove it
                        dest_path.unlink()
                        rel_display = self._get_display_path(dest_path)
                        logger.info(f"  Removed: {rel_display}")
                except Exception as e:
                    logger.warning(f"  Could not roll back {dest_path.name}: {e}")

            # Remove tracked directories (deepest first)
            for dir_path in reversed(self.tracked_dirs):
                try:
                    if dir_path.exists() and dir_path.is_dir():
                        if not any(dir_path.iterdir()):
                            dir_path.rmdir()
                            logger.debug(f"  Removed empty directory: {dir_path.name}")
                except OSError:
                    pass

            logger.info("Rollback complete.")

        return False  # Re-raise the exception

    def _get_display_path(self, file_path: Path) -> str:
        """Get a nice display path for user feedback."""
        # FIX 8: fall back to the full path rather than just the filename so
        # that rollback messages are unambiguous when directories are involved.
        try:
            assets_parent = next(
                (p for p in file_path.parents if p.name == "Assets"), None
            )
            if assets_parent:
                return str(file_path.relative_to(assets_parent))
        except ValueError:
            pass
        return str(file_path)


# --- Helper Functions ---

def is_unity_project(path: Path) -> bool:
    """Check if a directory is a Unity project."""
    return (path / "Assets").is_dir() and (path / "ProjectSettings").is_dir()


def is_git_repo(path: Path) -> bool:
    """Check if a directory is a Git repository."""
    return (path / ".git").is_dir()


def prompt_yes_no(question: str, assume_yes: bool = False) -> bool:
    """Prompt user for yes/no confirmation."""
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
        elif answer in ("n", "no"):
            return False
        print("Please answer y or n.")


def validate_dependency_name(name: str) -> bool:
    """Validate if a dependency name is well-formed."""
    return bool(DEP_NAME_PATTERN.fullmatch(name))


def validate_package_id(package_id: str) -> bool:
    """Validate if a Unity package ID is well-formed."""
    return bool(PACKAGE_ID_PATTERN.fullmatch(package_id))


def parse_dependencies_file(package_path: Path) -> Dict[str, List[str]]:
    """
    Parse a dependencies.txt file and return script and package dependencies.

    Raises InvalidDependencyFileError if the file is malformed.
    """
    dependencies_path = package_path / "dependencies.txt"
    if not dependencies_path.is_file():
        return {"scripts": [], "packages": []}

    script_deps = []
    package_deps = []
    current_section = None
    sections_seen: Set[str] = set()
    # FIX 4: track entries already seen within the current section so that
    # duplicates are detected and reported rather than silently passed through.
    seen_in_section: Set[str] = set()

    try:
        with open(dependencies_path, "r", encoding="utf-8") as f:
            for line_num, line in enumerate(f, 1):
                stripped_line = line.strip()

                # Skip empty lines and comments
                if not stripped_line or stripped_line.startswith("#"):
                    continue

                line_lower = stripped_line.lower()

                # Handle section headers
                if line_lower == "scripts:":
                    if "scripts" in sections_seen:
                        raise InvalidDependencyFileError(
                            f"Duplicate 'scripts:' section at line {line_num} in {dependencies_path}"
                        )
                    sections_seen.add("scripts")
                    current_section = "scripts"
                    seen_in_section = set()  # Reset per-section tracker
                    continue

                elif line_lower == "packages:":
                    if "packages" in sections_seen:
                        raise InvalidDependencyFileError(
                            f"Duplicate 'packages:' section at line {line_num} in {dependencies_path}"
                        )
                    sections_seen.add("packages")
                    current_section = "packages"
                    seen_in_section = set()  # Reset per-section tracker
                    continue

                # Handle dependency entries
                if current_section == "scripts" and stripped_line.startswith("-"):
                    dep_name = stripped_line[1:].strip()
                    if not validate_dependency_name(dep_name):
                        logger.warning(
                            f"Invalid script dependency name '{dep_name}' "
                            f"in {dependencies_path.name} line {line_num}. Skipping."
                        )
                    elif dep_name in seen_in_section:
                        logger.warning(
                            f"Duplicate script dependency '{dep_name}' "
                            f"in {dependencies_path.name} line {line_num}. Skipping."
                        )
                    else:
                        seen_in_section.add(dep_name)
                        script_deps.append(dep_name)

                elif current_section == "packages" and stripped_line.startswith("-"):
                    dep_name = stripped_line[1:].strip()
                    if not validate_package_id(dep_name):
                        logger.warning(
                            f"Invalid Unity package ID '{dep_name}' "
                            f"in {dependencies_path.name} line {line_num}. Skipping."
                        )
                    elif dep_name in seen_in_section:
                        logger.warning(
                            f"Duplicate package dependency '{dep_name}' "
                            f"in {dependencies_path.name} line {line_num}. Skipping."
                        )
                    else:
                        seen_in_section.add(dep_name)
                        package_deps.append(dep_name)

    except IOError as e:
        raise InvalidDependencyFileError(
            f"Cannot read dependencies file for '{package_path.name}': {e}"
        )

    return {"scripts": script_deps, "packages": package_deps}


def write_manifest_atomic(manifest_path: Path, manifest_data: dict) -> None:
    """
    Write the Unity package manifest as safely as possible.

    Uses a write-to-temp-then-rename strategy. On POSIX systems the rename is
    atomic (guaranteed by the OS). On Windows, os.replace() is NOT atomic —
    there is a small window where a crash could leave neither the old nor the
    new file intact — but it is still the safest available option on that
    platform without taking on a third-party dependency.
    """
    tmp_manifest_path = manifest_path.with_suffix(".tmp")

    try:
        # Write to temporary file
        with open(tmp_manifest_path, "w", encoding="utf-8") as f:
            json.dump(manifest_data, f, indent=2)

        # Validate the JSON before committing
        with open(tmp_manifest_path, "r", encoding="utf-8") as f:
            json.load(f)  # Raises JSONDecodeError if corrupt

        # FIX 7: use os.replace() explicitly; behaviour is the same as
        # Path.replace() but makes the cross-platform intent clear and avoids
        # the misleading "POSIX guarantees atomicity" implication for Windows.
        os.replace(tmp_manifest_path, manifest_path)

    except (IOError, OSError) as e:
        if tmp_manifest_path.exists():
            tmp_manifest_path.unlink()
        raise ManifestError(f"Failed to write manifest '{manifest_path}': {e}")
    except json.JSONDecodeError as e:
        if tmp_manifest_path.exists():
            tmp_manifest_path.unlink()
        raise ManifestError(f"Generated invalid JSON for manifest '{manifest_path}': {e}")


def load_manifest(manifest_path: Path) -> Dict:
    """Load and parse the Unity package manifest."""
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
    """Scan for available script packages."""
    if not scripts_dir.exists():
        return []
    return sorted([f for f in scripts_dir.iterdir() if f.is_dir()], key=lambda f: f.name.lower())


def resolve_script_dependencies(
    script_path: Path,
    name_map: Dict[str, Path],
    visited: Optional[Set[Path]] = None
) -> Tuple[Set[Path], Set[str]]:
    """
    Recursively resolve all dependencies for a script.

    Returns:
        Tuple of (script_paths, package_ids) that are required

    Raises:
        DependencyNotFoundError: If a required dependency is not found
        InvalidDependencyFileError: If a dependencies file is malformed
    """
    if visited is None:
        visited = set()

    # Already processed this script
    if script_path in visited:
        return set(), set()

    visited.add(script_path)
    scripts = {script_path}
    packages = set()

    # Parse dependencies for this script
    deps = parse_dependencies_file(script_path)
    packages.update(deps["packages"])

    # Recursively resolve script dependencies
    for dep_name in deps["scripts"]:
        if dep_name not in name_map:
            # Suggest similar names
            suggestions = difflib.get_close_matches(dep_name, name_map.keys(), n=3, cutoff=0.6)
            error_msg = f"Script '{dep_name}' (required by '{script_path.name}') not found."
            if suggestions:
                error_msg += f"\nDid you mean one of these?\n"
                error_msg += "\n".join(f"  - {s}" for s in suggestions)
            raise DependencyNotFoundError(error_msg)

        # Recursively get dependencies
        dep_scripts, dep_packages = resolve_script_dependencies(
            name_map[dep_name], name_map, visited
        )
        scripts.update(dep_scripts)
        packages.update(dep_packages)

    return scripts, packages


def copy_script_package(
    package_path: Path,
    target_dir: Path,
    assume_yes: bool,
    txn: InstallationTransaction
) -> List[Path]:
    """
    Copy a script package to the target directory.

    Files are tracked BEFORE copying for safe rollback. Pre-existing files are
    backed up before being overwritten so rollback can restore them.

    Returns:
        List of successfully copied file paths
    """
    logger.info(f"Installing script package: {package_path.name}")
    successfully_copied = []

    # Collect all files to copy
    files_to_copy = []
    for src_item_path in package_path.rglob('*'):
        if src_item_path.is_dir():
            continue
        if src_item_path.name in SKIP_FILES:
            continue
        files_to_copy.append(src_item_path)

    # Process each file
    for src_item_path in files_to_copy:
        relative_path = src_item_path.relative_to(package_path)
        dest_item_path = target_dir / relative_path
        rel_path_display = dest_item_path.relative_to(target_dir)

        # Ensure parent directories exist and track them
        if not dest_item_path.parent.exists():
            # Collect directories that need to be created
            dirs_to_create = []
            for parent in reversed(list(dest_item_path.parents)):
                try:
                    parent.relative_to(target_dir)
                    is_relative = True
                except ValueError:
                    is_relative = False

                if is_relative and parent != target_dir and not parent.exists():
                    dirs_to_create.append(parent)

            # Track directories BEFORE creating them
            for dir_path in dirs_to_create:
                txn.track_directory_creation(dir_path)

            # Create the directories
            dest_item_path.parent.mkdir(parents=True, exist_ok=True)

        # Check if file exists and handle overwrite
        should_copy = True
        if dest_item_path.exists():
            if not assume_yes:
                should_copy = prompt_yes_no(f"'{rel_path_display}' already exists. Overwrite?")
                if not should_copy:
                    logger.info(f"Skipped: {rel_path_display}")
                    continue

        # FIX 1: If the destination already exists, back it up before
        # touching it so rollback can restore the original rather than
        # simply deleting the file and losing its previous content.
        original_backup: Optional[Path] = None
        if dest_item_path.exists():
            original_backup = dest_item_path.with_suffix(
                dest_item_path.suffix + ".usl_backup"
            )
            shutil.copy2(dest_item_path, original_backup)

        # Track the file BEFORE copying (critical for rollback)
        txn.track_file_operation(dest_item_path, original_backup=original_backup)

        # Perform the copy
        shutil.copy2(src_item_path, dest_item_path)

        # Record success
        successfully_copied.append(dest_item_path)
        logger.info(f"Copied: {rel_path_display}")

    logger.info(f"Successfully installed script package: {package_path.name}")
    return successfully_copied


def init_git_repo(path: Path, assume_yes: bool) -> None:
    """Initialize a Git repository if one doesn't exist."""
    if is_git_repo(path):
        logger.info("Git repository already exists.")
        return

    if not prompt_yes_no("No git repository found. Initialize one?", assume_yes):
        return

    try:
        result = subprocess.run(
            ["git", "init"],
            cwd=path,
            check=True,
            capture_output=True,
            text=True
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
    """
    Add a Unity package ID to the manifest dictionary.

    Returns:
        True if package was added, False if already present
    """
    if "dependencies" not in manifest:
        manifest["dependencies"] = {}

    if package_id in manifest["dependencies"]:
        logger.info(f"Package '{package_id}' already present in manifest.")
        return False

    manifest["dependencies"][package_id] = "*"
    logger.info(f"Added '{package_id}' to manifest.")
    return True


def install_unity_package(package_id: str, project_path: Path) -> None:
    """
    Install a Unity package by its ID.

    Raises:
        ManifestError: If manifest operations fail
    """
    manifest_path = project_path / UNITY_PACKAGES_MANIFEST

    # Load manifest
    manifest = load_manifest(manifest_path)

    # Check if already present
    if "dependencies" not in manifest:
        manifest["dependencies"] = {}

    if package_id in manifest["dependencies"]:
        logger.info(f"Package '{package_id}' already present in manifest.")
        return

    # Add package
    manifest["dependencies"][package_id] = "*"

    # Write atomically
    write_manifest_atomic(manifest_path, manifest)

    logger.info(f"Added '{package_id}' to the project's package manifest.")
    logger.info("Note: '*' was used for version. Unity will resolve to the latest compatible version.")


# --- Command Handlers ---

def cmd_list_scripts(scripts: List[Path]) -> None:
    """List available script packages."""
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
    """
    Add script packages to the project with dependency resolution.

    Raises:
        DependencyNotFoundError: If a required dependency is missing
        InvalidDependencyFileError: If a dependencies file is malformed
        ManifestError: If manifest operations fail
    """
    name_map = {s.name: s for s in all_available_scripts}

    scripts_to_copy = set()
    packages_to_install = set()

    # Phase 1: Resolve Dependencies
    for primary_script_name in script_names:
        if primary_script_name not in name_map:
            suggestions = difflib.get_close_matches(
                primary_script_name, name_map.keys(), n=3, cutoff=0.6
            )
            error_msg = f"Primary script not found: {primary_script_name}"
            if suggestions:
                error_msg += "\nDid you mean one of these?\n"
                error_msg += "\n".join(f"  - {s}" for s in suggestions)
            raise DependencyNotFoundError(error_msg)

        # Recursively resolve dependencies
        scripts, packages = resolve_script_dependencies(
            name_map[primary_script_name], name_map
        )
        scripts_to_copy.update(scripts)
        packages_to_install.update(packages)

    if not scripts_to_copy and not packages_to_install:
        logger.info("No scripts or packages to add after dependency resolution.")
        return

    # Phase 2: Show Installation Plan
    logger.info("\n--- Installation Plan ---")
    if scripts_to_copy:
        logger.info("The following script packages will be copied:")
        for script_path in sorted(scripts_to_copy, key=lambda p: p.name):
            logger.info(f"  - {script_path.name}")
    if packages_to_install:
        logger.info("The following Unity packages will be added to manifest.json:")
        for pkg_id in sorted(packages_to_install):
            logger.info(f"  - {pkg_id}")
    logger.info("-------------------------\n")

    if not prompt_yes_no("Proceed with installation?", assume_yes):
        logger.info("Installation cancelled by user.")
        return

    # Phase 3: Install with Transaction Safety
    logger.info("\nStarting installation...")

    manifest_path = project_path / UNITY_PACKAGES_MANIFEST

    with InstallationTransaction(manifest_path) as txn:
        # Load or create manifest
        if manifest_path.exists():
            manifest_data = load_manifest(manifest_path)
        else:
            logger.info(f"Unity package manifest not found at '{manifest_path}'. Creating a new one.")
            manifest_data = {"dependencies": {}}

        # Add packages to in-memory manifest
        for pkg_id in sorted(packages_to_install):
            add_package_to_manifest(pkg_id, manifest_data)

        # Write manifest once (atomically)
        if packages_to_install:
            write_manifest_atomic(manifest_path, manifest_data)
            logger.info(f"Updated Unity package manifest at '{manifest_path}'.")

        # Copy script packages
        for script_path in sorted(scripts_to_copy, key=lambda p: p.name):
            copy_script_package(script_path, project_assets, assume_yes, txn)

        # Commit transaction
        txn.commit()
        logger.info("\n✓ Installation complete!")


def cmd_install_package(package_id: str, project_path: Path) -> None:
    """
    Install a Unity package by ID.

    Raises:
        ValueError: If package ID format is invalid
        ManifestError: If manifest operations fail
    """
    if not validate_package_id(package_id):
        raise ValueError(
            f"Invalid Unity package ID format: '{package_id}'\n"
            "Expected format: com.company.package (lowercase, at least 2 dots)"
        )

    logger.info(f"Attempting to install Unity package: {package_id}")
    install_unity_package(package_id, project_path)


def cmd_interactive_mode(project_assets: Path, project_path: Path) -> None:
    """Run interactive script selection mode.

    Note: this function is never reached when --yes is active (run_command
    rejects that combination before calling here), so assume_yes is not a
    parameter — it would always be False and would be misleading to accept.
    """
    # FIX 3: removed the unused `assume_yes` parameter. Interactive mode is
    # incompatible with --yes (enforced in run_command), so the parameter was
    # always False and its presence implied it might someday be True.
    scripts = scan_scripts(SCRIPTS_DIR)

    if not scripts:
        logger.info("No local scripts available to install.")
        return

    logger.info("Available scripts:")
    for idx, s in enumerate(scripts, 1):
        logger.info(f"{idx}. {s.name}")

    try:
        selection = input("Enter numbers to add (e.g., 1 3 5 or 1,2,3), or leave blank to cancel: ").strip()
        if not selection:
            logger.info("Operation cancelled.")
            return

        # Parse selection
        tokens = re.split(r'[,\s]+', selection)
        tokens = [t for t in tokens if t]

        indexes = [int(i) - 1 for i in tokens]
        selected_scripts = [scripts[i] for i in indexes if 0 <= i < len(scripts)]

        if not selected_scripts:
            raise ValueError("No valid scripts selected.")

        selected_script_names = [s.name for s in selected_scripts]
        # Interactive mode always runs without auto-confirm
        cmd_add_scripts(scripts, selected_script_names, project_assets, project_path, assume_yes=False)

    except (ValueError, IndexError):
        raise ValueError(
            f"Invalid input: '{selection}'\n"
            "Please enter numbers (e.g., '1 3 5' or '1,2,3')"
        )


# --- CLI Entry Point ---

def parse_args():
    """Parse command-line arguments."""
    # FIX 5: -y/--yes was previously defined on BOTH the main parser AND a
    # parent_parser that was inherited by every subparser. This meant the flag
    # was accepted in multiple positions (before and after the subcommand) but
    # only one instance was actually read — creating silent inconsistency.
    # The flag now lives solely on the main parser. Subcommand handlers receive
    # it via the top-level namespace, which is the authoritative location.
    parser = argparse.ArgumentParser(
        prog="usl",
        description="Unity Script Library - A tool for managing scripts and packages in Unity projects."
    )
    parser.add_argument(
        "--init-git",
        action="store_true",
        help="Initialize a new git repository if one doesn't exist."
    )
    parser.add_argument(
        "-y", "--yes",
        action="store_true",
        help="Automatically answer yes to all prompts."
    )
    parser.add_argument(
        "-v", "--verbose",
        action="store_true",
        help="Enable verbose logging."
    )

    subparsers = parser.add_subparsers(dest="command", help="Available commands")

    subparsers.add_parser(
        "list",
        help="List available local script packages."
    )

    add_parser = subparsers.add_parser(
        "add",
        help="Add local script packages to the Unity project."
    )
    add_parser.add_argument(
        "scripts",
        nargs="+",
        metavar="SCRIPT",
        help="Names of the script packages to add."
    )

    install_parser = subparsers.add_parser(
        "install",
        help="Install a Unity package by its ID."
    )
    install_parser.add_argument(
        "package_id",
        metavar="PACKAGE_ID",
        help="The Unity package ID (e.g., com.unity.inputsystem)."
    )

    return parser.parse_args()


def run_command(args, cwd: Path) -> int:
    """
    Route to the appropriate command handler.

    Returns:
        Exit code (0 for success, 1 for error)
    """
    # Set logging level
    if args.verbose:
        logger.setLevel(logging.DEBUG)

    # Validate SCRIPTS_DIR exists
    if not SCRIPTS_DIR.exists():
        logger.error(f"Error: Scripts directory not found at: {SCRIPTS_DIR}")
        logger.error("Please create it or run from the correct location.")
        return 1

    assume_yes = getattr(args, "yes", False)

    # List command doesn't require Unity project
    if args.command == "list":
        scripts = scan_scripts(SCRIPTS_DIR)
        cmd_list_scripts(scripts)
        return 0

    # All other commands require Unity project
    if not is_unity_project(cwd):
        logger.error("Error: This command must be run from a Unity project directory.")
        logger.error("       (Expected to find 'Assets' and 'ProjectSettings' subdirectories.)")
        return 1

    # Init git if requested
    if args.init_git:
        init_git_repo(cwd, assume_yes)

    # Route to command
    try:
        if args.command == "add":
            scripts = scan_scripts(SCRIPTS_DIR)
            cmd_add_scripts(scripts, args.scripts, cwd / "Assets", cwd, assume_yes)

        elif args.command == "install":
            cmd_install_package(args.package_id, cwd)

        else:  # Interactive mode
            if assume_yes:
                logger.error("Error: Interactive mode cannot be used with --yes flag.")
                logger.error("Please specify scripts to add or remove --yes flag.")
                return 1

            logger.info("No command specified. Entering interactive mode...")
            # FIX 3: cmd_interactive_mode no longer accepts assume_yes
            cmd_interactive_mode(cwd / "Assets", cwd)

        return 0

    except (DependencyNotFoundError, InvalidDependencyFileError, ManifestError, ValueError) as e:
        logger.error(f"Error: {e}")
        return 1
    except KeyboardInterrupt:
        logger.info("\nCancelled by user.")
        return 130  # Standard exit code for SIGINT
    except Exception as e:
        logger.error(f"Unexpected error: {e}")
        if args.verbose:
            import traceback
            traceback.print_exc()
        return 2


def main():
    """Main entry point for the USL tool."""
    args = parse_args()
    cwd = Path.cwd()
    return run_command(args, cwd)


if __name__ == "__main__":
    sys.exit(main())
