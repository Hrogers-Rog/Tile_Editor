"""edit_tiles.osm — OpenStreetMap overlay: fetch, cache, render."""
import math
import threading
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor

import numpy as np
from PIL import Image
import pygame

from .constants import OSM_ZOOM, OSM_TILE_URL, OSM_USER_AGENT, OSM_CACHE_DIR, OSM_MAX_FETCH


def osm_deg2tile(lat_deg: float, lon_deg: float, zoom: int) -> tuple:
    """Convert lat/lon to OSM slippy tile (xtile, ytile)."""
    lat_r = math.radians(lat_deg)
    n     = 2 ** zoom
    xtile = int((lon_deg + 180.0) / 360.0 * n)
    ytile = int((1.0 - math.log(math.tan(lat_r) + 1.0 / math.cos(lat_r)) / math.pi)
                / 2.0 * n)
    return xtile, ytile

def osm_tile2deg(xtile: int, ytile: int, zoom: int) -> tuple:
    """Convert OSM tile coords to top-left lat/lon of that tile."""
    n     = 2 ** zoom
    lon   = xtile / n * 360.0 - 180.0
    lat_r = math.atan(math.sinh(math.pi * (1 - 2 * ytile / n)))
    lat   = math.degrees(lat_r)
    return lat, lon


