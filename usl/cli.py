import argparse

def parse_args():
    parser = argparse.ArgumentParser(
        prog="usl",
        description="Unity Script Library"
    )

    # Global option
    parser.add_argument(
        "--no-git",
        action="store_true",
        help="Do not initialize git repository"
    )

    subparsers = parser.add_subparsers(
        dest="command",
        help="Available commands"
    )

    # usl list
    subparsers.add_parser(
        "list",
        help="List available scripts"
    )

    # usl add <scripts...>
    add_parser = subparsers.add_parser(
        "add",
        help="Add scripts to the current Unity project"
    )
    add_parser.add_argument(
        "scripts",
        nargs="+",
        metavar="SCRIPT",
        help="Script folder names to add"
    )

    return parser.parse_args()
