"""Build this repository's PDF manuals from the Markdown in docs/.

Usage:  python scripts/build_pdfs.py
Needs:  pip install reportlab
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from md2pdf import build  # noqa: E402

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def P(*parts):
    return os.path.join(REPO, *parts)


MANUALS = [
    dict(out=P("docs", "pdf", "Tile-Editor-User-Manual.pdf"),
         title="Tile Editor User Manual",
         subtitle="Terrain and map editor for Railroader mods",
         sections=[("Getting Started", P("docs","GETTING_STARTED.md")),
                   ("Keybind Reference", P("docs","KEYBINDS.md")),
                   ("Terrain Editing", P("docs","TERRAIN_EDITING.md")),
                   ("Track Editing", P("docs","TRACK_EDITING.md")),
                   ("Mod Tools", P("docs","MOD_TOOLS.md")),
                   ("In-Game Geo Workspace", P("docs","IN_GAME_GEO.md"))]),
    dict(out=P("docs", "pdf", "Tile-Editor-Modding-Guide.pdf"),
         title="Tile Editor Modding Guide",
         subtitle="Authoring map mods, data formats, and runtime components",
         sections=[("Data Formats And Examples", P("docs","SCHEMA_EXAMPLES.md")),
                   ("Mod Tools", P("docs","MOD_TOOLS.md")),
                   ("Track Editing", P("docs","TRACK_EDITING.md")),
                   ("In-Game Geo Workspace", P("docs","IN_GAME_GEO.md")),
                   ("Terrain Editing", P("docs","TERRAIN_EDITING.md"))]),
]


def main():
    ok = 0
    for m in MANUALS:
        os.makedirs(os.path.dirname(m["out"]), exist_ok=True)
        for _, p in m["sections"]:
            if not os.path.isfile(p):
                print("  ! missing section source: %s" % p)
        try:
            path, n = build(m["out"], m["title"], m["subtitle"], m["sections"])
            print("OK  %-44s %2d sections  %6.1f KB"
                  % (os.path.basename(path), n, os.path.getsize(path) / 1024.0))
            ok += 1
        except Exception as e:
            print("FAIL %-44s %s: %s" % (os.path.basename(m["out"]), type(e).__name__, e))
    print("")
    print("%d/%d manuals built" % (ok, len(MANUALS)))
    return 0 if ok == len(MANUALS) else 1


if __name__ == "__main__":
    raise SystemExit(main())
