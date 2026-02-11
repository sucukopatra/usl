#!/usr/bin/env python3
# -*- coding: utf-8 -*-

"""
Unity Script Library (USL)
A tool for managing scripts and packages in Unity projects.
"""

import argparse
import json
import shutil
import subprocess
import difflib
import re
from pathlib import Path
from collections import deque
from typing import Dict, List

# --- Constants ---

# Keep scripts folder outside the main script directory
LIBRARY_DIR = Path(__file__).parent.resolve()
SCRIPTS_DIR = LIBRARY_DIR / "scripts"
UNITY_PACKAGES_MANIFEST = "Packages/manifest.json"


# --- Helper Classes ---

class InstallationTransaction:
    """Context manager for safe installations with rollback."""
    
    def __init__(self, manifest_path: Path):
        self.manifest_path = manifest_path
        self.backup_path = manifest_path.with_suffix('.backup')
        self.copied_files = []
        
    def __enter__(self):
        # Backup manifest if it exists
        if self.manifest_path.exists():
            shutil.copy2(self.manifest_path, self.backup_path)
        return self
        
    def track_file(self, dest_path: Path):
        """Track a file that was copied (for rollback)."""
        self.copied_files.append(dest_path)
        
    def commit(self):
        """Mark transaction as successful."""
        if self.backup_path.exists():
            self.backup_path.unlink()
        self.copied_files.clear()
        
    def __exit__(self, exc_type, exc_val, exc_tb):
        if exc_type is not None:
            # Rollback on error
            print("\n✗ Error occurred! Rolling back changes...")
            
            # Restore manifest
            if self.backup_path.exists():
                self.backup_path.replace(self.manifest_path)
                print(f"  Restored manifest backup")
            
            # Remove copied files
            for file_path in reversed(self.copied_files):
                try:
                    if file_path.exists():
                        file_path.unlink()
                        # Calculate relative path for display
                        try:
                            assets_parent = next((p for p in file_path.parents if p.name == "Assets"), None)
                            if assets_parent:
                                rel_display = file_path.relative_to(assets_parent)
                            else:
                                rel_display = file_path.name
                        except (ValueError, IndexError):
                            rel_display = file_path.name
                        print(f"  Removed: {rel_display}")
                except Exception as e:
                    print(f"  Warning: Could not remove {file_path}: {e}")
            
            print("Rollback complete.")
        return False  # Re-raise the exception


# --- Helper Functions ---

def is_unity_project(path: Path) -> bool:
    """Check if a directory is a Unity project."""
    return (path / "Assets").is_dir() and (path / "ProjectSettings").is_dir()


def is_git_repo(path: Path) -> bool:
    """Check if a directory is a Git repository."""
    return (path / ".git").is_dir()


def ask_yes_no(prompt: str, assume_yes: bool = False) -> bool:
    """Ask a yes/no question. Returns False on EOF/Ctrl+C."""
    if assume_yes:
        return True
    
    while True:
        try:
            answer = input(f"{prompt} (y/n): ").strip().lower()
        except (EOFError, KeyboardInterrupt):
            print("\nCancelled.")
            return False
        
        if answer in ("y", "yes"):
            return True
        elif answer in ("n", "no"):
            return False
        print("Please answer y or n.")


_DEPENDENCY_NAME_PATTERN = re.compile(r"^[a-zA-Z][a-zA-Z0-9_-]*$")
_UNITY_PACKAGE_ID_PATTERN = re.compile(r"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*){2,}$")

def _is_valid_dependency_name(name: str) -> bool:
    """
    Validates if a dependency name starts with a letter and contains only
    letters, numbers, underscores, and hyphens.
    """
    return bool(_DEPENDENCY_NAME_PATTERN.fullmatch(name))


def _is_valid_unity_package_id(package_id: str) -> bool:
    """
    Validates if a Unity package ID follows the pattern com.company.package
    (requires at least 2 dots, starts with lowercase letter).
    """
    return bool(_UNITY_PACKAGE_ID_PATTERN.fullmatch(package_id))


