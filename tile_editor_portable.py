"""Frozen Windows entry point for the self-contained Tile Editor package."""

import multiprocessing
import os
import sys
import traceback
from pathlib import Path


os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")


def _crash_log_path() -> Path:
    return Path.cwd() / "crash.log"


def _write_crash(error: str) -> None:
    try:
        sys.stderr.write(error + "\n")
        _crash_log_path().write_text(error, encoding="utf-8")
        sys.stderr.write("\nCrash log: " + str(_crash_log_path()) + "\n")
    except Exception:
        pass
    try:
        input("\nPress Enter to close...")
    except (EOFError, OSError):
        pass


def _smoke_test() -> int:
    import numpy
    import pygame
    import requests
    import scipy
    from PIL import Image

    from edit_tiles.app import TileEditor  # noqa: F401
    from edit_tiles.version import __version__
    from mod_project import ModProject  # noqa: F401

    print(
        "Tile Editor portable runtime OK "
        f"suite={__version__} "
        f"python={sys.version.split()[0]} "
        f"pygame={pygame.version.ver} "
        f"numpy={numpy.__version__} "
        f"pillow={Image.__version__} "
        f"requests={requests.__version__} "
        f"scipy={scipy.__version__}"
    )
    return 0


def main() -> int:
    multiprocessing.freeze_support()
    if "--portable-smoke-test" in sys.argv:
        return _smoke_test()
    from edit_tiles.app import main as editor_main

    editor_main()
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except SystemExit:
        raise
    except Exception:
        _write_crash(traceback.format_exc())
        raise SystemExit(1)
