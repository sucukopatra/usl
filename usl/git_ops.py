import subprocess
from pathlib import Path

def ensure_git_repo(path: Path):
    git_dir = path / ".git"
    if git_dir.exists():
        print("Git repository already exists.")
        return

    try:
        subprocess.run(["git", "init"], cwd=path, check=True)
        print("Initialized new git repository.")
    except subprocess.CalledProcessError:
        print("Git initialization failed.")