class OsmOverlay:
    """
    Manages fetching, caching, and rendering OpenStreetMap slippy-map tiles
    overlaid on top of the terrain editor tiles.

    Each terrain editor tile (tx, ty) maps to a bounding box in lat/lon.
    We find which OSM tiles cover that bbox, fetch them (with disk cache),
    stitch them into a single pygame Surface at the right screen rectangle.
    """

    def __init__(self):
        self.enabled  = False
        self.opacity  = 140          # 0-255 alpha for overlay
        self.zoom     = OSM_ZOOM
        # surf_cache: (tx, ty, zoom) -> pygame.Surface (stitched, editor-tile-sized)
        self._surf_cache: dict = {}
        # fetch_queue: set of (osm_x, osm_y, zoom) currently being downloaded
        self._fetching: set   = set()
        self._lock            = threading.Lock()
        # raw tile cache: (osm_x, osm_y, zoom) -> raw PNG bytes
        self._raw: dict       = {}
        self._executor        = ThreadPoolExecutor(max_workers=OSM_MAX_FETCH,
                                                   thread_name_prefix='osm')
        OSM_CACHE_DIR.mkdir(parents=True, exist_ok=True)

    # ------------------------------------------------------------------
    def _cache_path(self, ox: int, oy: int, z: int) -> Path:
        return OSM_CACHE_DIR / str(z) / str(ox) / f"{oy}.png"

    def _load_disk(self, ox: int, oy: int, z: int) -> bytes | None:
        p = self._cache_path(ox, oy, z)
        if p.exists():
            return p.read_bytes()
        return None

    def _save_disk(self, ox: int, oy: int, z: int, data: bytes):
        p = self._cache_path(ox, oy, z)
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)

    def _fetch_tile(self, ox: int, oy: int, z: int):
        """Background thread: fetch one OSM tile, save to disk + memory."""
        key = (ox, oy, z)
        try:
            # Try disk cache first
            data = self._load_disk(ox, oy, z)
            if data is None:
                import urllib.request
                url = OSM_TILE_URL.format(z=z, x=ox, y=oy)
                req = urllib.request.Request(url,
                      headers={'User-Agent': OSM_USER_AGENT})
                with urllib.request.urlopen(req, timeout=10) as resp:
                    data = resp.read()
                self._save_disk(ox, oy, z, data)
            with self._lock:
                self._raw[key] = data
        except Exception as e:
            pass   # silently ignore — tile stays missing
        finally:
            with self._lock:
                self._fetching.discard(key)

    def _ensure_tile(self, ox: int, oy: int, z: int):
        """Queue a fetch if not already in memory or in-flight."""
        key = (ox, oy, z)
        with self._lock:
            if key in self._raw or key in self._fetching:
                return
            self._fetching.add(key)
        self._executor.submit(self._fetch_tile, ox, oy, z)

    def _raw_to_surf(self, data: bytes) -> pygame.Surface | None:
        """Decode PNG bytes → pygame Surface."""
        try:
            import io
            img = Image.open(io.BytesIO(data)).convert('RGBA')
            arr = np.array(img)
            surf = pygame.surfarray.make_surface(
                arr[:, :, :3].transpose(1, 0, 2))
            surf.set_alpha(255)
            return surf
        except Exception:
            return None

    def _build_editor_surf(self, tx: int, ty: int,
                           get_bounds_fn, screen_w: int, screen_h: int
                           ) -> pygame.Surface | None:
        """
        Build a Surface covering one editor tile (tx,ty) by stitching OSM tiles.
        Returns None if any OSM tiles are still missing.
        """
        z = self.zoom
        (min_lat, min_lon), (max_lat, max_lon) = get_bounds_fn(tx, ty)

        # OSM tile range covering this bbox
        ox0, oy0 = osm_deg2tile(max_lat, min_lon, z)   # top-left (higher lat = lower y)
        ox1, oy1 = osm_deg2tile(min_lat, max_lon, z)   # bottom-right

        # Ensure all needed OSM tiles are fetched
        all_ready = True
        for oy in range(oy0, oy1 + 1):
            for ox in range(ox0, ox1 + 1):
                key = (ox, oy, z)
                with self._lock:
                    if key not in self._raw:
                        all_ready = False
                self._ensure_tile(ox, oy, z)

        if not all_ready:
            return None

        # Stitch OSM tiles into one surface covering the full bbox
        osm_tile_px = 256
        n_x = ox1 - ox0 + 1
        n_y = oy1 - oy0 + 1
        stitched_w = n_x * osm_tile_px
        stitched_h = n_y * osm_tile_px
        stitched = pygame.Surface((stitched_w, stitched_h))

        for row_i, oy in enumerate(range(oy0, oy1 + 1)):
            for col_i, ox in enumerate(range(ox0, ox1 + 1)):
                with self._lock:
                    raw = self._raw.get((ox, oy, z))
                if raw is None:
                    return None
                s = self._raw_to_surf(raw)
                if s is None:
                    return None
                stitched.blit(s, (col_i * osm_tile_px, row_i * osm_tile_px))

        # Compute pixel sub-rect within stitched image that matches our editor tile bbox
        # Top-left of stitched image = top-left of OSM tile (ox0, oy0)
        tl_lat, tl_lon = osm_tile2deg(ox0, oy0, z)
        br_lat, br_lon = osm_tile2deg(ox1 + 1, oy1 + 1, z)

        total_lat_span = tl_lat - br_lat    # positive (top > bottom)
        total_lon_span = br_lon - tl_lon    # positive

        # Our bbox within the stitched image
        crop_x0 = int((min_lon - tl_lon) / total_lon_span * stitched_w)
        crop_y0 = int((tl_lat - max_lat) / total_lat_span * stitched_h)
        crop_x1 = int((max_lon - tl_lon) / total_lon_span * stitched_w)
        crop_y1 = int((tl_lat - min_lat) / total_lat_span * stitched_h)

        crop_w = max(1, crop_x1 - crop_x0)
        crop_h = max(1, crop_y1 - crop_y0)
        crop_rect = pygame.Rect(crop_x0, crop_y0, crop_w, crop_h)

        cropped = pygame.Surface((crop_w, crop_h))
        cropped.blit(stitched, (0, 0), crop_rect)
        return cropped

    def invalidate(self, tx: int = None, ty: int = None, zoom: int = None):
        """Invalidate stitched surface cache (call when zoom changes)."""
        if tx is None:
            self._surf_cache.clear()
        else:
            z = zoom or self.zoom
            self._surf_cache.pop((tx, ty, z), None)

    def draw(self, screen: pygame.Surface, editor,
             content_top: int, get_bounds_fn):
        """
        Render OSM overlay for all visible editor tiles.
        editor exposes: tiles, min_x, max_x, min_y, max_y,
                        tile_size, zoom, pan_x, pan_y, tile_screen_pos
        """
        if not self.enabled:
            return
        w, h = screen.get_size()
        ts    = editor.tile_size * editor.zoom

        for tile in list(editor.tiles.values()):
            sx, sy = editor.tile_screen_pos(tile.x, tile.y)
            disp   = int(ts)
            if sx > w or sy > h or sx + disp < 0 or sy + disp < content_top:
                continue

            cache_key = (tile.x, tile.y, self.zoom)
            surf = self._surf_cache.get(cache_key)

            if surf is None:
                built = self._build_editor_surf(
                    tile.x, tile.y, get_bounds_fn, w, h)
                if built is not None:
                    self._surf_cache[cache_key] = built
                    surf = built

            if surf is None:
                # Draw a subtle "loading" placeholder
                if disp > 30:
                    r = pygame.Rect(int(sx)+2, int(sy)+2, disp-4, disp-4)
                    pygame.draw.rect(screen, (20, 35, 50), r, 1)
                continue

            scaled = pygame.transform.scale(surf, (disp, disp))
            scaled.set_alpha(self.opacity)
            screen.blit(scaled, (int(sx), int(sy)))

