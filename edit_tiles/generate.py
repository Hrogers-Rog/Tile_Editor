"""edit_tiles.generate — Mapbox terrain tile generation."""
import json
import math
import os
import re
import time
import struct
from pathlib import Path

import numpy as np
import pygame
from PIL import Image

from .constants import (HEIGHT_MIN_M, HEIGHT_MAX_M, GEN_MAPBOX_ZOOM,
                         GEN_MAPBOX_TILE_SZ, GEN_HEIGHT_RES, GEN_TILE_DIM_M,
                         GEN_ORIGIN_LAT, GEN_ORIGIN_LON, GEN_ORIGIN_E_BIAS,
                         GEN_ORIGIN_N_BIAS, GEN_HEIGHT_MIN_G, GEN_HEIGHT_MAX_G,
                         GEN_OFFSET_EAST_X, GEN_OFFSET_WEST_X, GEN_OFFSET_MAX_M,
                         GEN_NLCD_BLUR, GEN_NLCD_URL, GEN_ALL_VEG, GEN_NLCD_COLORS,
                         OVERVIEW_RES, VEG_COLORS)
from .osm import osm_deg2tile, osm_tile2deg


class MapboxAuthError(RuntimeError):
    """Raised when Mapbox rejects the configured token."""


def clean_mapbox_token(token) -> str:
    """Strip clipboard/control junk that can invalidate an otherwise valid token."""
    if token is None:
        return ""
    cleaned = str(token).replace("\x00", "")
    cleaned = "".join(ch for ch in cleaned if ch.isprintable())
    return cleaned.strip()


def _format_tile_coord(value: int) -> str:
    value = int(value)
    if value < 0:
        return f"-{abs(value):03d}"
    return f"{value:03d}"


def tile_data_filename(gx: int, gy: int) -> str:
    return f"tile_{_format_tile_coord(gx)}_{_format_tile_coord(gy)}.data"


def _gen_f32(x):
    return struct.unpack("!f", struct.pack("!f", float(x)))[0]

def _gen_add_meters(lat, lon, north, east):
    lat, lon  = _gen_f32(lat),   _gen_f32(lon)
    north, east = _gen_f32(north), _gen_f32(east)
    lat_out = _gen_f32(lat + _gen_f32(north / _gen_f32(111111.0)))
    cos_arg = _gen_f32(_gen_f32(0.017453292) * lat)
    denom   = _gen_f32(_gen_f32(111111.0) * _gen_f32(math.cos(cos_arg)))
    lon_out = _gen_f32(lon + _gen_f32(east / denom))
    return lat_out, lon_out

def _gen_tile_bounds(gx, gy, origin_lat=GEN_ORIGIN_LAT,
                     origin_lon=GEN_ORIGIN_LON,
                     tile_dimension_m=GEN_TILE_DIM_M,
                     origin_e_bias=GEN_ORIGIN_E_BIAS,
                     origin_n_bias=GEN_ORIGIN_N_BIAS):
    """Return a game tile's lat/lon bounds for the supplied map georeference."""
    mn = _gen_add_meters(origin_lat, origin_lon,
         tile_dimension_m * gy + origin_n_bias,
         tile_dimension_m * gx + origin_e_bias)
    mx = _gen_add_meters(origin_lat, origin_lon,
         tile_dimension_m * (gy+1) + origin_n_bias,
         tile_dimension_m * (gx+1) + origin_e_bias)
    return mn, mx

def _gen_lon_to_wpx(lon, zoom):
    return (lon + 180.0) / 360.0 * GEN_MAPBOX_TILE_SZ * (2 ** zoom)

def _gen_lat_to_wpy(lat, zoom):
    lr = math.radians(lat)
    return (1.0 - math.log(math.tan(math.pi/4 + lr/2)) / math.pi) / 2.0 \
           * GEN_MAPBOX_TILE_SZ * (2 ** zoom)

