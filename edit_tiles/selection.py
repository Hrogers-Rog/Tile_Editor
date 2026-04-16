"""edit_tiles.selection — SelectionBuffer, Clipboard, rasterise_polygon."""
import math
import numpy as np
from .constants import TILE_STRIDE


class SelectionBuffer:
    """
    A rectangular region of world pixels that is selected.
    Stores r0,c0 (top-left) and r1,c1 (bottom-right inclusive) in world-pixel space.
    The active mask is a bool array of shape (r1-r0+1, c1-c0+1).
    """
    def __init__(self, r0: int, c0: int, r1: int, c1: int, mask: np.ndarray | None = None):
        self.r0 = r0; self.c0 = c0
        self.r1 = r1; self.c1 = c1
        h = r1 - r0 + 1
        w = c1 - c0 + 1
        if mask is not None:
            self.mask = mask.astype(bool)
        else:
            self.mask = np.ones((h, w), dtype=bool)

    @property
    def h(self): return self.r1 - self.r0 + 1
    @property
    def w(self): return self.c1 - self.c0 + 1

    def contains_wp(self, wr: int, wc: int) -> bool:
        if not (self.r0 <= wr <= self.r1 and self.c0 <= wc <= self.c1):
            return False
        return bool(self.mask[wr - self.r0, wc - self.c0])

    def tile_keys_covered(self, min_x: int, max_y: int) -> list:
        """Return tile (tx,ty) pairs that overlap this selection."""
        tx0 = self.c0 // TILE_STRIDE + min_x - 1  # extra margin
        tx1 = self.c1 // TILE_STRIDE + min_x + 1
        ty0 = max_y - self.r1 // TILE_STRIDE - 1
        ty1 = max_y - self.r0 // TILE_STRIDE + 1
        return [(tx, ty) for tx in range(tx0, tx1+1) for ty in range(ty0, ty1+1)]

    def iter_pixels(self, min_x: int, max_y: int, res: int = 513):
        """
        Yield (tile_key, flat_idx) for every selected pixel across all tiles.
        """
        for wr in range(self.r0, self.r1 + 1):
            for wc in range(self.c0, self.c1 + 1):
                if not self.mask[wr - self.r0, wc - self.c0]:
                    continue
                tx = wc // TILE_STRIDE + min_x
                ty = max_y - wr // TILE_STRIDE
                lr = wr - (max_y - ty) * TILE_STRIDE
                lc = wc - (tx - min_x) * TILE_STRIDE
                lr = min(lr, res - 1); lc = min(lc, res - 1)
                yield f'{tx},{ty}', lr * res + lc


# =========================
# Clipboard
# =========================
class Clipboard:
    """
    Stores copied terrain data in world-pixel space.
    h16, veg, water arrays of shape (h, w) — same grid as SelectionBuffer.
    """
    def __init__(self, h: int, w: int,
                 h16:   np.ndarray,
                 veg:   np.ndarray,
                 water: np.ndarray):
        self.h     = h
        self.w     = w
        self.h16   = h16.copy().astype(np.float32)
        self.veg   = veg.copy().astype(np.uint8)
        self.water = water.copy().astype(bool)


def rasterise_polygon(pts_rc: list, h: int, w: int) -> np.ndarray:
    """
    Scanline-fill a polygon given as (row, col) float pairs → bool mask (h, w).
    """
    mask = np.zeros((h, w), dtype=bool)
    if len(pts_rc) < 3:
        return mask
    pts = [(float(r), float(c)) for r, c in pts_rc]
    n   = len(pts)
    r_min = max(0, int(math.floor(min(p[0] for p in pts))))
    r_max = min(h - 1, int(math.ceil(max(p[0] for p in pts))))
    for row in range(r_min, r_max + 1):
        xs = []
        for i in range(n):
            r0, c0 = pts[i]; r1, c1 = pts[(i + 1) % n]
            if r0 == r1: continue
            if min(r0, r1) <= row < max(r0, r1):
                xs.append(c0 + (row - r0) / (r1 - r0) * (c1 - c0))
        xs.sort()
        for i in range(0, len(xs) - 1, 2):
            c0i = max(0, int(math.floor(xs[i])))
            c1i = min(w - 1, int(math.ceil(xs[i + 1])))
            mask[row, c0i:c1i + 1] = True
    return mask