def parse_dependencies_file(package_path: Path) -> Dict[str, List[str]]:
    """
    Parses a dependencies.txt file within a package and returns a dictionary
    of script and package dependencies.
    
    Raises RuntimeError if the dependencies file exists but cannot be read.
    """
    dependencies_path = package_path / "dependencies.txt"
    if not dependencies_path.is_file():
        return {"scripts": [], "packages": []}

    script_deps = []
    package_deps = []
    current_section = None

    try:
        with open(dependencies_path, "r", encoding="utf-8") as f:
            for line_num, line in enumerate(f, 1):
                stripped_line = line.strip()
                if not stripped_line or stripped_line.startswith("#"):
                    continue

                line_lower = stripped_line.lower()
                if line_lower == "scripts:":
                    current_section = "scripts"
                    continue
                elif line_lower == "packages:":
                    current_section = "packages"
                    continue

                if current_section == "scripts" and stripped_line.startswith("-"):
                    dep_name = stripped_line[1:].strip()
                    if _is_valid_dependency_name(dep_name):
                        script_deps.append(dep_name)
                    else:
                        print(f"Warning: Invalid script dependency name '{dep_name}' in '{dependencies_path}' line {line_num}. Skipping.")
                elif current_section == "packages" and stripped_line.startswith("-"):
                    dep_name = stripped_line[1:].strip()
                    if _is_valid_unity_package_id(dep_name):
                        package_deps.append(dep_name)
                    else:
                        print(f"Warning: Invalid Unity package ID '{dep_name}' in '{dependencies_path}' line {line_num}. Skipping.")
    except IOError as e:
        raise RuntimeError(
            f"Cannot read dependencies file for '{package_path.name}'. "
            f"Installation cannot continue safely. Error: {e}"
        )

    return {"scripts": script_deps, "packages": package_deps}


# --- Core Logic ---