def _gen_fetch(url, params=None, retries=3, delay=2.0):
    import requests, time
    last = None
    for i in range(retries):
        try:
            r = requests.get(url, params=params, timeout=30)
            if r.status_code in (401, 403):
                raise MapboxAuthError(
                    "Mapbox token unauthorized; re-paste it to remove hidden characters"
                )
            r.raise_for_status()
            return r
        except MapboxAuthError:
            raise
        except Exception as e:
            last = e
            if i < retries - 1:
                time.sleep(delay)
    raise last

def _gen_mosaic(left_px, top_px, right_px, bottom_px, token):
    import io
    token = clean_mapbox_token(token)
    if not token:
        raise MapboxAuthError("Mapbox token missing")
    sz  = GEN_MAPBOX_TILE_SZ
    tx0 = math.floor(left_px)  // sz;  ty0 = math.floor(top_px)    // sz
    tx1 = math.ceil(right_px)  // sz;  ty1 = math.ceil(bottom_px)  // sz
    mosaic = Image.new("RGB", ((tx1-tx0+1)*sz, (ty1-ty0+1)*sz))
    for ty in range(ty0, ty1+1):
        for tx in range(tx0, tx1+1):
            url  = f"https://api.mapbox.com/v4/mapbox.terrain-rgb/{GEN_MAPBOX_ZOOM}/{tx}/{ty}.pngraw"
            resp = _gen_fetch(url, {"access_token": token})
            img  = Image.open(io.BytesIO(resp.content)).convert("RGB")
            mosaic.paste(img, ((tx-tx0)*sz, (ty-ty0)*sz))
    arr = np.array(mosaic, dtype=np.float32)
    return (-10000.0 + 0.1*(arr[:,:,0]*65536 + arr[:,:,1]*256 + arr[:,:,2]),
            tx0*sz, ty0*sz)

def _uses_stock_height_correction(origin_lat, origin_lon) -> bool:
    """Return whether the old Bushnell/Whittier elevation correction applies."""
    return (
        abs(float(origin_lat) - GEN_ORIGIN_LAT) < 0.0001
        and abs(float(origin_lon) - GEN_ORIGIN_LON) < 0.0001
    )


def _gen_sample_heights(gx, gy, token,
                        origin_lat=GEN_ORIGIN_LAT,
                        origin_lon=GEN_ORIGIN_LON,
                        tile_dimension_m=GEN_TILE_DIM_M,
                        origin_e_bias=GEN_ORIGIN_E_BIAS,
                        origin_n_bias=GEN_ORIGIN_N_BIAS):
    from scipy.ndimage import map_coordinates
    (min_lat, min_lon), (max_lat, max_lon) = _gen_tile_bounds(
        gx, gy,
        origin_lat=origin_lat,
        origin_lon=origin_lon,
        tile_dimension_m=tile_dimension_m,
        origin_e_bias=origin_e_bias,
        origin_n_bias=origin_n_bias,
    )
    z   = GEN_MAPBOX_ZOOM
    lx  = _gen_lon_to_wpx(min_lon, z);  rx  = _gen_lon_to_wpx(max_lon, z)
    tpy = _gen_lat_to_wpy(max_lat, z);  bpy = _gen_lat_to_wpy(min_lat, z)
    src, ox, oy = _gen_mosaic(lx, tpy, rx, bpy, token)
    res = GEN_HEIGHT_RES
    ax  = np.arange(res, dtype=np.float64)
    src_x = np.clip((lx-ox) + ax*(rx-lx)/(res-1),  0, src.shape[1]-1.000001)
    src_y = np.clip((tpy-oy) + ax*(bpy-tpy)/(res-1), 0, src.shape[0]-1.000001)
    yy, xx = np.meshgrid(src_y, src_x, indexing='ij')
    sampled = map_coordinates(src, [yy.ravel(), xx.ravel()],
                              order=1, mode='nearest').reshape(res, res)
    # The westward correction compensates for a known stock-map elevation
    # mismatch. It must never leak into a map authored at another origin.
    if _uses_stock_height_correction(origin_lat, origin_lon):
        t = np.clip((float(gx) + ax/(res-1) - GEN_OFFSET_EAST_X) /
                    (GEN_OFFSET_WEST_X - GEN_OFFSET_EAST_X), 0.0, 1.0)
        sampled += (t * GEN_OFFSET_MAX_M).astype(np.float32)[np.newaxis, :]
    return sampled

