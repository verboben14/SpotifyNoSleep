from __future__ import annotations

import sys
from pathlib import Path

from spotify_nosleep.cli import main

if __name__ == "__main__":
    sys.exit(main(entry_script=Path(__file__).resolve()))