def _load_manifest(manifest_path: Path) -> Dict:
    """
    Load and parse the Unity package manifest.
    Returns the manifest dict on success, raises an exception on failure.
    """
    if not manifest_path.exists():
        raise FileNotFoundError(f"Unity package manifest not found at: {manifest_path}")
    
    try:
        with open(manifest_path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (IOError, OSError) as e:
        raise IOError(f"Error reading Unity package manifest '{manifest_path}': {e}")
    except json.JSONDecodeError as e:
        raise ValueError(f"Malformed JSON in Unity package manifest '{manifest_path}': {e}")


def scan_scripts(scripts_dir: Path) -> List[Path]:
    """Scan for available script packages."""
    if not scripts_dir.exists():
        return []
    return sorted([f for f in scripts_dir.iterdir() if f.is_dir()], key=lambda f: f.name.lower())


def copy_script_package(package_path: Path, target_dir: Path, assume_yes: bool) -> List[Path]:
    """
    Copies a script package directly into the target directory (Assets/),
    preserving its internal folder structure. Prompts for overwrite
    for each individual file if it already exists.
    
    Returns list of all copied file paths (for transaction tracking).
    Raises exception on error.
    """
    print(f"Installing script package: {package_path.name}")
    copied_files = []
    
    # Files to skip during copy (USL metadata, OS junk, etc.)
    skip_files = {
        'dependencies.txt',  # USL metadata - should NOT be copied to Unity
        '.DS_Store',         # macOS
        'Thumbs.db',         # Windows
        'desktop.ini',       # Windows
    }
    
    try:
        for src_item_path in package_path.rglob('*'):
            if src_item_path.is_dir():
                continue
            
            # Skip metadata and junk files
            if src_item_path.name in skip_files:
                continue

            relative_path = src_item_path.relative_to(package_path)
            dest_item_path = target_dir / relative_path
            
            # Cache the relative path for display
            rel_path_display = dest_item_path.relative_to(target_dir)

            dest_item_path.parent.mkdir(parents=True, exist_ok=True)

            if dest_item_path.exists():
                if ask_yes_no(f"'{rel_path_display}' already exists. Overwrite?", assume_yes):
                    shutil.copy2(src_item_path, dest_item_path)
                    copied_files.append(dest_item_path)
                    print(f"Overwrote: {rel_path_display}")
                else:
                    print(f"Skipped: {rel_path_display}")
            else:
                shutil.copy2(src_item_path, dest_item_path)
                copied_files.append(dest_item_path)
                print(f"Copied: {rel_path_display}")
        
        print(f"Successfully installed script package: {package_path.name}")
        return copied_files

    except (IOError, OSError, PermissionError) as e:
        raise RuntimeError(f"Error installing script package '{package_path.name}': {e}")

def ensure_git_repo(path: Path, assume_yes: bool):
    """Initialize a Git repository if one doesn't exist."""
    if is_git_repo(path):
        print("Git repository already exists.")
        return
    if ask_yes_no("No git repository found. Initialize one?", assume_yes):
        try:
            result = subprocess.run(
                ["git", "init"],
                cwd=path,
                check=True,
                capture_output=True,
                text=True
            )
            print("Initialized a new git repository.")
            if result.stdout:
                print(f"Git output: {result.stdout.strip()}")
        except subprocess.CalledProcessError as e:
            print(f"Failed to initialize git repository. Error: {e}")
            if e.stderr:
                print(f"Git error output: {e.stderr.strip()}")
            print("Please ensure Git is installed and configured correctly.")
        except FileNotFoundError:
            print("Failed to initialize git repository: 'git' command not found.")
            print("Please ensure Git is installed and in your system's PATH.")


def _add_package_to_manifest_in_memory(package_id: str, manifest: Dict) -> None:
    """
    Adds a Unity package ID to the in-memory manifest dictionary.
    Raises exception on error.
    """
    if "dependencies" not in manifest:
        manifest["dependencies"] = {}

    if package_id in manifest["dependencies"]:
        print(f"Package '{package_id}' already present in manifest.")
        return
    
    manifest["dependencies"][package_id] = "*"
    print(f"Added '{package_id}' to manifest.")


def install_unity_package(package_id: str, project_path: Path) -> int:
    """Install a Unity package by its ID (e.g., com.unity.inputsystem)."""
    manifest_path = project_path / UNITY_PACKAGES_MANIFEST
    
    try:
        manifest = _load_manifest(manifest_path)
    except (FileNotFoundError, IOError, ValueError) as e:
        print(f"Error: {e}")
        return 1

    if "dependencies" not in manifest:
        manifest["dependencies"] = {}

    if package_id in manifest["dependencies"]:
        print(f"Package '{package_id}' already present in manifest.")
        return 0
    
    manifest["dependencies"][package_id] = "*"
    
    tmp_manifest_path = manifest_path.with_suffix(".tmp")
    try:
        with open(tmp_manifest_path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, indent=2)
        tmp_manifest_path.replace(manifest_path)
    except (IOError, OSError, PermissionError) as e:
        print(f"Error: Failed to write Unity package manifest '{manifest_path}': {e}")
        return 1

    print(f"Added '{package_id}' to the project's package manifest.")
    print("Note: '*' was used for version. Unity will resolve to the latest compatible version.")
    return 0


# --- Command Handlers ---

def list_scripts(scripts: List[Path]):
    """List available script packages."""
    print("Available USL scripts:")
    if not scripts:
        print("  (No scripts found)")
        return
    for s in scripts:
        print(f"- {s.name}")


def add_scripts(all_available_scripts: List[Path], script_names: List[str], project_assets: Path, project_path: Path, assume_yes: bool) -> int:
    """
    Add selected script packages to the project, handling their dependencies.
    all_available_scripts: a list of all script Path objects found by scan_scripts
    script_names: names of the primary scripts to add (from CLI args)
    project_assets: Path to the Assets folder
    project_path: Path to the root of the Unity project
    assume_yes: whether to auto-approve all prompts
    """
    name_map = {s.name: s for s in all_available_scripts}

    scripts_to_copy = set()
    packages_to_install = set()
    dependency_cache = {}

    # --- Phase 1: Resolve Dependencies ---
    try:
        for primary_script_name in script_names:
            if primary_script_name not in name_map:
                print(f"Error: Primary script not found: {primary_script_name}")
                closest_matches = difflib.get_close_matches(primary_script_name, name_map.keys(), n=3, cutoff=0.6)
                if closest_matches:
                    print("Did you mean one of these?")
                    for match in closest_matches:
                        print(f"  - {match}")
                return 1

            current_script_path = name_map[primary_script_name]
            scripts_to_copy.add(current_script_path)

            # BFS for dependency resolution
            queue = deque([current_script_path])
            processed_scripts = set()

            while queue:
                script_path = queue.popleft()
                if script_path in processed_scripts:
                    continue
                processed_scripts.add(script_path)

                # Use cached dependencies if available
                if script_path in dependency_cache:
                    dependencies = dependency_cache[script_path]
                else:
                    dependencies = parse_dependencies_file(script_path)
                    dependency_cache[script_path] = dependencies

                # Add package dependencies
                for pkg_id in dependencies["packages"]:
                    packages_to_install.add(pkg_id)

                # Add script dependencies
                for dep_script_name in dependencies["scripts"]:
                    if dep_script_name not in name_map:
                        print(f"Error: Script '{dep_script_name}' (required by '{script_path.name}') not found.")
                        
                        # Suggest similar names
                        matches = difflib.get_close_matches(dep_script_name, name_map.keys(), n=3, cutoff=0.6)
                        if matches:
                            print("Did you mean one of these?")
                            for match in matches:
                                print(f"  - {match}")
                        
                        raise RuntimeError(f"Missing required dependency: {dep_script_name}")
                    
                    dep_script_path = name_map[dep_script_name]
                    scripts_to_copy.add(dep_script_path)
                    
                    # Only queue if not yet processed (avoids reprocessing in diamond dependencies)
                    if dep_script_path not in processed_scripts:
                        queue.append(dep_script_path)

    except RuntimeError as e:
        print(f"Error: {e}")
        return 1

    if not scripts_to_copy and not packages_to_install:
        print("No scripts or packages to add after dependency resolution.")
        return 0

    # --- Phase 2: Show Installation Plan ---
    print("\n--- Installation Plan ---")
    if scripts_to_copy:
        print("The following script packages will be copied:")
        for script_path in sorted(scripts_to_copy, key=lambda p: p.name):
            print(f"  - {script_path.name}")
    if packages_to_install:
        print("The following Unity packages will be added to manifest.json:")
        for pkg_id in sorted(packages_to_install):
            print(f"  - {pkg_id}")
    print("-------------------------\n")

    if not ask_yes_no("Proceed with installation?", assume_yes):
        print("Installation cancelled by user.")
        return 0

    # --- Phase 3: Install with Transaction Safety ---
    print("\nStarting installation...")

    manifest_path = project_path / UNITY_PACKAGES_MANIFEST

    try:
        with InstallationTransaction(manifest_path) as txn:
            # Load or create manifest
            if manifest_path.exists():
                manifest_data = _load_manifest(manifest_path)
            else:
                print(f"Unity package manifest not found at '{manifest_path}'. Creating a new one.")
                manifest_data = {"dependencies": {}}

            # Add packages to in-memory manifest
            for pkg_id in sorted(packages_to_install):
                _add_package_to_manifest_in_memory(pkg_id, manifest_data)

            # Write manifest once
            if packages_to_install:
                with open(manifest_path, "w", encoding="utf-8") as f:
                    json.dump(manifest_data, f, indent=2)
                print(f"Updated Unity package manifest at '{manifest_path}'.")

            # Copy script packages and track files
            for script_path in sorted(scripts_to_copy, key=lambda p: p.name):
                copied_files = copy_script_package(script_path, project_assets, assume_yes)
                for file_path in copied_files:
                    txn.track_file(file_path)

            # If we got here, everything worked
            txn.commit()
            print("\n✓ Installation complete!")
            return 0

    except Exception as e:
        print(f"\n✗ Installation failed: {e}")
        return 1


def install_package(package_id: str, project_path: Path) -> int:
    """Install a Unity package by validating and adding it to manifest."""
    if not _is_valid_unity_package_id(package_id):
        print(f"Error: Invalid Unity package ID format: '{package_id}'")
        print("Expected format: com.company.package (lowercase, at least 2 dots)")
        return 1
    
    print(f"Attempting to install Unity package: {package_id}")
    return install_unity_package(package_id, project_path)


def run_interactive_mode(project_assets: Path, project_path: Path, assume_yes: bool) -> int:
    """Run the interactive script selection mode."""
    # Re-scan to ensure we have all scripts for dependency resolution
    scripts = scan_scripts(SCRIPTS_DIR)
    
    if not scripts:
        print("No local scripts available to install.")
        return 0
    
    print("Available scripts:")
    for idx, s in enumerate(scripts, 1):
        print(f"{idx}. {s.name}")

    try:
        selection = input("Enter numbers to add (e.g., 1 3 5 or 1,2,3), or leave blank to cancel: ").strip()
        if not selection:
            print("Operation cancelled.")
            return 0
        
        # Split by both commas and spaces, then filter out empty strings
        tokens = re.split(r'[,\s]+', selection)
        tokens = [t for t in tokens if t]
        
        indexes = [int(i) - 1 for i in tokens]
        selected_scripts = [scripts[i] for i in indexes if 0 <= i < len(scripts)]

        if not selected_scripts:
            print("No valid scripts selected.")
            return 1

        selected_script_names = [s.name for s in selected_scripts]
        return add_scripts(scripts, selected_script_names, project_assets, project_path, assume_yes)

    except (ValueError, IndexError):
        print(f"Invalid input: '{selection}'")
        print("Please enter numbers (e.g., '1 3 5' or '1,2,3')")
        return 1


# --- Main ---

def _parse_args():
    """Parses command-line arguments for the USL tool."""
    # Parent parser for shared arguments
    parent_parser = argparse.ArgumentParser(add_help=False)
    parent_parser.add_argument("-y", "--yes", action="store_true", help="Automatically answer yes to all prompts.")
    
    parser = argparse.ArgumentParser(
        prog="usl",
        description="Unity Script Library - A tool for managing scripts and packages in Unity projects."
    )
    parser.add_argument("--init-git", action="store_true", help="Initialize a new git repository if one doesn't exist.")
    parser.add_argument("-y", "--yes", action="store_true", help="Automatically answer yes to all prompts.")

    subparsers = parser.add_subparsers(dest="command", help="Available commands")

    # Add parent_parser to each subcommand so -y works after the command too
    subparsers.add_parser("list", parents=[parent_parser], help="List available local script packages.")

    add_parser = subparsers.add_parser("add", parents=[parent_parser], help="Add local script packages to the Unity project.")
    add_parser.add_argument("scripts", nargs="+", metavar="SCRIPT", help="Names of the script packages to add.")

    install_parser = subparsers.add_parser("install", parents=[parent_parser], help="Install a Unity package by its ID.")
    install_parser.add_argument("package_id", metavar="PACKAGE_ID", help="The Unity package ID (e.g., com.unity.inputsystem).")

    return parser.parse_args()


def _run_command(args, cwd: Path):
    """Routes to the appropriate command handler based on parsed arguments."""
    # Validate SCRIPTS_DIR exists
    if not SCRIPTS_DIR.exists():
        print(f"Error: Scripts directory not found at: {SCRIPTS_DIR}")
        print("Please create it or run from the correct location.")
        return 1
    
    assume_yes = getattr(args, "yes", False)

    if args.command == "list":
        scripts = scan_scripts(SCRIPTS_DIR)
        list_scripts(scripts)
        return 0

    # For all other commands, check for Unity project
    if not is_unity_project(cwd):
        print("Error: This command must be run from a Unity project directory.")
        print("       (Expected to find 'Assets' and 'ProjectSettings' subdirectories.)")
        return 1

    if args.init_git:
        ensure_git_repo(cwd, assume_yes)

    if args.command == "add":
        scripts = scan_scripts(SCRIPTS_DIR)
        return add_scripts(scripts, args.scripts, cwd / "Assets", cwd, assume_yes)

    elif args.command == "install":
        return install_package(args.package_id, cwd)

    else:  # Interactive mode
        if assume_yes:
            print("Error: Interactive mode cannot be used with --yes flag.")
            print("Please specify scripts to add or remove --yes flag.")
            return 1
        
        print("No command specified. Entering interactive mode...")
        return run_interactive_mode(cwd / "Assets", cwd, assume_yes)

    return 0


def main():
    """Main entry point for the USL tool."""
    args = _parse_args()
    cwd = Path.cwd()
    return _run_command(args, cwd)


if __name__ == "__main__":
    exit(main())