def _gen_color_to_preset(color):
    if color in GEN_NLCD_COLORS:
        return GEN_NLCD_COLORS[color]
    best, best_d = (0, False), float('inf')
    for ref, val in GEN_NLCD_COLORS.items():
        d = sum((a-b)**2 for a,b in zip(color, ref))
        if d < best_d:
            best_d, best = d, val
    return best

_GEN_NLCD_GUTTER = 48   # extra pixels fetched on each side for better edge blending

def _gen_veg_water(min_lat, min_lon, max_lat, max_lon, blur_sigma):
    import io
    from scipy.ndimage import gaussian_filter
    gutter  = _GEN_NLCD_GUTTER
    out_res = GEN_HEIGHT_RES
    fetch_res = out_res + 2 * gutter
    params = {"SERVICE":"WMS","VERSION":"1.1.1","REQUEST":"GetMap",
              "LAYERS":"mrlc_display:NLCD_2021_Land_Cover_L48",
              "BBOX":f"{min_lon},{min_lat},{max_lon},{max_lat}",
              "WIDTH":str(fetch_res),"HEIGHT":str(fetch_res),
              "SRS":"EPSG:4326","FORMAT":"image/png","STYLES":""}
    resp = _gen_fetch(GEN_NLCD_URL, params)
    nlcd = Image.open(io.BytesIO(resp.content)).convert("RGB")
    arr  = np.array(nlcd.resize((fetch_res, fetch_res), Image.NEAREST))
    veg_raw   = np.zeros((fetch_res, fetch_res), dtype=np.uint8)
    water_raw = np.zeros((fetch_res, fetch_res), dtype=bool)
    colors = set(map(tuple, arr.reshape(-1,3).tolist()))
    for color in colors:
        preset, is_water = _gen_color_to_preset(color)
        mask = np.all(arr == np.array(color, dtype=np.uint8), axis=2)
        veg_raw[mask]   = preset
        water_raw[mask] = is_water
    if blur_sigma and blur_sigma > 0:
        stack = np.stack([(veg_raw==c).astype(np.float32) for c in GEN_ALL_VEG])
        stack = np.stack([gaussian_filter(m, sigma=blur_sigma) for m in stack])
        veg_raw   = np.array(GEN_ALL_VEG, dtype=np.uint8)[np.argmax(stack, axis=0)]
        water_raw = gaussian_filter(water_raw.astype(np.float32), sigma=blur_sigma) >= 0.5
    # Crop gutter
    return veg_raw[gutter:gutter+out_res, gutter:gutter+out_res], \
           water_raw[gutter:gutter+out_res, gutter:gutter+out_res]

def _gen_pack(heights_m, veg_grid, water_grid):
    u16  = np.clip(np.floor((heights_m - GEN_HEIGHT_MIN_G) /
                            (GEN_HEIGHT_MAX_G - GEN_HEIGHT_MIN_G) * 65535.0),
                   0, 65535).astype(np.uint16)
    r_ch = (u16 >> 8).astype(np.uint8)
    g_ch = (u16 & 0xFF).astype(np.uint8)
    h, w = u16.shape
    if veg_grid is not None:
        veg   = veg_grid[:h,:w].astype(np.uint8) & 0x7
        water = water_grid[:h,:w] if water_grid is not None else np.zeros((h,w), dtype=bool)
    else:
        veg   = np.zeros((h,w), dtype=np.uint8)
        water = np.zeros((h,w), dtype=bool)
    a_ch = (water.astype(np.uint8) << 7) | (veg << 4)
    return Image.fromarray(np.stack([r_ch, g_ch, np.zeros_like(r_ch), a_ch], axis=2), "RGBA")

