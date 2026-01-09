from pathlib import Path
import shutil

def copy_scripts(folders, target_dir: Path):
    target_dir.mkdir(parents=True, exist_ok=True)
    for folder in folders:
        dest = target_dir / folder.name
        if dest.exists():
            print(f"Skipping {folder.name} (already exists)")
            continue
        shutil.copytree(folder, dest)
        print(f"Copied {folder.name}")
