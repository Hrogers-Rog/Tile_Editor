"""edit_tiles.terrain — Tile data class, coordinate helpers, brush math."""
import math
import os
import shutil
import struct
import time
from pathlib import Path

import numpy as np
from PIL import Image
import pygame

from .constants import (HEIGHT_MIN_M, HEIGHT_MAX_M, OVERVIEW_RES, DETAIL_RES,
                         DETAIL_ZOOM, MISSING_COLOR, TILE_STRIDE)
from .generate import render_tile


# World-pixel coordinate helpers
TILE_STRIDE = 512   # world pixels per tile (not 513 — the overlap pixel is shared)


MAX_TILE_EDITOR_BACKUPS = 3


def _prune_tile_editor_backups(path: Path) -> None:
    prefix = path.name + ".tile-editor-backup-"
    backups = sorted(
        (candidate for candidate in path.parent.iterdir()
         if candidate.is_file() and candidate.name.startswith(prefix)),
        key=lambda candidate: candidate.stat().st_mtime,
        reverse=True,
    )
    for stale in backups[MAX_TILE_EDITOR_BACKUPS:]:
        try:
            stale.unlink()
        except OSError:
            pass


def tile_to_wp(tx: int, ty: int, max_y: int) -> tuple:
    """Top-left world-pixel (row, col) of tile (tx, ty)."""
    return (max_y - ty) * TILE_STRIDE, tx * TILE_STRIDE


def wp_to_tile_local(wp_row: int, wp_col: int, tx: int, ty: int,
                     max_y: int, res: int = 513) -> tuple:
    """World-pixel → (local_row, local_col) within tile (tx,ty)."""
    origin_r, origin_c = tile_to_wp(tx, ty, max_y)
    lr = int(np.clip(wp_row - origin_r, 0, res - 1))
    lc = int(np.clip(wp_col - origin_c, 0, res - 1))
    return lr, lc