def generate_tile(gx, gy, token, out_dir, use_nlcd=True,
                  nlcd_blur=GEN_NLCD_BLUR, veg_override=None, progress_cb=None,
                  origin_lat=GEN_ORIGIN_LAT,
                  origin_lon=GEN_ORIGIN_LON,
                  tile_dimension_m=GEN_TILE_DIM_M,
                  origin_e_bias=GEN_ORIGIN_E_BIAS,
                  origin_n_bias=GEN_ORIGIN_N_BIAS):
    """Generate one tile, save to out_dir. Returns Path on success."""
    token = clean_mapbox_token(token)
    def prog(msg):
        if progress_cb: progress_cb(msg)
    prog("fetching elevation…")
    heights = _gen_sample_heights(
        gx, gy, token,
        origin_lat=origin_lat,
        origin_lon=origin_lon,
        tile_dimension_m=tile_dimension_m,
        origin_e_bias=origin_e_bias,
        origin_n_bias=origin_n_bias,
    )
    veg_grid = water_grid = None
    if veg_override is not None:
        veg_grid   = np.full((GEN_HEIGHT_RES, GEN_HEIGHT_RES), veg_override & 0x7, dtype=np.uint8)
        water_grid = np.zeros((GEN_HEIGHT_RES, GEN_HEIGHT_RES), dtype=bool)
    elif use_nlcd:
        try:
            prog("fetching land cover…")
            (min_lat, min_lon), (max_lat, max_lon) = _gen_tile_bounds(
                gx, gy,
                origin_lat=origin_lat,
                origin_lon=origin_lon,
                tile_dimension_m=tile_dimension_m,
                origin_e_bias=origin_e_bias,
                origin_n_bias=origin_n_bias,
            )
            veg_grid, water_grid = _gen_veg_water(min_lat, min_lon, max_lat, max_lon, nlcd_blur)
        except Exception as e:
            prog(f"NLCD failed ({e}), veg=0")
    prog("packing…")
    rgba = _gen_pack(heights, veg_grid, water_grid)
    out_dir_path = Path(out_dir)
    out  = out_dir_path / tile_data_filename(gx, gy)
    rgba.save(out, format="PNG")
    legacy = out_dir_path / f"tile_{gx}_{gy}.data"
    if legacy != out and legacy.exists():
        try:
            legacy.unlink()
        except Exception:
            pass
    prog("done")
    return out


_TILE_DATA_RE = re.compile(r'^tile_(-?\d+)_(-?\d+)\.data$', re.IGNORECASE)


def tile_coordinates_in_folder(folder) -> list[tuple[int, int]]:
    """Return the signed coordinates represented by tile data files."""
    coordinates = set()
    for path in Path(folder).glob('tile_*.data'):
        match = _TILE_DATA_RE.fullmatch(path.name)
        if match:
            coordinates.add((int(match.group(1)), int(match.group(2))))
    return sorted(coordinates)


