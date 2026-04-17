"""edit_tiles — Terrain tile map editor package.

Submodules:
    constants   — colours, sizes, lookup tables (no pygame)
    selection   — SelectionBuffer, Clipboard, rasterise_polygon (no pygame)
    osm         — OsmOverlay + tile math helpers (needs pygame)
    terrain     — Tile, UndoRecord, brush helpers (needs pygame)
    generate    — Mapbox tile generation (needs numpy/PIL)

Import submodules directly; this __init__ only exposes pygame-free
utilities so tests can import without a display.
"""

# Always safe — no pygame dependency
from .constants import *                                               # noqa
from .selection import SelectionBuffer, Clipboard, rasterise_polygon  # noqa
