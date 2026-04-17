"""edit_tiles.constants — UI colours, sizes, and lookup tables."""
from pathlib import Path

# availability flags — set True if the respective package imports OK
try:
    import mod_project  # noqa: F401
    _MOD_AVAILABLE = True
except ImportError:
    _MOD_AVAILABLE = False

try:
    import railroader_bridge  # noqa: F401
    _BRIDGE_AVAILABLE = True
except ImportError:
    _BRIDGE_AVAILABLE = False

HEIGHT_MIN_M = 500.0
HEIGHT_MAX_M = 1500.0
OVERVIEW_RES = 513
DETAIL_RES   = 513
DETAIL_ZOOM  = 4.0

VEG_COLORS = [
    (20,  70,  25),
    (30,  90,  40),
    (55, 105,  35),
    (100, 130,  50),
    (140, 160,  65),
    (170, 185,  80),
    (200, 185, 110),
    (220, 200, 140),
]
VEG_NAMES = [
    "Dense Forest", "Woody Wetland", "Mixed Forest", "Sparse Undergrowth",
    "Grassland+Bush", "Open Grassland", "Pasture", "Crops/Developed",
]

WIN_W, WIN_H = 1920, 1080
PANEL_H       = 76          # top nav bar (two rows: 38px each)
TOOLBAR_H     = 52          # second row: mode + brush controls
EDIT_PANEL_H  = TOOLBAR_H   # alias kept for compat
STATUS_H      = 22          # thin status strip at bottom
BG_COLOR      = (8,  11, 16)
PANEL_COLOR   = (16, 21, 30)
TOOLBAR_COLOR = (12, 16, 24)
EDIT_PANEL_BG = TOOLBAR_COLOR
PANEL_ELEVATED_BG = (12, 18, 28)
PANEL_HEADER_BG   = (16, 25, 38)
PANEL_SECTION_BG  = (17, 25, 36)
PANEL_SECTION_ALT = (13, 19, 28)
PANEL_SECTION_BORDER = (46, 68, 94)
ROW_ALT_BG     = (15, 22, 32)
ROW_HOVER_BG   = (26, 38, 54)
ROW_ACTIVE_BG  = (24, 56, 84)
ROW_ACTIVE_ALT_BG = (30, 66, 98)
ROW_ACTIVE_BORDER = (90, 195, 255)
BTN_ACTIVE    = (0, 156, 204)
BTN_HOVER_C   = (42, 62, 88)
BTN_INACTIVE  = (30, 40, 56)
BTN_BORDER    = (78, 106, 140)
ACCENT_COLOR  = (0, 212, 255)
ACCENT2_COLOR = (255, 107, 53)
WARN_COLOR    = (255, 200,  50)
OK_COLOR      = (80, 220, 120)
TEXT_COLOR    = (200, 210, 220)
DIM_COLOR     = (70,  85,  100)
TEXT_SOFT     = (154, 168, 184)
TEXT_MUTED    = (118, 132, 148)
BORDER_COLOR  = (26,  32,  48)
MISSING_COLOR = (12,  16,  22)
BRUSH_COLORS  = {          # brush ring colour per mode
    'raise':   (100, 220, 255),
    'lower':   (255, 120,  80),
    'flatten': (255, 200,  60),
    'paint':   (180, 100, 255),
    'smooth':  (100, 255, 160),
    'noise':   (255, 160,  50),
    'erode':   (200, 100,  60),
}

MAX_UNDO = 64

# =========================
# OpenStreetMap Overlay
# =========================
OSM_ZOOM        = 15          # default slippy-map zoom (14-16 good for 500m tiles)
OSM_TILE_URL    = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
OSM_USER_AGENT  = "edit_tiles/1.0 (terrain map editor)"
OSM_CACHE_DIR   = Path.home() / ".edit_tiles_osm_cache"
OSM_MAX_FETCH   = 8           # concurrent HTTP fetches


# =========================
# World-pixel coordinate
# =========================
TILE_STRIDE = 512   # world pixels per tile (not 513 — overlap pixel is shared)

# =========================
# Terrain generation
# =========================
GEN_MAPBOX_ZOOM     = 15
GEN_MAPBOX_TILE_SZ  = 256
GEN_HEIGHT_RES      = 513
GEN_TILE_DIM_M      = 500.0
GEN_ORIGIN_LAT      = 35.382614
GEN_ORIGIN_LON      = -83.49541
GEN_ORIGIN_E_BIAS   = 8.0
GEN_ORIGIN_N_BIAS   = -8.0
GEN_HEIGHT_MIN_G    = 500.0
GEN_HEIGHT_MAX_G    = 1500.0
GEN_OFFSET_EAST_X   = -66
GEN_OFFSET_WEST_X   = -98
GEN_OFFSET_MAX_M    = 40.0
GEN_NLCD_BLUR       = 16.0
GEN_NLCD_URL        = "https://www.mrlc.gov/geoserver/mrlc_display/NLCD_2021_Land_Cover_L48/wms"
GEN_ALL_VEG         = [0, 1, 2, 3, 4, 5, 6, 7]
GEN_NLCD_COLORS = {
    (71,  107, 160): (0, True),  (186, 216, 234): (2, True),
    (112, 163, 186): (1, True),  (221, 201, 201): (5, False),
    (216, 147, 130): (6, False), (237, 0,   0  ): (7, False),
    (170, 0,   0  ): (7, False), (178, 173, 163): (6, False),
    (104, 170, 99 ): (0, False), (28,  99,  48 ): (0, False),
    (181, 201, 142): (1, False), (204, 186, 124): (3, False),
    (226, 226, 193): (5, False), (219, 216, 61 ): (5, False),
    (170, 112, 40 ): (7, False),
}