def sync_map_json_tile_list(folder, *, origin_lat=None, origin_lon=None,
                            tile_dimension_m=None, origin_e_bias=None,
                            origin_n_bias=None):
    """Synchronize Map.json with the tile files in *folder* atomically.

    An existing manifest keeps its georeference. Supplying an origin permits a
    new manifest to be created, which is how the editor scaffolds a new map.
    """
    folder = Path(folder)
    map_path = folder / 'Map.json'
    if map_path.is_file():
        document = json.loads(map_path.read_text(encoding='utf-8-sig'))
        if not isinstance(document, dict):
            raise ValueError(f"Map.json must contain an object: {map_path}")
    else:
        if origin_lat is None or origin_lon is None:
            return None
        document = {}

    origin = document.get('origin')
    if not isinstance(origin, dict):
        if origin_lat is None or origin_lon is None:
            raise ValueError(f"Map.json is missing a valid origin: {map_path}")
        origin = {
            'latitude': float(origin_lat),
            'longitude': float(origin_lon),
        }
        document['origin'] = origin
    if origin_e_bias is not None:
        origin['eastBiasMeters'] = float(origin_e_bias)
    if origin_n_bias is not None:
        origin['northBiasMeters'] = float(origin_n_bias)

    if tile_dimension_m is not None:
        document['tileDimension'] = float(tile_dimension_m)
    elif 'tileDimension' not in document:
        document['tileDimension'] = float(GEN_TILE_DIM_M)
    document['tiles'] = [
        {'x': x, 'y': y}
        for x, y in tile_coordinates_in_folder(folder)
    ]

    folder.mkdir(parents=True, exist_ok=True)
    temporary = map_path.with_name(map_path.name + '.tmp')
    temporary.write_text(json.dumps(document, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, map_path)
    return map_path


def compute_hillshade(heights: np.ndarray) -> np.ndarray:
    h, w = heights.shape
    lx, ly, lz = -0.6, -0.6, 1.0
    ll = math.sqrt(lx*lx + ly*ly + lz*lz)
    lx /= ll; ly /= ll; lz /= ll
    dx = np.zeros((h, w), dtype=np.float32)
    dy = np.zeros((h, w), dtype=np.float32)
    dx[:, 1:-1] = (heights[:, 2:] - heights[:, :-2]) * 20.0
    dy[1:-1, :] = (heights[2:, :] - heights[:-2, :]) * 20.0
    nx, ny, nz = -dx, -dy, np.ones((h, w), dtype=np.float32)
    nlen = np.maximum(np.sqrt(nx*nx + ny*ny + nz*nz), 1e-6)
    dot = (nx/nlen)*lx + (ny/nlen)*ly + (nz/nlen)*lz
    return np.clip(dot, 0, 1)


# =========================
# Tile rendering
# =========================
def render_tile(r_arr, g_arr, a_arr, res, mode, do_hillshade):
    n = res * res
    heights = ((r_arr.astype(np.float32) * 256 + g_arr.astype(np.float32)) / 65535.0).reshape(res, res)
    hs = compute_hillshade(heights).ravel() if do_hillshade else None
    shade = (0.25 + 0.75 * hs) if hs is not None else np.ones(n, dtype=np.float32)
    rgb = np.zeros((n, 3), dtype=np.uint8)

    if mode == 'height':
        rgb[:, 0] = np.round(r_arr.astype(np.float32) * shade).astype(np.uint8)
        rgb[:, 1] = np.round(g_arr.astype(np.float32) * shade).astype(np.uint8)
        rgb[:, 2] = 0
    elif mode == 'veg':
        veg = (a_arr >> 4) & 0x7
        vc = np.array(VEG_COLORS, dtype=np.float32)
        rgb[:, 0] = np.clip(vc[veg, 0] * shade, 0, 255).astype(np.uint8)
        rgb[:, 1] = np.clip(vc[veg, 1] * shade, 0, 255).astype(np.uint8)
        rgb[:, 2] = np.clip(vc[veg, 2] * shade, 0, 255).astype(np.uint8)
    else:  # water
        water = (a_arr >> 7) & 1
        rgb[water == 1] = [0, 100, 220]
        rgb[water == 0] = [18, 22, 30]

    surf = pygame.surfarray.make_surface(rgb.reshape(res, res, 3).transpose(1, 0, 2))
    return surf


# =========================
# Tile data
# =========================
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

        # Float32 height buffer — authoritative for painting.
        # r/g are kept in sync and used for rendering/saving.
        self.h16f = (r_arr.astype(np.float32) * 256.0
                     + g_arr.astype(np.float32))   # range [0, 65535]

        self._recalc_stats()

        self.surf_overview = None
        self.surf_detail   = None
        self._mode_cached  = None
        self._hs_cached    = None

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

    def invalidate(self):
        self.surf_overview = None
        self.surf_detail   = None
        self._mode_cached  = None

    def save(self):
        """Write modified data back to source PNG."""
        if self.path is None:
            print(f"Tile ({self.x},{self.y}) has no path, cannot save.")
            return False
        res = self.full_w
        r2d = self.r.reshape(res, res)
        g2d = self.g.reshape(res, res)
        b2d = np.zeros((res, res), dtype=np.uint8)
        a2d = self.a.reshape(res, res)
        rgba = np.stack([r2d, g2d, b2d, a2d], axis=2)
        img = Image.fromarray(rgba, 'RGBA')
        img.save(self.path)
        self.dirty = False
        print(f"Saved tile ({self.x},{self.y}) → {self.path}")
        return True