# Tile class
class Tile:
    def __init__(self, x, y, r_arr, g_arr, a_arr, full_w, path=None):
        self.x = x
        self.y = y
        self.r = r_arr.copy()
        self.g = g_arr.copy()
        self.a = a_arr.copy()
        self.full_w = full_w
        self.path = path          # source file path for saving
        self.dirty = False        # modified since last save
        self._backup_path = None

        # Float32 height buffer — authoritative for painting.
        # r/g are kept in sync and used for rendering/saving.
        self.h16f = (r_arr.astype(np.float32) * 256.0
                     + g_arr.astype(np.float32))   # range [0, 65535]

        self._recalc_stats()

        self.surf_overview = None
        self.surf_detail   = None
        self._mode_cached  = None
        self._hs_cached    = None
        self._scaled_overview = None
        self._scaled_size = None
        self._scaled_mode_cached = None
        self._scaled_hs_cached = None

    def _recalc_stats(self):
        h16 = (self.r.astype(np.uint32) * 256 + self.g.astype(np.uint32))
        self.min_m = float(h16.min()) / 65535 * (HEIGHT_MAX_M - HEIGHT_MIN_M) + HEIGHT_MIN_M
        self.max_m = float(h16.max()) / 65535 * (HEIGHT_MAX_M - HEIGHT_MIN_M) + HEIGHT_MIN_M
        self.avg_m = float(h16.mean()) / 65535 * (HEIGHT_MAX_M - HEIGHT_MIN_M) + HEIGHT_MIN_M
        veg = (self.a >> 4) & 0x7
        self.presets = [int((veg == i).sum()) for i in range(8)]
        self.dom_preset = int(np.argmax(self.presets))
        self.water_pct = float(((self.a >> 7) & 1).mean() * 100)

    def get_overview(self, mode, do_hillshade):
        if (self.surf_overview is None
                or self._mode_cached != mode
                or self._hs_cached != do_hillshade):
            self.surf_overview = render_tile(self.r, self.g, self.a, OVERVIEW_RES, mode, do_hillshade)
            self._mode_cached = mode
            self._hs_cached   = do_hillshade
        return self.surf_overview

    def peek_overview(self, mode, do_hillshade):
        if (self.surf_overview is None
                or self._mode_cached != mode
                or self._hs_cached != do_hillshade):
            return None
        return self.surf_overview

    def get_scaled_overview(self, mode, do_hillshade, size, allow_render=True):
        base = (self.get_overview(mode, do_hillshade)
                if allow_render else
                self.peek_overview(mode, do_hillshade))
        if base is None:
            return None
        size = max(1, int(size))
        if base.get_width() == size and base.get_height() == size:
            return base
        if (self._scaled_overview is None
                or self._scaled_size != size
                or self._scaled_mode_cached != mode
                or self._scaled_hs_cached != do_hillshade):
            self._scaled_overview = pygame.transform.scale(base, (size, size))
            self._scaled_size = size
            self._scaled_mode_cached = mode
            self._scaled_hs_cached = do_hillshade
        return self._scaled_overview

    def invalidate(self):
        self.surf_overview = None
        self.surf_detail   = None
        self._mode_cached  = None
        self._hs_cached    = None
        self._scaled_overview = None
        self._scaled_size = None
        self._scaled_mode_cached = None
        self._scaled_hs_cached = None

    def write_copy(self, path):
        """Write current tile pixels to ``path`` without changing dirty state."""
        path = Path(path)
        res = self.full_w
        r2d = self.r.reshape(res, res)
        g2d = self.g.reshape(res, res)
        b2d = np.zeros((res, res), dtype=np.uint8)
        a2d = self.a.reshape(res, res)
        rgba = np.stack([r2d, g2d, b2d, a2d], axis=2)
        img = Image.fromarray(rgba, 'RGBA')
        img.save(path, format='PNG')
        return True

    def save(self):
        """Write modified data back to source PNG."""
        if self.path is None:
            print(f"Tile ({self.x},{self.y}) has no path, cannot save.")
            return False
        path = Path(self.path)
        if self._backup_path is None and path.exists():
            backup = Path(
                str(path)
                + ".tile-editor-backup-"
                + time.strftime("%Y%m%d-%H%M%S")
            )
            shutil.copy2(path, backup)
            self._backup_path = backup
            _prune_tile_editor_backups(path)
        temporary = Path(str(path) + ".tile-editor.tmp")
        try:
            self.write_copy(temporary)
            os.replace(temporary, path)
        finally:
            if temporary.exists():
                temporary.unlink()
        self.dirty = False
        print(f"Saved tile ({self.x},{self.y}) -> {self.path}")
        return True




# Tile loader
def load_tile(path: Path):
    m = path.name.replace('tile_', '').replace('.data', '')
    parts = m.split('_')
    if len(parts) != 2:
        return None
    try:
        tx, ty = int(parts[0]), int(parts[1])
    except ValueError:
        return None
    try:
        img = Image.open(path).convert('RGBA')
        arr = np.array(img)
        h, w = arr.shape[:2]
        res = min(h, w)
        r = arr[:, :, 0].ravel()[:res*res].reshape(res, res).ravel()
        g = arr[:, :, 1].ravel()[:res*res].reshape(res, res).ravel()
        a = arr[:, :, 3].ravel()[:res*res].reshape(res, res).ravel()
        return Tile(tx, ty, r, g, a, res, path=path)
    except Exception as e:
        print(f"Failed to load {path.name}: {e}")
        return None


# =========================
# Brush helpers


# Brush helpers
def brush_mask(radius: int) -> np.ndarray:
    """Return a flat boolean mask of pixels within radius of center."""
    d = 2 * radius + 1
    cy = cx = radius
    ys, xs = np.mgrid[0:d, 0:d]
    return ((ys - cy)**2 + (xs - cx)**2) <= radius**2


def brush_falloff(radius: int) -> np.ndarray:
    """
    Smooth quintic falloff: 1 at centre → 0 at edge, no hard clip ring.
    """
    d = 2 * radius + 1
    cy = cx = radius
    ys, xs = np.mgrid[0:d, 0:d]
    dist = np.sqrt((ys - cy) ** 2.0 + (xs - cx) ** 2.0).astype(np.float32)
    t = np.clip(dist / max(radius, 1), 0.0, 1.0)
    falloff = (1.0 - t) ** 3 * (1.0 + 3.0 * t + 6.0 * t ** 2)
    return falloff.astype(np.float32)


