from pathlib import Path

def is_unity_project(path: Path) -> bool:
    return (path / "Assets").is_dir() and (path / "ProjectSettings").is_dir()

def is_git_repo(path: Path) -> bool:
    """Return True if path contains a .git folder"""
    return (path / ".git").exists()

def ask_yes_no(prompt: str) -> bool:
    answer = input(f"{prompt} (y/n): ").strip().lower()
    return answer == "y"
