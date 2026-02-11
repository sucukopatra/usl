# Unity Script Library (USL)

The Unity Script Library (USL) is a Python-based command-line interface (CLI) tool designed to streamline the management of custom scripts and Unity packages within Unity projects. It provides functionalities to list available local script packages, add them to Unity projects (including dependency resolution), and install official Unity packages by their IDs.

## Features

- **Local Script Package Management:** Easily add custom script packages from a local `scripts/` directory into your Unity project's `Assets/` folder.
- **Dependency Resolution:** Automatically resolves script and Unity package dependencies declared in `dependencies.txt` files.
- **Unity Package Installation:** Installs official Unity packages by adding them to your project's `Packages/manifest.json`.
- **Interactive Mode:** A user-friendly interactive prompt to select and install scripts.
- **Safe Operations:** Uses transactional operations for `manifest.json` modifications with automatic rollback on errors.
- **Input Validation:** Validates script names and Unity package IDs to ensure correct formatting.
- **Git Integration:** Option to initialize a Git repository if one doesn't exist before performing other operations.
- **Overwrite Protection:** Prompts before overwriting existing files, with an option to auto-approve.

## Installation

To use the USL tool, follow these setup steps:

1.  **Install the USL Command:**
    Execute the appropriate installer script for your operating system to add the `usl` command to your system's PATH.
    
    *   **Unix/Linux/macOS:**
        ```bash
        ./install.sh
        ```
        This script will create an executable `usl` wrapper in `~/.local/bin` (or a similar location) and set up the `PYTHONPATH`. Ensure `~/.local/bin` is in your system's PATH.
    
    *   **Windows:**
        ```cmd
        install.cmd
        ```
        This script will create a `usl.cmd` wrapper in `%USERPROFILE%\AppData\Local\Microsoft\WindowsApps` which is typically in the Windows PATH.

2.  **Create the Scripts Directory:**
    The tool expects a `scripts/` directory in the project root of the `usl` tool itself. This is where your custom script packages will reside. Create it if it doesn't exist:
    ```bash
    mkdir scripts
    ```

## Basic Usage

Most commands must be run from within a Unity project directory (i.e., a directory containing `Assets` and `ProjectSettings` folders).

### Global Options

-   `-y`, `--yes`: Automatically answer yes to all prompts (e.g., file overwrites, Git initialization).
-   `--init-git`: Initialize a new Git repository in the current directory if one doesn't already exist.

### Commands

#### `usl list`

Lists all available local script packages found in the `scripts/` directory. This command can be run from any directory.

```bash
usl list
```

#### `usl add <SCRIPT_NAME>...`

Adds one or more specified local script packages, along with all their resolved dependencies (both scripts and Unity packages), to the current Unity project.

-   `<SCRIPT_NAME>`: The name of a script package directory within your `scripts/` folder.

```bash
# Add a single script package
usl add MyCoolScript

# Add multiple script packages
usl add DialogueSystem FPController_CC

# Add with auto-approval for all prompts
usl -y add MyCoolScript
```

#### `usl install <PACKAGE_ID>`

Installs a Unity package by adding its ID to the project's `Packages/manifest.json`. The package ID must follow the `com.company.package` format.

-   `<PACKAGE_ID>`: The full ID of the Unity package (e.g., `com.unity.inputsystem`).

```bash
usl install com.unity.inputsystem
```
*Note: The tool currently uses wildcards (`*`) for version resolution. Unity will resolve to the latest compatible version. You can manually edit `Packages/manifest.json` to specify exact versions if needed.*

#### `usl` (Interactive Mode)

If no command or script names are provided, the tool enters an interactive mode, guiding you through selecting available script packages to add.

```bash
usl
```
*Note: Interactive mode cannot be used with the `--yes` flag and will return an error if attempted.*

## Script Package Structure

Local script packages reside in the `usl/scripts/` directory. Each script package:

-   Must be a directory with a valid name (starts with a letter, contains only letters/numbers/underscores/hyphens).
-   Can contain any folder structure with C# scripts and other Unity assets.
-   Can optionally include a `dependencies.txt` file to declare dependencies.

### `dependencies.txt` Format

```
# Comments are supported

Scripts:
- DependencyScriptName1
- DependencyScriptName2

Packages:
- com.unity.inputsystem
- com.unity.textmeshpro
```

**Validation rules:**
-   Script names must start with a letter and contain only letters, numbers, underscores, and hyphens.
-   Unity package IDs must be lowercase and contain at least 2 dots (e.g., `com.company.package`).
-   Invalid dependencies are logged as warnings and skipped.

## How USL Interacts with your Unity Project

The tool interacts with Unity projects by:

1.  **Copying scripts:** Recursively copies all files from script packages to the project's `Assets/` folder, preserving directory structure.
2.  **Managing packages:** Modifies `Packages/manifest.json` to add Unity package dependencies.
3.  **Overwrite handling:** Prompts for each file that already exists (can be bypassed with the `-y` flag).
4.  **Installation plan:** Shows a complete dependency tree and planned actions before making any changes.
