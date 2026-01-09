from pathlib import Path

def is_unity_project(path: Path) -> bool:
    return (path / "Assets").is_dir() and (path / "ProjectSettings").is_dir()

def ask_yes_no(prompt: str) -> bool:
    answer = input(f"{prompt} (y/n): ").strip().lower()
    return answer == "y"
