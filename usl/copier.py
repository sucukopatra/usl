from pathlib import Path
import shutil

def ask_overwrite(path: Path) -> bool:
    """Ask the user if they want to overwrite an existing file/folder."""
    while True:
        answer = input(f"'{path}' already exists. Overwrite? (y/n): ").strip().lower()
        if answer in ("y", "yes"):
            return True
        elif answer in ("n", "no"):
            return False
        print("Please answer y (yes) or n (no).")


def copy_folder_contents(src: Path, dest: Path, root: Path):
    """
    Recursively copy contents of src into dest.
    root is used for relative path prompts.
    """
    for item in src.iterdir():
        target = dest / item.name
        rel_path = target.relative_to(root)

        if item.is_file():
            if target.exists():
                if ask_overwrite(rel_path):
                    shutil.copy2(item, target)
                    print(f"Overwritten: {rel_path}")
                else:
                    print(f"Skipped: {rel_path}")
            else:
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(item, target)
                print(f"Copied: {rel_path}")

        elif item.is_dir():
            copy_folder_contents(item, target, root)


def copy_scripts(packages, project_assets: Path):
    """
    Copy one or more script packages into the Unity project's Assets folder,
    merging their contents rather than nesting the package folder itself.
    """
    for package in packages:
        print(f"Installing package: {package.name}")
        copy_folder_contents(package, project_assets, project_assets)
    print("All done!")
