from pathlib import Path
from usl.cli import parse_args
from usl.scanner import scan_scripts
from usl.copier import copy_scripts
from usl.git_ops import ensure_git_repo
from usl.utils import is_unity_project

# Keep scripts folder **outside** usl/
LIBRARY_DIR = Path(__file__).parent.parent.resolve()  # parent of usl/
SCRIPTS_DIR = LIBRARY_DIR / "scripts"

def interactive_mode(scripts, target_dir: Path):
    print("Available scripts:")
    for idx, s in enumerate(scripts, 1):
        print(f"{idx}. {s.name}")

    selection = input("Enter numbers to add (e.g. 1 3 5): ").strip()
    if not selection:
        print("Nothing selected.")
        return

    try:
        indexes = [int(i)-1 for i in selection.split()]
    except ValueError:
        print("Invalid input. Enter numbers separated by spaces.")
        return

    selected = []
    for i in indexes:
        if 0 <= i < len(scripts):
            selected.append(scripts[i])
        else:
            print(f"Invalid number: {i+1}")

    if not selected:
        print("No valid scripts selected.")
        return

    copy_scripts(selected, target_dir)
    print("Done!")

def main():
    args = parse_args()
    cwd = Path.cwd()

    if not is_unity_project(cwd):
        print("This is not a Unity project. Expected Assets/ and ProjectSettings/ folders.")
        return

    if not getattr(args, "no_git", False):
        ensure_git_repo(cwd)

    scripts = scan_scripts(SCRIPTS_DIR)
    if not scripts:
        print(f"No scripts found in USL library: {SCRIPTS_DIR}")
        return

    # Interactive mode
    if not getattr(args, "command", None):
        target_dir = cwd / "Assets" / "Scripts"
        interactive_mode(scripts, target_dir)
        return

    # list
    if args.command == "list":
        print("Available USL scripts:")
        for s in scripts:
            print(f"- {s.name}")
        return

    # add
    if args.command == "add":
        name_map = {s.name: s for s in scripts}
        selected = []

        for name in args.scripts:
            if name in name_map:
                selected.append(name_map[name])
            else:
                print(f"Script not found: {name}")

        if not selected:
            print("No valid scripts selected.")
            return

        target_dir = cwd / "Assets" / "Scripts"
        copy_scripts(selected, target_dir)
        print("Done!")
        return

    print("Nothing to do. Use 'list' or 'add'.")
