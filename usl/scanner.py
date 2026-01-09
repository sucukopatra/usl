from pathlib import Path

def scan_scripts(scripts_dir: Path):
    if not scripts_dir.exists():
        return []
    folders = [f for f in scripts_dir.iterdir() if f.is_dir()]
    return sorted(folders, key=lambda f: f.name.lower())
