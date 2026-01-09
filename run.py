#!/usr/bin/env python3
from pathlib import Path
import sys
from usl.main import main

if __name__ == "__main__":
    # Ensure imports work no matter where you run it
    USL_DIR = Path(__file__).parent.resolve()
    if str(USL_DIR) not in sys.path:
        sys.path.insert(0, str(USL_DIR))
    main()
