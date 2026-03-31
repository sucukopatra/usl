# Unity Script Library (USL)

A CLI tool for managing custom script packages in Unity projects. Drop in scripts, resolve dependencies, install Unity packages — without doing it by hand every time.

## Installation

**Unix/Linux/macOS:**
```bash
./install.sh
```
Adds a `usl` wrapper to `~/.local/bin`. Make sure that's in your PATH.

**Windows:**
```cmd
install.cmd
```
Drops a `usl.cmd` into `%USERPROFILE%\AppData\Local\Microsoft\WindowsApps`.

Then create the scripts directory in the USL project root:
```bash
mkdir scripts
```

## Usage

Run from inside a Unity project (needs `Assets/` and `ProjectSettings/` present).

```bash
usl list                             # see available script packages
usl add MyCoolScript                 # add a package + its dependencies
usl add DialogueSystem FPController  # add multiple at once
usl install com.unity.inputsystem    # add a Unity package to manifest.json
usl                                  # interactive mode
```

Pass `-y` to auto-approve any file overwrite prompts.

## Script Packages

Packages live in `usl/scripts/`. Each one is just a folder — whatever structure you want. Add a `dependencies.txt` if it depends on other scripts or Unity packages:

```
Scripts:
- SomeOtherScript

Packages:
- com.unity.inputsystem
- com.unity.textmeshpro
```

USL will copy scripts into `Assets/` and patch `Packages/manifest.json` automatically. It shows you the full dependency tree before touching anything.