def _perlin_grid(shape, scale):
    """
    Fast gradient noise (value-noise variant). Returns float32 in ~[-1, 1].
    """
    h, w = shape
    rng = np.random.default_rng()
    gh = max(2, math.ceil(h / scale) + 2)
    gw = max(2, math.ceil(w / scale) + 2)
    angles = rng.uniform(0, 2 * math.pi, (gh, gw)).astype(np.float32)
    gx_g = np.cos(angles)   # gradient x component
    gy_g = np.sin(angles)   # gradient y component

    # Pixel fractional position in cell space
    rows = np.arange(h, dtype=np.float32) / scale   # shape (h,)
    cols = np.arange(w, dtype=np.float32) / scale   # shape (w,)
    r0 = np.floor(rows).astype(np.int32)             # shape (h,)
    c0 = np.floor(cols).astype(np.int32)             # shape (w,)
    rf = rows - r0                                    # shape (h,)  in [0,1)
    cf = cols - c0                                    # shape (w,)  in [0,1)

    # Smooth step
    u = rf * rf * (3 - 2 * rf)   # shape (h,)
    v = cf * cf * (3 - 2 * cf)   # shape (w,)

    # Clamp grid indices
    r0c = np.clip(r0,   0, gh-1)   # (h,)
    r1c = np.clip(r0+1, 0, gh-1)   # (h,)
    c0c = np.clip(c0,   0, gw-1)   # (w,)
    c1c = np.clip(c0+1, 0, gw-1)   # (w,)

    # Gradient vectors at four corners — broadcast to (h, w)
    gx00 = gx_g[r0c, :][:, c0c]   # (h, w)
    gy00 = gy_g[r0c, :][:, c0c]
    gx10 = gx_g[r1c, :][:, c0c]
    gy10 = gy_g[r1c, :][:, c0c]
    gx01 = gx_g[r0c, :][:, c1c]
    gy01 = gy_g[r0c, :][:, c1c]
    gx11 = gx_g[r1c, :][:, c1c]
    gy11 = gy_g[r1c, :][:, c1c]

    # Offset vectors from each corner to pixel (broadcast rf/cf to (h,w))
    rf2 = rf[:, np.newaxis]          # (h,1)
    cf2 = cf[np.newaxis, :]          # (1,w)

    n00 = gx00 * cf2       + gy00 * rf2
    n10 = gx10 * cf2       + gy10 * (rf2 - 1)
    n01 = gx01 * (cf2 - 1) + gy01 * rf2
    n11 = gx11 * (cf2 - 1) + gy11 * (rf2 - 1)

    u2 = u[:, np.newaxis]   # (h,1)
    lerp = lambda a, b, t: a + t * (b - a)
    return lerp(lerp(n00, n10, u2), lerp(n01, n11, u2), v[np.newaxis, :]).astype(np.float32)




# Noise brush
def noise_brush(rows, cols, res, noise_scale, seed=None):
    """
    Return noise values in [-1,1] for the given pixel coords in a res×res tile.
    Uses fractional Brownian motion (4 octaves) for natural terrain texture.
    """
    # Build a noise field covering the tile, sample at requested coords
    rng = np.random.default_rng(seed)
    h16_shape = (res, res)
    result = np.zeros(h16_shape, dtype=np.float32)
    amp, freq = 1.0, 1.0
    for _ in range(4):
        sc = max(4, int(noise_scale / freq))
        layer = _perlin_grid(h16_shape, sc)
        result += amp * layer
        amp  *= 0.5
        freq *= 2.0
    result /= (1 + 0.5 + 0.25 + 0.125)   # normalise to [-1,1]
    return result[rows, cols]


# =========================
# Undo record
# =========================


# UndoRecord
class UndoRecord:
    """Snapshot of pixels that were changed in one brush stroke."""
    def __init__(self, tile_key, pixel_indices, old_r, old_g, old_a):
        self.tile_key     = tile_key
        self.pixel_indices = pixel_indices
        self.old_r = old_r.copy()
        self.old_g = old_g.copy()
        self.old_a = old_a.copy()


class TileDeleteRecord:
    """Files moved aside by one recoverable multi-tile cleanup operation."""

    def __init__(self, entries):
        self.entries = list(entries)
