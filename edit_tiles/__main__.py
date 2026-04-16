"""edit_tiles.__main__ — Entry point."""
import sys
import traceback
from pathlib import Path

log_path = Path(__file__).resolve().parent.parent / "crash.log"

def _write_crash(err):
    try:
        sys.stderr.write(err + "\n")
        log_path.write_text(err, encoding="utf-8")
        sys.stderr.write("\nCrash log: " + str(log_path) + "\n")
    except Exception:
        pass
    input("\nPress Enter to close...")
    sys.exit(1)

try:
    from .app import main
except Exception:
    _write_crash(traceback.format_exc())

try:
    main()
except Exception:
    _write_crash(traceback.format_exc())
