from __future__ import annotations

import sys
from pathlib import Path

from spotify_nosleep.cli import main

if __name__ == "__main__":
    # Always register the windowless .pyw launcher when enabling startup from CLI.
    launcher = Path(__file__).resolve().with_name("spotify_no_sleep.pyw")
    sys.exit(main(entry_script=launcher))
