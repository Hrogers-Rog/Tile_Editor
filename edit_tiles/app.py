"""edit_tiles.app — TileEditor application class.

TileEditor inherits mixins for draw sub-methods, event sub-methods,
and bridge integration:

    DrawMixin   (renderer.py)  — _draw_terrain, _draw_track_overlay, etc.
    EventsMixin (events.py)    — _handle_keydown, _handle_mousedown, etc.
    BridgeMixin (bridge.py)    — _init_bridge, _poll_bridge, etc.

draw() and handle_event() are slim routers; the logic lives in the mixins.
"""

import os
import sys
import math
import json
import copy
import heapq
import struct
import argparse
import threading
import collections
import traceback
import shutil
import subprocess
import time
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor, as_completed

import numpy as np
from PIL import Image
import pygame
import pygame.freetype
from .pygame_dialogs import (ask_directory, ask_open_filename,
                             ask_save_filename, ask_string, ask_text, ask_choice_list,
                             ask_integer, ask_yes_no)
from .version import __version__

try:
    from railroader_bridge import RailroaderBridge, preferred_railroader_path
    _BRIDGE_AVAILABLE = True
except ImportError:
    _BRIDGE_AVAILABLE = False
    preferred_railroader_path = None

try:
    from mod_project import (ModProject, LAYER_BASE, LAYER_GRAPH, LAYER_TOWN,
                              LAYER_RIVERS, LAYER_MIGRATION, LAYER_OTHER,
                              ProgressionProject, Layer, _save_json,
                              normalize_track_gauge,
                              _bezier_control_points, _bezier_for_nodes,
                              generate_curve, generate_parallel_tracks,
                              node_flatten, node_reverse, node_set_rotY,
                              segment_set_props,
                              span_set, span_delete, merge_nodes, split_node,
                              smooth_grade,
                              apply_grade_from_start,
                              build_vertical_alignment,
                              dense_vertical_alignment_stations,
                              straighten_chain_xz,
                              spliney_set_point, spliney_add_road, spliney_delete,
                              next_spliney_id, spliney_insert_point,
                              spliney_delete_point, scenery_set, scenery_delete,
                              create_trestle_from_segment,
                              fit_trestle_to_segment, next_scenery_id,
                              generate_turnout, turnout_leg_pose,
                              turnout_radius_for_chord,
                              generate_wye,
                              move_group,
                              mandela_set, mandela_delete, next_mandela_id,
                              validate_mod, export_clean_zip)
    _MOD_AVAILABLE = True
except ImportError:
    _MOD_AVAILABLE = False

from .constants import *  # noqa: F401,F403
from .osm import OsmOverlay
from .terrain import (Tile, UndoRecord, TileDeleteRecord, load_tile, brush_mask, brush_falloff,
                       noise_brush, tile_to_wp, wp_to_tile_local)
from .selection import SelectionBuffer, Clipboard, rasterise_polygon
from .generate import (generate_tile, compute_hillshade, render_tile,
                       _gen_tile_bounds, MapboxAuthError, clean_mapbox_token,
                       sync_map_json_tile_list)
from .bridge import BridgeMixin
from .renderer import DrawMixin
from .events import EventsMixin
from .alignment import (
    polyline_length as alignment_polyline_length,
    deviation_samples,
    fit_arc_to_chain,
    local_radius_samples,
)


class TileEditor(DrawMixin, EventsMixin, BridgeMixin):
    """Main application: terrain editor + mod project editor.

    Mixins provide:
      DrawMixin   — draw sub-methods (_draw_terrain, _draw_track_overlay, ...)
      EventsMixin — event sub-methods (_handle_keydown, _handle_mousedown, ...)
      BridgeMixin — bridge connection (_init_bridge, _poll_bridge, ...)
    """

    def __init__(self, folders=None):
        print("[__init__] pygame.init...", flush=True)
        os.environ.setdefault("SDL_VIDEO_CENTERED", "1")
        pygame.init()
        pygame.freetype.init()
        display_info = pygame.display.Info()
        display_w = int(getattr(display_info, 'current_w', 0) or WIN_W)
        display_h = int(getattr(display_info, 'current_h', 0) or WIN_H)
        window_w = min(WIN_W, max(800, display_w - 120))
        window_h = min(WIN_H, max(600, display_h - 140))
        window_w = min(window_w, display_w)
        window_h = min(window_h, display_h)
        self.screen = pygame.display.set_mode((window_w, window_h), pygame.RESIZABLE)
        pygame.display.set_caption(f"Railroader Tile Editor v{__version__}")
        self.folders = folders if isinstance(folders, list) else ([folders] if folders else [])
        self.track_graph_path: Path | None = None
        self._mod_source_kind: str | None = None
        self._mod_source_path: Path | None = None
        self._mod_source_paths: list[Path] = []
        self.ui_font_name = 'Consolas'
        self.ui_scale_steps = [0.85, 0.95, 1.0, 1.1, 1.2, 1.25]
        self.ui_scale = 1.0
        self.live_mod_apply = False
        self._ui_settings_file = Path.home() / ".edit_tiles_ui.json"
        self._load_ui_settings()
        self._apply_ui_scale(self.ui_scale, persist=False, announce=False)

        self.tiles = {}
        self.min_x = self.max_x = self.min_y = self.max_y = 0

        self.mode      = 'height'
        self.hillshade = True
        self.pan_x = 0.0
        self.pan_y = 0.0
        self.zoom  = 1.0
        self.tile_size = 64

        self.dragging     = False
        self.last_mouse   = (0, 0)
        self._suspend_canvas_drag = False
        self.hover_tile   = None
        self.loading      = False
        self.load_progress = (0, 0)
        self._tile_cache_visible_pending = 0
        self._pending_bridge_reload_paths: set[str] = set()

        self.modes       = ['height', 'veg', 'water']
        self.mode_labels = {'height': 'H Heightmap', 'veg': 'V Vegetation', 'water': 'W Water'}

        # Track graph overlay (preserved from viewer)
        self.track_nodes    = {}
        self.track_segments = []
        self.show_tracks    = False
        self.show_nodes     = False
        self.show_elev_colors  = False   # color nodes by elevation
        self.show_grade_labels = False   # draw grade % on segments
        self.track_node_list = []
        self.track_node_elevs = {}
        self.track_segment_meta = []
        self.track_segment_points = {}
        self.track_colors   = {
            'Mainline':   (255, 230,   0),
            'Branch':     (255, 120,   0),
            'Industrial': (200,  80, 255),
        }
        self.track_default_color = (220, 220, 100)
        self.UNITY_TILE = 500

        # ---- Editor state ----
        self.edit_mode        = False
        self.tile_delete_mode = False
        self.tile_delete_selection: set[str] = set()
        self.tile_delete_dragging = False
        self.tile_delete_drag_start: tuple[int, int] | None = None
        self.tile_delete_drag_end: tuple[int, int] | None = None
        self.tile_delete_drag_operation = 'replace'
        self.tile_delete_confirm = False
        self._tile_cleanup_rects = []
        self.painting     = False
        self.brush_radius   = 20             # radius in SCREEN pixels (zoom-independent)
        self.brush_strength = 0.008          # fraction of full 16-bit range per paint step
        self.brush_mode     = 'raise'        # 'raise' | 'flatten' | 'paint' | 'smooth' | 'noise'
        self.flatten_target: float | None = None
        self.paint_target:   float | None = None
        self.noise_scale     = 64            # noise feature size in tile-pixels
        # Height clamp
        self.clamp_floor_m: float | None = None
        self.clamp_ceil_m:  float | None = None
        self.height_raise  = True
        self.veg_preset    = 0
        self.water_paint   = True
        self.undo_stack: collections.deque = collections.deque(maxlen=MAX_UNDO)
        self._stroke_record: dict | None = None
        self._last_paint_pos: tuple | None = None
        # Live cursor readout
        self.cursor_height_m: float | None = None
        self.show_tile_info: bool = True
        self.cursor_veg: int | None = None
        self.cursor_water: bool | None = None
        self.status_msg   = ""
        self.status_timer = 0
        # Diff mode
        self.diff_mode    = False
        # OSM overlay
        self.osm          = OsmOverlay()
        self.osm_clear_cache_confirm = False
        self.map_origin_lat = GEN_ORIGIN_LAT
        self.map_origin_lon = GEN_ORIGIN_LON
        self.map_tile_dimension_m = GEN_TILE_DIM_M
        self.map_origin_e_bias = GEN_ORIGIN_E_BIAS
        self.map_origin_n_bias = GEN_ORIGIN_N_BIAS
        self.map_georef_path: Path | None = None
        # Help overlay
        self.show_help    = False
        # --- Live bridge ---
        self.bridge = None
        self.bridge_connected = False
        self.bridge_cars = []
        self._bridge_nodes_raw:    dict = {}
        self._bridge_segments_raw: dict = {}
        self._bridge_pending_state = None
        self._bridge_pending_editor_commands: list[dict] = []
        self._bridge_lock = threading.Lock()
        self._bridge_last_fingerprint = None  # skip bezier rebuild if unchanged
        self._last_editor_state_publish = 0.0
        self._game_graph_sync_locked = False
        self._game_terrain_sync_locked = False
        self._game_sync_session_started_at = int(
            time.time() * 1000)
        if _BRIDGE_AVAILABLE:
            self._init_bridge()
        # --- Mod project ---
        self.mod_project: 'ModProject | None' = None
        self.mod_panel = False
        self.mod_layer_scroll = 0
        # --- Progression / area editors ---
        self.prog_project: 'ProgressionProject | None' = None
        self.prog_panel  = False            # progression editor open
        self.area_panel  = False            # area/town editor open
        self.prog_sel_section: str | None = None   # selected section id
        self.prog_sel_feature: str | None = None   # selected feature id
        self.area_sel_id:      str | None = None   # selected area id
        self.area_sel_industry:str | None = None   # selected industry id
        self.area_sel_component: str | None = None # selected component id
        self.prog_scroll = 0
        self.area_scroll = 0
        self._area_dirty_layers: set[int] = set()
        # --- Geometry tools panel ---
        self.geo_panel   = False
        self.geo_mode    = 'curve'      # 'curve' | 'parallel'
        # Curve generator params
        self.geo_radius      = 150.0
        self.geo_degrees     = 90.0
        self.geo_height      = 0.0
        self.geo_direction   = 'left'   # 'left' | 'right'
        self.geo_n_segs      = 0        # 0 = auto
        self.geo_track_class = 'Mainline'
        self.geo_style       = 'Standard'
        self.geo_speed       = 45
        # RailLoader-compatible metadata consumed by FUSE Narrow Gauge.
        # New track tools and direct node connections inherit this value.
        self.geo_gauge       = 'Standard'
        # Parallel track params
        self.geo_separation  = 5.0
        self.geo_n_tracks    = 1
        self.geo_side        = 'right'  # 'right' | 'left' | 'both'
        # Preview nodes/segments (shown before committing)
        self.geo_preview: list = []     # [(nodes,segs), ...] from last generate
        self.geo_preview_meta: dict = {}
        self._geo_input_focus: str | None = None  # which numeric field is active
        self._geo_input_buf: str = ''
        self._geo_scroll_by_mode: dict[str, int] = {}
        self._geo_scroll_max_by_mode: dict[str, int] = {}
        self._geo_scroll_max: int = 0
        self._geo_scroll_view_rect = None
        self._geo_node_place_mode: bool = False
        self._geo_guide_place_mode: bool = False
        self.alignment_min_radius_m: float = 60.0
        self.alignment_guide_points: list = []
        self.geo_spline_style: str = 'Road'
        self.geo_spline_width: float = 0.0   # 0 = infer from existing style defaults
        self.alignment_fit_stats: dict = {}
        self.geo_piece_type: str = 'Straight'
        self.geo_piece_length: float = 60.0
        self.geo_piece_start_node_id: str | None = None
        self.geo_piece_start_pose: dict | None = None
        self.geo_piece_chain: list = []
        # Grade smoother state
        self.grade_chain: list = []          # ordered node IDs in chain
        self.grade_fix_first: bool = False
        self.grade_fix_last:  bool = False
        self.grade_target_pct: float = 0.0  # target grade % for Apply Grade %
        self.grade_start_pct: float = 0.0
        self.grade_end_pct: float = 0.0
        self.grade_transition_in_m: float = 100.0
        self.grade_transition_out_m: float = 100.0
        self.grade_transition_preview_active: bool = False
        self.place_y_lock: bool = False      # lock placement Y to fixed value
        self.place_y_value: float = 0.0     # the fixed Y to use when lock is on
        self.place_y_inherit: bool = False  # inherit Y from last placed node
        # Span editor state
        self.span_panel: bool = False
        self.span_sel_id: str | None = None
        self._span_rects: list = []
        self._span_edit_key: str = ''
        self._span_edit_buf: str = ''
        # --- Node/segment selection ---
        self.sel_mod_node_id:    str | None = None  # selected node id
        self.sel_mod_seg_id:     str | None = None  # selected segment id
        self.sel_mod_layer_idx:  int | None = None  # which layer it came from
        # --- Node drag ---
        self._prop_seg_rects:    list       = []
        self._prop_action_rects: list       = []
        self._connect_from_node: str | None = None
        # --- Mod edit undo stack ---
        self._mod_undo_stack: list = []  # list of (description, graph_layer_snapshot)
        self._mod_undo_max  = 50
        self._prop_edit_key:     str | None = None  # active editable field key
        self._prop_edit_buf:     str        = ''    # current edit buffer
        self.dragging_node:      bool       = False
        self._drag_snap_node:    str | None = None
        self._drag_snap_seg:     tuple|None = None
        # --- Spliney editing ---
        self.sel_spliney_id:     str | None = None
        self.sel_spliney_pt:     int        = -1
        self.sel_spliney_layer:  int | None = None
        self.sel_spliney_range_id: str | None = None
        self.sel_spliney_range_layer: int | None = None
        self.sel_spliney_range_anchor: int = -1
        self.dragging_spliney_pt:bool       = False
        self.spliney_panel:       bool       = False
        self.spliney_place_mode:  bool       = False
        self.spliney_seed_length: float      = 25.0
        self.spliney_place_rotY:  float      = 0.0
        self.spliney_use_selected_heading: bool = True
        self.spliney_target_path: str        = ''
        self._spliney_rects:      list       = []
        self._spliney_edit_key:   str        = ''
        self._spliney_edit_buf:   str        = ''
        # --- Multi-node selection (Move Group) ---
        self.group_sel_ids:    set        = set()   # selected node ids
        self.group_box_start:  tuple|None = None    # screen start of rubber band
        self.group_box_end:    tuple|None = None
        self.group_panel:      bool       = False
        self._group_rects:     list       = []
        self._group_dx:        str        = '0'
        self._group_dy:        str        = '0'
        self._group_dz:        str        = '0'
        self._group_rot:       str        = '0'
        self._group_edit:      str        = ''
        self._group_buf:       str        = ''
        # --- Calculator panel ---
        self.calc_panel:       bool       = False
        self.calc_mode:        str        = 'crossover'
        self._calc_inputs:     dict       = {}
        self._calc_rects:      list       = []
        self._calc_edit:       str        = ''
        self._calc_buf:        str        = ''
        self.profile_panel:    bool       = False
        self.profile_dock_h:   int        = 250
        self.profile_hover_station_m: float | None = None
        self.profile_hover_world: dict | None = None
        self.profile_hover_node_id: str | None = None
        self.profile_selected_node_id: str | None = None
        self.profile_drag_node_id: str | None = None
        self.profile_drag_origin_y: float = 0.0
        self.profile_drag_preview_y: float | None = None
        self.profile_drag_station_m: float = 0.0
        self.profile_benchmarks: list = []
        self.profile_grade_warn_pct: float = 4.0
        self.profile_break_warn_pct: float = 1.5
        self._profile_panel_rect = None
        self._profile_plot_rect = None
        self._profile_node_rects: list = []
        self._profile_button_rects: list = []
        self._profile_last_data: dict | None = None
        self._profile_cache_key = None
        self._profile_cache_data: dict | None = None
        self.measure_start_node_id: str | None = None
        self.measure_end_node_id: str | None = None
        self.station_origin_node_id: str | None = None
        self.measure_baseline_start_id: str | None = None
        self.measure_baseline_end_id: str | None = None
        self.measure_bearing_lock: bool = False
        self.measure_distance_lock: bool = False
        self._measure_graph_rev: int = 0
        self._measure_pair_cache: dict = {}
        self._station_cache: dict = {}
        self._measure_adjacency_cache: tuple | None = None
        # --- Mandela editor ---
        self.mandela_panel:    bool       = False
        self.sel_mandela_id:   str|None   = None
        self._mandela_rects:   list       = []
        self._mandela_type_buf:str        = ''
        self._mandela_edit:    str        = ''
        self._mandela_buf:     str        = ''
        self.mandela_place_mode:bool      = False
        self.mandela_place_type:str       = ''
        self.mandela_target_path:str      = ''
        self.mandela_rotation:dict        = {'x': 0.0, 'y': 0.0, 'z': 0.0}
        self.mandela_scale:dict           = {'x': 1.0, 'y': 1.0, 'z': 1.0}
        self.mandela_enabled_mode:str     = 'default'
        self._mandela_base_paths:list     = []
        self._mandela_base_path:Path|None = None
        self._mandela_base_error:str      = ''
        # --- Turnout generator ---
        self.turnout_diverge_angle: float   = 10.0
        self.turnout_leg_length:    float   = 30.0
        self.turnout_direction:     str     = 'left'
        self.turnout_track_class:   str     = 'Mainline'
        self.turnout_div_class:     str     = 'Branch'
        self.turnout_speed:         int     = 45
        self.turnout_div_speed:     int     = 15
        self.turnout_flip:          bool    = False
        self.turnout_through_curve: float   = 0.0   # 0 = straight through; >0 = curved turnout
        # --- Wye generator ---
        self.wye_left_angle:  float = 10.0
        self.wye_right_angle: float = 10.0
        self.wye_leg_length:  float = 30.0
        self.wye_track_class: str   = 'Mainline'
        self.wye_style:       str   = 'Standard'
        self.wye_speed:       int   = 25
        self.wye_flip:        bool  = False
        self.turnout_min_leg_m:     float   = 18.0
        self.turnout_warn_angle_deg: float  = 12.0
        self.turnout_max_angle_deg:  float  = 15.0
        # --- Turnout templates ---
        self._turnout_templates: dict        = {}
        self._turnout_template_file: Path    = Path(__file__).parent.parent / 'turnout_templates.json'
        self._turnout_active_template: str | None = None  # name of last loaded template
        # --- Spliney point properties ---
        self._spl_prop_rects:       list    = []
        self._spl_panel_rect = None
        self._spl_edit_key:         str     = ''
        self._spl_edit_buf:         str     = ''
        self._spl_rot_axis:         str     = 'y'
        self.spliney_grade_pct:     float   = 0.0
        # --- Scenery placement ---
        self.scenery_panel:      bool       = False
        self.sel_scenery_id:     str | None = None
        self.sel_scenery_layer:  int | None = None
        self.scenery_place_mode: bool       = False
        self.scenery_place_model:str        = ''
        self.scenery_place_rotY: float      = 0.0
        self.scenery_place_scale:float      = 1.0
        self._scenery_rects:     list       = []
        # --- Clipboard ---
        self._coord_clipboard:   dict | None = None  # {x,y,z,rotY}
        self.drag_node_id:       str | None = None   # node being dragged
        self.drag_node_origin:   dict | None = None  # original node data for undo
        self.drag_screen_pos:    tuple      = (0, 0) # current drag screen pos
        # Autosave
        self._autosave_interval = 300        # seconds between autosaves
        self._autosave_timer    = 0.0
        self._autosave_dir      = Path.home() / ".edit_tiles_autosave"

        # ---- Selection system ----
        self.selection: SelectionBuffer | None = None
        self.clipboard: Clipboard | None       = None
        self.select_mode  = False
        self.sel_tool     = 'rect'             # 'rect' | 'lasso' | 'wand'
        self.sel_dragging = False
        self.sel_drag_start: tuple | None = None
        self.sel_drag_end:   tuple | None = None
        self.sel_lasso_pts: list          = []  # screen (x,y) points for lasso
        self.sel_wand_tol  = 500               # h16 tolerance for magic wand
        self.sel_paste_pos:  tuple | None = None
        self.sel_pending_paste = False

        self._brush_surf     = None
        self._brush_r_cached = -1

        # ---- Generate panel state ----
        self.gen_panel       = False
        self.gen_token       = ""
        self.gen_out_dir     = ""
        self.gen_use_nlcd    = True
        self.gen_nlcd_blur   = GEN_NLCD_BLUR
        self.gen_veg_override: int | None = None
        self.gen_workers     = 4             # up to 32
        self.gen_queue: set  = set()
        self.gen_running: dict = {}
        self.gen_done: set   = set()
        self.gen_failed: set = set()
        self.gen_active      = False
        self._gen_lock       = threading.Lock()
        self._gen_input_focus = None
        self._gen_grid: dict | None = None
        # Presets
        self.gen_presets: dict = {}          # name -> settings dict
        self.gen_preset_name = ""            # name being typed for save
        self._gen_preset_file = Path.home() / ".edit_tiles_presets.json"
        self._gen_load_presets()
        self._load_turnout_templates()
        # Grid navigation inside the generate panel
        self.gen_pad         = 8
        self.gen_cell_sz     = 24
        self.gen_view_x      = 0.0
        self.gen_view_y      = 0.0
        self.gen_dragging_grid = False
        self.gen_drag_last   = (0, 0)
        # Box-select drag
        self.gen_box_start: tuple | None = None
        self.gen_box_end:   tuple | None = None
        self._gen_grid        = {}            # cached grid layout from last draw

        if folders:
            if isinstance(folders, str):
                folders = [folders]
            self.load_folders(folders)

    def _normalize_ui_scale(self, value: float) -> float:
        try:
            numeric = float(value)
        except (TypeError, ValueError):
            numeric = 1.0
        steps = list(getattr(self, 'ui_scale_steps', [1.0])) or [1.0]
        return min(steps, key=lambda step: abs(step - numeric))

    def _ui_scale_label(self) -> str:
        return f"{int(round(float(getattr(self, 'ui_scale', 1.0)) * 100.0))}%"

    def _load_ui_settings(self):
        path = getattr(self, '_ui_settings_file', None)
        if not path or not Path(path).exists():
            return
        try:
            data = json.loads(Path(path).read_text(encoding='utf-8'))
        except Exception as ex:
            print(f"[ui] failed to load settings: {ex}")
            return
        if isinstance(data, dict):
            self.ui_scale = self._normalize_ui_scale(data.get('ui_scale', self.ui_scale))
            self.live_mod_apply = bool(data.get('auto_save_mod_edits', False))

    def _save_ui_settings(self):
        path = getattr(self, '_ui_settings_file', None)
        if not path:
            return
        try:
            _save_json(Path(path), {
                'ui_scale': float(getattr(self, 'ui_scale', 1.0)),
                'auto_save_mod_edits': bool(getattr(self, 'live_mod_apply', False)),
            })
        except Exception as ex:
            print(f"[ui] failed to save settings: {ex}")

    def _apply_ui_scale(self, scale: float, persist: bool = True, announce: bool = True) -> bool:
        normalized = self._normalize_ui_scale(scale)
        previous = float(getattr(self, 'ui_scale', 1.0))
        self.ui_scale = normalized
        small_px = max(10, int(round(12 * normalized)))
        big_px = max(small_px + 2, int(round(14 * normalized)))
        self.font = pygame.freetype.SysFont(self.ui_font_name, small_px)
        self.font_big = pygame.freetype.SysFont(self.ui_font_name, big_px)
        changed = abs(previous - normalized) > 1e-6
        if persist:
            self._save_ui_settings()
        if changed and hasattr(self, 'invalidate_all'):
            self.invalidate_all()
        if announce:
            self._set_status(f"UI scale {self._ui_scale_label()}")
        return changed

    def _adjust_ui_scale(self, step: int = 0, reset: bool = False):
        steps = list(getattr(self, 'ui_scale_steps', [1.0])) or [1.0]
        current = self._normalize_ui_scale(getattr(self, 'ui_scale', 1.0))
        if reset:
            target = 1.0
        else:
            index = min(range(len(steps)), key=lambda idx: abs(steps[idx] - current))
            index = max(0, min(len(steps) - 1, index + int(step)))
            target = steps[index]
        changed = self._apply_ui_scale(target, persist=True, announce=False)
        if changed:
            self._set_status(f"UI scale {self._ui_scale_label()}  (Ctrl+- / Ctrl+= / Ctrl+0)")
        else:
            self._set_status(f"UI scale already {self._ui_scale_label()}")

    def _pending_mod_apply_count(self) -> int:
        if not self.mod_project:
            return 0
        return sum(
            1 for layer in self.mod_project.layers
            if layer.dirty and not layer.read_only
        )

    def _sync_mod_project_save_mode(self):
        if self.mod_project:
            self.mod_project.defer_writes = (
                not bool(getattr(self, 'live_mod_apply', True))
                or bool(
                    getattr(
                        self,
                        '_game_graph_sync_locked',
                        False))
            )

    def _apply_pending_mod_changes(self, announce: bool = True) -> tuple[int, int]:
        if not self.mod_project:
            if announce:
                self._set_status("No mod project loaded")
            return 0, 0
        if getattr(self, '_game_graph_sync_locked', False):
            if announce:
                self._set_status(
                    "Game has unsaved map edits; desktop save is paused")
            return 0, 0

        dirty_layers = [
            layer for layer in self.mod_project.layers
            if layer.dirty and not layer.read_only
        ]
        pending_reload_paths = {
            str(layer.path) for layer in dirty_layers
            if getattr(layer, 'path', None) is not None
        }
        pending_reload_paths.update(getattr(self, '_pending_bridge_reload_paths', set()))

        saved_layers = self.mod_project.save_all(force=True)
        self._area_dirty_layers.clear()

        reload_count = 0
        if pending_reload_paths:
            self._pending_bridge_reload_paths.update(pending_reload_paths)
            reload_count = self._flush_pending_bridge_reload_paths()

        saved_count = len(saved_layers)
        if announce:
            if saved_count or reload_count:
                parts = []
                if saved_count:
                    parts.append(f"saved {saved_count} layer(s)")
                if reload_count:
                    parts.append(f"reloaded {reload_count} file(s)")
                self._set_status("Saved pending mod edits: " + ", ".join(parts))
            else:
                self._set_status("No pending mod edits")
        return saved_count, reload_count

    def _set_live_mod_apply(self, enabled: bool, persist: bool = True, announce: bool = True):
        enabled = bool(enabled)
        previous = bool(getattr(self, 'live_mod_apply', True))
        self.live_mod_apply = enabled
        self._sync_mod_project_save_mode()

        saved_count = 0
        reload_count = 0
        if enabled and not previous and self.mod_project:
            saved_count, reload_count = self._apply_pending_mod_changes(announce=False)

        if persist:
            self._save_ui_settings()

        if announce:
            if enabled:
                if saved_count or reload_count:
                    parts = []
                    if saved_count:
                        parts.append(f"saved {saved_count} layer(s)")
                    if reload_count:
                        parts.append(f"reloaded {reload_count} file(s)")
                    self._set_status("Auto Save ON  (" + ", ".join(parts) + ")")
                else:
                    self._set_status("Auto Save ON")
            else:
                pending = self._pending_mod_apply_count()
                suffix = f"  ({pending} pending layer(s))" if pending else ""
                self._set_status("Auto Save OFF - edits stay cached until Save" + suffix)

    def _save_mod_layer_now(self, li: int) -> tuple[object, int]:
        if not self.mod_project or not (0 <= li < len(self.mod_project.layers)):
            return None, 0
        if getattr(self, '_game_graph_sync_locked', False):
            self._set_status(
                "Game has unsaved map edits; desktop save is paused")
            return None, 0
        layer = self.mod_project.layers[li]
        should_reload = bool(layer.dirty) or str(layer.path) in self._pending_bridge_reload_paths
        saved = self.mod_project.save_layer(li, force=True)
        reload_count = 0
        if should_reload:
            self._pending_bridge_reload_paths.add(str(layer.path))
            reload_count = self._flush_pending_bridge_reload_paths()
        else:
            self._pending_bridge_reload_paths.discard(str(layer.path))
        return saved, reload_count

    # ------------------------------------------------------------------
    # Loading
    # ------------------------------------------------------------------

    @staticmethod
    def _find_map_json(folder) -> Path | None:
        """Find the nearest Map.json for a tile folder or a path inside one."""
        path = Path(folder).expanduser()
        start = path.parent if path.is_file() else path
        search_dirs = [start]
        search_dirs.extend(list(start.parents)[:4])
        for directory in search_dirs:
            for candidate in (directory / 'Map.json',
                              directory / 'map.json',
                              directory / 'Map' / 'Map.json'):
                if candidate.is_file():
                    return candidate.resolve()
        return None

    @classmethod
    def _read_map_georeference(cls, folders):
        """Read the first valid map georeference associated with tile folders."""
        selected = None
        checked = set()
        for folder in folders:
            map_json = cls._find_map_json(folder)
            if map_json is None or map_json in checked:
                continue
            checked.add(map_json)
            try:
                data = json.loads(map_json.read_text(encoding='utf-8-sig'))
                origin = data.get('origin')
                if not isinstance(origin, dict):
                    raise ValueError("missing origin object")
                lat = float(origin['latitude'])
                lon = float(origin['longitude'])
                tile_dimension = float(data['tileDimension'])
                stock_origin = (
                    abs(lat - GEN_ORIGIN_LAT) < 0.0001
                    and abs(lon - GEN_ORIGIN_LON) < 0.0001
                )
                east_bias = float(origin.get(
                    'eastBiasMeters',
                    GEN_ORIGIN_E_BIAS if stock_origin else 0.0,
                ))
                north_bias = float(origin.get(
                    'northBiasMeters',
                    GEN_ORIGIN_N_BIAS if stock_origin else 0.0,
                ))
                if not (-90.0 <= lat <= 90.0 and -180.0 <= lon <= 180.0):
                    raise ValueError("origin is outside valid latitude/longitude bounds")
                if not all(math.isfinite(value) for value in
                           (lat, lon, tile_dimension, east_bias, north_bias)):
                    raise ValueError("georeference contains a non-finite number")
                if tile_dimension <= 0.0:
                    raise ValueError("tileDimension must be greater than zero")
                georef = (lat, lon, tile_dimension, east_bias, north_bias, map_json)
            except (OSError, ValueError, TypeError, KeyError, json.JSONDecodeError) as ex:
                print(f"[map] ignoring invalid georeference in {map_json}: {ex}")
                continue

            if selected is None:
                selected = georef
            elif georef[:5] != selected[:5]:
                print(f"[map] georeference conflict: using {selected[5]}, ignoring {map_json}")
        return selected

    def _configure_map_georeference(self, folders, preserve_if_missing=False):
        """Update OSM tile bounds from Map.json, with legacy NC defaults as fallback."""
        georef = self._read_map_georeference(folders)
        if georef is None:
            if preserve_if_missing:
                return False
            georef = (GEN_ORIGIN_LAT, GEN_ORIGIN_LON, GEN_TILE_DIM_M,
                      GEN_ORIGIN_E_BIAS, GEN_ORIGIN_N_BIAS, None)

        lat, lon, tile_dimension, east_bias, north_bias, map_json = georef
        previous = (self.map_origin_lat, self.map_origin_lon,
                    self.map_tile_dimension_m, self.map_origin_e_bias,
                    self.map_origin_n_bias, self.map_georef_path)
        current = (lat, lon, tile_dimension, east_bias, north_bias, map_json)
        self.map_origin_lat = lat
        self.map_origin_lon = lon
        self.map_tile_dimension_m = tile_dimension
        self.map_origin_e_bias = east_bias
        self.map_origin_n_bias = north_bias
        self.map_georef_path = map_json
        if current != previous:
            self.osm.invalidate()
        source = str(map_json) if map_json else 'legacy North Carolina defaults'
        print(f"[map] OSM georeference {lat:.6f}, {lon:.6f}; "
              f"tile {tile_dimension:g} m ({source})")
        return current != previous

    def _sync_map_manifest(self, folder, create=False):
        """Keep Map.json's complete tile list aligned with files on disk."""
        kwargs = {}
        if create:
            kwargs = {
                'origin_lat': self.map_origin_lat,
                'origin_lon': self.map_origin_lon,
                'tile_dimension_m': self.map_tile_dimension_m,
                'origin_e_bias': self.map_origin_e_bias,
                'origin_n_bias': self.map_origin_n_bias,
            }
        try:
            return sync_map_json_tile_list(folder, **kwargs)
        except (OSError, ValueError, TypeError, json.JSONDecodeError) as ex:
            print(f"[map] Map.json synchronization failed for {folder}: {ex}")
            return None


    def load_folders(self, folders, preserve_view=False):
        if isinstance(folders, str):
            folders = [folders]
        folders = [str(Path(folder)) for folder in folders]
        self.folders = folders
        all_files = []
        for folder in folders:
            all_files.extend(Path(folder).glob('tile_*.data'))
        if not all_files:
            self._set_status("No .data tiles found in selected folders")
            print("No .data tiles found in selected folders")
            return
        self._configure_map_georeference(folders)
        self.loading = True
        self.load_progress = (0, len(all_files))
        self.tiles = {}
        self.undo_stack.clear()
        saved_view = (self.pan_x, self.pan_y, self.zoom) if preserve_view else None

        def worker():
            try:
                import os
                n_workers = min(8, max(2, os.cpu_count() or 2))
                loaded_count = [0]
                lock = threading.Lock()

                def load_one(f):
                    try:
                        tile = load_tile(f)
                        with lock:
                            loaded_count[0] += 1
                            self.load_progress = (loaded_count[0], len(all_files))
                            if tile:
                                self.tiles[f'{tile.x},{tile.y}'] = tile
                    except Exception as e:
                        print(f"[load] error on {f.name}: {e}")
                        with lock:
                            loaded_count[0] += 1
                            self.load_progress = (loaded_count[0], len(all_files))

                with ThreadPoolExecutor(max_workers=n_workers) as pool:
                    list(pool.map(load_one, all_files))

            except Exception as e:
                print(f"[load] parallel load failed ({e}), falling back to single-threaded")
                loaded = 0
                for f in all_files:
                    try:
                        tile = load_tile(f)
                        if tile:
                            self.tiles[f'{tile.x},{tile.y}'] = tile
                    except Exception as e2:
                        print(f"[load] skipping {f.name}: {e2}")
                    loaded += 1
                    self.load_progress = (loaded, len(all_files))

            self._update_bounds()
            if saved_view is not None:
                self.pan_x, self.pan_y, self.zoom = saved_view
                self.invalidate_all()
            else:
                self._fit_view()
            self.loading = False

        threading.Thread(target=worker, daemon=True).start()

    def load_folder(self, folder):
        self.load_folders([folder])

    def load_tiles_folders(self, folders):
        """Append tiles from additional folders without clearing existing tiles."""
        if isinstance(folders, str):
            folders = [folders]
        folders = [str(Path(folder)) for folder in folders]
        merged_folders = list(self.folders)
        for folder in folders:
            if folder not in merged_folders:
                merged_folders.append(folder)
        self.folders = merged_folders
        all_files = []
        for folder in folders:
            path = Path(folder)
            new_files = list(path.glob('tile_*.data'))
            # Skip tiles already loaded
            new_files = [f for f in new_files
                         if self._tile_key_from_path(f) not in self.tiles]
            all_files.extend(new_files)
        if not all_files:
            self._set_status("No new tiles found in selected folder(s)")
            return
        self._configure_map_georeference(merged_folders, preserve_if_missing=True)
        self.loading = True
        existing = len(self.tiles)
        self.load_progress = (0, len(all_files))
        self._set_status(f"Loading {len(all_files)} new tile(s)...")

        def worker():
            try:
                import os
                n_workers = min(8, max(2, os.cpu_count() or 2))
                loaded_count = [0]
                lock = threading.Lock()

                def load_one(f):
                    try:
                        tile = load_tile(f)
                        with lock:
                            loaded_count[0] += 1
                            self.load_progress = (loaded_count[0], len(all_files))
                            if tile:
                                self.tiles[f'{tile.x},{tile.y}'] = tile
                    except Exception as e:
                        print(f"[load_tiles] error on {f.name}: {e}")
                        with lock:
                            loaded_count[0] += 1
                            self.load_progress = (loaded_count[0], len(all_files))

                with ThreadPoolExecutor(max_workers=n_workers) as pool:
                    list(pool.map(load_one, all_files))

            except Exception as e:
                print(f"[load_tiles] parallel failed ({e}), falling back")
                loaded = 0
                for f in all_files:
                    try:
                        tile = load_tile(f)
                        if tile:
                            self.tiles[f'{tile.x},{tile.y}'] = tile
                    except Exception as e2:
                        print(f"[load_tiles] skipping {f.name}: {e2}")
                    loaded += 1
                    self.load_progress = (loaded, len(all_files))

            added = len(self.tiles) - existing
            self._update_bounds()
            self.loading = False
            self._set_status(f"Loaded {added} new tile(s) — total {len(self.tiles)}")

        threading.Thread(target=worker, daemon=True).start()

    def _tile_key_from_path(self, path):
        """Return 'x,y' key from a tile path, or None if unparseable."""
        m = path.name.replace('tile_', '').replace('.data', '')
        parts = m.split('_')
        if len(parts) == 2:
            try:
                return f'{int(parts[0])},{int(parts[1])}'
            except ValueError:
                pass
        return None

    # ------------------------------------------------------------------
    # Generation presets
    # ------------------------------------------------------------------
    def _gen_load_presets(self):
        try:
            if self._gen_preset_file.exists():
                import json
                self.gen_presets = json.loads(self._gen_preset_file.read_text())
                changed = False
                cleaned_presets = {}
                for name, preset in self.gen_presets.items():
                    if not isinstance(preset, dict):
                        cleaned_presets[name] = preset
                        continue
                    fixed = dict(preset)
                    token = clean_mapbox_token(fixed.get('token', ''))
                    if token != fixed.get('token', ''):
                        changed = True
                    fixed['token'] = token
                    cleaned_presets[name] = fixed
                self.gen_presets = cleaned_presets
                if changed:
                    self._gen_save_presets()
        except Exception as e:
            print(f"Could not load presets: {e}")
            self.gen_presets = {}

    def _gen_save_presets(self):
        try:
            import json
            self._gen_preset_file.write_text(json.dumps(self.gen_presets, indent=2))
        except Exception as e:
            print(f"Could not save presets: {e}")

    # ------------------------------------------------------------------
    # Turnout templates
    # ------------------------------------------------------------------
    def _load_turnout_templates(self):
        try:
            if self._turnout_template_file.exists():
                data = json.loads(self._turnout_template_file.read_text(encoding='utf-8'))
                if isinstance(data, dict):
                    self._turnout_templates = data
        except Exception as ex:
            print(f'[turnout templates] failed to load: {ex}')
            self._turnout_templates = {}

    def _save_turnout_templates(self):
        try:
            _save_json(self._turnout_template_file, self._turnout_templates)
        except Exception as ex:
            print(f'[turnout templates] failed to save: {ex}')

    def _turnout_template_to_dict(self) -> dict:
        """Capture current turnout settings as a template dict."""
        return {
            'diverge_angle': float(self.turnout_diverge_angle),
            'leg_length':    float(self.turnout_leg_length),
            'direction':     str(self.turnout_direction),
            'track_class':   str(self.turnout_track_class),
            'div_class':     str(self.turnout_div_class),
            'speed':         int(self.turnout_speed),
            'div_speed':     int(self.turnout_div_speed),
            'flip':          bool(self.turnout_flip),
            'through_curve': float(self.turnout_through_curve),
        }

    def _apply_turnout_template(self, name: str):
        """Load a named template into current turnout settings."""
        t = self._turnout_templates.get(name)
        if not t:
            return
        self.turnout_diverge_angle = float(t.get('diverge_angle', self.turnout_diverge_angle))
        self.turnout_leg_length    = float(t.get('leg_length',    self.turnout_leg_length))
        self.turnout_direction     = str(t.get('direction',    self.turnout_direction))
        self.turnout_track_class   = str(t.get('track_class',  self.turnout_track_class))
        self.turnout_div_class     = str(t.get('div_class',    self.turnout_div_class))
        self.turnout_speed         = int(t.get('speed',        self.turnout_speed))
        self.turnout_div_speed     = int(t.get('div_speed',    self.turnout_div_speed))
        self.turnout_flip          = bool(t.get('flip',        self.turnout_flip))
        self.turnout_through_curve = float(t.get('through_curve', 0.0))
        self._turnout_active_template = name
        self._clear_geo_preview()
        self._set_status(f'Turnout template loaded: {name}')

    def _gen_preset_to_dict(self):
        return {
            'token':        clean_mapbox_token(self.gen_token),
            'out_dir':      str(self.gen_out_dir),
            'use_nlcd':     self.gen_use_nlcd,
            'nlcd_blur':    self.gen_nlcd_blur,
            'veg_override': self.gen_veg_override,
            'workers':      self.gen_workers,
        }

    def _gen_apply_preset(self, name):
        p = self.gen_presets.get(name)
        if not p:
            return
        self.gen_token        = clean_mapbox_token(p.get('token', ''))
        self.gen_out_dir      = p.get('out_dir', '')
        self.gen_use_nlcd     = p.get('use_nlcd', True)
        self.gen_nlcd_blur    = p.get('nlcd_blur', GEN_NLCD_BLUR)
        self.gen_veg_override = p.get('veg_override', None)
        self.gen_workers      = max(1, min(32, p.get('workers', 4)))
        self._set_status(f"Preset loaded: {name}")

    def _gen_save_preset(self, name):
        if not name.strip():
            return
        self.gen_presets[name.strip()] = self._gen_preset_to_dict()
        self._gen_save_presets()
        self._set_status(f"Preset saved: {name.strip()}")

    def _gen_delete_preset(self, name):
        if name in self.gen_presets:
            del self.gen_presets[name]
            self._gen_save_presets()
            self._set_status(f"Preset deleted: {name}")

    def _get_system_clipboard_text(self) -> str | None:
        """Best-effort system clipboard text, independent of terrain clipboard."""
        text = None

        if sys.platform.startswith("win"):
            try:
                import ctypes
                CF_UNICODETEXT = 13
                user32 = ctypes.windll.user32
                kernel32 = ctypes.windll.kernel32
                if user32.OpenClipboard(None):
                    try:
                        handle = user32.GetClipboardData(CF_UNICODETEXT)
                        if handle:
                            locked = kernel32.GlobalLock(handle)
                            if locked:
                                try:
                                    text = ctypes.wstring_at(locked)
                                finally:
                                    kernel32.GlobalUnlock(handle)
                    finally:
                        user32.CloseClipboard()
            except Exception:
                text = None

        if not text and sys.platform == "darwin":
            try:
                result = subprocess.run(
                    ["pbpaste"],
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=1,
                )
                if result.returncode == 0:
                    text = result.stdout
            except Exception:
                text = None

        if not text:
            linux_cmds = [
                ["wl-paste", "-n"],
                ["xclip", "-selection", "clipboard", "-o"],
                ["xsel", "--clipboard", "--output"],
            ]
            for cmd in linux_cmds:
                if shutil.which(cmd[0]) is None:
                    continue
                try:
                    result = subprocess.run(
                        cmd,
                        capture_output=True,
                        text=True,
                        check=False,
                        timeout=1,
                    )
                    if result.returncode == 0 and result.stdout:
                        text = result.stdout
                        break
                except Exception:
                    continue

        if not text:
            try:
                if not pygame.scrap.get_init():
                    pygame.scrap.init()
                raw = pygame.scrap.get(pygame.SCRAP_TEXT)
                if raw:
                    text = raw.decode("utf-8", errors="ignore") if isinstance(raw, bytes) else str(raw)
            except Exception:
                text = None

        if not text:
            try:
                import tkinter as tk
                root = tk.Tk()
                root.withdraw()
                text = root.clipboard_get()
                root.destroy()
            except Exception:
                text = None

        return text if text else None

    def _paste_generate_token_from_clipboard(self) -> bool:
        """Paste a full Mapbox token from the system clipboard."""
        text = self._get_system_clipboard_text()
        if text is None:
            self._set_status("Clipboard text unavailable")
            return False

        raw_token = text.replace("\r", "").replace("\n", "").strip()
        token = clean_mapbox_token(raw_token)
        if not token:
            self._set_status("Clipboard did not contain text")
            return False

        self.gen_token = token
        if token != raw_token:
            self._set_status("Pasted Mapbox token (removed hidden clipboard characters)")
        else:
            self._set_status("Pasted Mapbox token")
        return True

    def load_track_graph(self, path):
        path = Path(path)
        self.track_graph_path = path
        import json
        with open(path) as f:
            data = json.load(f)
        nodes    = data.get('tracks', {}).get('nodes', {})
        segments = data.get('tracks', {}).get('segments', {})

        self.track_nodes = {
            k: (
                v['position']['x'],
                v['position']['z'],
                v['rotation']['y'],
            )
            for k, v in nodes.items()
        }
        self.track_node_elevs = {
            k: float(v.get('position', {}).get('y', 0.0))
            for k, v in nodes.items()
        }
        self.track_segments = []
        self.track_node_list = []
        self.track_segment_meta = []
        self.track_segment_points = {}

        for nid, (nx, nz, _) in self.track_nodes.items():
            self.track_node_list.append((nx, nz, nid))

        for seg_id, seg in segments.items():
            s_id = seg.get('startId')
            e_id = seg.get('endId')
            tc   = seg.get('trackClass', 'Mainline')
            style = seg.get('style', 'Standard')
            if s_id not in self.track_nodes or e_id not in self.track_nodes:
                continue
            x0, z0, ry0 = self.track_nodes[s_id]
            x1, z1, ry1 = self.track_nodes[e_id]
            dist = math.sqrt((x1-x0)**2 + (z1-z0)**2)
            if dist < 0.1:
                continue
            raw_n0 = nodes.get(s_id, {}) or {}
            raw_n1 = nodes.get(e_id, {}) or {}
            pts = _bezier_for_nodes(
                {
                    'x': x0,
                    'y': self.track_node_elevs.get(s_id, 0.0),
                    'z': z0,
                    'rotX': float(raw_n0.get('rotation', {}).get('x', 0.0) or 0.0),
                    'rotY': ry0,
                },
                {
                    'x': x1,
                    'y': self.track_node_elevs.get(e_id, 0.0),
                    'z': z1,
                    'rotX': float(raw_n1.get('rotation', {}).get('x', 0.0) or 0.0),
                    'rotY': ry1,
                },
            )
            self.track_segments.append((pts, tc))
            self.track_segment_points[seg_id] = pts
            y0 = self.track_node_elevs.get(s_id, 0.0)
            y1 = self.track_node_elevs.get(e_id, 0.0)
            self.track_segment_meta.append({
                'id': seg_id,
                'start_id': s_id,
                'end_id': e_id,
                'track_class': tc,
                'style': style,
                'speed_limit': seg.get('speedLimit', ''),
                'gauge': normalize_track_gauge(seg.get('gauge', 'Standard')),
                'start_y': y0,
                'end_y': y1,
                'run_m': dist,
                'grade_pct': ((y1 - y0) / dist * 100.0) if dist > 0.1 else None,
            })

        self._mark_measure_cache_dirty()
        self.show_tracks = True
        self.show_nodes  = False
        if not self.tiles and self.track_node_list:
            self._fit_track_view()
        self._set_status(f"Loaded {len(self.track_segments)} segments, {len(self.track_node_list)} nodes")
        print(f"Loaded {len(self.track_segments)} track segments, {len(self.track_node_list)} nodes")

    def _mark_measure_cache_dirty(self):
        self._measure_graph_rev += 1
        self._measure_pair_cache = {}
        self._station_cache = {}
        self._measure_adjacency_cache = None
        self._profile_cache_key = None
        self._profile_cache_data = None

    def _polyline_length_xz(self, points):
        if not points or len(points) < 2:
            return 0.0
        total = 0.0
        px, pz = points[0]
        for x1, z1 in points[1:]:
            total += math.hypot(x1 - px, z1 - pz)
            px, pz = x1, z1
        return total

    def _get_track_node_state(self, node_id: str | None):
        if not node_id:
            return None
        if self.mod_project:
            node = self.mod_project.merged_nodes.get(node_id)
            if node and not node.get('deleted'):
                return {
                    'id': node_id,
                    'x': float(node.get('x', 0.0)),
                    'y': float(node.get('y', 0.0)),
                    'z': float(node.get('z', 0.0)),
                    'rotY': float(node.get('rotY', 0.0)),
                    'source': 'mod',
                }
        node_obj = self._bridge_nodes_raw.get(node_id)
        if node_obj is not None:
            return {
                'id': node_id,
                'x': float(node_obj.x),
                'y': float(node_obj.y),
                'z': float(node_obj.z),
                'rotY': float(node_obj.rotY),
                'source': 'bridge',
            }
        node_xyz = self.track_nodes.get(node_id)
        if node_xyz:
            x, z, rotY = node_xyz
            return {
                'id': node_id,
                'x': float(x),
                'y': float(self.track_node_elevs.get(node_id, 0.0)),
                'z': float(z),
                'rotY': float(rotY),
                'source': 'loaded',
            }
        return None


    def _get_track_segment_state(self, seg_id: str | None):
        if not seg_id:
            return None
        if self.mod_project:
            seg = self.mod_project.merged_segments.get(seg_id)
            if seg and not seg.get('deleted'):
                return {
                    'id': seg_id,
                    'startId': seg.get('startId', ''),
                    'endId': seg.get('endId', ''),
                    'trackClass': seg.get('trackClass', ''),
                    'style': seg.get('style', ''),
                    'speedLimit': seg.get('speedLimit', ''),
                    'priority': seg.get('priority', ''),
                    'groupId': seg.get('groupId', ''),
                    'gauge': normalize_track_gauge(
                        seg.get('gauge', 'Standard')
                    ),
                    'source': 'mod',
                }
        seg_obj = self._bridge_segments_raw.get(seg_id)
        if seg_obj is not None:
            return {
                'id': seg_id,
                'startId': seg_obj.startId,
                'endId': seg_obj.endId,
                'trackClass': seg_obj.trackClass,
                'style': getattr(seg_obj, 'style', ''),
                'speedLimit': seg_obj.speedLimit,
                'priority': seg_obj.priority,
                'groupId': getattr(seg_obj, 'groupId', ''),
                'gauge': normalize_track_gauge(
                    getattr(seg_obj, 'gauge', 'Standard')
                ),
                'source': 'bridge',
            }
        for meta in self.track_segment_meta:
            if meta.get('id') == seg_id:
                return {
                    'id': seg_id,
                    'startId': meta.get('start_id', ''),
                    'endId': meta.get('end_id', ''),
                    'trackClass': meta.get('track_class', ''),
                    'style': meta.get('style', ''),
                    'speedLimit': meta.get('speed_limit', ''),
                    'priority': '',
                    'groupId': '',
                    'gauge': normalize_track_gauge(
                        meta.get('gauge', 'Standard')
                    ),
                    'source': 'loaded',
                }
        return None

    def _apply_geo_nudge(self, key: str, delta) -> bool:
        try:
            delta_val = float(delta)
        except (TypeError, ValueError):
            return False

        float_keys = {
            'geo_radius': (1.0, 5000.0),
            'geo_degrees': (1.0, 360.0),
            'alignment_min_radius_m': (1.0, 5000.0),
            'turnout_leg_length': (1.0, 1000.0),
            'turnout_diverge_angle': (1.0, 90.0),
            'geo_spline_width': (0.0, 1000.0),
            'geo_piece_length': (1.0, 5000.0),
            'grade_transition_in_m': (0.0, 10000.0),
            'grade_transition_out_m': (0.0, 10000.0),
        }
        int_keys = {
            'geo_speed': (1, 200),
            'turnout_speed': (1, 200),
            'turnout_div_speed': (1, 200),
            'geo_n_segs': (0, 256),
        }
        if key in float_keys:
            lo, hi = float_keys[key]
            updated = max(lo, min(hi, float(getattr(self, key, 0.0)) + delta_val))
            setattr(self, key, round(updated, 3))
        elif key in int_keys:
            lo, hi = int_keys[key]
            updated = int(round(float(getattr(self, key, 0)) + delta_val))
            setattr(self, key, max(lo, min(hi, updated)))
        else:
            return False

        self._geo_input_focus = None
        self._geo_input_buf = ''
        if not (self.geo_mode == 'pieces' and key in ('geo_piece_length', 'geo_radius', 'geo_degrees', 'geo_n_segs', 'geo_speed')):
            self._clear_geo_preview()
        if key.startswith('grade_'):
            self._profile_cache_key = None
            self._profile_cache_data = None
        return True

    def _segments_for_track_node(self, node_id: str | None):
        if not node_id:
            return []
        if self.mod_project:
            return self.mod_project.segments_for_node(node_id)
        matches = []
        for seg in self._iter_active_track_segments():
            if seg['start_id'] != node_id and seg['end_id'] != node_id:
                continue
            seg_state = self._get_track_segment_state(seg['id'])
            if seg_state:
                matches.append(seg_state)
        return matches

    def _iter_active_track_segments(self):
        if self.mod_project:
            curve_points = {}
            for layer in self.mod_project.layers:
                for pts, _col, seg_id in getattr(layer, 'curves', []):
                    if pts:
                        curve_points[seg_id] = [(float(p[0]), float(p[1])) for p in pts]
            for seg_id, seg in self.mod_project.merged_segments.items():
                start_id = seg.get('startId')
                end_id = seg.get('endId')
                if not start_id or not end_id:
                    continue
                start = self._get_track_node_state(start_id)
                end = self._get_track_node_state(end_id)
                if not start or not end:
                    continue
                points = curve_points.get(seg_id)
                plan_m = (self._polyline_length_xz(points) if points
                          else math.hypot(end['x'] - start['x'], end['z'] - start['z']))
                yield {
                    'id': seg_id,
                    'start_id': start_id,
                    'end_id': end_id,
                    'points': points,
                    'plan_m': plan_m,
                }
            return

        for meta in self.track_segment_meta:
            seg_id = meta.get('id')
            start_id = meta.get('start_id')
            end_id = meta.get('end_id')
            if not seg_id or not start_id or not end_id:
                continue
            start = self._get_track_node_state(start_id)
            end = self._get_track_node_state(end_id)
            if not start or not end:
                continue
            points = self.track_segment_points.get(seg_id)
            plan_m = (self._polyline_length_xz(points) if points
                      else math.hypot(end['x'] - start['x'], end['z'] - start['z']))
            yield {
                'id': seg_id,
                'start_id': start_id,
                'end_id': end_id,
                'points': points,
                'plan_m': plan_m,
            }

    def _track_adjacency(self):
        cache = self._measure_adjacency_cache
        if cache and cache[0] == self._measure_graph_rev:
            return cache[1]
        adjacency = collections.defaultdict(list)
        for seg in self._iter_active_track_segments():
            length = max(seg['plan_m'], 0.01)
            adjacency[seg['start_id']].append((seg['end_id'], length, seg['id']))
            adjacency[seg['end_id']].append((seg['start_id'], length, seg['id']))
        self._measure_adjacency_cache = (self._measure_graph_rev, adjacency)
        return adjacency

    def _shortest_track_path(self, start_id: str | None, end_id: str | None):
        if not start_id or not end_id:
            return None
        if start_id == end_id:
            return {'distance': 0.0, 'segments': [], 'nodes': [start_id]}
        adjacency = self._track_adjacency()
        if start_id not in adjacency or end_id not in adjacency:
            return None
        heap = [(0.0, start_id)]
        best = {start_id: 0.0}
        prev = {}
        while heap:
            dist, node_id = heapq.heappop(heap)
            if dist > best.get(node_id, float('inf')) + 1e-9:
                continue
            if node_id == end_id:
                break
            for next_id, length, seg_id in adjacency.get(node_id, ()):
                next_dist = dist + length
                if next_dist + 1e-9 < best.get(next_id, float('inf')):
                    best[next_id] = next_dist
                    prev[next_id] = (node_id, seg_id)
                    heapq.heappush(heap, (next_dist, next_id))
        if end_id not in best:
            return None
        seg_ids = []
        node_ids = [end_id]
        cur = end_id
        while cur != start_id:
            prev_node, seg_id = prev[cur]
            seg_ids.append(seg_id)
            cur = prev_node
            node_ids.append(cur)
        seg_ids.reverse()
        node_ids.reverse()
        return {
            'distance': best[end_id],
            'segments': seg_ids,
            'nodes': node_ids,
        }

    def _station_distance_for_node(self, node_id: str | None):
        origin_id = self.station_origin_node_id
        if not origin_id or not node_id:
            return None
        if origin_id == node_id:
            return 0.0
        cache_key = (self._measure_graph_rev, origin_id, node_id)
        if cache_key in self._station_cache:
            return self._station_cache[cache_key]
        path = self._shortest_track_path(origin_id, node_id)
        dist = path['distance'] if path else None
        self._station_cache[cache_key] = dist
        return dist

    def _format_station_value(self, meters: float | None):
        if meters is None:
            return '--'
        sign = '-' if meters < 0 else ''
        meters = abs(float(meters))
        hundreds = int(meters // 100)
        remainder = meters - hundreds * 100
        return f"{sign}{hundreds}+{remainder:04.1f}"

    def _format_station_readout(self, meters: float | None):
        if meters is None:
            return '--'
        miles = float(meters) / 1609.344
        return f"Sta {self._format_station_value(meters)}  /  MP {miles:.3f}"

    def _wrap_text_lines(self, font_obj, text, max_w: int):
        text = str(text or '')
        max_w = max(24, int(max_w))
        lines = []
        for raw_line in text.splitlines() or ['']:
            words = raw_line.split()
            if not words:
                lines.append('')
                continue
            line = words[0]
            for word in words[1:]:
                test = f"{line} {word}"
                if font_obj.get_rect(test).width <= max_w:
                    line = test
                    continue
                lines.append(line)
                if font_obj.get_rect(word).width <= max_w:
                    line = word
                    continue
                chunk = ''
                for ch in word:
                    test_chunk = chunk + ch
                    if chunk and font_obj.get_rect(test_chunk).width > max_w:
                        lines.append(chunk)
                        chunk = ch
                    else:
                        chunk = test_chunk
                line = chunk
            lines.append(line)
        return lines or ['']

    def _fit_text_to_width(self, font_obj, text, max_w: int):
        text = str(text or '')
        max_w = max(0, int(max_w))
        if max_w <= 0:
            return ''
        if font_obj.get_rect(text).width <= max_w:
            return text
        ellipsis = '...'
        if font_obj.get_rect(ellipsis).width > max_w:
            return ''
        trimmed = text.rstrip()
        while trimmed and font_obj.get_rect(trimmed + ellipsis).width > max_w:
            trimmed = trimmed[:-1].rstrip()
        return (trimmed + ellipsis) if trimmed else ellipsis

    def _set_grade_chain_start(self, node_id: str | None):
        if not node_id or not self.mod_project:
            self._set_status("Grade chain: select a track node first")
            return False
        self.grade_chain = [str(node_id)]
        self._set_grade_transition_preview(False)
        self._clear_geo_preview()
        self._set_status(f"Grade chain started at {node_id}")
        return True

    def _extend_grade_chain_to(self, node_id: str | None):
        if not node_id or not self.mod_project:
            self._set_status("Grade chain: select a destination node first")
            return False
        node_id = str(node_id)
        if not self.grade_chain:
            return self._set_grade_chain_start(node_id)
        tail_id = str(self.grade_chain[-1])
        if node_id == tail_id:
            self._set_status(f"Grade chain already ends at {node_id}")
            return False
        if node_id in self.grade_chain:
            self._set_status(f"{node_id} is already in the current grade chain")
            return False
        path = self._shortest_track_path(tail_id, node_id)
        if not path or len(path.get('nodes', [])) < 2:
            self._set_status(f"No connected track path found from {tail_id} to {node_id}")
            return False
        extension = [str(nid) for nid in path.get('nodes', [])[1:] if nid]
        loop_nodes = [nid for nid in extension[:-1] if nid in self.grade_chain]
        if loop_nodes:
            self._set_status(
                f"Grade path from {tail_id} to {node_id} loops back through {loop_nodes[0]}; clear or pick a different end node"
            )
            return False
        self.grade_chain.extend(extension)
        self._set_grade_transition_preview(False)
        self._clear_geo_preview()
        distance_m = float(path.get('distance', 0.0) or 0.0)
        intermediate_count = max(0, len(extension) - 1)
        if intermediate_count:
            self._set_status(
                f"Grade path added to {node_id}: {intermediate_count} in-between node(s), {distance_m:.1f} m"
            )
        else:
            self._set_status(f"Grade chain extended directly to {node_id}: {distance_m:.1f} m")
        return True

    def _profile_base_cache_key(self):
        source_sig = None
        if len(self.grade_chain) >= 2:
            source_sig = ('grade_chain', tuple(str(node_id) for node_id in self.grade_chain))
        elif (self.measure_start_node_id and self.measure_end_node_id and
                self.measure_start_node_id != self.measure_end_node_id):
            source_sig = ('measure_path', str(self.measure_start_node_id), str(self.measure_end_node_id))
        elif self.sel_mod_seg_id:
            source_sig = ('segment', str(self.sel_mod_seg_id))
        if source_sig is None:
            return None

        bench_sig = tuple(sorted(
            (
                str(entry.get('node_id') or ''),
                round(float(entry.get('y', 0.0) or 0.0), 4),
                str(entry.get('label') or ''),
            )
            for entry in getattr(self, 'profile_benchmarks', [])
            if entry.get('node_id')
        ))
        return (
            self._measure_graph_rev,
            source_sig,
            round(float(self.profile_grade_warn_pct), 3),
            round(float(self.profile_break_warn_pct), 3),
            bool(getattr(self, 'grade_transition_preview_active', False)),
            round(float(getattr(self, 'grade_start_pct', 0.0)), 4),
            round(float(getattr(self, 'grade_target_pct', 0.0)), 4),
            round(float(getattr(self, 'grade_end_pct', 0.0)), 4),
            round(float(getattr(self, 'grade_transition_in_m', 0.0)), 3),
            round(float(getattr(self, 'grade_transition_out_m', 0.0)), 3),
            bench_sig,
            id(self.tiles),
            len(self.tiles),
        )

    def _construction_line(self):
        start = self._get_track_node_state(self.measure_baseline_start_id)
        end = self._get_track_node_state(self.measure_baseline_end_id)
        if not start or not end:
            return None
        dx = end['x'] - start['x']
        dz = end['z'] - start['z']
        length = math.hypot(dx, dz)
        if length < 0.01:
            return None
        return {
            'start': start,
            'end': end,
            'ux': dx / length,
            'uz': dz / length,
            'length': length,
            'heading': (math.degrees(math.atan2(dx, dz)) + 360.0) % 360.0,
        }

    def _baseline_offset(self, ux: float, uz: float, line=None):
        line = line or self._construction_line()
        if not line:
            return None
        rel_x = ux - line['start']['x']
        rel_z = uz - line['start']['z']
        return rel_x * (-line['uz']) + rel_z * line['ux']

    def _measure_step_m(self):
        try:
            return abs(float(self._calc_inputs.get('ms_step', '25.0')))
        except Exception:
            return 0.0

    def _resolve_measure_anchor(self, anchor=None):
        if isinstance(anchor, str):
            return self._get_track_node_state(anchor)
        if isinstance(anchor, dict):
            if anchor.get('id'):
                node = self._get_track_node_state(anchor.get('id'))
                if node:
                    return node
            if anchor.get('x') is not None and anchor.get('z') is not None:
                return {
                    'id': anchor.get('id'),
                    'x': float(anchor.get('x', 0.0)),
                    'y': float(anchor.get('y', 0.0)),
                    'z': float(anchor.get('z', 0.0)),
                    'rotY': float(anchor.get('rotY', 0.0)),
                    'source': anchor.get('source', 'temp'),
                }
        if self.sel_mod_node_id:
            node = self._get_track_node_state(self.sel_mod_node_id)
            if node:
                return node
        last_node_id = getattr(self, '_last_placed_node_id', None)
        if last_node_id:
            node = self._get_track_node_state(last_node_id)
            if node:
                return node
        return None

    def _apply_measure_constraints(self, ux: float, uz: float, anchor=None):
        info = {
            'bearing_locked': False,
            'distance_locked': False,
            'baseline_offset': None,
            'heading': None,
        }
        line = self._construction_line()
        if line:
            info['baseline_offset'] = self._baseline_offset(ux, uz, line)
            info['heading'] = line['heading']
        anchor_node = self._resolve_measure_anchor(anchor)
        if not line or not anchor_node or not (self.measure_bearing_lock or self.measure_distance_lock):
            return ux, uz, info
        dir_x = line['ux']
        dir_z = line['uz']
        rel_x = ux - anchor_node['x']
        rel_z = uz - anchor_node['z']
        along = rel_x * dir_x + rel_z * dir_z
        ux = anchor_node['x'] + along * dir_x
        uz = anchor_node['z'] + along * dir_z
        if self.measure_bearing_lock:
            info['bearing_locked'] = True
        step = self._measure_step_m()
        if self.measure_distance_lock and step > 0.0:
            along = round(along / step) * step
            ux = anchor_node['x'] + along * dir_x
            uz = anchor_node['z'] + along * dir_z
            info['distance_locked'] = True
            info['snap_distance'] = along
        info['baseline_offset'] = self._baseline_offset(ux, uz, line)
        return ux, uz, info

    def _build_live_measure_hud(self, anchor=None, ux=None, uy=None, uz=None):
        anchor_node = self._resolve_measure_anchor(anchor)
        if not anchor_node or ux is None or uy is None or uz is None:
            return []
        dx = float(ux) - anchor_node['x']
        dz = float(uz) - anchor_node['z']
        dy = float(uy) - anchor_node['y']
        run = math.hypot(dx, dz)
        grade = (dy / run * 100.0) if run > 0.01 else None
        heading = ((math.degrees(math.atan2(dx, dz)) + 360.0) % 360.0
                   if run > 0.01 else None)
        lines = []
        if heading is None:
            lines.append('Dist 0.0 m')
        else:
            lines.append(f"Hdg {heading:.1f} deg   Dist {run:.1f} m")
        if grade is None:
            lines.append(f"dY {dy:+.2f} m")
        else:
            lines.append(f"dY {dy:+.2f} m   Grade {abs(grade):.2f}%")
        offset = self._baseline_offset(float(ux), float(uz))
        if offset is not None:
            lines.append(f"Offset {offset:+.2f} m")
        line = self._construction_line()
        if line and (self.measure_bearing_lock or self.measure_distance_lock):
            bits = []
            if self.measure_bearing_lock:
                bits.append(f"Bear {line['heading']:.1f} deg")
            if self.measure_distance_lock and self._measure_step_m() > 0.0:
                bits.append(f"Step {self._measure_step_m():.1f} m")
            if bits:
                lines.append('   '.join(bits))
        return lines

    def _measure_between_nodes(self, start_id: str | None = None, end_id: str | None = None):
        start_id = start_id or self.measure_start_node_id
        end_id = end_id or self.measure_end_node_id
        if not start_id or not end_id:
            return None
        start = self._get_track_node_state(start_id)
        end = self._get_track_node_state(end_id)
        if not start or not end:
            return None
        cache_key = (self._measure_graph_rev, start_id, end_id, self.station_origin_node_id)
        if cache_key in self._measure_pair_cache:
            return self._measure_pair_cache[cache_key]
        dx = end['x'] - start['x']
        dz = end['z'] - start['z']
        dy = end['y'] - start['y']
        direct_xz = math.hypot(dx, dz)
        direct_3d = math.sqrt(direct_xz * direct_xz + dy * dy)
        path = self._shortest_track_path(start_id, end_id)
        along_track = path['distance'] if path else None
        grade_run = along_track if along_track and along_track > 0.01 else direct_xz
        avg_grade = (dy / grade_run * 100.0) if grade_run and grade_run > 0.01 else None
        result = {
            'start': start,
            'end': end,
            'direct_xz_m': direct_xz,
            'direct_3d_m': direct_3d,
            'delta_y_m': dy,
            'along_track_m': along_track,
            'avg_grade_pct': avg_grade,
            'heading_deg': ((math.degrees(math.atan2(dx, dz)) + 360.0) % 360.0
                            if direct_xz > 0.01 else None),
            'path_segment_count': len(path['segments']) if path else 0,
            'start_station_m': self._station_distance_for_node(start_id),
            'end_station_m': self._station_distance_for_node(end_id),
        }
        self._measure_pair_cache[cache_key] = result
        return result

    def _clear_geo_preview(self):
        self.geo_preview = []
        self.geo_preview_meta = {}
        self.alignment_fit_stats = {}
        self.geo_piece_start_node_id = None
        self.geo_piece_start_pose = None
        self.geo_piece_chain = []

    def _geo_piece_anchor_from_selection(self):
        node_id = self.sel_mod_node_id
        node = self._get_track_node_state(node_id)
        if not node:
            return None
        return {
            'id': str(node.get('id', node_id)),
            'x': float(node.get('x', 0.0)),
            'y': float(node.get('y', 0.0)),
            'z': float(node.get('z', 0.0)),
            'rotX': float(node.get('rotX', 0.0)),
            'rotY': float(node.get('rotY', 0.0)),
            'rotZ': float(node.get('rotZ', 0.0)),
            'flipSwitchStand': bool(node.get('flipSwitchStand', False)),
        }

    def _geo_piece_set_start_from_selection(self):
        anchor = self._geo_piece_anchor_from_selection()
        if not anchor:
            self._set_status("Pieces: select a track node to use as the start anchor")
            return False
        self.geo_piece_start_node_id = str(anchor.get('id'))
        self.geo_piece_start_pose = anchor
        self.geo_piece_chain = []
        self.geo_preview = []
        self.geo_preview_meta = {
            'mode': 'pieces',
            'piece_count': 0,
            'start_id': self.geo_piece_start_node_id,
            'end_pose': copy.deepcopy(anchor),
        }
        self._set_status(f"Pieces start set to {self.geo_piece_start_node_id}")
        return True

    def _geo_piece_rebuild_preview(self):
        self.geo_preview = []
        self.geo_preview_meta = {}
        self.alignment_fit_stats = {}
        anchor = copy.deepcopy(self.geo_piece_start_pose)
        if not anchor:
            return False

        if not self.geo_piece_chain:
            self.geo_preview_meta = {
                'mode': 'pieces',
                'piece_count': 0,
                'start_id': self.geo_piece_start_node_id,
                'end_pose': copy.deepcopy(anchor),
                'total_length_m': 0.0,
            }
            return True

        if not self.mod_project:
            self._set_status("Pieces need a loaded mod project")
            return False

        existing_ids = set(self.mod_project.merged_nodes.keys()) | set(self.mod_project.merged_segments.keys())
        preview_nodes: list = []
        preview_segs: list = []
        current = copy.deepcopy(anchor)
        prev_current = copy.deepcopy(anchor)  # tracks previous pose for curvature inference
        total_length_m = 0.0
        radius_samples = []
        warnings = []

        for index, piece in enumerate(self.geo_piece_chain, start=1):
            kind = str(piece.get('kind', 'straight')).lower()
            track_class = str(piece.get('trackClass', self.geo_track_class))
            style = str(piece.get('style', self.geo_style))
            speed_limit = int(piece.get('speedLimit', self.geo_speed))
            gauge = normalize_track_gauge(
                piece.get(
                    'gauge',
                    getattr(self, 'geo_gauge', 'Standard'),
                )
            )
            if kind == 'straight':
                length_m = max(0.1, float(piece.get('length_m', 0.0)))
                heading = math.radians(float(current.get('rotY', 0.0)))
                node_id = self.mod_project.next_node_id(exclude=existing_ids)
                existing_ids.add(node_id)
                seg_id = self.mod_project.next_seg_id(exclude=existing_ids)
                existing_ids.add(seg_id)
                next_node = {
                    'id': node_id,
                    'x': float(current.get('x', 0.0)) + math.sin(heading) * length_m,
                    'y': float(current.get('y', 0.0)),
                    'z': float(current.get('z', 0.0)) + math.cos(heading) * length_m,
                    'rotX': float(current.get('rotX', 0.0)),
                    'rotY': float(current.get('rotY', 0.0)),
                    'rotZ': float(current.get('rotZ', 0.0)),
                    'flipSwitchStand': False,
                }
                preview_nodes.append(next_node)
                preview_segs.append({
                    'id': seg_id,
                    'startId': str(current.get('id')),
                    'endId': node_id,
                    'trackClass': track_class,
                    'style': style,
                    'speedLimit': speed_limit,
                    'priority': 0,
                    'groupId': '',
                    'gauge': gauge,
                })
                prev_current = copy.deepcopy(current)
                current = copy.deepcopy(next_node)
                total_length_m += length_m
                continue

            if kind == 'arc':
                radius_m = max(0.1, float(piece.get('radius_m', 0.0)))
                degrees = abs(float(piece.get('degrees', 0.0)))
                direction = str(piece.get('direction', 'left'))
                n_segments = int(piece.get('n_segments', 0))
                nodes, segs = generate_curve(
                    float(current.get('x', 0.0)),
                    float(current.get('y', 0.0)),
                    float(current.get('z', 0.0)),
                    float(current.get('rotY', 0.0)),
                    radius=radius_m,
                    degrees=degrees,
                    height_change=0.0,
                    direction=direction,
                    n_segments=n_segments,
                    track_class=track_class,
                    style=style,
                    speed_limit=speed_limit,
                    id_prefix='N_piece',
                    seg_prefix='S_piece',
                    existing_ids=existing_ids,
                )
                if not nodes or len(nodes) < 2 or not segs:
                    self._set_status(f"Pieces: failed to build arc {index}")
                    return False
                arc_nodes = copy.deepcopy(nodes[1:])
                arc_segs = copy.deepcopy(segs)
                arc_segs[0]['startId'] = str(current.get('id'))
                for arc_segment in arc_segs:
                    arc_segment['gauge'] = gauge
                preview_nodes.extend(arc_nodes)
                preview_segs.extend(arc_segs)
                prev_current = copy.deepcopy(current)
                current = copy.deepcopy(nodes[-1])
                total_length_m += abs(math.radians(degrees) * radius_m)
                mid_node = nodes[len(nodes) // 2]
                if radius_m < float(self.alignment_min_radius_m):
                    radius_samples.append({
                        'point': (float(mid_node.get('x', 0.0)), float(mid_node.get('z', 0.0))),
                        'radius': radius_m,
                    })
                    warnings.append(
                        f"Piece {index}: radius {radius_m:.1f} m is under the {float(self.alignment_min_radius_m):.0f} m warning threshold"
                    )
                continue

            if kind == 'turnout':
                approach_rotY = float(current.get('rotY', 0.0))
                leg           = max(0.1, float(piece.get('leg_length', 30.0)))
                div_angle     = float(piece.get('diverge_angle', 10.0))
                direction     = str(piece.get('direction', 'left'))
                flip          = bool(piece.get('flip', False))
                div_class     = str(piece.get('divClass', track_class))
                div_speed     = int(piece.get('divSpeedLimit', speed_limit))

                # Auto-derive through_curve from curvature of incoming piece
                # if the user left it at 0
                explicit_tc = float(piece.get('through_curve', 0.0))
                if explicit_tc == 0.0:
                    prev_rotY = float(prev_current.get('rotY', approach_rotY))
                    prev_dist = max(1.0, math.hypot(
                        float(current.get('x', 0.0)) - float(prev_current.get('x', 0.0)),
                        float(current.get('z', 0.0)) - float(prev_current.get('z', 0.0))
                    ))
                    raw_delta = ((approach_rotY - prev_rotY + 180) % 360) - 180
                    curvature = raw_delta / prev_dist
                    through_curve_angle = curvature * leg
                else:
                    through_curve_angle = explicit_tc

                # Place frog one leg-length ahead of current so entry coincides with current
                heading = math.radians(approach_rotY)
                sw_x = float(current.get('x', 0.0)) + math.sin(heading) * leg
                sw_y = float(current.get('y', 0.0))
                sw_z = float(current.get('z', 0.0)) + math.cos(heading) * leg

                t_nodes, t_segs, sw_id, ent_id, thru_id, div_id = generate_turnout(
                    sw_x, sw_y, sw_z,
                    approach_rotY,
                    diverge_angle=div_angle,
                    leg_length=leg,
                    direction=direction,
                    flip_switch_stand=flip,
                    track_class=track_class,
                    diverge_class=div_class,
                    style=style,
                    speed_limit=speed_limit,
                    diverge_speed=div_speed,
                    through_curve_angle=through_curve_angle,
                    id_prefix='N_piece',
                    seg_prefix='S_piece',
                    existing_ids=existing_ids,
                )
                # t_nodes: [sw, entry(≈current), through, diverge]
                # t_segs:  [entry->sw, sw->through, sw->diverge]
                # Wire entry seg from the actual current node; skip generated entry node
                t_segs[0]['startId'] = str(current.get('id'))
                for n in t_nodes:
                    existing_ids.add(n['id'])
                for s in t_segs:
                    existing_ids.add(s['id'])
                    s['gauge'] = gauge
                # Add sw, through, diverge — not the redundant entry node
                preview_nodes.append(copy.deepcopy(t_nodes[0]))  # sw / frog
                preview_nodes.append(copy.deepcopy(t_nodes[2]))  # through
                preview_nodes.append(copy.deepcopy(t_nodes[3]))  # diverge
                preview_segs.extend(copy.deepcopy(t_segs))
                # Through leg is the chain continuation
                prev_current = copy.deepcopy(current)
                current = copy.deepcopy(t_nodes[2])
                total_length_m += leg * 2  # entry leg + through leg
                continue

            self._set_status(f"Pieces: unsupported piece type '{piece.get('kind', '')}'")
            return False

        self.geo_preview = [(preview_nodes, preview_segs)]
        self.geo_preview_meta = {
            'mode': 'pieces',
            'piece_count': len(self.geo_piece_chain),
            'start_id': self.geo_piece_start_node_id,
            'end_pose': copy.deepcopy(current),
            'total_length_m': total_length_m,
            'radius_warnings': radius_samples,
            'warnings': warnings,
        }
        return True

    def _geo_piece_add_current(self):
        if not self.mod_project:
            self._set_status("Pieces need a loaded mod project")
            return
        if not self.geo_piece_start_pose and not self._geo_piece_set_start_from_selection():
            return
        piece_type = str(self.geo_piece_type or 'Straight')
        if piece_type == 'Straight':
            length_m = max(0.1, float(self.geo_piece_length))
            descriptor = {
                'kind': 'straight',
                'length_m': length_m,
                'trackClass': self.geo_track_class,
                'style': self.geo_style,
                'speedLimit': int(self.geo_speed),
                'gauge': getattr(self, 'geo_gauge', 'Standard'),
            }
            summary = f"Straight {length_m:.1f} m"
        elif piece_type == 'Arc':
            radius_m = max(0.1, float(self.geo_radius))
            degrees = abs(float(self.geo_degrees))
            descriptor = {
                'kind': 'arc',
                'radius_m': radius_m,
                'degrees': degrees,
                'direction': self.geo_direction,
                'n_segments': int(self.geo_n_segs),
                'trackClass': self.geo_track_class,
                'style': self.geo_style,
                'speedLimit': int(self.geo_speed),
                'gauge': getattr(self, 'geo_gauge', 'Standard'),
            }
            summary = f"Arc R {radius_m:.1f} m  {degrees:.1f}° {self.geo_direction}"
        elif piece_type == 'Turnout':
            descriptor = {
                'kind': 'turnout',
                'leg_length':    float(self.turnout_leg_length),
                'diverge_angle': float(self.turnout_diverge_angle),
                'through_curve': float(self.turnout_through_curve),
                'direction':     self.turnout_direction,
                'flip':          bool(self.turnout_flip),
                'trackClass':    self.geo_track_class,
                'divClass':      self.turnout_div_class,
                'style':         self.geo_style,
                'speedLimit':    int(self.geo_speed),
                'divSpeedLimit': int(self.turnout_div_speed),
                'gauge':         getattr(
                    self, 'geo_gauge', 'Standard'
                ),
            }
            summary = (
                f"Turnout  leg {self.turnout_leg_length:.1f} m  "
                f"{self.turnout_diverge_angle:.1f}°  {self.turnout_direction}"
            )
        else:
            self._set_status(f"Pieces: unknown type '{piece_type}'")
            return
        self.geo_piece_chain.append(descriptor)
        if not self._geo_piece_rebuild_preview():
            self.geo_piece_chain.pop()
            self._geo_piece_rebuild_preview()
            return
        end_pose = self.geo_preview_meta.get('end_pose', {}) or {}
        self._set_status(
            f"Pieces: added {summary}  -> end ({float(end_pose.get('x', 0.0)):.1f}, {float(end_pose.get('z', 0.0)):.1f})"
        )

    def _geo_piece_undo_last(self):
        if not self.geo_piece_chain:
            self._set_status("Pieces: nothing to undo")
            return
        removed = self.geo_piece_chain.pop()
        self._geo_piece_rebuild_preview()
        self._set_status(f"Pieces: removed last {removed.get('kind', 'piece')}")

    def _geo_preview_errors(self):
        errors = self.geo_preview_meta.get('errors', [])
        return [str(err) for err in errors if err]

    def _geo_preview_warnings(self):
        warnings = self.geo_preview_meta.get('warnings', [])
        return [str(warn) for warn in warnings if warn]

    def _geo_preview_radius_warnings(self):
        samples = self.geo_preview_meta.get('radius_warnings', [])
        if not samples:
            samples = [sample for sample in self.geo_preview_meta.get('warnings', [])
                       if isinstance(sample, dict)]
        return [sample for sample in samples if isinstance(sample, dict) and sample.get('point')]

    def _geo_preview_commit_enabled(self):
        return bool(self.geo_preview) and not self._geo_preview_errors()

    def _build_curve_preview_meta(self, preview_points):
        radius_warnings = [
            sample for sample in local_radius_samples(preview_points)
            if float(sample.get('radius', 0.0)) < float(self.alignment_min_radius_m)
        ]
        errors = []
        radius = float(self.geo_radius)
        arc_deg = abs(float(self.geo_degrees))
        if radius <= 0.0:
            errors.append("Arc radius must be positive")
        elif radius < float(self.alignment_min_radius_m):
            errors.append(
                f"Arc radius {radius:.1f} m is under the limit of "
                f"{self.alignment_min_radius_m:.0f} m"
            )
        if arc_deg < 0.1:
            errors.append("Arc angle must be greater than 0 degrees")
        return {
            'mode': 'curve',
            'radius_warnings': radius_warnings,
            'warnings': [],
            'errors': errors,
        }

    def _build_fit_arc_preview_meta(self, fit, preview_points):
        radius_warnings = [
            sample for sample in local_radius_samples(preview_points)
            if float(sample.get('radius', 0.0)) < float(self.alignment_min_radius_m)
        ]
        errors = []
        warnings = []
        radius = float(fit.get('radius', 0.0))
        if radius <= 0.0:
            errors.append("Fit Arc solved an invalid radius")
        elif radius < float(self.alignment_min_radius_m):
            errors.append(
                f"Fit radius {radius:.1f} m is under the limit of "
                f"{self.alignment_min_radius_m:.0f} m"
            )
        rms = float(fit.get('rms_error', 0.0))
        if rms > 2.0:
            warnings.append(
                f"High fit error: RMS {rms:.2f} m means the source chain is not close to a true arc"
            )
        return {
            'mode': 'fit_arc',
            'fit': fit,
            'radius_warnings': radius_warnings,
            'warnings': warnings,
            'errors': errors,
        }

    def _build_turnout_preview_meta(
        self,
        conn_segs,
        entry_segs,
        forward_segs,
        leg,
        diverge_angle_deg,
        diverge_radius,
        diverge_point,
        through_angle_deg,
        through_radius,
        through_point,
        approach_grade_pct,
    ):
        errors = []
        warnings = []
        radius_warnings = []
        angle = abs(float(diverge_angle_deg))

        if len(conn_segs) > 2:
            errors.append("Selected node already has more than 2 connected segments")
        if len(entry_segs) > 1:
            errors.append("Selected node has multiple entry segments, so the approach is ambiguous")
        if len(forward_segs) > 1:
            errors.append("Selected node has multiple forward segments, so the through route is ambiguous")
        if leg < float(self.turnout_min_leg_m):
            errors.append(
                f"Turnout leg {leg:.1f} m is under the safe minimum of "
                f"{self.turnout_min_leg_m:.0f} m"
            )
        if angle < 1.0:
            errors.append("Turnout diverge angle must be at least 1 degree")
        elif angle > float(self.turnout_max_angle_deg):
            errors.append(
                f"Turnout diverge angle {angle:.1f} deg exceeds the safe limit of "
                f"{self.turnout_max_angle_deg:.0f} deg"
            )
        elif angle > float(self.turnout_warn_angle_deg):
            warnings.append(
                f"Sharp turnout: {angle:.1f} deg is above the preferred "
                f"{self.turnout_warn_angle_deg:.0f} deg range"
            )

        if diverge_radius is None:
            errors.append("Could not solve a stable diverge radius from this turnout geometry")
        elif diverge_radius < float(self.alignment_min_radius_m):
            errors.append(
                f"Sharp diverge: estimated radius {diverge_radius:.1f} m is under "
                f"{self.alignment_min_radius_m:.0f} m"
            )
            radius_warnings.append({
                'point': diverge_point,
                'radius': float(diverge_radius),
            })

        through_angle = abs(float(through_angle_deg))
        if through_angle > 1e-6:
            if through_radius is None:
                errors.append("Could not solve a stable through-route radius")
            elif through_radius < float(self.alignment_min_radius_m):
                errors.append(
                    f"Sharp through route: radius {through_radius:.1f} m is under "
                    f"{self.alignment_min_radius_m:.0f} m"
                )
                radius_warnings.append({
                    'point': through_point,
                    'radius': float(through_radius),
                })

        if not conn_segs:
            warnings.append("Standalone switch preview: entry and through legs will both be created")
        elif not entry_segs or not forward_segs:
            warnings.append("One turnout leg is inferred because only one route is connected at the node")

        if int(self.turnout_div_speed) > int(self.turnout_speed):
            warnings.append("Diverge speed is higher than the main route speed")

        return {
            'mode': 'turnout',
            'diverge_radius_m': diverge_radius,
            'through_radius_m': through_radius,
            'approach_grade_pct': float(approach_grade_pct),
            'radius_warnings': radius_warnings,
            'warnings': warnings,
            'errors': errors,
        }

    def _segment_curve_radius_m_for_nodes(self, start_node: dict, end_node: dict):
        if not start_node or not end_node:
            return None
        ax, az = float(start_node.get('x', 0.0)), float(start_node.get('z', 0.0))
        bx, bz = float(end_node.get('x', 0.0)), float(end_node.get('z', 0.0))
        ay_deg = float(start_node.get('rotY', 0.0))
        by_deg = float(end_node.get('rotY', 0.0))
        delta = ay_deg - by_deg
        while delta > 180.0:
            delta -= 360.0
        while delta < -180.0:
            delta += 360.0
        ry0 = math.radians(ay_deg)
        ry1 = math.radians(by_deg)
        if delta < 0.0:
            ry0 += math.pi
            ry1 += math.pi
        rx0, rz0 = math.cos(ry0), -math.sin(ry0)
        rx1, rz1 = math.cos(ry1), -math.sin(ry1)
        denom = rx0 * rz1 - rz0 * rx1
        if abs(denom) < 1e-6:
            return None
        dx = bx - ax
        dz = bz - az
        t = (dx * rz1 - dz * rx1) / denom
        cx = ax + t * rx0
        cz = az + t * rz0
        ra = math.hypot(ax - cx, az - cz)
        rb = math.hypot(bx - cx, bz - cz)
        radius = (ra + rb) * 0.5
        if radius <= 0.01 or not math.isfinite(radius):
            return None
        return radius

    def _alignment_source_chain(self):
        if not self.mod_project:
            return None
        if len(self.grade_chain) >= 2:
            nodes = []
            node_ids = []
            for node_id in self.grade_chain:
                node = self.mod_project.merged_nodes.get(node_id)
                if not node or node.get('deleted'):
                    continue
                nodes.append(node)
                node_ids.append(node_id)
            if len(nodes) >= 2:
                return {
                    'kind': 'grade_chain',
                    'label': f"Grade chain ({len(nodes)} nodes)",
                    'nodes': nodes,
                    'node_ids': node_ids,
                    'segments': [],
                }
        if self.sel_mod_seg_id:
            seg = self.mod_project.merged_segments.get(self.sel_mod_seg_id)
            if seg:
                start_id = seg.get('startId', '')
                end_id = seg.get('endId', '')
                start = self.mod_project.merged_nodes.get(start_id)
                end = self.mod_project.merged_nodes.get(end_id)
                if start and end and not start.get('deleted') and not end.get('deleted'):
                    return {
                        'kind': 'segment',
                        'label': f"Selected segment {self.sel_mod_seg_id}",
                        'nodes': [start, end],
                        'node_ids': [start_id, end_id],
                        'segments': [seg],
                    }
        return None

    def _alignment_source_points(self, source=None):
        source = source or self._alignment_source_chain()
        if not source:
            return []
        return [
            (float(node.get('x', 0.0)), float(node.get('z', 0.0)))
            for node in source.get('nodes', [])
        ]

    def _alignment_source_polyline(self, source=None):
        source = source or self._alignment_source_chain()
        if not source:
            return []
        if source.get('kind') == 'segment':
            seg_id = source.get('segments', [{}])[0].get('id')
            if seg_id:
                for seg in self._iter_active_track_segments():
                    if seg.get('id') == seg_id and seg.get('points'):
                        return [(float(x), float(z)) for x, z in seg.get('points', [])]
        return self._alignment_source_points(source)

    def _alignment_guide_points_xz(self):
        return [
            (float(point.get('x', 0.0)), float(point.get('z', 0.0)))
            for point in self.alignment_guide_points
        ]

    def _alignment_current_deviation(self, source=None):
        return deviation_samples(
            self._alignment_guide_points_xz(),
            self._alignment_source_polyline(source),
        )

    def _alignment_current_radius_warnings(self, source=None):
        source_points = self._alignment_source_points(source)
        warnings = []
        for sample in local_radius_samples(source_points):
            radius = float(sample.get('radius', 0.0))
            if radius <= 0.0 or radius >= float(self.alignment_min_radius_m):
                continue
            warnings.append(sample)
        return warnings

    def _alignment_chain_contains_turnout(self, source=None):
        source = source or self._alignment_source_chain()
        if not source or not self.mod_project:
            return False
        for node_id in source.get('node_ids', []):
            if len(self.mod_project.segments_for_node(node_id)) > 2:
                return True
        return False

    def _alignment_use_source_as_guide(self):
        source = self._alignment_source_chain()
        if not source:
            self._set_status("Guide path: select a segment or build a grade chain first")
            return
        self.alignment_guide_points = [
            {
                'x': float(node.get('x', 0.0)),
                'y': float(node.get('y', 0.0)),
                'z': float(node.get('z', 0.0)),
            }
            for node in source.get('nodes', [])
        ]
        guide_len = alignment_polyline_length(self._alignment_guide_points_xz())
        self._set_status(
            f"Guide path copied from {source.get('label', 'source')} - "
            f"{len(self.alignment_guide_points)} pts, {guide_len:.1f} m"
        )

    def _alignment_add_guide_point_at(self, sx: float, sy: float):
        ux, uz = self.screen_to_unity(sx, sy)
        anchor = self._resolve_measure_anchor()
        ux, uz, _lock_info = self._apply_measure_constraints(ux, uz, anchor=anchor)
        uy = self._sample_terrain_y(ux, uz) or 0.0
        self.alignment_guide_points.append({'x': ux, 'y': uy, 'z': uz})
        guide_len = alignment_polyline_length(self._alignment_guide_points_xz())
        self._set_status(
            f"Guide point {len(self.alignment_guide_points)} added at "
            f"({ux:.1f}, {uz:.1f}) - {guide_len:.1f} m"
        )

    def _alignment_pop_guide_point(self):
        if not self.alignment_guide_points:
            self._set_status("Guide path is already empty")
            return
        removed = self.alignment_guide_points.pop()
        self._set_status(
            f"Guide point removed ({removed.get('x', 0.0):.1f}, "
            f"{removed.get('z', 0.0):.1f})"
        )

    def _profile_dock_height(self) -> int:
        if not getattr(self, 'profile_panel', False):
            return 0
        _w, h = self.screen.get_size()
        max_h = max(180, h - PANEL_H - (TOOLBAR_H if self.edit_mode else 0) - STATUS_H - 80)
        pref = int(getattr(self, 'profile_dock_h', 250) or 250)
        return max(180, min(max_h, pref))

    def _profile_panel_top(self) -> int:
        _w, h = self.screen.get_size()
        return h - STATUS_H - self._profile_dock_height()

    def _profile_canvas_bottom(self) -> int:
        _w, h = self.screen.get_size()
        return h - STATUS_H - self._profile_dock_height()

    def _profile_anchor_node_id(self) -> str | None:
        for node_id in (
                getattr(self, 'profile_selected_node_id', None),
                getattr(self, 'profile_hover_node_id', None),
                self.sel_mod_node_id):
            if node_id:
                return node_id
        return None

    def _find_track_segment_between(self, start_id: str, end_id: str, seg_id: str | None = None):
        for seg in self._iter_active_track_segments():
            if seg_id and seg.get('id') != seg_id:
                continue
            a_id = seg.get('start_id')
            b_id = seg.get('end_id')
            if a_id == start_id and b_id == end_id:
                return dict(seg), False
            if a_id == end_id and b_id == start_id:
                return dict(seg), True
        if seg_id:
            seg_state = self._get_track_segment_state(seg_id)
            if seg_state:
                points = self.track_segment_points.get(seg_id) if not self.mod_project else None
                return {
                    'id': seg_id,
                    'start_id': seg_state.get('startId'),
                    'end_id': seg_state.get('endId'),
                    'points': points,
                    'plan_m': self._polyline_length_xz(points) if points else 0.0,
                }, bool(seg_state.get('startId') == end_id and seg_state.get('endId') == start_id)
        return None, False

    def _profile_source_chain(self):
        if len(self.grade_chain) >= 2:
            nodes = []
            node_ids = []
            seg_ids = []
            for node_id in self.grade_chain:
                node = self._get_track_node_state(node_id)
                if not node:
                    continue
                nodes.append(node)
                node_ids.append(node_id)
            if len(nodes) >= 2:
                for a_id, b_id in zip(node_ids, node_ids[1:]):
                    seg, _rev = self._find_track_segment_between(a_id, b_id)
                    seg_ids.append(seg.get('id') if seg else None)
                return {
                    'kind': 'grade_chain',
                    'label': f"Grade chain ({len(nodes)} nodes)",
                    'node_ids': node_ids,
                    'nodes': nodes,
                    'segment_ids': seg_ids,
                }

        if (self.measure_start_node_id and self.measure_end_node_id and
                self.measure_start_node_id != self.measure_end_node_id):
            path = self._shortest_track_path(self.measure_start_node_id, self.measure_end_node_id)
            if path and len(path.get('nodes', [])) >= 2:
                node_ids = [node_id for node_id in path.get('nodes', []) if self._get_track_node_state(node_id)]
                nodes = [self._get_track_node_state(node_id) for node_id in node_ids]
                if len(nodes) >= 2:
                    return {
                        'kind': 'measure_path',
                        'label': f"Measure path ({len(nodes)} nodes)",
                        'node_ids': node_ids,
                        'nodes': nodes,
                        'segment_ids': list(path.get('segments', [])),
                    }

        if self.sel_mod_seg_id:
            seg = self._get_track_segment_state(self.sel_mod_seg_id)
            if seg:
                start_id = seg.get('startId')
                end_id = seg.get('endId')
                start = self._get_track_node_state(start_id)
                end = self._get_track_node_state(end_id)
                if start and end:
                    return {
                        'kind': 'segment',
                        'label': f"Selected segment {self.sel_mod_seg_id}",
                        'node_ids': [start_id, end_id],
                        'nodes': [start, end],
                        'segment_ids': [self.sel_mod_seg_id],
                    }

        return None

    def _calculate_grade_transition_preview(self, source: dict, node_marks: list) -> dict:
        """Build node and plot samples for the active grade-chain settings."""
        if not source or source.get('kind') != 'grade_chain':
            return {
                'points': [],
                'dense_points': [],
                'errors': ["smooth transitions require a Grade chain"],
                'warnings': [],
            }
        if len(node_marks) < 2:
            return {
                'points': [],
                'dense_points': [],
                'errors': ["smooth transitions need at least two chain nodes"],
                'warnings': [],
            }

        stations = [float(mark.get('station_m', 0.0)) for mark in node_marks]
        start_y = float(node_marks[0].get('track_y', 0.0))
        node_result = build_vertical_alignment(
            stations,
            start_y,
            self.grade_start_pct,
            self.grade_target_pct,
            self.grade_end_pct,
            self.grade_transition_in_m,
            self.grade_transition_out_m,
        )
        result = dict(node_result)
        result['dense_points'] = []
        result['node_points'] = []
        if node_result.get('errors'):
            return result

        total_length = float(node_result.get('total_length_m', 0.0))
        dense_stations = dense_vertical_alignment_stations(
            total_length,
            self.grade_transition_in_m,
            self.grade_transition_out_m,
        )
        dense_result = build_vertical_alignment(
            dense_stations,
            start_y,
            self.grade_start_pct,
            self.grade_target_pct,
            self.grade_end_pct,
            self.grade_transition_in_m,
            self.grade_transition_out_m,
        )
        result['dense_points'] = list(dense_result.get('points', []))
        result['node_points'] = [
            dict(point, node_id=str(mark.get('id', '')))
            for point, mark in zip(
                node_result.get('points', []),
                node_marks,
            )
        ]

        warnings = list(result.get('warnings', []))
        boundary_specs = [
            ("entry transition end", float(self.grade_transition_in_m)),
            (
                "exit transition start",
                total_length - float(self.grade_transition_out_m),
            ),
        ]
        for label, boundary in boundary_specs:
            if boundary <= 0.001 or boundary >= total_length - 0.001:
                continue
            nearest = min(abs(station - boundary) for station in stations)
            if nearest > 10.0:
                warnings.append(
                    f"no node within {nearest:.1f} m of the {label}; "
                    "add a node there for a more exact curve"
                )
        result['warnings'] = warnings
        return result

    def _build_profile_data(self):
        preview_node_id = getattr(self, 'profile_drag_node_id', None)
        preview_y = getattr(self, 'profile_drag_preview_y', None)
        preview_active = preview_node_id is not None and preview_y is not None
        cache_key = None if preview_active else self._profile_base_cache_key()
        if cache_key is not None and cache_key == self._profile_cache_key and self._profile_cache_data is not None:
            return self._profile_cache_data

        source = self._profile_source_chain()
        if not source:
            return {'source': None}

        nodes = []
        for node in source.get('nodes', []):
            current = dict(node)
            if preview_node_id and preview_y is not None and current.get('id') == preview_node_id:
                current['y'] = float(preview_y)
            nodes.append(current)
        node_ids = list(source.get('node_ids', []))
        seg_ids = list(source.get('segment_ids', []))

        samples = []
        node_marks = []
        grade_labels = []
        warnings = []
        total_station = 0.0
        max_cut_fill = 0.0
        prev_grade = None

        def terrain_at(x_val: float, z_val: float, fallback_y: float):
            if not self.tiles:
                return fallback_y
            sampled = float(self._sample_terrain_y(x_val, z_val))
            if sampled == 0.0 and not any(list(self.tiles.values())):
                return fallback_y
            return sampled

        for idx in range(len(nodes) - 1):
            start = nodes[idx]
            end = nodes[idx + 1]
            seg_id = seg_ids[idx] if idx < len(seg_ids) else None
            seg, reversed_points = self._find_track_segment_between(
                str(start.get('id', '')),
                str(end.get('id', '')),
                seg_id,
            )
            xz_points = list(seg.get('points') or []) if seg else []
            if not xz_points:
                xz_points = [
                    (float(start.get('x', 0.0)), float(start.get('z', 0.0))),
                    (float(end.get('x', 0.0)), float(end.get('z', 0.0))),
                ]
            if reversed_points:
                xz_points.reverse()
            if len(xz_points) < 2:
                continue

            seg_len = self._polyline_length_xz(xz_points)
            seg_len = max(seg_len, 0.01)
            seg_run = total_station
            acc_len = 0.0
            last_x, last_z = xz_points[0]
            if idx == 0:
                first_track_y = float(start.get('y', 0.0))
                first_terrain_y = terrain_at(last_x, last_z, first_track_y)
                samples.append({
                    'station_m': 0.0,
                    'x': float(last_x),
                    'z': float(last_z),
                    'track_y': first_track_y,
                    'terrain_y': first_terrain_y,
                    'node_id': start.get('id'),
                })
                node_marks.append({
                    'id': start.get('id'),
                    'station_m': 0.0,
                    'track_y': first_track_y,
                    'terrain_y': first_terrain_y,
                    'x': float(last_x),
                    'z': float(last_z),
                })

            for pt_index in range(1, len(xz_points)):
                px, pz = xz_points[pt_index]
                acc_len += math.hypot(px - last_x, pz - last_z)
                frac = acc_len / seg_len
                station_m = seg_run + acc_len
                track_y = float(start.get('y', 0.0)) + (float(end.get('y', 0.0)) - float(start.get('y', 0.0))) * frac
                terrain_y = terrain_at(px, pz, track_y)
                max_cut_fill = max(max_cut_fill, abs(track_y - terrain_y))
                samples.append({
                    'station_m': station_m,
                    'x': float(px),
                    'z': float(pz),
                    'track_y': track_y,
                    'terrain_y': terrain_y,
                    'node_id': end.get('id') if pt_index == len(xz_points) - 1 else None,
                })
                last_x, last_z = px, pz

            total_station += seg_len
            end_track_y = float(end.get('y', 0.0))
            end_terrain_y = samples[-1]['terrain_y'] if samples else terrain_at(float(end.get('x', 0.0)), float(end.get('z', 0.0)), end_track_y)
            node_marks.append({
                'id': end.get('id'),
                'station_m': total_station,
                'track_y': end_track_y,
                'terrain_y': end_terrain_y,
                'x': float(end.get('x', 0.0)),
                'z': float(end.get('z', 0.0)),
            })

            grade_pct = ((float(end.get('y', 0.0)) - float(start.get('y', 0.0))) / seg_len * 100.0
                         if seg_len > 0.01 else 0.0)
            grade_labels.append({
                'station_m': seg_run + seg_len * 0.5,
                'grade_pct': grade_pct,
                'start_id': start.get('id'),
                'end_id': end.get('id'),
            })
            if abs(grade_pct) >= float(self.profile_grade_warn_pct):
                warnings.append({
                    'kind': 'grade',
                    'station_m': seg_run + seg_len * 0.5,
                    'text': f"Steep {abs(grade_pct):.2f}%",
                    'severity': 'warn' if abs(grade_pct) < float(self.profile_grade_warn_pct) + 1.5 else 'error',
                })
            if prev_grade is not None:
                delta_grade = grade_pct - prev_grade
                if abs(delta_grade) >= float(self.profile_break_warn_pct):
                    warnings.append({
                        'kind': 'break',
                        'station_m': seg_run,
                        'text': f"Grade break {delta_grade:+.2f}%",
                        'severity': 'warn' if abs(delta_grade) < float(self.profile_break_warn_pct) + 1.0 else 'error',
                    })
            prev_grade = grade_pct

        bench_nodes = []
        benchmark_lookup = {str(entry.get('node_id')): entry for entry in getattr(self, 'profile_benchmarks', []) if entry.get('node_id')}
        for mark in node_marks:
            entry = benchmark_lookup.get(str(mark.get('id')))
            if not entry:
                continue
            bench_nodes.append({
                'id': mark.get('id'),
                'label': str(entry.get('label') or mark.get('id') or 'Bench'),
                'station_m': float(mark.get('station_m', 0.0)),
                'track_y': float(entry.get('y', mark.get('track_y', 0.0))),
            })

        vertical_preview = None
        if getattr(self, 'grade_transition_preview_active', False):
            vertical_preview = self._calculate_grade_transition_preview(
                source,
                node_marks,
            )
            for message in vertical_preview.get('errors', []):
                warnings.append({
                    'kind': 'vertical_curve',
                    'station_m': 0.0,
                    'text': message,
                    'severity': 'error',
                })
            for message in vertical_preview.get('warnings', []):
                warnings.append({
                    'kind': 'vertical_curve',
                    'station_m': 0.0,
                    'text': message,
                    'severity': 'warn',
                })

        y_values = []
        for sample in samples:
            y_values.append(float(sample.get('track_y', 0.0)))
            y_values.append(float(sample.get('terrain_y', 0.0)))
        for bench in bench_nodes:
            y_values.append(float(bench.get('track_y', 0.0)))
        if vertical_preview:
            for point in vertical_preview.get('dense_points', []):
                y_values.append(float(point.get('y', 0.0)))
        if not y_values:
            y_values = [0.0, 1.0]
        y_min = min(y_values)
        y_max = max(y_values)
        if abs(y_max - y_min) < 1.0:
            y_min -= 1.0
            y_max += 1.0
        pad = max(2.0, (y_max - y_min) * 0.12)
        y_min -= pad
        y_max += pad

        result = {
            'source': source,
            'nodes': nodes,
            'node_ids': node_ids,
            'samples': samples,
            'node_marks': node_marks,
            'grade_labels': grade_labels,
            'warnings': warnings,
            'benchmarks': bench_nodes,
            'station_end_m': total_station,
            'max_cut_fill_m': max_cut_fill,
            'y_min': y_min,
            'y_max': y_max,
            'vertical_preview': vertical_preview,
        }
        if cache_key is not None:
            self._profile_cache_key = cache_key
            self._profile_cache_data = result
        return result

    def _add_profile_benchmark(self):
        data = self._build_profile_data()
        source = data.get('source')
        anchor_id = self._profile_anchor_node_id()
        if not source or not anchor_id:
            self._set_status("Profile benchmark: select or hover a node in the active chain first")
            return
        node_ids = set(str(node_id) for node_id in source.get('node_ids', []))
        if str(anchor_id) not in node_ids:
            self._set_status("Profile benchmark: selected node is not in the active profile chain")
            return
        node = next((mark for mark in data.get('node_marks', []) if str(mark.get('id')) == str(anchor_id)), None)
        if not node:
            self._set_status("Profile benchmark: anchor node not found")
            return
        updated = []
        replaced = False
        for entry in self.profile_benchmarks:
            if str(entry.get('node_id')) == str(anchor_id):
                updated.append({
                    'node_id': str(anchor_id),
                    'y': float(node.get('track_y', 0.0)),
                    'label': f"Bench {anchor_id}",
                })
                replaced = True
            else:
                updated.append(entry)
        if not replaced:
            updated.append({
                'node_id': str(anchor_id),
                'y': float(node.get('track_y', 0.0)),
                'label': f"Bench {anchor_id}",
            })
        self.profile_benchmarks = updated
        self._set_status(f"Profile benchmark set at {anchor_id}  {float(node.get('track_y', 0.0)):.2f} m")

    def _clear_profile_benchmarks(self):
        count = len(self.profile_benchmarks)
        self.profile_benchmarks = []
        self._set_status(f"Cleared {count} profile benchmark(s)" if count else "Profile benchmarks already clear")

    def _commit_profile_node_y(self, node_id: str, new_y: float):
        if not self.mod_project:
            self._set_status("Profile edit needs a loaded mod project")
            return
        graph_layer = self.mod_project.get_graph_layer()
        if graph_layer is None:
            self._set_status("No writable game-graph layer found for profile edit")
            return
        existing = self.mod_project.merged_nodes.get(node_id)
        if not existing:
            self._set_status(f"Profile edit node not found: {node_id}")
            return
        self._push_undo(f"profile y {node_id}")
        source = self._profile_source_chain()
        source_nodes = list((source or {}).get('nodes', []))
        source_ids = [str(node.get('id')) for node in source_nodes]
        if node_id in source_ids and len(source_nodes) >= 2:
            y_by_id = {
                str(node.get('id')): float(node.get('y', 0.0))
                for node in source_nodes
            }
            y_by_id[node_id] = float(new_y)
            grade_by_id = self._grades_for_node_elevations(
                source_nodes, y_by_id
            )
            moved_index = source_ids.index(node_id)
            affected = range(
                max(0, moved_index - 1),
                min(len(source_nodes), moved_index + 2),
            )
            for index in affected:
                node = source_nodes[index]
                current_id = str(node.get('id'))
                graph_layer.set_node(
                    current_id,
                    float(node.get('x', 0.0)),
                    float(y_by_id[current_id]),
                    float(node.get('z', 0.0)),
                    self._grade_pitch_for_node(
                        source_nodes,
                        index,
                        grade_by_id.get(current_id, 0.0),
                    ),
                    float(node.get('rotY', 0.0)),
                    float(node.get('rotZ', 0.0)),
                    bool(node.get('flipSwitchStand', False)),
                )
        else:
            graph_layer.set_node(
                node_id,
                float(existing.get('x', 0.0)),
                float(new_y),
                float(existing.get('z', 0.0)),
                float(existing.get('rotX', 0.0)),
                float(existing.get('rotY', 0.0)),
                float(existing.get('rotZ', 0.0)),
                bool(existing.get('flipSwitchStand', False)),
            )
        self._commit_mod_layer_edit(graph_layer, graph_changed=True)
        self.sel_mod_node_id = node_id
        self.sel_mod_seg_id = None
        self.profile_selected_node_id = node_id
        self._set_status(f"Profile: {node_id} Y -> {float(new_y):.2f} m")

    def _spliney_heading_deg(self, start: dict, end: dict, fallback: float = 0.0) -> float:
        dx = float(end.get('x', 0.0)) - float(start.get('x', 0.0))
        dz = float(end.get('z', 0.0)) - float(start.get('z', 0.0))
        if math.hypot(dx, dz) < 0.001:
            return float(fallback) % 360.0
        return (math.degrees(math.atan2(dx, dz)) + 360.0) % 360.0

    def _spliney_pitch_deg(self, start: dict, end: dict, fallback: float = 0.0) -> float:
        dx = float(end.get('x', 0.0)) - float(start.get('x', 0.0))
        dy = float(end.get('y', 0.0)) - float(start.get('y', 0.0))
        dz = float(end.get('z', 0.0)) - float(start.get('z', 0.0))
        run = math.hypot(dx, dz)
        if run < 0.001:
            return float(fallback)
        # Project convention: negative rotX means uphill.
        return -math.degrees(math.atan2(dy, run))

    def _spliney_candidate_layers(self, style: str | None = None) -> list[tuple[int, Layer]]:
        if not self.mod_project:
            return []
        preferred_term = 'river' if str(style).lower() == 'river' else 'road'
        active_source = self.mod_project.active_source
        active_source_idx = (
            self.mod_project.sources.index(active_source)
            if active_source in self.mod_project.sources else None
        )
        candidates = [
            (li, layer)
            for li, layer in enumerate(self.mod_project.layers)
            if (not layer.read_only
                and layer.layer_type in (LAYER_GRAPH, LAYER_RIVERS, LAYER_OTHER)
                and layer.path.name.lower() not in ('progressions.json', 'progressions-new.json')
                and (active_source_idx is None or getattr(layer, 'source_idx', None) == active_source_idx))
        ]
        if candidates:
            def sort_key(entry):
                li, layer = entry
                name = layer.path.name.lower()
                return (
                    0 if layer is self.mod_project.active_layer else 1,
                    0 if layer.layer_type == LAYER_RIVERS else 1,
                    0 if preferred_term in name else 1,
                    name,
                )
            return sorted(candidates, key=sort_key)
        return []

    def _selected_spliney_target_layer(self, style: str) -> tuple[int | None, Layer | None]:
        target_path = str(getattr(self, 'spliney_target_path', '') or '').strip()
        if not target_path:
            return None, None
        for li, layer in self._spliney_candidate_layers(style):
            if str(layer.path) == target_path or layer.path.name == target_path:
                return li, layer
        return None, None

    def _spliney_target_layer(self, style: str):
        if not self.mod_project:
            return None
        li, layer = self._selected_spliney_target_layer(style)
        if layer is not None:
            return layer
        candidates = self._spliney_candidate_layers(style)
        if candidates:
            li, layer = candidates[0]
            self.spliney_target_path = str(layer.path)
            return layer
        return self.mod_project.get_graph_layer()

    def _spliney_style_defaults(self, style: str) -> dict:
        style = 'River' if str(style).lower() == 'river' else 'Road'
        fallback_profile = (
            'R2_Profile_River_Mountain'
            if style == 'River' else
            'RAM Road profile'
        )
        fallback_width = 65.0 if style == 'River' else 3.5
        if not self.mod_project:
            return {'profile': fallback_profile, 'width': fallback_width}

        active_source = self.mod_project.active_source
        active_source_idx = (
            self.mod_project.sources.index(active_source)
            if active_source in self.mod_project.sources else None
        )

        def scan(source_only: bool):
            profiles = collections.Counter()
            widths = []
            for layer in self.mod_project.layers:
                if layer.read_only:
                    continue
                if source_only and active_source_idx is not None and getattr(layer, 'source_idx', None) != active_source_idx:
                    continue
                for spl in layer.splineys.values():
                    if not isinstance(spl, dict):
                        continue
                    if 'FlowyThing' not in str(spl.get('handler', '')):
                        continue
                    if str(spl.get('style', 'Road')).lower() != style.lower():
                        continue
                    profile = str(spl.get('profile', '')).strip()
                    if profile:
                        profiles[profile] += 1
                    for pt in spl.get('points', []):
                        if not isinstance(pt, dict):
                            continue
                        try:
                            width = float(pt.get('width', 0.0))
                        except (TypeError, ValueError):
                            width = 0.0
                        if width > 0.0:
                            widths.append(width)
            return profiles, widths

        profiles, widths = scan(source_only=True)
        if not profiles and not widths:
            profiles, widths = scan(source_only=False)

        width_value = fallback_width
        if widths:
            widths = sorted(widths)
            mid = len(widths) // 2
            width_value = widths[mid] if len(widths) % 2 else (widths[mid - 1] + widths[mid]) / 2.0
        profile_value = profiles.most_common(1)[0][0] if profiles else fallback_profile
        return {'profile': profile_value, 'width': width_value}

    def _spliney_seed_heading(self) -> float:
        if self.spliney_use_selected_heading and self.sel_mod_node_id and self.mod_project:
            node = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
            if node and not node.get('deleted'):
                return float(node.get('rotY', 0.0)) % 360.0
        return float(self.spliney_place_rotY) % 360.0

    def _spliney_seed_points(self, ux: float, uz: float, heading_deg: float,
                             length: float, width: float) -> list:
        r = math.radians(float(heading_deg))
        x2 = float(ux) + float(length) * math.sin(r)
        z2 = float(uz) + float(length) * math.cos(r)
        y1 = self._sample_terrain_y(float(ux), float(uz)) or 0.0
        y2 = self._sample_terrain_y(x2, z2) or y1
        return [
            {
                'position': {'x': float(ux), 'y': float(y1), 'z': float(uz)},
                'rotation': {'x': 0.0, 'y': float(heading_deg) % 360.0, 'z': 0.0},
                'width': float(width),
            },
            {
                'position': {'x': float(x2), 'y': float(y2), 'z': float(z2)},
                'rotation': {'x': 0.0, 'y': float(heading_deg) % 360.0, 'z': 0.0},
                'width': float(width),
            },
        ]

    def _flowy_splineys(self) -> list[tuple[str, dict, int]]:
        if not self.mod_project:
            return []
        active_source = self.mod_project.active_source
        active_source_idx = (
            self.mod_project.sources.index(active_source)
            if active_source in self.mod_project.sources else None
        )
        items: list[tuple[str, dict, int]] = []
        for li, layer in enumerate(self.mod_project.layers):
            if layer.read_only:
                continue
            if active_source_idx is not None and getattr(layer, 'source_idx', None) != active_source_idx:
                continue
            for sid, spl in layer.splineys.items():
                if spl and 'FlowyThing' in str(spl.get('handler', '')):
                    items.append((sid, spl, li))
        items.sort(key=lambda item: (self.mod_project.layers[item[2]].path.name.lower(), item[0].lower()))
        return items

    def _river_trace_spacing_m(self, width: float) -> float:
        width_value = max(0.0, float(width))
        target = width_value * 6.0 if width_value > 0.0 else 120.0
        return max(100.0, min(220.0, target))

    def _river_trace_simplify_tolerance_m(self, width: float) -> float:
        spacing = self._river_trace_spacing_m(width)
        return max(8.0, min(28.0, spacing * 0.15))

    def _guide_point_line_distance_xz(self, point: dict, start: dict, end: dict) -> float:
        px = float(point.get('x', 0.0))
        pz = float(point.get('z', 0.0))
        x0 = float(start.get('x', 0.0))
        z0 = float(start.get('z', 0.0))
        x1 = float(end.get('x', 0.0))
        z1 = float(end.get('z', 0.0))
        dx = x1 - x0
        dz = z1 - z0
        denom = dx * dx + dz * dz
        if denom <= 1e-6:
            return math.hypot(px - x0, pz - z0)
        t = ((px - x0) * dx + (pz - z0) * dz) / denom
        t = min(1.0, max(0.0, t))
        proj_x = x0 + dx * t
        proj_z = z0 + dz * t
        return math.hypot(px - proj_x, pz - proj_z)

    def _simplify_guide_path(self, guide: list[dict], tolerance_m: float) -> list[dict]:
        if len(guide) < 3 or tolerance_m <= 0.0:
            return [
                {
                    'x': float(point.get('x', 0.0)),
                    'y': float(point.get('y', 0.0)),
                    'z': float(point.get('z', 0.0)),
                }
                for point in guide
            ]

        cleaned: list[dict] = []
        for point in guide:
            current = {
                'x': float(point.get('x', 0.0)),
                'y': float(point.get('y', 0.0)),
                'z': float(point.get('z', 0.0)),
            }
            if cleaned:
                prev = cleaned[-1]
                if math.hypot(current['x'] - prev['x'], current['z'] - prev['z']) < 0.01:
                    cleaned[-1] = current
                    continue
            cleaned.append(current)
        if len(cleaned) < 3:
            return cleaned

        keep = [False] * len(cleaned)
        keep[0] = True
        keep[-1] = True
        stack = [(0, len(cleaned) - 1)]
        while stack:
            start_idx, end_idx = stack.pop()
            start = cleaned[start_idx]
            end = cleaned[end_idx]
            max_dist = -1.0
            max_idx = -1
            for idx in range(start_idx + 1, end_idx):
                dist = self._guide_point_line_distance_xz(cleaned[idx], start, end)
                if dist > max_dist:
                    max_dist = dist
                    max_idx = idx
            if max_idx >= 0 and max_dist > tolerance_m:
                keep[max_idx] = True
                if max_idx - start_idx > 1:
                    stack.append((start_idx, max_idx))
                if end_idx - max_idx > 1:
                    stack.append((max_idx, end_idx))
        return [cleaned[idx] for idx, keep_pt in enumerate(keep) if keep_pt]

    def _subdivide_guide_spans(self, guide: list[dict], max_span_m: float,
                               max_extra_per_span: int = 2) -> list[dict]:
        if len(guide) < 2 or max_span_m <= 0.0:
            return [
                {
                    'x': float(point.get('x', 0.0)),
                    'y': float(point.get('y', 0.0)),
                    'z': float(point.get('z', 0.0)),
                }
                for point in guide
            ]

        cleaned: list[dict] = []
        for point in guide:
            current = {
                'x': float(point.get('x', 0.0)),
                'y': float(point.get('y', 0.0)),
                'z': float(point.get('z', 0.0)),
            }
            if cleaned:
                prev = cleaned[-1]
                if math.hypot(current['x'] - prev['x'], current['z'] - prev['z']) < 0.01:
                    cleaned[-1] = current
                    continue
            cleaned.append(current)
        if len(cleaned) < 2:
            return cleaned

        points: list[dict] = [dict(cleaned[0])]
        for idx in range(1, len(cleaned)):
            a = cleaned[idx - 1]
            b = cleaned[idx]
            span = math.hypot(b['x'] - a['x'], b['z'] - a['z'])
            if span > max_span_m:
                extra = max(0, math.ceil(span / max_span_m) - 1)
                extra = min(max_extra_per_span, extra)
                for insert_idx in range(1, extra + 1):
                    t = insert_idx / (extra + 1)
                    points.append({
                        'x': a['x'] + (b['x'] - a['x']) * t,
                        'y': a['y'] + (b['y'] - a['y']) * t,
                        'z': a['z'] + (b['z'] - a['z']) * t,
                    })
            points.append(dict(b))
        return points

    def _normalize_spliney_points(self, points: list) -> list:
        normalized: list = []
        total = len(points)
        for idx, source in enumerate(points):
            pt = copy.deepcopy(source)
            pos = dict(pt.get('position', {}) or {})
            rot = dict(pt.get('rotation', {}) or {})
            pos = {
                'x': float(pos.get('x', 0.0)),
                'y': float(pos.get('y', 0.0)),
                'z': float(pos.get('z', 0.0)),
            }
            rot_x = float(rot.get('x', 0.0))
            if idx > 0 and idx + 1 < total:
                prev_pos = dict(points[idx - 1].get('position', {}) or {})
                next_pos = dict(points[idx + 1].get('position', {}) or {})
                rot_y = self._spliney_heading_deg(prev_pos, next_pos, float(rot.get('y', 0.0)))
                rot_x = self._spliney_pitch_deg(prev_pos, next_pos, rot_x)
            elif idx + 1 < total:
                next_pos = dict(points[idx + 1].get('position', {}) or {})
                rot_y = self._spliney_heading_deg(pos, next_pos, float(rot.get('y', 0.0)))
                rot_x = self._spliney_pitch_deg(pos, next_pos, rot_x)
            elif idx > 0:
                prev_pos = dict(points[idx - 1].get('position', {}) or {})
                rot_y = self._spliney_heading_deg(prev_pos, pos, float(rot.get('y', 0.0)))
                rot_x = self._spliney_pitch_deg(prev_pos, pos, rot_x)
            else:
                rot_y = float(rot.get('y', 0.0)) % 360.0
            pt['position'] = pos
            pt['rotation'] = {
                'x': float(rot_x),
                'y': float(rot_y),
                'z': float(rot.get('z', 0.0)),
            }
            if 'width' in pt and pt.get('width') is not None:
                pt['width'] = float(pt.get('width'))
            normalized.append(pt)
        return normalized

    def _reverse_flowy_points(self, points: list) -> list:
        reversed_points: list = []
        for source in reversed(points):
            pt = copy.deepcopy(source)
            rot = dict(pt.get('rotation', {}) or {})
            pt['rotation'] = {
                'x': float(rot.get('x', 0.0)),
                'y': (float(rot.get('y', 0.0)) + 180.0) % 360.0,
                'z': float(rot.get('z', 0.0)),
            }
            reversed_points.append(pt)
        return reversed_points

    def _fit_flowy_points_to_terrain(self, points: list, style: str = 'Road',
                                     normalize_rotations: bool = False) -> tuple[list, dict]:
        fitted: list = []
        for source in points:
            pt = copy.deepcopy(source)
            pos = dict(pt.get('position', {}) or {})
            x = float(pos.get('x', 0.0))
            z = float(pos.get('z', 0.0))
            pos['x'] = x
            pos['z'] = z
            pos['y'] = float(self._sample_terrain_y(x, z))
            pt['position'] = pos
            fitted.append(pt)

        reversed_flow = False
        style_name = str(style or 'Road')
        if style_name.lower() == 'river' and len(fitted) >= 2:
            start_y = float(fitted[0].get('position', {}).get('y', 0.0))
            end_y = float(fitted[-1].get('position', {}).get('y', 0.0))
            if end_y > start_y:
                fitted = self._reverse_flowy_points(fitted)
                reversed_flow = True
            if len(fitted) >= 3:
                y_values = [float(pt.get('position', {}).get('y', 0.0)) for pt in fitted]
                for _ in range(2):
                    smoothed = list(y_values)
                    for idx in range(1, len(y_values) - 1):
                        smoothed[idx] = (
                            y_values[idx - 1] +
                            (2.0 * y_values[idx]) +
                            y_values[idx + 1]
                        ) / 4.0
                    y_values = smoothed
                for pt, y_value in zip(fitted, y_values):
                    pt.setdefault('position', {})['y'] = float(y_value)

        if normalize_rotations:
            fitted = self._normalize_spliney_points(fitted)
        start_y = float(fitted[0].get('position', {}).get('y', 0.0)) if fitted else 0.0
        end_y = float(fitted[-1].get('position', {}).get('y', 0.0)) if fitted else 0.0
        return fitted, {
            'reversed_flow': reversed_flow,
            'start_y': start_y,
            'end_y': end_y,
            'drop_m': start_y - end_y,
        }

    def _guide_spliney_points(self, width: float, style: str = 'Road') -> tuple[list, dict]:
        guide = [
            {
                'x': float(point.get('x', 0.0)),
                'y': float(point.get('y', 0.0)),
                'z': float(point.get('z', 0.0)),
            }
            for point in self.alignment_guide_points
        ]
        span_limit = None
        simplify_tolerance = None
        inserted_points = 0
        # Trace-built roads and rivers now map 1:1 from guide clicks to control
        # points, except near-duplicate clicks collapse into one point.
        cleaned: list[dict] = []
        for point in guide:
            current = dict(point)
            if cleaned:
                prev = cleaned[-1]
                if math.hypot(current['x'] - prev['x'], current['z'] - prev['z']) < 0.01:
                    cleaned[-1] = current
                    continue
            cleaned.append(current)
        guide = cleaned

        points = []
        for point in guide:
            y_value = point.get('y', self._sample_terrain_y(point.get('x', 0.0), point.get('z', 0.0)))
            points.append({
                'position': {
                    'x': float(point.get('x', 0.0)),
                    'y': float(y_value if y_value is not None else 0.0),
                    'z': float(point.get('z', 0.0)),
                },
                'rotation': {'x': 0.0, 'y': 0.0, 'z': 0.0},
                'width': float(width),
            })

        points = self._normalize_spliney_points(points)
        build_meta = {
            'span_limit_m': span_limit,
            'simplify_tolerance_m': simplify_tolerance,
            'source_points': len(self.alignment_guide_points),
            'simplified_points': len(guide),
            'inserted_points': inserted_points,
        }
        if str(style).lower() == 'river':
            points, fit_meta = self._fit_flowy_points_to_terrain(
                points,
                style=style,
                normalize_rotations=True,
            )
            build_meta.update(fit_meta)
        return points, build_meta

    def _solve_spliney_point_rotation(self, points: list, idx: int):
        if idx < 0 or idx >= len(points):
            return
        pt = copy.deepcopy(points[idx])
        pos = dict(pt.get('position', {}) or {})
        rot = dict(pt.get('rotation', {}) or {})
        fallback_y = float(rot.get('y', 0.0))
        fallback_x = float(rot.get('x', 0.0))
        if idx > 0 and idx + 1 < len(points):
            prev_pos = dict(points[idx - 1].get('position', {}) or {})
            next_pos = dict(points[idx + 1].get('position', {}) or {})
            rot_y = self._spliney_heading_deg(prev_pos, next_pos, fallback_y)
            rot_x = self._spliney_pitch_deg(prev_pos, next_pos, fallback_x)
        elif idx + 1 < len(points):
            next_pos = dict(points[idx + 1].get('position', {}) or {})
            rot_y = self._spliney_heading_deg(pos, next_pos, fallback_y)
            rot_x = self._spliney_pitch_deg(pos, next_pos, fallback_x)
        elif idx > 0:
            prev_pos = dict(points[idx - 1].get('position', {}) or {})
            rot_y = self._spliney_heading_deg(prev_pos, pos, fallback_y)
            rot_x = self._spliney_pitch_deg(prev_pos, pos, fallback_x)
        else:
            rot_y = fallback_y % 360.0
            rot_x = fallback_x
        rot['x'] = float(rot_x)
        rot['y'] = float(rot_y)
        rot['z'] = float(rot.get('z', 0.0))
        pt['rotation'] = rot
        points[idx] = pt

    def _solve_spliney_rotation_span(self, points: list, start_idx: int = 0, end_idx: int | None = None):
        if not points:
            return
        last_idx = len(points) - 1
        start = max(0, int(start_idx))
        end = last_idx if end_idx is None else min(last_idx, int(end_idx))
        if start > end:
            return
        solve_start = max(0, start - 1)
        solve_end = min(last_idx, end + 1)
        for idx in range(solve_start, solve_end + 1):
            self._solve_spliney_point_rotation(points, idx)

    def _selected_flowy_extend_target(self, style: str):
        layer, spl = self._selected_flowy_entry()
        if not layer or not spl or getattr(layer, 'read_only', False):
            return None
        style_name = 'River' if str(style).lower() == 'river' else 'Road'
        if str(spl.get('style', 'Road')).lower() != style_name.lower():
            return None
        pts = list(spl.get('points', []))
        if len(pts) < 1:
            return None
        if self.sel_spliney_pt == 0:
            return {'layer': layer, 'spl': spl, 'points': pts, 'side': 'start'}
        if self.sel_spliney_pt == len(pts) - 1:
            return {'layer': layer, 'spl': spl, 'points': pts, 'side': 'end'}
        return None

    def _extend_flowy_with_guide(self, spl: dict, width: float, style: str, side: str):
        existing_points = copy.deepcopy(spl.get('points', []))
        if len(existing_points) < 1:
            return None, {}

        guide = [
            {
                'x': float(point.get('x', 0.0)),
                'y': float(point.get('y', 0.0)),
                'z': float(point.get('z', 0.0)),
            }
            for point in self.alignment_guide_points
        ]
        cleaned: list[dict] = []
        for point in guide:
            current = dict(point)
            if cleaned:
                prev = cleaned[-1]
                if math.hypot(current['x'] - prev['x'], current['z'] - prev['z']) < 0.01:
                    cleaned[-1] = current
                    continue
            cleaned.append(current)
        if not cleaned:
            return None, {}

        anchor_point = existing_points[0] if side == 'start' else existing_points[-1]
        anchor_pos = dict(anchor_point.get('position', {}) or {})
        anchor = {
            'x': float(anchor_pos.get('x', 0.0)),
            'y': float(anchor_pos.get('y', 0.0)),
            'z': float(anchor_pos.get('z', 0.0)),
        }
        first_dist = math.hypot(cleaned[0]['x'] - anchor['x'], cleaned[0]['z'] - anchor['z'])
        last_dist = math.hypot(cleaned[-1]['x'] - anchor['x'], cleaned[-1]['z'] - anchor['z'])
        oriented = list(cleaned)
        if side == 'end':
            if first_dist > last_dist:
                oriented.reverse()
            if math.hypot(oriented[0]['x'] - anchor['x'], oriented[0]['z'] - anchor['z']) > 0.01:
                oriented.insert(0, anchor)
        else:
            if last_dist > first_dist:
                oriented.reverse()
            if math.hypot(oriented[-1]['x'] - anchor['x'], oriented[-1]['z'] - anchor['z']) > 0.01:
                oriented.append(anchor)

        draft_backup = self.alignment_guide_points
        try:
            self.alignment_guide_points = oriented
            new_points, build_meta = self._guide_spliney_points(width, style=style)
        finally:
            self.alignment_guide_points = draft_backup

        if side == 'end':
            if new_points and math.hypot(
                    float(new_points[0].get('position', {}).get('x', 0.0)) - anchor['x'],
                    float(new_points[0].get('position', {}).get('z', 0.0)) - anchor['z']) <= 0.01:
                new_points = new_points[1:]
            if not new_points:
                return None, build_meta
            merged = existing_points + new_points
            self._solve_spliney_point_rotation(merged, len(existing_points) - 1)
            select_index = len(merged) - 1
        else:
            if new_points and math.hypot(
                    float(new_points[-1].get('position', {}).get('x', 0.0)) - anchor['x'],
                    float(new_points[-1].get('position', {}).get('z', 0.0)) - anchor['z']) <= 0.01:
                new_points = new_points[:-1]
            if not new_points:
                return None, build_meta
            merged = new_points + existing_points
            self._solve_spliney_point_rotation(merged, len(new_points))
            select_index = 0

        build_meta = dict(build_meta)
        build_meta['extended_points'] = len(new_points)
        build_meta['side'] = side
        build_meta['select_index'] = select_index
        return merged, build_meta

    def _commit_guide_spliney(self):
        if not self.mod_project:
            self._set_status("Load a mod first")
            return
        if len(self.alignment_guide_points) < 2:
            self._set_status("Guide path needs at least 2 points")
            return

        style = 'River' if str(self.geo_spline_style).lower() == 'river' else 'Road'
        defaults = self._spliney_style_defaults(style)
        width = float(self.geo_spline_width) if float(self.geo_spline_width) > 0.0 else float(defaults['width'])
        if width <= 0.0:
            self._set_status("Spline width must be greater than 0")
            return
        extend_target = self._selected_flowy_extend_target(style)
        layer = extend_target['layer'] if extend_target else self._spliney_target_layer(style)
        if layer is None:
            self._set_status(f"No writable layer found for {style.lower()} splineys")
            return

        guide_len = alignment_polyline_length(self._alignment_guide_points_xz())
        if extend_target:
            merged_points, build_meta = self._extend_flowy_with_guide(
                extend_target['spl'], width, style, extend_target['side']
            )
            if not merged_points:
                self._set_status("Trace needs at least one new point beyond the selected endpoint")
                return
            extend_id = self.sel_spliney_id
            updated = dict(extend_target['spl'])
            updated['points'] = merged_points
            self._save_flowy_spliney(layer, extend_id, updated)
            if self.mod_project and layer in self.mod_project.layers:
                layer_idx = self.mod_project.layers.index(layer)
                self.mod_project.set_active_layer(layer_idx)
                self._set_selected_spliney_point(
                    extend_id,
                    layer_idx,
                    int(build_meta.get('select_index', self.sel_spliney_pt)),
                )
            self._geo_guide_place_mode = False
            self.alignment_guide_points = []
            extras = [f"+{int(build_meta.get('extended_points', 0))} pts"]
            if build_meta.get('reversed_flow'):
                extras.append("flow reversed")
            if style == 'River':
                extras.append(f"drop {float(build_meta.get('drop_m', 0.0)):.1f} m")
            extra_text = f", {', '.join(extras)}" if extras else ""
            self._set_status(
                f"Extended {style.lower()} spliney {extend_id}  "
                f"({guide_len:.1f} m, {extend_target['side']}{extra_text})"
            )
            return

        spliney_id = next_spliney_id(layer, prefix=style)
        points, build_meta = self._guide_spliney_points(width, style=style)
        spliney_add_road(layer, spliney_id, defaults['profile'], points, style=style)
        layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))

        if self.mod_project and layer in self.mod_project.layers:
            layer_idx = self.mod_project.layers.index(layer)
            self.mod_project.set_active_layer(layer_idx)
            self._set_selected_spliney_point(spliney_id, layer_idx, 0)
        self._geo_guide_place_mode = False
        self.alignment_guide_points = []
        extras = []
        if build_meta.get('span_limit_m'):
            extras.append(f"span <= {float(build_meta['span_limit_m']):.1f} m")
        if style == 'River' and build_meta.get('source_points') and int(build_meta['source_points']) != len(points):
            extras.append(f"trace {int(build_meta['source_points'])}->{len(points)} pts")
        if style == 'River' and build_meta.get('inserted_points'):
            extras.append(f"+{int(build_meta['inserted_points'])} inserts")
        if build_meta.get('reversed_flow'):
            extras.append("flow reversed")
        if style == 'River':
            extras.append(f"drop {float(build_meta.get('drop_m', 0.0)):.1f} m")
        extra_text = f", {', '.join(extras)}" if extras else ""
        self._set_status(
            f"Built {style.lower()} spliney {spliney_id} -> {layer.label}  "
            f"({len(points)} pts, {guide_len:.1f} m, {defaults['profile']}, {width:.1f} m{extra_text})"
        )

    def _alignment_fit_arc_preview(self):
        source = self._alignment_source_chain()
        if not source or len(source.get('nodes', [])) < 3:
            self._set_status("Fit Arc needs at least 3 nodes from the grade chain")
            return
        if self._alignment_chain_contains_turnout(source):
            self._set_status("Fit Arc blocked: source chain includes turnout/switch nodes")
            return
        source_points = self._alignment_source_points(source)
        fit = fit_arc_to_chain(source_points)
        if not fit:
            self._set_status("Fit Arc could not solve a stable circle for the selected chain")
            return

        update_nodes = []
        fitted_points = fit.get('points', [])
        for node_id, node, fitted in zip(source.get('node_ids', []), source.get('nodes', []), fitted_points):
            x, z, rot_y = fitted
            update_nodes.append({
                'id': node_id,
                'x': x,
                'y': float(node.get('y', 0.0)),
                'z': z,
                'rotX': float(node.get('rotX', 0.0)),
                'rotY': rot_y,
                'rotZ': float(node.get('rotZ', 0.0)),
                'flipSwitchStand': bool(node.get('flipSwitchStand', False)),
            })

        self.geo_preview = [([], [], update_nodes)]
        preview_points = [(x, z) for x, z, _ in fitted_points]
        self.alignment_fit_stats = fit
        self.geo_preview_meta = self._build_fit_arc_preview_meta(fit, preview_points)
        self.geo_preview_meta['source_label'] = source.get('label', '')
        status = (
            f"Fit Arc preview: R {fit['radius']:.1f} m, "
            f"angle {fit['delta_angle_deg']:.1f} deg, "
            f"RMS {fit['rms_error']:.2f} m"
        )
        if self._geo_preview_errors():
            status += " - commit blocked"
        self._set_status(status)

    # ------------------------------------------------------------------
    # Live bridge
    # ------------------------------------------------------------------
    # ------------------------------------------------------------------
    # Mod project
    # ------------------------------------------------------------------
    def open_mod_folder_dialog(self):
        if not _MOD_AVAILABLE: return
        try:
            folders = self._pick_mod_folders()
            if folders:
                factory = ModProject.open_mod_folders if len(folders) > 1 else ModProject.open_mod_folder
                payload = folders if len(folders) > 1 else folders[0]
                self._load_mod_project(factory, payload,
                                       source_kind='mod_folder')
        except Exception as ex:
            self._set_status(f"Open mod failed: {ex}")

    def add_mod_folder_dialog(self):
        if not _MOD_AVAILABLE:
            return
        try:
            folders = self._pick_mod_folders()
            if not folders:
                return
            if not self.mod_project:
                factory = ModProject.open_mod_folders if len(folders) > 1 else ModProject.open_mod_folder
                payload = folders if len(folders) > 1 else folders[0]
                self._load_mod_project(factory, payload, source_kind='mod_folder')
                return
            added_layers = 0
            for folder in folders:
                added_layers += self.mod_project.append_mod_folder(folder)
            self._mod_source_kind = 'mod_folder'
            existing = list(self._mod_source_paths)
            for folder in folders:
                path = Path(folder)
                if path not in existing:
                    existing.append(path)
            self._mod_source_paths = existing
            self._mod_source_path = self._mod_source_paths[0] if self._mod_source_paths else None
            self._mod_undo_stack.clear()
            self.sel_mod_node_id = None
            self.sel_mod_seg_id = None
            self.mod_panel = True
            self._set_status(
                f"Loaded {len(folders)} additional mod(s)  {self.mod_project.stats()}  ({added_layers} layers)"
            )
        except Exception as ex:
            self._set_status(f"Add mod failed: {ex}")

    def _pick_mod_folders(self):
        initial_dir = (str(preferred_railroader_path())
                       if preferred_railroader_path else None)
        folders = []
        while True:
            folder = ask_directory(
                self.screen,
                title=f"Select mod folder to load ({len(folders)+1}) - Cancel when done",
                initial_dir=initial_dir,
            )
            if not folder:
                break
            path = Path(folder)
            if path in folders:
                self._set_status(f"Already selected: {folder}")
                continue
            folders.append(path)
            initial_dir = str(path.parent)
            if not ask_yes_no(
                self.screen,
                "Add another mod?",
                "Added: " + folder + "\n\nLoad another mod folder into the workspace?"
            ):
                break
        return folders

    def open_base_graph_dialog(self):
        if not _MOD_AVAILABLE: return
        try:
            path = ask_open_filename(self.screen,
                title="Select base game graph-data.json or game-graph.json",
                filetypes=[("JSON files", "*.json"), ("All files", "*.*")],
                initial_dir=(str(preferred_railroader_path())
                             if preferred_railroader_path else None))
            if path:
                self._load_mod_project(ModProject.open_base_game, path,
                                       source_kind='base_graph')
        except Exception as ex:
            self._set_status(f"Open base graph failed: {ex}")

    def new_mod_dialog(self):
        if not _MOD_AVAILABLE: return
        try:
            mod_id = ask_string(
                self.screen,
                "Create New Mod",
                "Mod ID (letters, numbers, underscores, and dots):",
                initialvalue="YourName.NewMap",
            )
            if mod_id is None:
                return
            mod_id = mod_id.strip()
            mod_name = ask_string(
                self.screen,
                "Create New Mod",
                "Display name:",
                initialvalue=mod_id,
            )
            if mod_name is None:
                return
            author = ask_string(
                self.screen,
                "Create New Mod",
                "Author (optional):",
                initialvalue="",
            )
            if author is None:
                return
            fuse_label = "Native FUSE package (Recommended)"
            compatible_label = "Legacy RailLoader package (Limited)"
            package_format = ask_choice_list(
                self.screen,
                "New Mod Format",
                [fuse_label, compatible_label],
                prompt=(
                    "Native FUSE supports the complete editor schema. "
                    "Legacy writes game-graph.json and cannot represent "
                    "every FUSE feature."
                ),
            )
            if not package_format:
                return
            loader = 'fuse' if package_format == fuse_label else 'compatible'
            complete_map = False
            map_origin_lat = None
            map_origin_lon = None
            if loader == 'fuse':
                overlay_label = "Base-game modification / add-on"
                standalone_label = "Complete standalone map"
                project_kind = ask_choice_list(
                    self.screen,
                    "Native FUSE Project Type",
                    [overlay_label, standalone_label],
                    prompt=(
                        "An add-on edits the stock map. A standalone map gets "
                        "its own Map.json, terrain folder, and suppresses the "
                        "stock world when launched."
                    ),
                )
                if not project_kind:
                    return
                complete_map = project_kind == standalone_label
                if complete_map:
                    latitude_text = ask_string(
                        self.screen,
                        "Standalone Map Origin",
                        "Origin latitude (-90 to 90):",
                        initialvalue=f"{self.map_origin_lat:.8f}",
                    )
                    if latitude_text is None:
                        return
                    longitude_text = ask_string(
                        self.screen,
                        "Standalone Map Origin",
                        "Origin longitude (-180 to 180):",
                        initialvalue=f"{self.map_origin_lon:.8f}",
                    )
                    if longitude_text is None:
                        return
                    try:
                        map_origin_lat = float(latitude_text.strip())
                        map_origin_lon = float(longitude_text.strip())
                    except ValueError:
                        raise ValueError(
                            "Map origin latitude and longitude must be numbers"
                        )

            initial_dir = None
            if preferred_railroader_path:
                game_root = preferred_railroader_path()
                if game_root:
                    initial_dir = str(Path(game_root) / 'Mods')
            parent = ask_directory(
                self.screen,
                title="Select the parent folder for the new mod",
                initial_dir=initial_dir,
            )
            if not parent:
                return
            target = Path(parent) / mod_id
            file_summary = (
                (
                    "Info.json + map.fuse.json + Map/Map.json"
                    if complete_map
                    else "Info.json + map.fuse.json"
                )
                if loader == 'fuse'
                else "Definition.json + game-graph.json"
            )
            if not ask_yes_no(
                self.screen,
                "Create New Mod",
                f"Create:\n{target}\n\nFiles: {file_summary}?",
            ):
                return
            created = self._load_mod_project(
                lambda p: ModProject.new_mod(
                    Path(p),
                    mod_id,
                    mod_name.strip(),
                    author=author.strip(),
                    loader=loader,
                    complete_map=complete_map,
                    map_origin_lat=map_origin_lat,
                    map_origin_lon=map_origin_lon,
                ),
                str(target),
                source_kind='mod_folder')
            if created:
                self._set_status(
                    f"Created {package_format}: {target}"
                )
        except Exception as ex:
            self._set_status(f"New mod failed: {ex}")

    def _reload_discard_items(self):
        items = []
        # Terrain folders load on a worker thread. Snapshot the dictionary
        # before inspecting it so a completed tile cannot resize the live
        # collection while the renderer is drawing the navigation bar.
        dirty_terrain = sum(
            1
            for tile in list(self.tiles.values())
            if tile.dirty
        )
        if dirty_terrain:
            items.append(
                f"{dirty_terrain} unsaved terrain tile(s)")
        if self.mod_project and self.mod_project.dirty:
            items.append("unsaved mod layer changes")
        if self.prog_project and self.prog_project.dirty:
            items.append("unsaved progression changes")
        if self._area_dirty_layers:
            items.append(f"{len(self._area_dirty_layers)} unsaved town layer(s)")
        return items

    def _has_reloadable_source(self) -> bool:
        return bool(
            self.folders
            or self.track_graph_path
            or self._mod_source_paths
            or self._mod_source_path)

    def _has_unsaved_reload_changes(self) -> bool:
        return bool(self._reload_discard_items())

    def reload_current_sources(self):
        if self.loading:
            self._set_status("Already loading from disk")
            return
        if not self._has_reloadable_source():
            self._set_status("Nothing graph or mod related loaded to reload")
            return

        discard_items = self._reload_discard_items()
        if discard_items:
            msg = "Reload from disk and discard " + ", ".join(discard_items) + "?"
            if not ask_yes_no(self.screen, "Discard unsaved changes?", msg):
                self._set_status("Reload cancelled")
                return

        old_mod_panel = self.mod_panel
        old_prog_panel = self.prog_panel
        old_area_panel = self.area_panel
        reloaded = []

        if self.folders:
            self.load_folders(
                list(self.folders),
                preserve_view=True)
            reloaded.append("terrain tiles")

        if self.track_graph_path:
            try:
                self.load_track_graph(self.track_graph_path)
                reloaded.append("track graph")
            except Exception as ex:
                self._set_status(f"Reload graph failed: {ex}")
                return

        if (self._mod_source_paths or self._mod_source_path) and self._mod_source_kind and _MOD_AVAILABLE:
            mod_paths = self._mod_source_paths or ([self._mod_source_path] if self._mod_source_path else [])
            factory = (ModProject.open_mod_folders
                       if self._mod_source_kind == 'mod_folder' and len(mod_paths) > 1
                       else ModProject.open_mod_folder
                       if self._mod_source_kind == 'mod_folder'
                       else ModProject.open_base_game
                       if self._mod_source_kind == 'base_graph'
                       else None)
            payload = mod_paths if self._mod_source_kind == 'mod_folder' and len(mod_paths) > 1 else mod_paths[0]
            if factory and self._load_mod_project(factory, payload,
                                                  source_kind=self._mod_source_kind,
                                                  show_panel=old_mod_panel):
                if old_area_panel:
                    self._open_area_editor()
                elif old_prog_panel:
                    self._open_progression_editor()
                reloaded.append("mod project")

        if reloaded:
            self._set_status("Reloaded from disk: " + ", ".join(reloaded))

    def _load_mod_project(self, factory, path, source_kind=None, show_panel=True):
        """Load a mod project using factory(path), update state."""
        try:
            if isinstance(path, (list, tuple)):
                source_paths = [Path(p) for p in path]
                payload = source_paths
            else:
                source_paths = [Path(path)]
                payload = source_paths[0]

            self.mod_project = factory(payload)
            self._pending_bridge_reload_paths.clear()
            self._sync_mod_project_save_mode()
            self._mark_measure_cache_dirty()
            self._mod_source_kind = source_kind
            self._mod_source_paths = source_paths if source_kind == 'mod_folder' else []
            self._mod_source_path = source_paths[0] if source_kind else None
            self._mod_undo_stack.clear()
            self.sel_mod_node_id = None
            self.sel_mod_seg_id = None
            self.prog_project = None
            self.prog_panel = False
            self.area_panel = False
            self.area_sel_id = None
            self.area_sel_industry = None
            self.area_sel_component = None
            self._area_dirty_layers.clear()
            self._activate_project_map_package()
            self._set_status(f"Loaded: {self.mod_project.name}  {self.mod_project.stats()}")
            self.mod_panel = show_panel
            print(f"[mod] loaded {self.mod_project.name} — {len(self.mod_project.layers)} layers")
            return True
        except Exception as ex:
            self._set_status(f"Load failed: {ex}")
            print(f"[mod] load error: {ex}")
            import traceback; traceback.print_exc()
            return False

    def _activate_project_map_package(self):
        """Load a native package's standalone terrain and georeference."""
        for layer in self.mod_project.layers:
            raw = getattr(layer, '_raw', None)
            declaration = raw.get('map') if isinstance(raw, dict) else None
            if not isinstance(declaration, dict):
                continue
            relative = str(declaration.get('mapFolder') or '').strip()
            if not relative:
                continue
            package_root = Path(layer.path).parent.resolve()
            map_folder = (package_root / relative).resolve()
            try:
                map_folder.relative_to(package_root)
            except ValueError:
                print(f"[map] rejected mapFolder outside package: {relative}")
                continue
            map_json = map_folder / 'Map.json'
            if not map_json.is_file():
                print(f"[map] standalone package is missing {map_json}")
                continue
            self.gen_out_dir = str(map_folder)
            tile_files = list(map_folder.glob('tile_*.data'))
            if tile_files:
                self.load_folders([str(map_folder)])
            else:
                self.folders = [str(map_folder)]
                self.tiles = {}
                self._configure_map_georeference([str(map_folder)])
                self._update_bounds()
            return

    def save_mod_project(self):
        if self.mod_project:
            saved_count, reload_count = self._apply_pending_mod_changes(announce=False)
            extras = []
            if saved_count:
                extras.append(f"{saved_count} layer(s)")
            if reload_count:
                extras.append(f"{reload_count} reload(s)")
            suffix = f"  ({', '.join(extras)})" if extras else ""
            self._set_status(f"Saved: {self.mod_project.name}{suffix}")

    def _save_active_layer(self):
        if self.mod_project:
            li = self.mod_project.active_layer_idx
            if 0 <= li < len(self.mod_project.layers):
                lyr = self.mod_project.layers[li]
                saved, reload_count = self._save_mod_layer_now(li)
                extras = []
                if saved:
                    extras.append("saved")
                if reload_count:
                    extras.append(f"{reload_count} reload")
                suffix = f"  ({', '.join(extras)})" if extras else ""
                self._set_status(f"Saved layer: {lyr.label}{suffix}")

    def validate_mod_project(self):
        """Run the complete saved-package validation and show a copyable report."""
        if not self.mod_project or not self.mod_project.folder:
            self._set_status("Open a mod project before validating")
            return
        if self.mod_project.dirty:
            if not ask_yes_no(
                    self.screen,
                    "Save Before Validation?",
                    "Validation reads the package on disk. Save all pending "
                    "editor changes first?"):
                self._set_status("Validation cancelled; project has unsaved changes")
                return
            self.save_mod_project()
        try:
            issues = validate_mod(Path(self.mod_project.folder))
        except Exception as exc:
            self._set_status(f"Validation failed: {exc}")
            ask_text(
                self.screen,
                "Validation Failed",
                "Copy this diagnostic when reporting the problem.",
                str(exc),
            )
            return
        report = self._format_mod_validation_report(issues)
        errors = sum(1 for severity, _ in issues if severity == "error")
        warnings = sum(1 for severity, _ in issues if severity == "warning")
        self._set_status(
            "Validation passed"
            if not issues
            else f"Validation: {errors} error(s), {warnings} warning(s)"
        )
        ask_text(
            self.screen,
            "Mod Validation Report",
            "Review or copy the report. Editing this window does not change the mod.",
            report,
        )

    def export_mod_project(self):
        """Validate and export the active mod as a clean distribution ZIP."""
        if not self.mod_project or not self.mod_project.folder:
            self._set_status("Open a mod project before exporting")
            return
        if self.mod_project.dirty:
            if not ask_yes_no(
                    self.screen,
                    "Save Before Export?",
                    "Export uses the files on disk. Save all pending editor "
                    "changes first?"):
                self._set_status("Export cancelled; project has unsaved changes")
                return
            self.save_mod_project()
        folder = Path(self.mod_project.folder)
        try:
            issues = validate_mod(folder)
        except Exception as exc:
            self._set_status(f"Validation failed: {exc}")
            return
        errors = [message for severity, message in issues if severity == "error"]
        if errors:
            self._set_status(
                f"Export blocked by {len(errors)} validation error(s)"
            )
            ask_text(
                self.screen,
                "Export Blocked",
                "Fix every ERROR before exporting. Warnings do not block export.",
                self._format_mod_validation_report(issues),
            )
            return
        output_path = ask_save_filename(
            self.screen,
            title="Export Clean Mod Package",
            defaultextension=".zip",
            filetypes=[("ZIP packages", "*.zip"), ("All files", "*.*")],
            initial_dir=folder.parent,
        )
        if not output_path:
            return
        try:
            if not export_clean_zip(folder, Path(output_path)):
                self._set_status("Export was blocked by validation")
                return
        except Exception as exc:
            self._set_status(f"Export failed: {exc}")
            ask_text(
                self.screen,
                "Export Failed",
                "Copy this diagnostic when reporting the problem.",
                str(exc),
            )
            return
        warning_count = sum(
            1 for severity, _ in issues if severity == "warning"
        )
        self._set_status(
            f"Exported {Path(output_path).name}"
            + (f" with {warning_count} warning(s)" if warning_count else "")
        )

    @staticmethod
    def _format_mod_validation_report(issues):
        if not issues:
            return "PASS\n\nNo validation errors or warnings were found."
        errors = sum(1 for severity, _ in issues if severity == "error")
        warnings = sum(1 for severity, _ in issues if severity == "warning")
        lines = [
            f"RESULT: {errors} error(s), {warnings} warning(s)",
            "",
        ]
        lines.extend(
            f"{severity.upper()}: {message}"
            for severity, message in issues
        )
        return "\n".join(lines)

    def handle_mod_panel_click(self, mx, my, content_top):
        """Handle mouse clicks inside the mod panel. Returns True if consumed."""
        if not self.mod_panel or not _MOD_AVAILABLE:
            return False
        bounds = getattr(self, "_mod_panel_bounds", None)
        if bounds is None:
            w, h = self.screen.get_size()
            pw  = min(w - 40, 980)
            ph  = h - content_top - STATUS_H - 20
            px  = (w - pw) // 2
            py  = content_top + 10
            bounds = pygame.Rect(px, py, pw, ph)
        if not bounds.collidepoint(mx, my):
            return False

        xbtn = getattr(self, "_mod_panel_close_rect", None)
        if xbtn and xbtn.collidepoint(mx, my):
            self.mod_panel = False
            return True

        for rect, action in getattr(self, "_mod_panel_action_rects", []):
            if rect.collidepoint(mx, my):
                action()
                return True

        if not self.mod_project:
            return True

        for rect, li in getattr(self, "_mod_panel_save_rects", []):
            if rect.collidepoint(mx, my):
                lyr = self.mod_project.layers[li]
                saved, reload_count = self._save_mod_layer_now(li)
                extras = []
                if saved:
                    extras.append("saved")
                if reload_count:
                    extras.append(f"{reload_count} reload")
                suffix = f"  ({', '.join(extras)})" if extras else ""
                self._set_status(f"Saved: {lyr.label}{suffix}")
                return True

        for rect, li in getattr(self, "_mod_panel_vis_rects", []):
            if rect.collidepoint(mx, my):
                self.mod_project.toggle_layer(li)
                return True

        for rect, li in getattr(self, "_mod_panel_row_rects", []):
            if rect.collidepoint(mx, my):
                lyr = self.mod_project.layers[li]
                prev_graph = self.mod_project.get_graph_layer()
                self.mod_project.set_active_layer(li)
                new_graph = self.mod_project.get_graph_layer()
                if prev_graph is not new_graph:
                    self._mod_undo_stack.clear()
                    self.sel_mod_node_id = None
                    self.sel_mod_seg_id = None
                self._set_status(
                    f"Active layer: {lyr.label}  "
                    f"nodes={len(lyr.nodes)}  segs={len(lyr.segments)}")
                return True

        return True

    # ------------------------------------------------------------------
    # Node / segment creation and deletion
    # ------------------------------------------------------------------
    # ------------------------------------------------------------------
    # Mod edit undo system
    # ------------------------------------------------------------------
    def _push_undo(self, description: str):
        """Snapshot every editable collection in the current graph layer."""
        if not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        import copy
        snapshot = {
            'desc':         description,
            'nodes':        copy.deepcopy(graph.nodes),
            'segments':     copy.deepcopy(graph.segments),
            'spans':        copy.deepcopy(graph.spans),
            'splineys':     copy.deepcopy(graph.splineys),
            'scenery':      copy.deepcopy(graph.scenery),
            'mandelas':     copy.deepcopy(graph.mandelas),
            'areas':        copy.deepcopy(graph.areas),
            'texts':        copy.deepcopy(graph.texts),
            'simpleGraphs': copy.deepcopy(graph.simpleGraphs),
            'loads':        copy.deepcopy(graph.loads),
            'raw':          copy.deepcopy(graph._raw),
        }
        self._mod_undo_stack.append(snapshot)
        if len(self._mod_undo_stack) > self._mod_undo_max:
            self._mod_undo_stack.pop(0)

    def _pop_undo(self):
        """Restore last snapshot."""
        if not self._mod_undo_stack:
            self._set_status("Nothing to undo")
            return
        if not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        snap = self._mod_undo_stack.pop()
        import copy
        graph.nodes        = copy.deepcopy(snap['nodes'])
        graph.segments     = copy.deepcopy(snap['segments'])
        graph.spans        = copy.deepcopy(snap['spans'])
        graph.splineys     = copy.deepcopy(snap.get('splineys', {}))
        graph.scenery      = copy.deepcopy(snap.get('scenery', {}))
        graph.mandelas     = copy.deepcopy(snap.get('mandelas', {}))
        graph.areas        = copy.deepcopy(snap.get('areas', {}))
        graph.texts        = copy.deepcopy(snap.get('texts', {}))
        graph.simpleGraphs = copy.deepcopy(snap.get('simpleGraphs', {}))
        graph.loads        = copy.deepcopy(snap.get('loads', {}))
        graph._raw         = copy.deepcopy(snap['raw'])
        graph.dirty = True

        self._commit_mod_layer_edit(graph, graph_changed=True)
        if (
            self.sel_mod_node_id
            and self.sel_mod_node_id not in self.mod_project.merged_nodes
        ):
            self.sel_mod_node_id = None
        if (
            self.sel_mod_seg_id
            and self.sel_mod_seg_id not in self.mod_project.merged_segments
        ):
            self.sel_mod_seg_id = None
        if (
            self.sel_scenery_id
            and self.sel_scenery_id not in self.mod_project.merged_scenery
        ):
            self.sel_scenery_id = None
            self.sel_scenery_layer = None
        self._set_status(f"Undo: {snap['desc']}")

    def _commit_mod_layer_edit(self, layer, graph_changed: bool = False):
        """Refresh, save, and hot-reload one RailLoader map layer consistently."""
        if not self.mod_project or layer is None:
            return False
        self.mod_project._rebuild_merge()
        if graph_changed:
            self._mark_measure_cache_dirty()
        saved = layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        return saved

    # ------------------------------------------------------------------
    # Merge / Split
    # ------------------------------------------------------------------
    def merge_selected_node(self):
        """Remove selected middle node and merge its two segments."""
        if not self.sel_mod_node_id or not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        self._push_undo(f"merge node {self.sel_mod_node_id}")
        new_sid = merge_nodes(graph, self.mod_project.merged_nodes,
                              self.mod_project.merged_segments,
                              self.sel_mod_node_id)
        if not new_sid:
            self._mod_undo_stack.pop()
            self._set_status("Merge failed — node must have exactly 2 segments")
            return
        self.sel_mod_node_id = None
        self.sel_mod_seg_id  = new_sid

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(f"Merged → {new_sid}")

    def split_selected_node(self):
        """Duplicate selected node, disconnecting one of its segments."""
        if not self.sel_mod_node_id or not self.mod_project:
            return
        segs = self.mod_project.segments_for_node(self.sel_mod_node_id)
        if not segs:
            self._set_status("No segments to split")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        # Split using the first segment (or the selected one if available)
        seg_to_cut = self.sel_mod_seg_id if self.sel_mod_seg_id else segs[0]['id']
        self._push_undo(f"split node {self.sel_mod_node_id}")
        new_nid = split_node(graph, self.mod_project.merged_nodes,
                             self.sel_mod_node_id, seg_to_cut)

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(f"Split → {new_nid}  (drag to separate)")

    def _handle_prop_keydown(self, event) -> bool:
        """Handle keyboard for property panel editable fields. Returns True if consumed."""
        key  = self._prop_edit_key   # e.g. "prop_X", "prop_RotY", "prop_ID"
        field = key[len('prop_'):]   # e.g. "X", "RotY", "ID"

        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self._commit_prop_edit(field, self._prop_edit_buf)
            self._prop_edit_key = None
            self._prop_edit_buf = ''
            return True
        elif event.key == pygame.K_ESCAPE:
            self._prop_edit_key = None
            self._prop_edit_buf = ''
            return True
        elif event.key == pygame.K_BACKSPACE:
            self._prop_edit_buf = self._prop_edit_buf[:-1]
            return True
        elif event.unicode and event.unicode in '0123456789.-_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ':
            self._prop_edit_buf += event.unicode
            return True
        return False

    def _commit_prop_edit(self, field: str, value: str):
        """Apply an edited field value to the selected node or segment."""
        if not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        nid = self.sel_mod_node_id
        sid = self.sel_mod_seg_id
        undo_depth = len(self._mod_undo_stack)
        self._push_undo(f"edit {field} on {nid or sid}")

        def discard_undo():
            if len(self._mod_undo_stack) > undo_depth:
                self._mod_undo_stack.pop()

        if nid:
            node = dict(self.mod_project.merged_nodes.get(nid, {}))
            if not node:
                discard_undo()
                return
            try:
                if   field == 'X':    node['x']    = float(value)
                elif field == 'Y':    node['y']    = float(value)
                elif field == 'Z':    node['z']    = float(value)
                elif field == 'RotY': node['rotY'] = float(value) % 360
                elif field == 'RotX': node['rotX'] = float(value)
                elif field == 'RotZ': node['rotZ'] = float(value)
                elif field == 'ID':
                    # Rename node — update all segment references too
                    new_id = value.strip()
                    if new_id and new_id != nid:
                        if new_id in self.mod_project.merged_nodes:
                            discard_undo()
                            self._set_status(f"Node ID already exists: {new_id}")
                            return
                        self._rename_node(nid, new_id, graph)
                        return
            except ValueError:
                discard_undo()
                self._set_status(f"Invalid value: {value}")
                return
            graph.set_node(nid, node['x'], node['y'], node['z'],
                           node.get('rotX',0), node.get('rotY',0),
                           node.get('rotZ',0), node.get('flipSwitchStand',False))

            self._commit_mod_layer_edit(graph, graph_changed=True)
            self._set_status(f"{nid}  {field}={value}")

        elif sid:
            seg = dict(self.mod_project.merged_segments.get(sid, {}))
            if not seg:
                discard_undo()
                return
            try:
                if   field == 'Speed':    seg['speedLimit'] = int(float(value))
                elif field == 'Priority': seg['priority']   = int(float(value))
                elif field == 'GroupID':  seg['groupId']    = value.strip()
                elif field == 'ID':
                    new_id = value.strip()
                    if new_id and new_id != sid:
                        if new_id in self.mod_project.merged_segments:
                            discard_undo()
                            self._set_status(f"Segment ID already exists: {new_id}")
                            return
                        self._rename_segment(sid, new_id, graph)
                        return
            except ValueError:
                discard_undo()
                self._set_status(f"Invalid value: {value}")
                return
            graph.set_segment(sid, seg['startId'], seg['endId'],
                              seg.get('trackClass','Mainline'),
                              seg.get('style','Standard'),
                              seg.get('speedLimit',45),
                              seg.get('priority',0),
                              seg.get('groupId',''),
                              seg.get('gauge','Standard'))

            self._commit_mod_layer_edit(graph, graph_changed=True)
            self._set_status(f"{sid}  {field}={value}")
        else:
            discard_undo()

    def _rename_node(self, old_id: str, new_id: str, graph):
        """Rename a node ID and update all segment references."""
        node = dict(self.mod_project.merged_nodes.get(old_id, {}))
        if not node:
            return
        node['id'] = new_id
        graph.set_node(new_id, node['x'], node['y'], node['z'],
                       node.get('rotX',0), node.get('rotY',0),
                       node.get('rotZ',0), node.get('flipSwitchStand',False))
        graph.delete_node(old_id)
        # Update all segments referencing old_id
        for seg in list(self.mod_project.merged_segments.values()):
            changed = False
            s = dict(seg)
            if s.get('startId') == old_id: s['startId'] = new_id; changed = True
            if s.get('endId')   == old_id: s['endId']   = new_id; changed = True
            if changed:
                graph.set_segment(s['id'], s['startId'], s['endId'],
                                  s.get('trackClass','Mainline'), s.get('style','Standard'),
                                  s.get('speedLimit',45), s.get('priority',0),
                                  s.get('groupId',''),
                                  s.get('gauge','Standard'))
        self.sel_mod_node_id = new_id

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(f"Renamed {old_id} → {new_id}")

    def _rename_segment(self, old_id: str, new_id: str, graph):
        """Rename a segment ID."""
        seg = dict(self.mod_project.merged_segments.get(old_id, {}))
        if not seg:
            return
        graph.set_segment(new_id, seg['startId'], seg['endId'],
                          seg.get('trackClass','Mainline'), seg.get('style','Standard'),
                          seg.get('speedLimit',45), seg.get('priority',0),
                          seg.get('groupId',''),
                          seg.get('gauge','Standard'))
        graph.delete_segment(old_id)
        self.sel_mod_seg_id = new_id

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(f"Renamed segment {old_id} → {new_id}")

    def _do_prop_action(self, action: str):
        """Execute a property panel action (node/segment operations)."""
        # --- Field edit activation (no mod check needed) ---
        if action.startswith('prop_edit_'):
            field = action[len('prop_edit_'):]
            # Get current value from node or segment
            nid = self.sel_mod_node_id
            sid = self.sel_mod_seg_id
            cur = ''
            if nid and self.mod_project:
                n = self.mod_project.merged_nodes.get(nid, {})
                cur = str({'X': n.get('x',0), 'Y': n.get('y',0), 'Z': n.get('z',0),
                           'RotY': n.get('rotY',0), 'RotX': n.get('rotX',0),
                           'RotZ': n.get('rotZ',0), 'ID': n.get('id','')
                           }.get(field, ''))
            elif sid and self.mod_project:
                s = self.mod_project.merged_segments.get(sid, {})
                cur = str({'Speed': s.get('speedLimit',''), 'Priority': s.get('priority',''),
                           'GroupID': s.get('groupId',''), 'ID': s.get('id','')
                           }.get(field, ''))
            self._prop_edit_key = f"prop_{field}"
            self._prop_edit_buf = cur
            return

        if not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            self._set_status("No game-graph layer")
            return

        nid = self.sel_mod_node_id
        sid = self.sel_mod_seg_id

        # --- Flip toggle ---
        if action in ('flip_true', 'flip_false') and nid:
            node = dict(self.mod_project.merged_nodes.get(nid, {}))
            if node:
                self._push_undo(f"flip {nid}")
                flip = (action == 'flip_true')
                graph.set_node(nid, node['x'], node['y'], node['z'],
                               node.get('rotX',0), node.get('rotY',0),
                               node.get('rotZ',0), flip)

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"{nid}  flipSwitchStand={flip}")
            return

        # --- Node actions ---
        if action == 'del_node':
            self.delete_selected()
        elif action == 'del_seg':
            self.delete_selected()
        elif action == 'connect_node':
            self.start_connect()
        elif action == 'node_flatten' and nid:
            node = dict(self.mod_project.merged_nodes.get(nid, {}))
            if node:
                self._push_undo(f"flatten {nid}")
                graph.set_node(nid, node['x'], node['y'], node['z'],
                               0.0, node.get('rotY',0), 0.0,
                               node.get('flipSwitchStand',False))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"Flattened {nid}")
        elif action == 'node_reverse' and nid:
            node = dict(self.mod_project.merged_nodes.get(nid, {}))
            if node:
                self._push_undo(f"reverse node {nid}")
                new_rotY = (node.get('rotY',0) + 180) % 360
                graph.set_node(nid, node['x'], node['y'], node['z'],
                               node.get('rotX',0), new_rotY, node.get('rotZ',0),
                               node.get('flipSwitchStand',False))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"Reversed {nid}  rotY={new_rotY:.1f}°")
        elif action.startswith('rotY_') and nid:
            node = dict(self.mod_project.merged_nodes.get(nid, {}))
            if node:
                # Key format: rotY_m45, rotY_p0d01, rotY_m0d001 etc
                # Strip prefix, restore decimal, parse float
                raw   = action[5:]          # e.g. "m45" or "p0d001"
                sign  = -1 if raw[0] == 'm' else 1
                val_s = raw[1:].replace('d', '.')
                try:
                    delta = sign * float(val_s)
                except ValueError:
                    delta = 0.0
                new_rotY = (node.get('rotY', 0) + delta) % 360
                self._push_undo(f"rotate {nid}")
                graph.set_node(nid, node['x'], node['y'], node['z'],
                               node.get('rotX',0), new_rotY, node.get('rotZ',0),
                               node.get('flipSwitchStand',False))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"{nid}  rotY={new_rotY:.3f}°")

        # --- Segment actions ---
        elif action.startswith('seg_gauge_') and sid:
            gauge = normalize_track_gauge(action[len('seg_gauge_'):])
            seg = dict(self.mod_project.merged_segments.get(sid, {}))
            if seg:
                self._push_undo(f"gauge {sid}")
                graph.set_segment(
                    sid, seg['startId'], seg['endId'],
                    seg.get('trackClass', 'Mainline'),
                    seg.get('style', 'Standard'),
                    seg.get('speedLimit', 45),
                    seg.get('priority', 0),
                    seg.get('groupId', ''),
                    gauge,
                )
                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"{sid}  gauge={gauge}")
        elif action.startswith('seg_class_') and sid:
            tc  = action[len('seg_class_'):]
            seg = dict(self.mod_project.merged_segments.get(sid, {}))
            if seg:
                self._push_undo(f"class {sid}")
                graph.set_segment(sid, seg['startId'], seg['endId'],
                                  tc, seg.get('style','Standard'),
                                  seg.get('speedLimit',45), seg.get('priority',0),
                                  seg.get('groupId',''),
                                  seg.get('gauge','Standard'))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"{sid}  class={tc}")
        elif action.startswith('seg_style_') and sid:
            st  = action[len('seg_style_'):]
            seg = dict(self.mod_project.merged_segments.get(sid, {}))
            if seg:
                self._push_undo(f"style {sid}")
                graph.set_segment(sid, seg['startId'], seg['endId'],
                                  seg.get('trackClass','Mainline'),
                                  st,
                                  seg.get('speedLimit',45), seg.get('priority',0),
                                  seg.get('groupId',''),
                                  seg.get('gauge','Standard'))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"{sid}  style={st}")
        elif action.startswith('spd_') and sid:
            delta = {'spd_m25':-25,'spd_m10':-10,'spd_m5':-5,'spd_m1':-1,
                     'spd_p1':  1,'spd_p5':   5,'spd_p10': 10,'spd_p25': 25
                     }.get(action, 0)
            seg = dict(self.mod_project.merged_segments.get(sid, {}))
            if seg:
                self._push_undo(f"speed {sid}")
                new_spd = max(0, int(seg.get('speedLimit',45)) + delta)
                graph.set_segment(sid, seg['startId'], seg['endId'],
                                  seg.get('trackClass','Mainline'),
                                  seg.get('style','Standard'),
                                  new_spd, seg.get('priority',0),
                                  seg.get('groupId',''),
                                  seg.get('gauge','Standard'))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"{sid}  speed={new_spd}mph")
        elif action == 'node_merge' and nid:
            self.merge_selected_node()
        elif action == 'node_split' and nid:
            self.split_selected_node()
        elif action == 'node_copy' and nid:
            self.copy_node_coords()
        elif action == 'node_paste_y' and nid:
            self.paste_node_height()
        elif action == 'seg_trestle' and sid:
            self.create_trestle_from_sel_segment()
        elif action == 'seg_reverse' and sid:
            seg = dict(self.mod_project.merged_segments.get(sid, {}))
            if seg:
                self._push_undo(f"reverse segment {sid}")
                graph.set_segment(sid, seg['endId'], seg['startId'],
                                  seg.get('trackClass','Mainline'),
                                  seg.get('style','Standard'),
                                  seg.get('speedLimit',45), seg.get('priority',0),
                                  seg.get('groupId',''),
                                  seg.get('gauge','Standard'))

                self._commit_mod_layer_edit(graph, graph_changed=True)
                self._set_status(f"Reversed {sid}")

    def create_node_at(self, sx: float, sy: float):
        """Create a new node at screen position, write to graph layer."""
        if not self.mod_project:
            self._set_status("Load a mod first to create nodes")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            self._set_status("No game-graph layer in mod")
            return
        ux, uz = self.screen_to_unity(sx, sy)
        anchor_node = self._resolve_measure_anchor()
        ux, uz, _lock_info = self._apply_measure_constraints(ux, uz, anchor=anchor_node)

        # Determine Y based on placement mode
        if self.place_y_lock:
            uy = self.place_y_value
        elif self.place_y_inherit and getattr(self, '_last_placed_y', None) is not None:
            uy = self._last_placed_y
        else:
            uy = self._sample_terrain_y(ux, uz)

        nid = self.mod_project.next_node_id()
        self._push_undo(f"create node {nid}")
        graph.set_node(nid, ux, uy, uz, 0.0, 0.0, 0.0, False)

        self._commit_mod_layer_edit(graph, graph_changed=True)
        # A standalone node has no segment to make it visible. Node overlays
        # default to off, so enable the track/node layers after placement.
        self.show_tracks = True
        self.show_nodes = True
        # Remember Y for inherit mode
        self._last_placed_y = uy
        self._last_placed_node_id = nid
        # Select the new node
        self.sel_mod_node_id = nid
        self.sel_mod_seg_id  = None
        for i, l in enumerate(self.mod_project.layers):
            if l is graph:
                self.sel_mod_layer_idx = i
                break
        mode_tag = " [locked Y]" if self.place_y_lock else " [inherited Y]" if self.place_y_inherit else ""
        self._set_status(f"Created {nid}  ({ux:.1f}, {uy:.1f}, {uz:.1f}){mode_tag}")

    def delete_selected(self):
        """Delete selected node or segment — writes null to graph layer."""
        if not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        if self.sel_mod_node_id:
            nid = self.sel_mod_node_id
            self._push_undo(f"delete node {nid}")
            # Also delete all connected segments
            conn = self.mod_project.segments_for_node(nid)
            for s in conn:
                graph.delete_segment(s['id'])
            graph.delete_node(nid)
            self.sel_mod_node_id  = None
            self.sel_mod_seg_id   = None
            self._connect_from_node = None
            msg = f"Deleted node {nid}"
            if conn:
                msg += f" + {len(conn)} segment(s)"
            self._set_status(msg)
        elif self.sel_mod_seg_id:
            sid = self.sel_mod_seg_id
            self._push_undo(f"delete segment {sid}")
            graph.delete_segment(sid)
            self.sel_mod_seg_id = None
            self._set_status(f"Deleted segment {sid}")

        self._commit_mod_layer_edit(graph, graph_changed=True)

    def start_connect(self):
        """Begin connecting from selected node — next node click completes the segment."""
        if self.sel_mod_node_id and self.mod_project:
            self._connect_from_node = self.sel_mod_node_id
            self._set_status(
                f"Connect: click target node to link from {self._connect_from_node}  "
                f"(Escape to cancel)")

    def finish_connect(self, target_node_id: str):
        """Complete a segment from _connect_from_node to target_node_id."""
        if not self._connect_from_node or not self.mod_project:
            return
        if target_node_id == self._connect_from_node:
            self._set_status("Cannot connect a node to itself")
            self._connect_from_node = None
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        sid = self.mod_project.next_seg_id()
        self._push_undo(f"create segment {sid}")
        graph.set_segment(sid, self._connect_from_node, target_node_id,
                          'Mainline', 'Standard', 45, 0, '',
                          getattr(self, 'geo_gauge', 'Standard'))

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(
            f"Created segment {sid}: {self._connect_from_node} → {target_node_id}")
        self.sel_mod_seg_id    = sid
        self.sel_mod_node_id   = None
        for i, l in enumerate(self.mod_project.layers):
            if l is graph:
                self.sel_mod_layer_idx = i
                break
        self._connect_from_node = None

    def _sample_terrain_y(self, ux: float, uz: float) -> float:
        """Sample the heightmap at a Unity world position and return Y in metres.
        Falls back to 0 if no tile data is available at that position."""
        # Convert Unity coords to screen then read pixel
        sx, sy = self.unity_to_screen(ux, uz)
        result = self._read_pixel_at(sx, sy)
        if result is not None:
            h16, _, _ = result
            return h16 / 65535.0 * (HEIGHT_MAX_M - HEIGHT_MIN_M) + HEIGHT_MIN_M
        return 0.0

    def _bezier_tangent_rotY(self, n0: dict, n1: dict, t: float = 0.5) -> float:
        """Heading (rotY) of the bezier curve tangent at parameter t.
        Uses the same control-point selection as the rendered segment geometry."""
        import math as _m
        p0, p1, p2, p3 = _bezier_control_points(n0, n1)
        x0, z0 = p0[0], p0[2]
        cx0, cz0 = p1[0], p1[2]
        cx1, cz1 = p2[0], p2[2]
        x1, z1 = p3[0], p3[2]
        dx = 3*(1-t)**2*(cx0-x0) + 6*(1-t)*t*(cx1-cx0) + 3*t**2*(x1-cx1)
        dz = 3*(1-t)**2*(cz0-z0) + 6*(1-t)*t*(cz1-cz0) + 3*t**2*(z1-cz1)
        if _m.sqrt(dx*dx+dz*dz) < 0.001:
            return n0.get('rotY', 0)
        return _m.degrees(_m.atan2(dx, dz)) % 360

    def _bezier_tangent_rotX(
            self, n0: dict, n1: dict, t: float = 0.5) -> float:
        """Pitch of the 3D bezier tangent in the n0-to-n1 direction."""
        p0, p1, p2, p3 = _bezier_control_points(n0, n1)
        omt = 1.0 - float(t)
        dx = (
            3.0 * omt * omt * (p1[0] - p0[0])
            + 6.0 * omt * t * (p2[0] - p1[0])
            + 3.0 * t * t * (p3[0] - p2[0])
        )
        dy = (
            3.0 * omt * omt * (p1[1] - p0[1])
            + 6.0 * omt * t * (p2[1] - p1[1])
            + 3.0 * t * t * (p3[1] - p2[1])
        )
        dz = (
            3.0 * omt * omt * (p1[2] - p0[2])
            + 6.0 * omt * t * (p2[2] - p1[2])
            + 3.0 * t * t * (p3[2] - p2[2])
        )
        plan_run = math.hypot(dx, dz)
        if plan_run < 0.001:
            return float(n0.get('rotX', 0.0))
        return -math.degrees(math.atan2(dy, plan_run))

    def _turnout_settings_error(self):
        leg = float(self.turnout_leg_length)
        angle = abs(float(self.turnout_diverge_angle))
        if leg < float(self.turnout_min_leg_m):
            return (
                f"Turnout leg {leg:.1f} m is under the "
                f"{self.turnout_min_leg_m:.0f} m minimum"
            )
        if angle < 1.0 or angle > float(self.turnout_max_angle_deg):
            return (
                f"Turnout angle must be between 1 and "
                f"{self.turnout_max_angle_deg:.0f} degrees"
            )
        radius = turnout_radius_for_chord(leg, angle)
        if radius is None or radius < float(self.alignment_min_radius_m):
            radius_text = "unknown" if radius is None else f"{radius:.1f} m"
            return (
                f"Turnout radius {radius_text} is under the "
                f"{self.alignment_min_radius_m:.0f} m minimum"
            )
        return None

    def _turnout_approach_grade_pct(
            self, sw: dict, approach_rot_y: float,
            entry_segs: list, forward_segs: list) -> float:
        """Estimate the tangent grade through a proposed turnout frog."""
        grades = []
        grade_sources = [
            (segment, 'startId', True) for segment in entry_segs[:1]
        ]
        grade_sources.extend(
            (segment, 'endId', False) for segment in forward_segs[:1]
        )
        for segment, other_key, incoming in grade_sources:
            other = self.mod_project.merged_nodes.get(segment.get(other_key, ''))
            if not other:
                continue
            run = math.hypot(
                float(sw.get('x', 0.0)) - float(other.get('x', 0.0)),
                float(sw.get('z', 0.0)) - float(other.get('z', 0.0)),
            )
            if run < 0.01:
                continue
            rise = (
                float(sw.get('y', 0.0)) - float(other.get('y', 0.0))
                if incoming else
                float(other.get('y', 0.0)) - float(sw.get('y', 0.0))
            )
            grades.append(rise / run * 100.0)

        if grades:
            return sum(grades) / len(grades)

        # A standalone switch can still preserve the pitch already stored on
        # the node. Account for a possible 180-degree yaw-axis reversal.
        local_grade = -math.tan(math.radians(float(sw.get('rotX', 0.0)))) * 100.0
        node_yaw = math.radians(float(sw.get('rotY', approach_rot_y)))
        approach_yaw = math.radians(float(approach_rot_y))
        orientation = 1.0 if (
            math.sin(node_yaw) * math.sin(approach_yaw)
            + math.cos(node_yaw) * math.cos(approach_yaw)
        ) >= 0.0 else -1.0
        return local_grade * orientation

    def _add_turnout_leg(
            self, sw_node_id: str, commit: bool = True) -> bool:
        """Add diverge leg to a switch node after inserting it into a segment.
        switch rotY = approach direction. diverge turns by turnout_diverge_angle."""
        if not self.mod_project:
            return False
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return False
        sw = self.mod_project.merged_nodes.get(sw_node_id)
        if not sw:
            return False
        settings_error = self._turnout_settings_error()
        if settings_error:
            self._set_status(settings_error)
            return False

        pid = self.mod_project.definition.get('id','T').replace('.','_')[:6]

        # Approach = direction of entry segment arriving at sw
        conn      = self.mod_project.segments_for_node(sw_node_id)
        entry_segs = [s for s in conn if s.get('endId') == sw_node_id]
        forward_segs = [s for s in conn if s.get('startId') == sw_node_id]
        entry_seg = entry_segs[0] if entry_segs else None
        approach_rotY = sw.get('rotY', 0)
        if entry_seg:
            n0 = self.mod_project.merged_nodes.get(entry_seg['startId'])
            if n0:
                approach_rotY = self._bezier_tangent_rotY(n0, sw, t=1.0)

        sign = 1.0 if self.turnout_direction == 'right' else -1.0
        leg = float(self.turnout_leg_length)
        grade_pct = self._turnout_approach_grade_pct(
            sw, approach_rotY, entry_segs, forward_segs,
        )
        div_x, div_y, div_z, div_rot_x, div_rotY = turnout_leg_pose(
            float(sw['x']), float(sw['y']), float(sw['z']),
            approach_rotY,
            sign * float(self.turnout_diverge_angle),
            leg,
            grade_pct=grade_pct,
        )
        sw_rot_x = -math.degrees(math.atan(grade_pct / 100.0))

        # Generate unique IDs — pass exclude set so they don't collide with each other
        div_sid = self.mod_project.next_seg_id()
        div_nid = self.mod_project.next_node_id({div_sid})

        # Place diverge node
        graph.set_node(
            div_nid, div_x, div_y, div_z,
            div_rot_x, div_rotY, 0, False,
        )
        # switch_node → diverge_node
        graph.set_segment(div_sid, sw_node_id, div_nid,
                          self.turnout_div_class, 'Standard',
                          int(self.turnout_div_speed), 0, '',
                          getattr(self, 'geo_gauge', 'Standard'))
        # Update switch node: rotY = approach, flipSwitchStand as set
        graph.set_node(sw_node_id, sw['x'], sw['y'], sw['z'],
                       sw_rot_x, approach_rotY,
                       sw.get('rotZ',0), self.turnout_flip)

        if commit:
            self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(
            f"Turnout: sw={sw_node_id}  approach={approach_rotY:.1f}°  "
            f"diverge {self.turnout_direction} {self.turnout_diverge_angle}° → {div_nid}")

        return True

    def _insert_node_into_segment(
            self, node_id: str, seg_id: str,
            commit: bool = True) -> bool:
        """Insert node_id into seg_id, replacing it with two new segments.
        Sets the inserted node's rotY to the bezier tangent direction at insertion point."""
        if not self.mod_project:
            return False
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return False
        seg = self.mod_project.merged_segments.get(seg_id)
        if not seg:
            self._set_status(f"Segment {seg_id} not found")
            return False
        start_id = seg['startId']
        end_id   = seg['endId']
        tc       = seg.get('trackClass','Mainline')
        st       = seg.get('style','Standard')
        spd      = seg.get('speedLimit',45)
        pri      = seg.get('priority',0)
        grp      = seg.get('groupId','')
        gauge    = seg.get('gauge','Standard')

        # Compute the tangent direction at the node's position along the segment
        n0s = self.mod_project.merged_nodes.get(start_id)
        n1s = self.mod_project.merged_nodes.get(end_id)
        node = self.mod_project.merged_nodes.get(node_id)
        if n0s and n1s and node:
            import math as _m
            # Find t parameter: project node position onto segment
            dx2 = n1s['x']-n0s['x']; dz2 = n1s['z']-n0s['z']
            seg_len = _m.sqrt(dx2*dx2+dz2*dz2)
            if seg_len > 0.01:
                t = max(0, min(1, ((node['x']-n0s['x'])*dx2 +
                                   (node['z']-n0s['z'])*dz2) / (seg_len*seg_len)))
            else:
                t = 0.5
            correct_rotY = self._bezier_tangent_rotY(n0s, n1s, t)
            correct_rotX = self._bezier_tangent_rotX(n0s, n1s, t)
            # Update node with correct heading
            graph.set_node(node_id, node['x'], node['y'], node['z'],
                           correct_rotX, correct_rotY,
                           node.get('rotZ',0), node.get('flipSwitchStand',False))

        # Delete original segment, create two new ones
        graph.delete_segment(seg_id)
        sid_a = self.mod_project.next_seg_id()
        sid_b = self.mod_project.next_seg_id({sid_a})
        graph.set_segment(
            sid_a, start_id, node_id, tc, st, spd, pri, grp, gauge
        )
        graph.set_segment(
            sid_b, node_id, end_id, tc, st, spd, pri, grp, gauge
        )

        if commit:
            self._commit_mod_layer_edit(graph, graph_changed=True)
        else:
            self.mod_project._rebuild_merge()
        self.sel_mod_node_id = node_id
        self.sel_mod_seg_id  = None
        self._set_status(
            f"Inserted {node_id} into {seg_id} → [{sid_a}] + [{sid_b}]")

        return True

    def _insert_turnout_into_segment(
            self, node_id: str, seg_id: str) -> bool:
        """Split a segment and add its diverging leg as one saved edit."""
        settings_error = self._turnout_settings_error()
        if settings_error:
            self._set_status(settings_error)
            return False
        if not self._insert_node_into_segment(
                node_id, seg_id, commit=False):
            return False
        if not self._add_turnout_leg(node_id, commit=False):
            return False
        graph = self.mod_project.get_graph_layer()
        if graph is None:
            return False
        self._commit_mod_layer_edit(graph, graph_changed=True)
        self.sel_mod_node_id = node_id
        self.sel_mod_seg_id = None
        return True

    def _commit_node_drag(self, node_id: str, new_ux: float, new_uz: float):
        """Write the moved node to the mod game-graph layer and trigger SC reload."""
        if not self.mod_project:
            return
        graph_layer = self.mod_project.get_graph_layer()
        if graph_layer is None:
            self._set_status("No game-graph layer found in mod — cannot save node move")
            return

        # Get existing node data for Y and rotation
        existing = self.mod_project.merged_nodes.get(node_id)
        if existing:
            old_y   = existing.get('y', 0.0)
            rotX    = existing.get('rotX', 0.0)
            rotY    = existing.get('rotY', 0.0)
            rotZ    = existing.get('rotZ', 0.0)
            flip    = existing.get('flipSwitchStand', False)
        else:
            old_y, rotX, rotY, rotZ, flip = 0.0, 0.0, 0.0, 0.0, False

        # Sample terrain height at new position
        new_y = self._sample_terrain_y(new_ux, new_uz)
        if new_y == 0.0:
            new_y = old_y  # keep existing if no tile data

        self._push_undo(f"move node {node_id}")
        # Write to graph layer
        graph_layer.set_node(node_id, new_ux, new_y, new_uz,
                             rotX, rotY, rotZ, flip)
        # Full rebuild — updates merged_nodes then rebuilds all layer curves

        # Update the selected node's layer index to graph layer
        for i, l in enumerate(self.mod_project.layers):
            if l is graph_layer:
                self.sel_mod_layer_idx = i
                break

        # Refresh all curves, then save and hot-reload.
        self._commit_mod_layer_edit(graph_layer, graph_changed=True)

        self._set_status(
            f"Moved {node_id} → ({new_ux:.1f}, {new_y:.1f}, {new_uz:.1f})  "
            f"[saved to {graph_layer.label}]")

    def screen_to_unity(self, sx: float, sy: float) -> tuple:
        """Convert screen pixel → Unity world (x, z)."""
        ts  = self.tile_size * self.zoom
        ftx = (sx - self.pan_x) / ts + self.min_x
        ftz = self.max_y + 1 - (sy - self.pan_y) / ts
        ux  = ftx * self.UNITY_TILE
        uz  = ftz * self.UNITY_TILE
        return ux, uz

    def _point_to_screen_segment_distance(self, px: float, py: float,
                                          ax: float, ay: float,
                                          bx: float, by: float) -> float:
        dx = float(bx) - float(ax)
        dy = float(by) - float(ay)
        if abs(dx) < 1e-6 and abs(dy) < 1e-6:
            return math.hypot(float(px) - float(ax), float(py) - float(ay))
        t = (((float(px) - float(ax)) * dx) + ((float(py) - float(ay)) * dy)) / (dx * dx + dy * dy)
        t = max(0.0, min(1.0, t))
        qx = float(ax) + dx * t
        qy = float(ay) + dy * t
        return math.hypot(float(px) - qx, float(py) - qy)

    def _point_to_world_polyline_distance(self, sx: float, sy: float, points) -> float:
        if not points:
            return float('inf')
        screen_pts = [self.unity_to_screen(float(x), float(z)) for x, z in points]
        if len(screen_pts) == 1:
            px, py = screen_pts[0]
            return math.hypot(float(sx) - float(px), float(sy) - float(py))
        best = float('inf')
        for (ax, ay), (bx, by) in zip(screen_pts, screen_pts[1:]):
            best = min(best, self._point_to_screen_segment_distance(sx, sy, ax, ay, bx, by))
        return best

    def pick_mod_element(self, sx: float, sy: float, radius_px: float = 12.0):
        """Find the closest node or segment within radius_px of screen pos.
        Prefers editable mod-layer geometry first, then falls back to loaded/live track.
        Returns (kind, id, layer_idx) where layer_idx is None for bridge/loaded data.
        kind is 'node' or 'segment'.
        """
        best_dist = radius_px
        best_kind = None
        best_id = None
        best_li = None

        def consider_node(node_id, x_val, z_val, layer_idx):
            nonlocal best_dist, best_kind, best_id, best_li
            nx, ny2 = self.unity_to_screen(float(x_val), float(z_val))
            d = math.hypot(float(nx) - float(sx), float(ny2) - float(sy))
            if d < best_dist:
                best_dist = d
                best_kind = 'node'
                best_id = node_id
                best_li = layer_idx

        def consider_segment(seg_id, pts, layer_idx):
            nonlocal best_dist, best_kind, best_id, best_li
            d = self._point_to_world_polyline_distance(sx, sy, pts)
            if d < best_dist:
                best_dist = d
                best_kind = 'segment'
                best_id = seg_id
                best_li = layer_idx

        if self.mod_project:
            for li, layer in enumerate(self.mod_project.layers):
                if not layer.visible:
                    continue
                for nid, node in layer.nodes.items():
                    if node.get('deleted'):
                        continue
                    consider_node(nid, node.get('x', 0.0), node.get('z', 0.0), li)
            if best_kind is None:
                for li, layer in enumerate(self.mod_project.layers):
                    if not layer.visible:
                        continue
                    for pts, _col, seg_id in layer.curves:
                        if pts:
                            consider_segment(seg_id, pts, li)

        if self.show_tracks and best_kind is None:
            for nx2, nz2, nid in self.track_node_list:
                consider_node(nid, nx2, nz2, None)
            if best_kind is None:
                for seg_id, pts in self.track_segment_points.items():
                    if pts:
                        consider_segment(seg_id, pts, None)

        return best_kind, best_id, best_li

    def _init_bridge(self):
        """Start the RailroaderBridge background watcher.
        Tries to infer the Railroader game directory from the tile folder path.
        e.g. C:/Steam/.../Railroader/Mods/tiles/ -> C:/Steam/.../Railroader
        Falls back to auto-detect (searches Steam paths).
        """
        game_dir = None
        if self.folders:
            # Walk up from the first tile folder looking for a Mods/ sibling
            p = Path(self.folders[0]).resolve()
            for parent in [p] + list(p.parents):
                if (parent / 'Mods').is_dir() and (parent / 'Railroader_Data').is_dir():
                    game_dir = str(parent)
                    break
        self.bridge = RailroaderBridge(game_dir=game_dir)
        self._configure_bridge(self.bridge)
        self.bridge.on_state_update = self._on_bridge_state
        self.bridge.on_connect    = lambda: self._set_status("Bridge: Railroader connected ●")
        self.bridge.on_disconnect = lambda: self._set_status("Bridge: Railroader disconnected")
        self.bridge.start()
        print(f"[bridge] game_dir = {self.bridge.game_dir}")
        print(f"[bridge] watching = {self.bridge._state_file}")
        print(f"[bridge] file exists = {self.bridge._state_file.exists()}")
        self._set_status(f"Bridge: {self.bridge._state_file}")

    def _on_bridge_state(self, state):
        """Called from the bridge watcher thread — just stash the state."""
        with self._bridge_lock:
            self._bridge_pending_state = state
            self.bridge_connected = True

    def _apply_bridge_state(self, state):
        """Called from the main thread to update track graph and cars from bridge state."""
        import math
        # Rebuild track_nodes dict
        self.track_nodes = {
            n.id: (n.x, n.z, n.rotY)
            for n in state.nodes
        }
        self.track_node_list = [(n.x, n.z, n.id) for n in state.nodes]
        self.track_node_elevs = {n.id: n.y for n in state.nodes}
        # Keep full objects for property display
        self._bridge_nodes_raw    = {n.id: n for n in state.nodes}
        self._bridge_segments_raw = {s.id: s for s in state.segments}

        # trackClass int -> colour key
        CLASS_NAMES = {0: 'Mainline', 1: 'Branch', 2: 'Industrial'}

        self.track_segments = []
        self.track_segment_meta = []
        self.track_segment_points = {}
        for seg in state.segments:
            if seg.startId not in self.track_nodes or seg.endId not in self.track_nodes:
                continue
            x0, z0, ry0 = self.track_nodes[seg.startId]
            x1, z1, ry1 = self.track_nodes[seg.endId]
            dist = math.sqrt((x1-x0)**2 + (z1-z0)**2)
            if dist < 0.1:
                continue
            tc = CLASS_NAMES.get(seg.trackClass, 'Mainline')
            start_raw = self._bridge_nodes_raw.get(seg.startId)
            end_raw = self._bridge_nodes_raw.get(seg.endId)
            pts = _bezier_for_nodes(
                {
                    'x': x0,
                    'y': self.track_node_elevs.get(seg.startId, 0.0),
                    'z': z0,
                    'rotX': float(getattr(start_raw, 'rotX', 0.0) or 0.0),
                    'rotY': ry0,
                },
                {
                    'x': x1,
                    'y': self.track_node_elevs.get(seg.endId, 0.0),
                    'z': z1,
                    'rotX': float(getattr(end_raw, 'rotX', 0.0) or 0.0),
                    'rotY': ry1,
                },
            )
            self.track_segments.append((pts, tc))
            self.track_segment_points[seg.id] = pts
            y0 = self.track_node_elevs.get(seg.startId, 0.0)
            y1 = self.track_node_elevs.get(seg.endId, 0.0)
            self.track_segment_meta.append({
                'id': seg.id,
                'start_id': seg.startId,
                'end_id': seg.endId,
                'track_class': tc,
                'start_y': y0,
                'end_y': y1,
                'run_m': dist,
                'grade_pct': ((y1 - y0) / dist * 100.0) if dist > 0.1 else None,
            })

        # Cars
        self.bridge_cars = list(state.cars)
        self._mark_measure_cache_dirty()
        self.show_tracks = True
        if not self.tiles and self.track_node_list:
            self._fit_track_view()

    def _poll_bridge(self):
        """Call once per frame from the main loop to apply any pending bridge state."""
        with self._bridge_lock:
            state = self._bridge_pending_state
            self._bridge_pending_state = None
            editor_commands = self._bridge_pending_editor_commands[:]
            self._bridge_pending_editor_commands.clear()
            if self.bridge is not None:
                self.bridge_connected = self.bridge.connected
        for command in editor_commands:
            self._handle_editor_bridge_command(command)
        self._update_game_sync_locks()
        if state is not None:
            # Only rebuild the geometry if something actually changed
            fingerprint = self._bridge_track_fingerprint(state)
            if fingerprint != self._bridge_last_fingerprint:
                self._bridge_last_fingerprint = fingerprint
                self._apply_bridge_state(state)
            else:
                # Just update cars — cheap
                self.bridge_cars = list(state.cars)

        now = time.monotonic()
        if (
            self.bridge is not None
            and now - self._last_editor_state_publish >= 0.5
        ):
            self._last_editor_state_publish = now
            try:
                self.bridge.publish_editor_state(self._editor_bridge_state())
            except OSError:
                pass

    # ------------------------------------------------------------------
    # Coordinate math
    # ------------------------------------------------------------------
    def _update_bounds(self):
        if not self.tiles:
            self.min_x = self.max_x = self.min_y = self.max_y = 0
            return
        tile_values = list(self.tiles.values())
        xs = [t.x for t in tile_values]
        ys = [t.y for t in tile_values]
        self.min_x, self.max_x = min(xs), max(xs)
        self.min_y, self.max_y = min(ys), max(ys)

    def _fit_view(self):
        w, h = self.screen.get_size()
        map_w = (self.max_x - self.min_x + 1) * self.tile_size
        map_h = (self.max_y - self.min_y + 1) * self.tile_size
        self.zoom  = min(w / map_w, (h - PANEL_H) / map_h) * 0.9
        self.pan_x = (w - map_w * self.zoom) / 2
        self.pan_y = PANEL_H + ((h - PANEL_H) - map_h * self.zoom) / 2

    def _fit_track_view(self, pad_tiles: float = 1.0):
        if not self.track_node_list:
            return
        txs = [float(nx) / self.UNITY_TILE for nx, _nz, _nid in self.track_node_list]
        tzs = [float(nz) / self.UNITY_TILE for _nx, nz, _nid in self.track_node_list]
        self.min_x = math.floor(min(txs) - pad_tiles)
        self.max_x = math.ceil(max(txs) + pad_tiles)
        self.min_y = math.floor(min(tzs) - pad_tiles)
        self.max_y = math.ceil(max(tzs) + pad_tiles)
        self._fit_view()

    def tile_screen_pos(self, tx, ty):
        x = (tx - self.min_x) * self.tile_size * self.zoom + self.pan_x
        y = (self.max_y - ty) * self.tile_size * self.zoom + self.pan_y
        return x, y

    def screen_to_tile(self, sx, sy):
        tx = math.floor((sx - self.pan_x) / (self.tile_size * self.zoom)) + self.min_x
        ty = self.max_y - math.floor((sy - self.pan_y) / (self.tile_size * self.zoom))
        return tx, ty

    # ------------------------------------------------------------------
    # Recoverable whole-tile cleanup
    # ------------------------------------------------------------------
    def _tile_cleanup_box_keys(self, start=None, end=None):
        """Return loaded tile keys inside an inclusive tile-coordinate box."""
        start = start if start is not None else self.tile_delete_drag_start
        end = end if end is not None else self.tile_delete_drag_end
        if start is None or end is None:
            return set()
        x0, x1 = sorted((int(start[0]), int(end[0])))
        y0, y1 = sorted((int(start[1]), int(end[1])))
        result = set()
        # Filter the loaded keys instead of iterating every coordinate in a
        # potentially enormous off-map drag rectangle.
        for key in self.tiles:
            try:
                tx_text, ty_text = key.split(',', 1)
                tx, ty = int(tx_text), int(ty_text)
            except (AttributeError, TypeError, ValueError):
                continue
            if x0 <= tx <= x1 and y0 <= ty <= y1:
                result.add(key)
        return result

    def _tile_cleanup_preview_keys(self):
        if not self.tile_delete_dragging:
            return set()
        return self._tile_cleanup_box_keys()

    def _commit_tile_cleanup_box(self):
        box_keys = self._tile_cleanup_box_keys()
        operation = self.tile_delete_drag_operation
        if operation == 'add':
            self.tile_delete_selection.update(box_keys)
        elif operation == 'subtract':
            self.tile_delete_selection.difference_update(box_keys)
        else:
            self.tile_delete_selection = set(box_keys)
        self.tile_delete_confirm = False
        self.tile_delete_dragging = False
        self.tile_delete_drag_start = None
        self.tile_delete_drag_end = None
        self._set_status(
            f"Tile Cleanup: {len(self.tile_delete_selection)} tile(s) marked"
        )

    def _toggle_tile_cleanup(self):
        self.tile_delete_mode = not self.tile_delete_mode
        self.tile_delete_confirm = False
        self.tile_delete_dragging = False
        self.tile_delete_drag_start = None
        self.tile_delete_drag_end = None
        if self.tile_delete_mode:
            self.edit_mode = False
            self.select_mode = False
            self._close_workspace_panels()
            self._set_status(
                "Tile Cleanup ON - drag a box; Shift adds, Ctrl/right-click removes"
            )
        else:
            self.tile_delete_selection.clear()
            self._set_status("Tile Cleanup OFF")

    def _tile_cleanup_recovery_target(self, original_path, stamp):
        original_path = Path(original_path).resolve()
        source_dir = original_path.parent
        recovery_root = (
            source_dir.parent
            / '_TileEditor_Deleted_Tiles'
            / source_dir.name
            / stamp
        )
        return recovery_root / original_path.name

    def _write_tile_cleanup_manifests(self, entries):
        by_folder = collections.defaultdict(list)
        for entry in entries:
            recovery_path = Path(entry['recovery_path'])
            by_folder[recovery_path.parent].append(entry)
        for recovery_folder, folder_entries in by_folder.items():
            payload = {
                'format': 'Hrogers Tile Editor recoverable tile cleanup',
                'createdUtc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
                'tiles': [
                    {
                        'tile': entry['key'],
                        'original': str(entry['original_path']),
                        'recovery': str(entry['recovery_path']),
                    }
                    for entry in folder_entries
                ],
            }
            try:
                (recovery_folder / 'restore-manifest.json').write_text(
                    json.dumps(payload, indent=2), encoding='utf-8'
                )
            except OSError as ex:
                print(f"[tile cleanup] manifest failed: {ex}")

    def _delete_selected_tiles(self):
        """Move marked tile files to recovery and remove them from the canvas."""
        selected = sorted(set(self.tile_delete_selection) & set(self.tiles))
        if not selected:
            self.tile_delete_confirm = False
            self._set_status("Tile Cleanup: mark at least one loaded tile")
            return False
        if getattr(self, '_game_terrain_sync_locked', False):
            self.tile_delete_confirm = False
            self._set_status(
                "Game has unsaved terrain edits; tile cleanup is paused"
            )
            return False

        stamp = time.strftime('%Y%m%d-%H%M%S') + f'-{time.time_ns() % 1000000:06d}'
        entries = []
        failures = []
        for key in selected:
            tile = self.tiles.get(key)
            if tile is None:
                continue
            if tile.path is None:
                failures.append(f'{key} has no source file')
                continue
            original_path = Path(tile.path).resolve()
            if not original_path.is_file():
                failures.append(f'{key} source is missing')
                continue
            recovery_path = self._tile_cleanup_recovery_target(
                original_path, stamp
            )
            try:
                recovery_path.parent.mkdir(parents=True, exist_ok=True)
                if tile.dirty:
                    # Preserve the exact edited pixels, then remove the old source.
                    tile.write_copy(recovery_path)
                    original_path.unlink()
                else:
                    shutil.move(str(original_path), str(recovery_path))
            except OSError as ex:
                failures.append(f'{key}: {ex}')
                try:
                    if recovery_path.exists() and original_path.exists():
                        recovery_path.unlink()
                except OSError:
                    pass
                continue

            entries.append({
                'key': key,
                'tile': tile,
                'original_path': original_path,
                'recovery_path': recovery_path,
            })
            self.tiles.pop(key, None)

        if not entries:
            self.tile_delete_confirm = False
            detail = failures[0] if failures else 'no files were moved'
            self._set_status(f"Tile Cleanup failed: {detail}")
            return False

        self._write_tile_cleanup_manifests(entries)
        for source_folder in {
                Path(entry['original_path']).parent for entry in entries}:
            self._sync_map_manifest(source_folder)
        self.undo_stack.append(TileDeleteRecord(entries))
        self.tile_delete_selection.difference_update(
            entry['key'] for entry in entries
        )
        self.tile_delete_confirm = False
        self._update_bounds()
        message = (
            f"Moved {len(entries)} tile(s) to recovery - Ctrl+Z restores them"
        )
        if failures:
            message += f"; {len(failures)} skipped"
        self._set_status(message)
        return True

    def _restore_deleted_tiles(self, record):
        restored = 0
        conflicts = 0
        for entry in record.entries:
            key = entry['key']
            tile = entry['tile']
            original_path = Path(entry['original_path'])
            recovery_path = Path(entry['recovery_path'])
            if original_path.exists():
                conflicts += 1
                continue
            if not recovery_path.exists():
                conflicts += 1
                continue
            try:
                original_path.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(recovery_path), str(original_path))
            except OSError:
                conflicts += 1
                continue
            tile.path = original_path
            tile.dirty = False
            self.tiles[key] = tile
            restored += 1
        for source_folder in {
                Path(entry['original_path']).parent for entry in record.entries}:
            self._sync_map_manifest(source_folder)
        self._update_bounds()
        if restored:
            message = f"Restored {restored} deleted tile(s)"
            if conflicts:
                message += f"; {conflicts} conflict(s) left in recovery"
            self._set_status(message)
        else:
            self._set_status("Deleted tiles could not be restored; recovery was preserved")

    def screen_to_pixel(self, sx, sy):
        """
        Convert screen (sx,sy) → (tile_key, pixel_row, pixel_col) at tile resolution.
        Returns (None, None, None) if not over a loaded tile.
        """
        tx, ty = self.screen_to_tile(sx, sy)
        key = f'{tx},{ty}'
        tile = self.tiles.get(key)
        if tile is None:
            return None, None, None
        # Position within tile in [0,1]
        tile_px, tile_py = self.tile_screen_pos(tx, ty)
        ts = self.tile_size * self.zoom
        fx = (sx - tile_px) / ts   # 0..1 left→right
        fy = (sy - tile_py) / ts   # 0..1 top→bottom
        res = tile.full_w
        col = int(np.clip(fx * res, 0, res - 1))
        row = int(np.clip(fy * res, 0, res - 1))
        return key, row, col

    def unity_to_screen(self, ux, uz):
        tx = ux / self.UNITY_TILE
        tz = uz / self.UNITY_TILE
        sx = (tx - self.min_x) * self.tile_size * self.zoom + self.pan_x
        sy = (self.max_y - tz + 1) * self.tile_size * self.zoom + self.pan_y
        return int(sx), int(sy)

    def invalidate_all(self):
        for tile in list(self.tiles.values()):
            tile.invalidate()

    # ------------------------------------------------------------------
    # World-pixel coordinate helpers
    # ------------------------------------------------------------------
    def screen_to_wp(self, sx: float, sy: float) -> tuple:
        """Convert screen pos → world-pixel (row, col). May be outside tile bounds."""
        ts = self.tile_size * self.zoom
        res = OVERVIEW_RES
        # Fractional tile position
        ftx = (sx - self.pan_x) / ts + self.min_x
        fty = self.max_y - (sy - self.pan_y) / ts
        wp_col = int(math.floor((ftx - self.min_x) * TILE_STRIDE))
        wp_row = int(math.floor((self.max_y - fty) * TILE_STRIDE))
        return wp_row, wp_col

    def wp_to_screen(self, wp_row: int, wp_col: int) -> tuple:
        """Convert world-pixel → screen position (top-left of that pixel)."""
        ts = self.tile_size * self.zoom
        px_size = ts / TILE_STRIDE
        sx = self.pan_x + (wp_col + (self.min_x * TILE_STRIDE) - self.min_x * TILE_STRIDE) * px_size
        # Simplified: col in world = col - min_x*TILE_STRIDE would be rel col
        # wp_col is absolute: tile tx = wp_col//TILE_STRIDE + min_x
        sx = self.pan_x + (wp_col / TILE_STRIDE - 0) * ts + (self.min_x - self.min_x) * ts
        # Cleaner: derive from tile_screen_pos
        tx = wp_col // TILE_STRIDE + self.min_x
        lc = wp_col % TILE_STRIDE
        tile_sx, tile_sy = self.tile_screen_pos(tx, self.max_y)  # ty=max_y gives row=0
        # Actually compute properly
        ty = self.max_y - wp_row // TILE_STRIDE
        lr = wp_row % TILE_STRIDE
        tsx, tsy = self.tile_screen_pos(tx, ty)
        px_sz = ts / TILE_STRIDE
        sx = tsx + lc * px_sz
        sy = tsy + lr * px_sz
        return sx, sy

    def _read_tile_pixel(self, key: str, flat_idx: int) -> tuple:
        """Return (h16, veg, water) for a flat pixel index in a tile."""
        tile = self.tiles.get(key)
        if tile is None:
            return 0, 0, False
        h16   = int(tile.r[flat_idx]) * 256 + int(tile.g[flat_idx])
        veg   = (tile.a[flat_idx] >> 4) & 0x7
        water = bool((tile.a[flat_idx] >> 7) & 1)
        return h16, veg, water

    def _write_tile_pixel(self, key: str, flat_idx: int,
                          h16: int, veg: int, water: bool):
        """Write (h16, veg, water) to a flat pixel index in a tile."""
        tile = self.tiles.get(key)
        if tile is None:
            return
        tile.r[flat_idx] = (h16 >> 8) & 0xFF
        tile.g[flat_idx] =  h16        & 0xFF
        tile.a[flat_idx] = (int(water) << 7) | ((veg & 0x7) << 4)
        tile.dirty = True
        tile.invalidate()

    # ------------------------------------------------------------------
    # Selection operations
    # ------------------------------------------------------------------
    def _sel_commit_drag(self):
        """Finalise a rect drag into a SelectionBuffer."""
        if self.sel_drag_start is None or self.sel_drag_end is None:
            return
        r0 = min(self.sel_drag_start[0], self.sel_drag_end[0])
        r1 = max(self.sel_drag_start[0], self.sel_drag_end[0])
        c0 = min(self.sel_drag_start[1], self.sel_drag_end[1])
        c1 = max(self.sel_drag_start[1], self.sel_drag_end[1])
        if r0 == r1 and c0 == c1:
            self.selection = None
        else:
            self.selection = SelectionBuffer(r0, c0, r1, c1)
        self.sel_dragging   = False
        self.sel_drag_start = None
        self.sel_drag_end   = None
        n = (r1-r0+1)*(c1-c0+1) if self.selection else 0
        self._set_status(f"Selected {n:,} pixels" if n else "Selection cleared")

    def _sel_commit_lasso(self):
        """Rasterise the lasso polygon into a SelectionBuffer."""
        pts = self.sel_lasso_pts
        self.sel_lasso_pts = []
        self.sel_dragging  = False
        if len(pts) < 3:
            return
        # Convert screen points → world-pixel coords
        wp_pts = [self.screen_to_wp(sx, sy) for sx, sy in pts]
        rows   = [p[0] for p in wp_pts]
        cols   = [p[1] for p in wp_pts]
        r0, r1 = int(min(rows)), int(max(rows))
        c0, c1 = int(min(cols)), int(max(cols))
        h = r1 - r0 + 1; w = c1 - c0 + 1
        # Local coordinates for rasterisation
        local_pts = [(r - r0, c - c0) for r, c in wp_pts]
        mask = rasterise_polygon(local_pts, h, w)
        n = int(mask.sum())
        if n == 0:
            return
        self.selection = SelectionBuffer(r0, c0, r1, c1, mask)
        self._set_status(f"Lasso: {n:,} pixels selected")

    def sel_magic_wand(self, sx: float, sy: float):
        """
        Flood-fill selection from screen pos (sx,sy) selecting all connected
        world pixels within sel_wand_tol of the clicked height.
        """
        wr, wc = self.screen_to_wp(sx, sy)
        # Sample seed height
        tx0 = wc // TILE_STRIDE + self.min_x
        ty0 = self.max_y - wr // TILE_STRIDE
        key0 = f'{tx0},{ty0}'
        if key0 not in self.tiles:
            self._set_status("Magic wand: no tile here"); return
        res  = OVERVIEW_RES
        lr0  = min(wr % TILE_STRIDE, res - 1)
        lc0  = min(wc % TILE_STRIDE, res - 1)
        flat0 = lr0 * res + lc0
        tile0 = self.tiles[key0]
        seed_h16 = int(tile0.r[flat0]) * 256 + int(tile0.g[flat0])
        tol = self.sel_wand_tol

        def get_h16(wr2, wc2):
            tx2 = wc2 // TILE_STRIDE + self.min_x
            ty2 = self.max_y - wr2 // TILE_STRIDE
            key2 = f'{tx2},{ty2}'
            if key2 not in self.tiles:
                return None
            lr2 = min(wr2 % TILE_STRIDE, res - 1)
            lc2 = min(wc2 % TILE_STRIDE, res - 1)
            t2 = self.tiles[key2]
            return int(t2.r[lr2*res+lc2])*256 + int(t2.g[lr2*res+lc2])

        # BFS flood fill
        visited = set()
        queue   = [(wr, wc)]
        selected = set()
        visited.add((wr, wc))
        # Compute reasonable bounds — don't flood entire map
        max_px  = 512 * 512   # cap at ~quarter million pixels
        while queue and len(selected) < max_px:
            r2, c2 = queue.pop()
            h16_v  = get_h16(r2, c2)
            if h16_v is None:
                continue
            if abs(h16_v - seed_h16) <= tol:
                selected.add((r2, c2))
                for dr, dc in ((-1,0),(1,0),(0,-1),(0,1)):
                    nb = (r2+dr, c2+dc)
                    if nb not in visited:
                        visited.add(nb)
                        queue.append(nb)

        if not selected:
            self._set_status("Magic wand: nothing matched"); return

        rows_s = [p[0] for p in selected]
        cols_s = [p[1] for p in selected]
        r0_s = min(rows_s); r1_s = max(rows_s)
        c0_s = min(cols_s); c1_s = max(cols_s)
        mask = np.zeros((r1_s-r0_s+1, c1_s-c0_s+1), dtype=bool)
        for r2, c2 in selected:
            mask[r2-r0_s, c2-c0_s] = True
        self.selection = SelectionBuffer(r0_s, c0_s, r1_s, c1_s, mask)
        self._set_status(f"Wand: {len(selected):,} px  (tol ±{tol})")

    # ------------------------------------------------------------------
    # Erosion brush
    # ------------------------------------------------------------------
    def _apply_erosion(self, sx: float, sy: float, mode: str = 'thermal'):
        """
        Apply one pass of erosion centred at screen pos (sx,sy).
        mode: 'thermal' (talus angle) or 'hydraulic' (water flow).
        Uses same radius/strength as the current brush.
        If a selection is active, erosion is clipped to it.
        """
        r_scr = self.brush_radius
        res   = OVERVIEW_RES
        corners = [
            (sx - r_scr, sy - r_scr), (sx + r_scr, sy - r_scr),
            (sx - r_scr, sy + r_scr), (sx + r_scr, sy + r_scr),
            (sx,          sy),
        ]
        candidate_keys = set()
        for cx2, cy2 in corners:
            tx, ty = self.screen_to_tile(cx2, cy2)
            candidate_keys.add(f'{tx},{ty}')

        for key in candidate_keys:
            tile = self.tiles.get(key)
            if tile is None: continue
            screen_px_per_tile_px = (self.tile_size * self.zoom) / res
            r = max(2, int(round(r_scr / screen_px_per_tile_px)))
            r = min(r, res // 2)

            tile_px, tile_py = self.tile_screen_pos(tile.x, tile.y)
            ts = self.tile_size * self.zoom
            centre_col = ((sx - tile_px) / ts) * res
            centre_row = ((sy - tile_py) / ts) * res

            r0 = max(1, int(math.floor(centre_row - r)))
            r1 = min(res - 2, int(math.ceil(centre_row + r)))
            c0 = max(1, int(math.floor(centre_col - r)))
            c1 = min(res - 2, int(math.ceil(centre_col + r)))
            if r0 > r1 or c0 > c1: continue

            rows_idx = np.arange(r0, r1+1); cols_idx = np.arange(c0, c1+1)
            rr, cc = np.meshgrid(rows_idx, cols_idx, indexing='ij')
            dist = np.sqrt((rr - centre_row)**2 + (cc - centre_col)**2).astype(np.float32)
            t    = np.clip(dist / max(r, 1), 0, 1)
            falloff = ((1-t)**3 * (1 + 3*t + 6*t**2)).astype(np.float32)
            keep = falloff > 0.01
            flat_rows = rr[keep]; flat_cols = cc[keep]; flat_f = falloff[keep]
            flat_idx  = flat_rows * res + flat_cols

            # Undo record
            self._record_pixels(key, flat_idx.tolist())

            h16_2d = (tile.r.astype(np.int32)*256 +
                      tile.g.astype(np.int32)).reshape(res, res).astype(np.float32)
            new_h16 = h16_2d.copy()

            if mode == 'thermal':
                # Thermal erosion: if slope > talus_angle, move material downhill
                talus_h16 = int(self.brush_strength * 65535 * 2)
                for ri, ci in zip(flat_rows, flat_cols):
                    h_c = h16_2d[ri, ci]
                    for dr, dc in ((-1,0),(1,0),(0,-1),(0,1)):
                        nr, nc = ri+dr, ci+dc
                        if 0 <= nr < res and 0 <= nc < res:
                            diff = h_c - h16_2d[nr, nc]
                            if diff > talus_h16:
                                transfer = int(diff * 0.25)
                                new_h16[ri, ci]   -= transfer
                                new_h16[nr, nc]   += transfer

            elif mode == 'hydraulic':
                # Hydraulic erosion: water pools and carries sediment downhill
                # Simplified: each pixel loses material proportional to local
                # height difference from neighbours
                for ri, ci in zip(flat_rows, flat_cols):
                    h_c = h16_2d[ri, ci]
                    neighbours = []
                    for dr, dc in ((-1,0),(1,0),(0,-1),(0,1),
                                   (-1,-1),(-1,1),(1,-1),(1,1)):
                        nr, nc = ri+dr, ci+dc
                        if 0 <= nr < res and 0 <= nc < res:
                            neighbours.append(h16_2d[nr, nc])
                    if not neighbours: continue
                    avg = np.mean(neighbours)
                    if h_c > avg:
                        drop = (h_c - avg) * self.brush_strength * 0.3
                        new_h16[ri, ci] = h_c - drop

            # Apply falloff blend and write back
            h16_flat = h16_2d.ravel()
            new_flat = new_h16.ravel()
            blended  = (h16_flat[flat_idx] * (1-flat_f) +
                        new_flat[flat_idx] * flat_f).astype(np.int32)
            blended  = self._clamp_h16(blended)
            tile.r[flat_idx] = (blended >> 8).astype(np.uint8)
            tile.g[flat_idx] = (blended & 0xFF).astype(np.uint8)
            tile.dirty = True
            tile.invalidate()
            tile._recalc_stats()

    def sel_copy(self):
        """Copy selected region into clipboard."""
        if self.selection is None:
            self._set_status("Nothing selected"); return
        sel = self.selection
        h16_buf   = np.zeros((sel.h, sel.w), dtype=np.float32)
        veg_buf   = np.zeros((sel.h, sel.w), dtype=np.uint8)
        water_buf = np.zeros((sel.h, sel.w), dtype=bool)
        res = OVERVIEW_RES
        for ri, wr in enumerate(range(sel.r0, sel.r1 + 1)):
            for ci, wc in enumerate(range(sel.c0, sel.c1 + 1)):
                if not sel.mask[ri, ci]:
                    continue
                tx = wc // TILE_STRIDE + self.min_x
                ty = self.max_y - wr // TILE_STRIDE
                lr = min(wr % TILE_STRIDE, res - 1)
                lc = min(wc % TILE_STRIDE, res - 1)
                key = f'{tx},{ty}'
                flat = lr * res + lc
                h16, veg, water = self._read_tile_pixel(key, flat)
                h16_buf[ri, ci]   = h16
                veg_buf[ri, ci]   = veg
                water_buf[ri, ci] = water
        self.clipboard = Clipboard(sel.h, sel.w, h16_buf, veg_buf, water_buf)
        self._set_status(f"Copied {sel.h}×{sel.w} px region")

    def sel_cut(self):
        """Copy then fill selection with neutral height."""
        self.sel_copy()
        self.sel_fill(neutral=True)

    def sel_paste_begin(self):
        """Enter paste-preview mode — next click commits paste at that position."""
        if self.clipboard is None:
            self._set_status("Clipboard empty"); return
        self.sel_pending_paste = True
        self.sel_paste_pos     = None
        self._set_status("Click to paste · ESC to cancel")

    def sel_paste_commit(self, wp_row: int, wp_col: int, blend: float = 1.0):
        """Paste clipboard top-left at (wp_row, wp_col). Fully vectorised."""
        if self.clipboard is None:
            return
        cb  = self.clipboard
        res = OVERVIEW_RES

        # Build full index arrays for all clipboard pixels
        ris = np.arange(cb.h, dtype=np.int32)
        cis = np.arange(cb.w, dtype=np.int32)
        RI, CI = np.meshgrid(ris, cis, indexing='ij')   # (h,w)
        WR = (wp_row + RI).ravel()
        WC = (wp_col + CI).ravel()
        RI_flat = RI.ravel(); CI_flat = CI.ravel()

        TX = WC // TILE_STRIDE + self.min_x
        TY = self.max_y - WR // TILE_STRIDE
        LR = np.minimum(WR % TILE_STRIDE, res - 1)
        LC = np.minimum(WC % TILE_STRIDE, res - 1)

        # Group by tile key
        tile_groups: dict = {}
        for i in range(len(WR)):
            key = f'{TX[i]},{TY[i]}'
            if key not in self.tiles:
                continue
            if key not in tile_groups:
                tile_groups[key] = {'lr': [], 'lc': [], 'ri': [], 'ci': []}
            tile_groups[key]['lr'].append(LR[i])
            tile_groups[key]['lc'].append(LC[i])
            tile_groups[key]['ri'].append(RI_flat[i])
            tile_groups[key]['ci'].append(CI_flat[i])

        for key, td in tile_groups.items():
            tile  = self.tiles[key]
            lrs   = np.array(td['lr'], dtype=np.int32)
            lcs   = np.array(td['lc'], dtype=np.int32)
            ris2  = np.array(td['ri'], dtype=np.int32)
            cis2  = np.array(td['ci'], dtype=np.int32)
            flats = lrs * res + lcs

            # Undo
            self.undo_stack.append(UndoRecord(key, flats.copy(),
                tile.r[flats].copy(), tile.g[flats].copy(), tile.a[flats].copy()))

            src_h16   = cb.h16[ris2, cis2].astype(np.int32)
            src_veg   = cb.veg[ris2, cis2].astype(np.uint8)
            src_water = cb.water[ris2, cis2].astype(bool)

            if blend < 1.0:
                cur = tile.r[flats].astype(np.int32)*256 + tile.g[flats].astype(np.int32)
                src_h16 = (cur*(1-blend) + src_h16*blend).astype(np.int32)

            src_h16 = np.clip(src_h16, 0, 65535)
            tile.r[flats] = (src_h16 >> 8).astype(np.uint8)
            tile.g[flats] = (src_h16 & 0xFF).astype(np.uint8)
            tile.a[flats] = (src_water.astype(np.uint8) << 7) | ((src_veg & 0x7) << 4)
            tile.dirty = True
            tile.invalidate()
            tile._recalc_stats()

        self.sel_pending_paste = False
        self._set_status(f"Pasted {cb.h}×{cb.w} px")

    def sel_fill(self, neutral: bool = False):
        """Fill selection with paint target height (or neutral). Vectorised."""
        if self.selection is None:
            self._set_status("Nothing selected"); return
        sel = self.selection
        res = OVERVIEW_RES
        target_h16 = int(np.clip(
            0 if neutral else
            (int(self.paint_target) if self.paint_target is not None
             else self._m_to_h16((HEIGHT_MIN_M + HEIGHT_MAX_M) / 2)),
            0, 65535))

        # Build world-pixel arrays for the selection
        ri_arr = np.arange(sel.h, dtype=np.int32)
        ci_arr = np.arange(sel.w, dtype=np.int32)
        RI, CI = np.meshgrid(ri_arr, ci_arr, indexing='ij')
        mask_flat = sel.mask.ravel()
        RI_f = RI.ravel()[mask_flat]; CI_f = CI.ravel()[mask_flat]
        WR = sel.r0 + RI_f; WC = sel.c0 + CI_f
        TX = WC // TILE_STRIDE + self.min_x
        TY = self.max_y - WR // TILE_STRIDE
        LR = np.minimum(WR % TILE_STRIDE, res - 1)
        LC = np.minimum(WC % TILE_STRIDE, res - 1)

        tile_groups: dict = {}
        for i in range(len(WR)):
            key = f'{TX[i]},{TY[i]}'
            if key not in self.tiles: continue
            tile_groups.setdefault(key, []).append(LR[i] * res + LC[i])

        for key, flat_list in tile_groups.items():
            tile  = self.tiles[key]
            flats = np.array(flat_list, dtype=np.int32)
            self.undo_stack.append(UndoRecord(key, flats.copy(),
                tile.r[flats].copy(), tile.g[flats].copy(), tile.a[flats].copy()))
            tile.r[flats] = (target_h16 >> 8) & 0xFF
            tile.g[flats] =  target_h16        & 0xFF
            tile.dirty = True; tile.invalidate(); tile._recalc_stats()

        self._set_status(f"Filled {sel.h}×{sel.w} px")

    def sel_mirror_h(self):
        """Mirror selection data horizontally (flip left↔right) and repaint."""
        self._sel_transform(flip_h=True, flip_v=False, rot=0)

    def sel_mirror_v(self):
        """Mirror selection data vertically (flip top↔bottom) and repaint."""
        self._sel_transform(flip_h=False, flip_v=True, rot=0)

    def sel_rotate_90(self):
        """Rotate selection 90° clockwise."""
        self._sel_transform(flip_h=False, flip_v=False, rot=1)

    def _sel_transform(self, flip_h: bool, flip_v: bool, rot: int):
        """Copy selection, transform the data, paste back in place."""
        if self.selection is None:
            self._set_status("Nothing selected"); return
        self.sel_copy()
        if self.clipboard is None:
            return
        cb = self.clipboard
        h16 = cb.h16.copy()
        veg = cb.veg.copy()
        water = cb.water.copy()
        if flip_h:
            h16 = h16[:, ::-1]; veg = veg[:, ::-1]; water = water[:, ::-1]
        if flip_v:
            h16 = h16[::-1, :]; veg = veg[::-1, :]; water = water[::-1, :]
        for _ in range(rot % 4):
            h16 = np.rot90(h16, k=-1)
            veg = np.rot90(veg, k=-1)
            water = np.rot90(water, k=-1)
        self.clipboard = Clipboard(h16.shape[0], h16.shape[1], h16, veg, water)
        self.sel_paste_commit(self.selection.r0, self.selection.c0)
        label = ("mirror H" if flip_h else "mirror V" if flip_v
                 else f"rotate {90*rot}°")
        self._set_status(f"Selection {label}")

    def _read_pixel_at(self, sx, sy):
        """Return (h16, veg, water) under screen pos, or None if not over a tile."""
        key, row, col = self.screen_to_pixel(sx, sy)
        if key is None:
            return None
        tile = self.tiles[key]
        idx  = row * tile.full_w + col
        h16  = int(tile.r[idx]) * 256 + int(tile.g[idx])
        veg  = (tile.a[idx] >> 4) & 0x7
        water = bool((tile.a[idx] >> 7) & 1)
        return h16, veg, water

    def _update_cursor_readout(self, mx, my):
        """Update live cursor value display — called every frame on mouse pos."""
        result = self._read_pixel_at(mx, my)
        if result is None:
            self.cursor_height_m = None
            self.cursor_veg      = None
            self.cursor_water    = None
        else:
            h16, veg, water = result
            self.cursor_height_m = h16 / 65535.0 * (HEIGHT_MAX_M - HEIGHT_MIN_M) + HEIGHT_MIN_M
            self.cursor_veg      = veg
            self.cursor_water    = water

    # ------------------------------------------------------------------
    # Painting
    # ------------------------------------------------------------------
    def _m_to_h16(self, metres: float) -> int:
        return int(np.clip((metres - HEIGHT_MIN_M) / (HEIGHT_MAX_M - HEIGHT_MIN_M) * 65535, 0, 65535))

    def _h16_to_m(self, h16) -> float:
        return float(h16) / 65535.0 * (HEIGHT_MAX_M - HEIGHT_MIN_M) + HEIGHT_MIN_M

    def _clamp_h16(self, h16_arr: np.ndarray) -> np.ndarray:
        """Apply floor/ceil clamps (if set) to an array of h16 values."""
        lo = self._m_to_h16(self.clamp_floor_m) if self.clamp_floor_m is not None else 0
        hi = self._m_to_h16(self.clamp_ceil_m)  if self.clamp_ceil_m  is not None else 65535
        return np.clip(h16_arr, lo, hi)

    def _begin_stroke(self, sx=None, sy=None):
        """Start recording undo info for a new stroke."""
        self._stroke_record = {}
        if sx is not None and sy is not None:
            result = self._read_pixel_at(sx, sy)
            if self.brush_mode == 'flatten':
                self.flatten_target = float(result[0]) if result else None
            elif self.brush_mode == 'paint':
                # Use explicitly set paint_target; if none set yet, sample from click point
                if self.paint_target is None and result is not None:
                    self.paint_target = float(result[0])
        else:
            self.flatten_target = None

    def _end_stroke(self):
        """Commit stroke to undo stack."""
        if not self._stroke_record:
            self._stroke_record = None
            return
        for key, pixel_dict in self._stroke_record.items():
            idxs = np.array(list(pixel_dict.keys()), dtype=np.int32)
            old_r = np.array([pixel_dict[i][0] for i in idxs], dtype=np.uint8)
            old_g = np.array([pixel_dict[i][1] for i in idxs], dtype=np.uint8)
            old_a = np.array([pixel_dict[i][2] for i in idxs], dtype=np.uint8)
            self.undo_stack.append(UndoRecord(key, idxs, old_r, old_g, old_a))
        self._stroke_record = None

    def _record_pixels(self, key, indices):
        """Save original values for undo (only first touch per stroke)."""
        if self._stroke_record is None:
            return
        tile = self.tiles.get(key)
        if tile is None:
            return
        rec = self._stroke_record.setdefault(key, {})
        for i in indices:
            if i not in rec:
                rec[i] = (tile.r[i], tile.g[i], tile.a[i])

    def _paint_at(self, sx, sy, erase=False):
        """Apply brush centred at screen pos (sx, sy)."""
        if getattr(self, '_game_terrain_sync_locked', False):
            self._set_status(
                "Game has unsaved terrain edits; save or undo them "
                "before painting on desktop")
            return
        # Erosion brush delegates to its own method
        if self.mode == 'height' and self.brush_mode == 'erode':
            erode_mode = 'hydraulic' if erase else 'thermal'
            self._apply_erosion(sx, sy, mode=erode_mode)
            return

        r_scr = self.brush_radius

        # Gather candidate tile coords by checking corners of brush bounding box
        corners = [
            (sx - r_scr, sy - r_scr),
            (sx + r_scr, sy - r_scr),
            (sx - r_scr, sy + r_scr),
            (sx + r_scr, sy + r_scr),
            (sx,         sy),           # centre
        ]
        candidate_keys = set()
        for cx2, cy2 in corners:
            tx, ty = self.screen_to_tile(cx2, cy2)
            candidate_keys.add(f'{tx},{ty}')

        for key in candidate_keys:
            tile = self.tiles.get(key)
            if tile is None:
                continue
            res = tile.full_w

            # Convert screen-pixel radius → tile-pixel radius for this tile
            screen_px_per_tile_px = (self.tile_size * self.zoom) / res
            r = max(1, int(round(r_scr / screen_px_per_tile_px)))
            r = min(r, res // 2)

            # Find where the brush centre maps to within this tile (may be outside)
            tile_px, tile_py = self.tile_screen_pos(tile.x, tile.y)
            ts = self.tile_size * self.zoom
            fx = (sx - tile_px) / ts
            fy = (sy - tile_py) / ts
            centre_col = fx * res   # float, may be outside [0, res)
            centre_row = fy * res

            # Subregion: only the clamped area that actually exists in this tile
            r0 = max(0, int(math.floor(centre_row - r)))
            r1 = min(res - 1, int(math.ceil(centre_row + r)))
            c0 = max(0, int(math.floor(centre_col - r)))
            c1 = min(res - 1, int(math.ceil(centre_col + r)))
            if r0 > r1 or c0 > c1:
                continue

            rows_idx = np.arange(r0, r1 + 1)
            cols_idx = np.arange(c0, c1 + 1)
            rr, cc   = np.meshgrid(rows_idx, cols_idx, indexing='ij')

            # Brush-relative float distance from brush centre
            dist = np.sqrt((rr - centre_row) ** 2 + (cc - centre_col) ** 2).astype(np.float32)
            t    = np.clip(dist / max(r, 1), 0.0, 1.0)
            f_vals = ((1.0 - t) ** 3 * (1.0 + 3.0 * t + 6.0 * t ** 2)).astype(np.float32)

            keep      = f_vals > 0.01
            flat_rows = rr[keep]
            flat_cols = cc[keep]
            flat_f    = f_vals[keep]
            flat_idx  = flat_rows * res + flat_cols

            if len(flat_idx) == 0:
                continue

            self._record_pixels(key, flat_idx.tolist())

            if self.mode == 'height':
                h16 = tile.r[flat_idx].astype(np.int32) * 256 + tile.g[flat_idx].astype(np.int32)

                if self.brush_mode == 'flatten' and self.flatten_target is not None:
                    target = int(self.flatten_target)
                    blend  = np.clip(flat_f * self.brush_strength * 10.0, 0.0, 1.0)
                    h16    = (h16 * (1.0 - blend) + target * blend).astype(np.int32)

                elif self.brush_mode == 'paint' and self.paint_target is not None:
                    target = int(self.paint_target)
                    blend  = np.clip(flat_f, 0.0, 1.0)
                    h16    = (h16 * (1.0 - blend) + target * blend).astype(np.int32)

                elif self.brush_mode == 'noise':
                    # fBm noise displaced by falloff and strength
                    nv = noise_brush(flat_rows, flat_cols, res, self.noise_scale)
                    sign = -1 if erase else 1
                    h16  = h16 + (sign * nv * flat_f * self.brush_strength * 65535
                                  ).astype(np.int32)

                elif self.brush_mode == 'smooth':
                    # Box-blur: average each pixel with its 8 neighbours inside the tile,
                    # then blend toward that average weighted by falloff × strength.
                    h16_2d = (tile.r.astype(np.int32) * 256 + tile.g.astype(np.int32)).reshape(res, res)
                    rows_2d = flat_rows  # shape (N,)
                    cols_2d = flat_cols
                    # Gather neighbour sum with boundary clamping
                    neighbour_sum = np.zeros(len(flat_idx), dtype=np.float32)
                    neighbour_count = np.zeros(len(flat_idx), dtype=np.float32)
                    for dr in (-1, 0, 1):
                        for dc in (-1, 0, 1):
                            nr = np.clip(rows_2d + dr, 0, res - 1)
                            nc = np.clip(cols_2d + dc, 0, res - 1)
                            neighbour_sum   += h16_2d[nr, nc]
                            neighbour_count += 1.0
                    local_avg = (neighbour_sum / neighbour_count).astype(np.int32)
                    blend = np.clip(flat_f * self.brush_strength * 8.0, 0.0, 1.0)
                    h16   = (h16 * (1.0 - blend) + local_avg * blend).astype(np.int32)

                else:  # raise / lower
                    delta = self.brush_strength * (-1 if erase else 1)
                    h16   = h16 + (delta * flat_f * 65535).astype(np.int32)

                h16 = self._clamp_h16(h16.astype(np.int32))
                tile.r[flat_idx] = (h16 >> 8).astype(np.uint8)
                tile.g[flat_idx] = (h16 & 0xFF).astype(np.uint8)

            elif self.mode == 'veg':
                preset = self.veg_preset if not erase else 0
                tile.a[flat_idx] = (tile.a[flat_idx] & 0x8F) | ((preset & 0x7) << 4)

            elif self.mode == 'water':
                if erase:
                    tile.a[flat_idx] = tile.a[flat_idx] & 0x7F
                else:
                    tile.a[flat_idx] = tile.a[flat_idx] | 0x80

            tile.dirty = True
            tile.invalidate()
            tile._recalc_stats()

    def _sample_at(self, sx, sy):
        """Eyedropper — read value under cursor and set as current brush value."""
        key, row, col = self.screen_to_pixel(sx, sy)
        if key is None:
            return
        tile = self.tiles[key]
        res  = tile.full_w
        idx  = row * res + col
        if self.mode == 'height':
            h16 = int(tile.r[idx]) * 256 + int(tile.g[idx])
            metres = self._h16_to_m(h16)
            if self.brush_mode == 'paint':
                self.paint_target = float(h16)
                self._set_status(f"Paint target set: {metres:.1f}m")
            else:
                self._set_status(f"Sampled height: {metres:.1f}m  (raw {h16})")
        elif self.mode == 'veg':
            preset = (tile.a[idx] >> 4) & 0x7
            self.veg_preset = preset
            self._set_status(f"Sampled veg preset: {preset} ({VEG_NAMES[preset]})")
        elif self.mode == 'water':
            w = (tile.a[idx] >> 7) & 1
            self._set_status(f"Sampled water: {'YES' if w else 'NO'}")

    def undo(self):
        if not self.undo_stack:
            self._set_status("Nothing to undo")
            return
        rec = self.undo_stack.pop()
        if isinstance(rec, TileDeleteRecord):
            self._restore_deleted_tiles(rec)
            return
        tile = self.tiles.get(rec.tile_key)
        if tile is None:
            return
        tile.r[rec.pixel_indices] = rec.old_r
        tile.g[rec.pixel_indices] = rec.old_g
        tile.a[rec.pixel_indices] = rec.old_a
        tile.dirty = True
        tile.invalidate()
        tile._recalc_stats()
        self._set_status(f"Undo — {len(self.undo_stack)} remaining")

    def save_all(self):
        dirty_tiles = [t for t in list(self.tiles.values()) if t.dirty]
        save_parts = []

        if dirty_tiles:
            if getattr(self, '_game_terrain_sync_locked', False):
                self._set_status(
                    "Game has unsaved terrain edits; desktop terrain save "
                    "is paused")
                return
            saved_tiles = 0
            saved_tile_paths = []
            for tile in dirty_tiles:
                if tile.save():
                    saved_tiles += 1
                    if tile.path is not None:
                        saved_tile_paths.append(str(tile.path))
            save_parts.append(f"{saved_tiles} tile(s)")
            if (
                saved_tile_paths
                and self.bridge is not None
                and self.bridge.connected
            ):
                self.bridge.reload_terrain_tiles(
                    saved_tile_paths)

        if self.mod_project and (
                self.mod_project.dirty or
                self._area_dirty_layers or
                self._pending_bridge_reload_paths):
            saved_count, reload_count = self._apply_pending_mod_changes(announce=False)
            extras = []
            if saved_count:
                extras.append(f"{saved_count} layer(s)")
            if reload_count:
                extras.append(f"{reload_count} reload(s)")
            label = "mod project"
            if extras:
                label += f" ({', '.join(extras)})"
            save_parts.append(label)

        if self.prog_project and self.prog_project.dirty:
            self.prog_project.save()
            save_parts.append("progressions")

        if not save_parts:
            self._set_status("No unsaved changes")
            return
        self._set_status("Saved " + ", ".join(save_parts))

    def export_heightmap(self):
        """Stitch all tiles into a single 16-bit greyscale PNG."""
        if not self.tiles:
            self._set_status("No tiles loaded")
            return
        try:
            out_path = ask_save_filename(self.screen,
                title="Export heightmap as 16-bit PNG",
                defaultextension=".png",
                filetypes=[("PNG files", "*.png"), ("All files", "*.*")])
            if not out_path:
                return
        except Exception:
            self._set_status("Could not open save dialog")
            return

        def worker():
            self._set_status("Exporting…")
            res    = OVERVIEW_RES
            span_x = self.max_x - self.min_x + 1
            span_y = self.max_y - self.min_y + 1
            out_w  = span_x * (res - 1) + 1
            out_h  = span_y * (res - 1) + 1
            canvas = np.zeros((out_h, out_w), dtype=np.uint16)
            for tile in list(self.tiles.values()):
                col_off = (tile.x - self.min_x) * (res - 1)
                row_off = (self.max_y - tile.y) * (res - 1)   # Y flipped
                h16 = (tile.r.astype(np.uint16) * 256 +
                       tile.g.astype(np.uint16)).reshape(res, res)
                canvas[row_off:row_off + res, col_off:col_off + res] = h16
            img = Image.fromarray(canvas, mode='I;16')
            img.save(out_path)
            self._set_status(
                f"Exported {out_w}×{out_h}px → {Path(out_path).name}")
            print(f"Exported heightmap: {out_path}")

        threading.Thread(target=worker, daemon=True).start()

    def toggle_diff(self):
        """Toggle diff overlay — tints modified tiles orange."""
        self.diff_mode = not self.diff_mode
        self._set_status(
            "Diff ON — modified tiles highlighted" if self.diff_mode else "Diff OFF")

    def _osm_bounds(self, tx: int, ty: int) -> tuple:
        """Return ((min_lat,min_lon),(max_lat,max_lon)) for editor tile (tx,ty)."""
        return _gen_tile_bounds(
            tx, ty,
            origin_lat=self.map_origin_lat,
            origin_lon=self.map_origin_lon,
            tile_dimension_m=self.map_tile_dimension_m,
            origin_e_bias=self.map_origin_e_bias,
            origin_n_bias=self.map_origin_n_bias,
        )

    def toggle_osm(self):
        """Toggle OSM map overlay."""
        self.osm.enabled = not self.osm.enabled
        if self.osm.enabled:
            self.osm.invalidate()   # clear stale surfaces on re-enable
        georef = f"{self.map_origin_lat:.5f}, {self.map_origin_lon:.5f}"
        self._set_status(
            f"OSM overlay ON  ({georef}; opacity {self.osm.opacity}  zoom z{self.osm.zoom})"
            if self.osm.enabled else "OSM overlay OFF")

    def _adjust_osm_zoom(self, delta):
        """Nudge the OSM overlay zoom within a safe editing range."""
        min_zoom = max(10, OSM_ZOOM - 3)
        max_zoom = min(19, OSM_ZOOM + 3)
        new_zoom = max(min_zoom, min(max_zoom, self.osm.zoom + delta))
        if new_zoom == self.osm.zoom:
            self._set_status(f"OSM zoom stays at z{self.osm.zoom}")
            return
        self.osm.zoom = new_zoom
        self.osm.invalidate()
        self.invalidate_all()
        self._set_status(f"OSM zoom set to z{self.osm.zoom}")

    def _adjust_osm_opacity(self, delta):
        """Nudge the OSM overlay opacity."""
        new_opacity = max(40, min(255, self.osm.opacity + delta))
        if new_opacity == self.osm.opacity:
            pct = int(round(self.osm.opacity * 100 / 255))
            self._set_status(f"OSM opacity stays at {pct}%")
            return
        self.osm.opacity = new_opacity
        pct = int(round(self.osm.opacity * 100 / 255))
        self._set_status(f"OSM opacity set to {pct}%")

    def _autosave(self):
        """Save dirty tiles to autosave directory (not the source files)."""
        dirty = [t for t in list(self.tiles.values()) if t.dirty and t.path is not None]
        if not dirty:
            return
        self._autosave_dir.mkdir(parents=True, exist_ok=True)
        saved = 0
        for tile in dirty:
            try:
                import shutil
                dst = self._autosave_dir / Path(tile.path).name
                shutil.copy2(tile.path, dst)   # copy last saved version first
                # Now write current state
                res = tile.full_w
                import numpy as np2
                r2d = tile.r.reshape(res, res)
                g2d = tile.g.reshape(res, res)
                b2d = np.zeros((res, res), dtype=np.uint8)
                a2d = tile.a.reshape(res, res)
                rgba = np.stack([r2d, g2d, b2d, a2d], axis=2)
                Image.fromarray(rgba, 'RGBA').save(dst, format="PNG")
                saved += 1
            except Exception:
                pass
        if saved:
            self._set_status(f"Autosaved {saved} tile(s) to {self._autosave_dir.name}")

    # Help overlay page index
    _help_page: int = 0

    def _draw_help_overlay(self, surf):
        """Draw full help / documentation overlay. Multi-page."""
        if not self.show_help:
            self._help_tab_rects = []
            self._help_close_rect = None
            return
        w, h = surf.get_size()
        bg = pygame.Surface((w, h), pygame.SRCALPHA)
        bg.fill((4, 6, 10, 220))
        surf.blit(bg, (0, 0))

        self._help_tab_rects = []   # reset each frame
        mx0, my0 = pygame.mouse.get_pos()
        pw = min(w - 60, 1200)
        ph = min(h - 60, 700)
        px = (w - pw) // 2
        py = (h - ph) // 2
        pygame.draw.rect(surf, (12, 17, 26), (px, py, pw, ph), border_radius=10)
        pygame.draw.rect(surf, BTN_BORDER,   (px, py, pw, ph), 1, border_radius=10)

        # Page definitions
        PAGES = [
            "Home",
            "View",
            "Terrain",
            "Track",
            "Mods",
            "Panels",
            "Splineys",
            "Geo",
            "Keys",
        ]
        self._help_page_count = len(PAGES)
        page = getattr(self, '_help_page', 0)

        # Tab bar
        tx = px + 16; ty = py + 10
        for i, pname in enumerate(PAGES):
            tw2 = self.font.get_rect(pname).width + 16
            tr2 = pygame.Rect(tx, ty, tw2, 22)
            act = i == page
            hov = tr2.collidepoint(mx0, my0)
            col_tab = ACCENT_COLOR
            pygame.draw.rect(surf,
                tuple(int(c*0.4) for c in col_tab) if act else
                ((20,30,45) if hov else (14,20,30)), tr2, border_radius=4)
            if act: pygame.draw.rect(surf, col_tab, tr2, 1, border_radius=4)
            self.font.render_to(surf, (tx+8, ty+5), pname,
                TEXT_COLOR if act else DIM_COLOR)
            self._help_tab_rects.append((tr2, i))
            tx += tw2 + 4

        # X close button
        xbtn = pygame.Rect(px+pw-28, py+8, 22, 22)
        hxb  = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180,60,60) if hxb else (80,40,40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px+pw-22, py+11), "✕", (220,200,200))
        self._help_close_rect = xbtn
        self.font.render_to(surf, (px+pw-130, ty+5), "? / Esc  ← →", DIM_COLOR)
        pygame.draw.line(surf, BTN_BORDER, (px+8, py+38), (px+pw-8, py+38), 1)

        cy = py + 46
        cx = px + 16
        lh  = 18   # line height
        lh2 = 15   # compact line height
        col_w3 = (pw - 32) // 3
        col_w2 = (pw - 32) // 2

        def heading(txt, col=(0,200,255)):
            nonlocal cy
            self.font_big.render_to(surf, (cx, cy), txt, col)
            cy += 20
            pygame.draw.line(surf, (40,60,80), (cx, cy), (cx+pw-32, cy))
            cy += 6

        def subhead(txt):
            nonlocal cy
            cy += 4
            self.font.render_to(surf, (cx, cy), txt, (140,160,180))
            cy += lh + 2

        def row(key, desc, key_col=ACCENT_COLOR, desc_col=TEXT_COLOR, indent=0):
            nonlocal cy
            self.font.render_to(surf, (cx+indent, cy), key, key_col)
            self.font.render_to(surf, (cx+indent+130, cy), desc, desc_col)
            cy += lh2

        def para(txt, col=(160,180,200), max_w=None):
            nonlocal cy
            mw = max_w or (pw - 32)
            # Word wrap
            words = txt.split()
            line = ''
            for word in words:
                test = (line + ' ' + word).strip()
                if self.font.get_rect(test).width > mw - 10:
                    self.font.render_to(surf, (cx, cy), line, col)
                    cy += lh2
                    line = word
                else:
                    line = test
            if line:
                self.font.render_to(surf, (cx, cy), line, col)
                cy += lh2

        def two_col(left_items, right_items):
            """Draw two columns of (key,desc) rows side by side."""
            nonlocal cy, cx
            save_cy = cy
            for key, desc in left_items:
                self.font.render_to(surf, (cx, cy), key, ACCENT_COLOR)
                self.font.render_to(surf, (cx+130, cy), desc, TEXT_COLOR)
                cy += lh2
            right_cy = save_cy
            for key, desc in right_items:
                self.font.render_to(surf, (cx+col_w2+8, right_cy), key, ACCENT_COLOR)
                self.font.render_to(surf, (cx+col_w2+138, right_cy), desc, TEXT_COLOR)
                right_cy += lh2
            cy = max(cy, right_cy) + 4

        def three_col(cols3):
            """Draw three columns of (key,desc) rows."""
            nonlocal cy
            heights = [cy, cy, cy]
            for ci3, items in enumerate(cols3):
                for key, desc in items:
                    self.font.render_to(surf, (cx + ci3*col_w3, heights[ci3]), key, ACCENT_COLOR)
                    self.font.render_to(surf, (cx + ci3*col_w3+110, heights[ci3]), desc, TEXT_COLOR)
                    heights[ci3] += lh2
            cy = max(heights) + 4

        # ================================================================
        if page == 0:  # Overview
        # ================================================================
            heading("Railroader Terrain & Mod Editor")
            para("A visual map editor for Railroader mod development. Edit terrain heightmaps and "
                 "vegetation, manage mod track graphs visually on real terrain, and hot-reload "
                 "changes directly into a running game via the TrackBridge mod.")
            cy += 8

            heading("What each nav button does")
            three_col([
                [("Heightmap",  "View elevation as contours"),
                 ("Vegetation", "View veg/biome layer"),
                 ("Water",      "View water coverage"),
                 ("Hillshade",  "Toggle terrain shading (S)"),
                 ("Fit",        "Fit all tiles in view (F)"),
                 ("Tracks",     "Toggle track overlay (T)"),
                 ("Nodes",      "Toggle node dots (N)"),
                 ("Load Graph", "Load a track graph JSON"),
                ("Load Tiles", "Load tile folders (drag-drop also works)"),
                ("Reload",     "Reload current graph / mod JSON data from disk"),],
                [("●LIVE/OFF",  "TrackBridge connection status"),
                 ("Generate",   "Open tile generator panel"),
                 ("Mod",        "Mod layer manager"),
                 ("Prog",       "Progression editor"),
                 ("Areas",      "Area / town editor"),
                 ("Spans",      "Track spans editor"),
                 ("Scenery",    "Place scenery / buildings"),
                 ("Group",      "Multi-node select & move"),
                 ("Calc",       "Track geometry calculators"),],
                [("Mandela",    "Prefab instance placer"),
                 ("Spliney",    "Road / river spliney editor"),
                 ("Geo",        "Guide, arc, grade, turnout tools"),
                 ("OSM",        "OpenStreetMap overlay"),
                 ("Export",     "Export heightmap PNG"),
                 ("Diff",       "Show modified tile overlay"),
                 ("?",          "This help screen (F1)"),
                 ("Edit",       "Toggle terrain paint mode (E)"),
                 ("",           "Row 1 = terrain  Row 2 = mod tools"),],
            ])
            cy += 4
            heading("Reload Button")
            row("What it does", "Reopens the current track graph and mod JSON data from disk")
            row("Gray",         "Nothing loaded that can be reloaded", desc_col=DIM_COLOR)
            row("Blue",         "Reload is available", desc_col=ACCENT_COLOR)
            row("Yellow",       "Reload is available and unsaved changes would be discarded", desc_col=WARN_COLOR)
            cy += 4
            heading("Quick start — editing track")
            para("1. Open a mod folder (Mod button → Open Mod Folder). "
                 "2. The map shows all track layers coloured by source file. "
                 "3. Click any node to select it — properties appear top-left. "
                 "4. Drag a selected node to move it. "
                 "5. Ctrl+click empty space (or Geo → Add Node) to place new nodes. "
                 "6. Select a node then click Connect → to draw a segment to another node. "
                 "7. All changes auto-save to game-graph.json and hot-reload in game.")

        # ================================================================
        elif page == 1:  # Navigation & View
        # ================================================================
            heading("Navigation & keyboard shortcuts")
            two_col(
                [("Scroll wheel",    "Zoom in / out"),
                 ("LMB drag",        "Pan the map"),
                 ("F",               "Fit all tiles in view"),
                 ("R",               "Redraw / refresh view"),
                 ("O",               "Toggle OSM overlay"),
                 ("B",               "Cycle brush (edit) / pick bridge game dir"),
                 ("Drag-drop folder","Drop a tile folder onto the window to load"),],
                [("H",               "Heightmap mode"),
                 ("V",               "Vegetation mode"),
                 ("W",               "Water mode"),
                 ("S",               "Toggle hillshade shading"),
                 ("D",               "Toggle diff (changed tiles) mode"),
                 ("T",               "Toggle track overlay"),
                 ("N",               "Toggle node dots"),]
            )
            heading("Overlays & UI")
            two_col(
                [("I",               "Toggle tile info tooltip"),
                 ("L",               "Load a track graph JSON file"),
                 ("Ctrl+R",          "Reload current graph / mod data from disk"),
                 ("Ctrl+- / Ctrl+=", "Shrink / grow the whole UI"),
                 ("Ctrl+0",          "Reset UI scale to 100%"),
                 ("? / F1",          "Open this help screen"),
                 ("Escape",          "Close panels / cancel operations / quit"),
                 ("Ctrl+Z",          "Undo (terrain edits or mod edits)"),
                 ("Ctrl+S",          "Save all modified tiles / mod project"),],
                [("A- / 100% / A+",  "Sidebar UI scale controls under Workspace"),
                 ("Scale settings",  "Saved between launches in your home folder"),
                 ("Tile tooltip",    "Hover: tile coords, height range, veg, water"),
                 ("Status bar",      "Bottom: current action / error feedback"),
                 ("↩ N  in status",  "N mod undo steps available"),
                 ("Minimap",         "Top-right: full map overview"),
                 ("Two-row nav",     "Row 1 = terrain tools  Row 2 = mod tools"),
                 ("Mod undo",        "Ctrl+Z when mod loaded — up to 50 steps"),]
            )
            heading("Tile colours in Heightmap mode")
            para("Contour lines are drawn at regular elevation intervals. "
                 "Colour shifts from dark green (low) through bright green to yellow/white (high). "
                 "The hillshade (S) adds directional lighting to reveal terrain shape. "
                 "Modified/unsaved tiles show a yellow border in Diff mode.")
            cy += 4
            heading("Display modes explained")
            row("Heightmap",  "Primary mode. Shows elevation contours with hillshade.")
            row("Vegetation", "Shows dominant vegetation/biome per pixel (0-7 presets).")
            row("Water",      "Shows water mask — white = water, black = land.")
            row("Diff",       "Highlights tiles you have modified since last save.")

        # ================================================================
        elif page == 2:  # Terrain Editing
        # ================================================================
            heading("Edit Mode  (press E to toggle)")
            para("In edit mode the cursor becomes a brush. Left-click paints, right-click erases. "
                 "The toolbar appears below the nav bar with brush controls.")
            cy += 4
            two_col(
                [("E",               "Toggle edit mode on/off"),
                 ("B  (edit mode)",  "Cycle brush mode"),
                 ("[ / ]",           "Decrease / increase brush size"),
                 ("Ctrl+Scroll",     "Brush size (mouse wheel)"),
                 ("- / =",           "Decrease / increase brush strength"),
                 ("MMB",             "Eyedropper — sample height/veg at cursor"),
                 ("LMB",             "Paint / raise terrain"),
                 ("RMB",             "Erase / lower terrain"),
                 ("Ctrl+Z",          "Undo last stroke"),
                 ("Ctrl+S",          "Save all modified tiles"),
                 ("Ctrl+X",          "Export heightmap as PNG"),],
                [("0 – 7",          "Set vegetation preset (in veg mode)"),
                 (",",               "Set height floor clamp to cursor height"),
                 (".",               "Set height ceiling clamp to cursor height"),
                 ("Ctrl+,",          "Clear height floor clamp"),
                 ("Ctrl+.",          "Clear height ceiling clamp"),
                 ("",               ""),
                 ("Brush modes",    ""),
                 ("Raise",          "Raise (LMB) or lower (RMB) terrain"),
                 ("Flatten",        "Level area to the height you first clicked"),
                 ("Paint",          "Stamp exact target height value"),
                 ("Smooth",         "Average pixels with their neighbours"),
                 ("Noise",          "Add fBm procedural noise texture"),
                 ("Erode",          "Simulate hydraulic erosion"),]
            )
            cy += 4
            heading("Selection tools  (M in edit mode)")
            two_col(
                [("M",              "Toggle selection mode"),
                 ("Rect / Lasso",   "Click+drag to select a region"),
                 ("Wand",           "Click to flood-fill select similar values"),
                 ("Ctrl+C",         "Copy selection"),
                 ("Ctrl+V",         "Paste selection (click to place)"),
                 ("Ctrl+X",         "Cut selection / export PNG"),
                 ("Delete",         "Clear selected region"),],
                [("Generate panel", ""),
                 ("Click tile cell","Queue tile for AI generation"),
                 ("Drag cells",     "Box-select a region to queue"),
                 ("RMB tile cell",  "Remove tile from queue"),
                 ("Scroll",         "Zoom the generate grid"),
                 ("MMB drag",       "Pan the generate grid"),
                 ("Run",            "Generate all queued tiles"),]
            )

        # ================================================================
        elif page == 3:  # Track Editing
        # ================================================================
            heading("Selecting & properties panel")
            two_col(
                [("Click node/segment","Select — properties panel appears top-left"),
                 ("Click segment",    "Click anywhere along track line (not just midpoint)"),
                 ("Click empty",      "Deselect"),
                 ("Click selected node","Second click starts drag mode"),
                 ("Drag release",     "Commit move — samples terrain height automatically"),
                 ("Escape",           "Cancel drag / connect / place mode"),
                 ("Delete key",       "Delete selected node or all its segments"),],
                [("Editable fields",  "Click any box, type value, Enter to save"),
                 ("Node: X Y Z",      "Direct position editing"),
                 ("Node: RotY nudge", "Fine grid: ±90/45/30/15/10/5/1/0.1/0.05/0.001°"),
                 ("Flatten",          "Zeros rotX and rotZ — levels node on terrain"),
                 ("Reverse",          "Flips heading 180°"),
                 ("Merge",            "Remove middle node between exactly 2 segments"),
                 ("Split",            "Duplicate node, rewire selected segment to copy"),]
            )
            cy += 2
            heading("Segment inline editing  (in properties panel)")
            two_col(
                [("Track Class",     "Coloured buttons: Mainline / Branch / Industrial"),
                 ("Style",           "Standard / Yard / Bridge / Tunnel — one-click change"),
                 ("Speed",           "Nudge ±25/10/5/1 mph, or click value box to type"),
                 ("Priority",        "Editable box — click to type, Enter to save"),
                 ("GroupID",         "Editable box — for track group membership"),
                 ("Reverse",         "Swap startId and endId of segment"),
                 ("Trestle",         "Wrap segment in AutoTrestleBuilder spliney"),],
                [("Copy XYZ",        "Copy selected node position to clipboard"),
                 ("Paste Y",         "Paste only height from clipboard onto node"),
                 ("All changes",     "Auto-save + hot-reload into game on every edit"),
                 ("Undo",            "Ctrl+Z — up to 50 steps for mod edits"),
                 ("Connected segs",  "Clickable list at bottom of node panel"),
                 ("Seg class colour","Button brightens when that class is active"),]
            )
            cy += 2
            heading("Creating track")
            two_col(
                [("Ctrl+click map",   "Create node at cursor (samples terrain Y)"),
                 ("Geo → Add Node",   "Placement mode — crosshair shows live coords"),
                 ("Connect → button", "Enter connect mode from selected node"),
                 ("Ctrl+click node",  "Finish connection to that node"),
                 ("Drag node → node", "Snap ring — release to connect"),
                 ("Drag node → seg",  "Cyan highlight — release to insert into segment"),
                 ("Shift+drag → seg", "Yellow — insert node AND add turnout diverge leg"),],
                [("Node rotY",        "Set automatically to face connection direction"),
                 ("Insert into seg",  "Deletes seg, creates two — node gets bezier heading"),
                 ("Turnout (shift)",  "Uses current Geo→Turnout settings for angle/length"),
                 ("Geo→Turnout tab",  "Select node → Preview → Commit for full switch"),
                 ("New nodes",        "Default Mainline, Standard, 45mph"),
                 ("Writes to",        "Always game-graph.json + hot-reload ~500ms"),]
            )
            cy += 2
            heading("Spliney control points  (rivers, roads, trestles)")
            two_col(
                [("Yellow dots",      "Zoom in and click a road/river control point to select it"),
                 ("Click again",      "Second click starts point drag mode"),
                 ("Drag release",     "Move control point; terrain can resample Y"),
                 ("Geo point tools",  "Prev / Next / Ins Before / Ins After / Sample Y"),
                 ("Spliney Panel",    "Open the full road/river editor from Geo"),],
                [("Auto Rot",         "Rebuild rotY from neighboring points"),
                 ("Delete Pt",        "Remove a point while the spline keeps at least 2"),
                 ("Fit Terrain",      "Terrain-fit the whole selected road/river"),
                 ("Reverse Flow",     "River only: reverse points and add 180 deg rotY"),
                 ("Flow arrows",      "River preview arrows show first-point to last-point flow"),]
            )

        # ================================================================
        elif page == 4:  # Mod Tools
        # ================================================================
            heading("Mod panel  (Mod button)")
            two_col(
                [("Open Mod Folder", "Load a mod folder (needs Definition.json)"),
                 ("Open Base Game",  "Load a standalone graph-data.json"),
                 ("New Mod",         "Create a new mod folder structure"),
                 ("Save All",        "Save all dirty layer files"),
                 ("✕ button",        "Close panel — mod stays loaded"),
                 ("Escape",          "Also closes the panel"),
                 ("Mousewheel",      "Scroll the layer list"),],
                [("Layer dot",       "Click to toggle layer visibility on/off"),
                 ("Layer row",       "Click to set as active layer"),
                 ("● indicator",     "Unsaved changes in this layer"),
                 ("Nodes col",       "Count + deleted count for this layer"),
                 ("Segs col",        "Segment count for this layer"),
                 ("Spl col",         "Spliney count (rivers/roads/stations)"),
                 ("Areas col",       "Area/town count in this layer"),]
            )
            cy += 2
            heading("What renders on the map with a mod loaded")
            two_col(
                [("Yellow lines",    "game-graph.json track segments"),
                 ("Grey lines",      "Base game track (game-graph layer)"),
                 ("Blue polylines",  "Rivers (FlowyThingBuilder)"),
                 ("Tan polylines",   "Roads (FlowyThingBuilder)"),
                 ("Grey/cream",      "AutoTrestle bridge spans"),
                 ("Text labels",     "Map labels (zoom in to see)"),],
                [("Orange circles",  "Turntables"),
                 ("Green diamonds",  "Stations"),
                 ("Orange diamonds", "Loaders / industry"),
                 ("Coloured squares","Scenery buildings (zoom in for model name)"),
                 ("Large circles",   "Area/town centres with name label"),
                 ("Orange circle",   "Live locomotive (from bridge)"),
                 ("Teal square",     "Live railcar (from bridge)"),]
            )

        # ================================================================
        elif page == 5:  # Mod Tools II
        # ================================================================
            heading("Progression editor  (Prog button)")
            two_col(
                [("Left column",     "Sections in topological order (prereqs first)"),
                 ("Right column",    "Map features — what areas/groups each unlocks"),
                 ("Click row",       "Select section or feature — details at bottom"),
                 ("+ Section",       "Add a new purchasable section (dialog)"),
                 ("+ Feature",       "Add a new map feature (dialog)"),],
                [("Del Section/Feature","Remove selected item"),
                 ("Save",            "Write back to progressions.json"),
                 ("Section fields",  "ID, display name, prereqs, cost, feature to enable"),
                 ("Feature fields",  "ID, display name, areas unlocked, track groups"),
                 ("Mousewheel",      "Scroll both lists"),]
            )
            cy += 2
            heading("Area / Town editor  (Areas button)")
            two_col(
                [("Left column",     "Areas sorted by order, with source-layer colour dot"),
                 ("Middle column",   "Industries in the selected town / area"),
                 ("Right column",    "Components in the selected industry"),
                 ("Click area row",  "Select a town and load its industries"),
                 ("Click ind./comp.","Select and edit deeper JSON objects"),
                 ("Go to Area",      "Pan map to this area and close panel"),
                 ("+ Area",          "Create new area at current map centre"),
                 ("+ Industry",      "Create a new industry in the selected town"),
                 ("+ Component",     "Create a component in the selected industry"),],
                [("Edit Area",       "Edit the selected area JSON object directly"),
                 ("Edit Industry",   "Edit the selected industry JSON object directly"),
                 ("Edit Comp",       "Edit the selected component JSON object directly"),
                 ("Save",            "Write all dirty town JSON files back to disk"),
                 ("Del Area",        "Mark area for deletion (not yet saved)"),
                 ("Del Industry",    "Remove selected industry from the town"),
                 ("Del Comp",        "Remove selected component from the industry"),]
            )
            cy += 2
            heading("Scenery placement  (Scenery button)")
            two_col(
                [("Model ID field",  "Type a model identifier or click a quick-pick"),
                 ("RotY nudge",      "±90/45 buttons to rotate before placing"),
                 ("Place on Map",    "Enter placement mode — click map to drop object"),
                 ("Escape",          "Exit placement mode"),
                 ("Object list",     "All placed scenery with position/rotation details"),],
                [("Go To",           "Pan map to selected scenery object"),
                 ("Del Object",      "Remove selected scenery from layer"),
                 ("Quick-picks",     "Common model IDs shown as click buttons"),
                 ("Y position",      "Auto-sampled from terrain at click point"),
                 ("Undo",            "Ctrl+Z undoes scenery placement"),]
            )
            cy += 2
            heading("Mandela / Prefab Instances  (Mandela button)")
            two_col(
                [("Target field",       "Destination scene path key for the placed entry"),
                 ("Prefab field",       "Base-game prefab path to instantiate"),
                 ("Base Pick",          "Search dumped-mandelas.txt and fill the Prefab"),
                 ("Reload Base",        "Reload the dumped base prefab catalog"),
                 ("Load Sel / Save Sel","Pull draft values from selected entry or write them back"),
                 ("Duplicate",          "Prepare a copied placement with a new target path"),],
                [("Place on Map",       "Drop the current draft at terrain height"),
                 ("Enabled",            "Cycle default / enabled / disabled states"),
                 ("Placed list",        "Select existing prefab instances from loaded layers"),
                 ("Go To",              "Pan to the selected placed entry"),
                 ("Delete",             "Remove the selected placed entry"),
                 ("Writes to",          "Owning mod JSON layer plus bridge reload"),]
            )
            cy += 2
            heading("Track Spans  (Spans)  |  Move Group  (Group)  |  Calculators  (Calc)")
            two_col(
                [("Spans: span list",   "All spans across layers with layer colour"),
                 ("Click span",         "Edit upper/lower segment, distance, and end"),
                 ("+ New Span / Del",   "Create or remove span entries"),
                 ("Group: Ctrl+drag",   "Rubber-band select nodes on the map"),
                 ("Shift+rubber-band",  "Add to existing selection"),
                 ("Apply dX/Y/Z/Rot",   "Bulk translate and rotate selected nodes"),],
                [("Measure",            "Read run, rise, grade, and heading between picks"),
                 ("Calc: Crossover",    "Enter separation + angle -> leg lengths"),
                 ("Curved Turnout",     "Radius + gauge + angle -> diverge geometry"),
                 ("Grade / Slope",      "Run + rise -> percent, ratio, angle"),
                 ("Undo",               "Ctrl+Z works on panel edits too"),
                 ("All writes to",      "Mod JSON layers plus hot-reload"),]
            )

        # ================================================================
        elif page == 6:  # Splineys & Rivers
        # ================================================================
            heading("Standalone Spliney panel  (Spliney button)")
            two_col(
                [("Type",              "Choose Road or River"),
                 ("Target",            "Pick an existing writable spliney layer"),
                 ("New JSON...",       "Create/register a new road or river JSON file"),
                 ("Width / Seed",      "Defaults for new map placement"),
                 ("Heading",           "Panel heading for the seed segment"),
                 ("Use Sel Rot",       "Use selected track node heading for placement"),],
                [("Place on Map",      "First click creates a new spliney with 2 starter points"),
                 ("Splineys list",     "All road/river FlowyThing entries in loaded layers"),
                 ("Details card",      "Shows style, width, point count, and length"),
                 ("Go To",             "Pan to the selected spliney point"),
                 ("Delete",            "Remove the selected road/river spliney"),
                 ("Writes to",         "Chosen JSON layer plus bridge reload"),]
            )
            cy += 4
            heading("Geo -> Spliney trace workflow  (Geo button, Spliney tab)")
            two_col(
                [("Trace",             "Click map to add guide points"),
                 ("Stop Trace / RMB",  "Exit guide tracing without leaving the panel"),
                 ("Use Chain",         "Copy the selected segment chain or Grade chain"),
                 ("Undo Pt / Clear",   "Trim the last point or reset the guide"),
                 ("Build Road / River","Create a spliney from the current guide path"),
                 ("Build target",      "Uses the current writable target layer"),],
                [("Warn radius",       "Flags tight bends under the current minimum radius"),
                 ("Guide path",        "Stays draft-only until you build the spliney"),
                 ("Point tools",       "Select a control dot to unlock edit shortcuts"),
                 ("Prev / Next",       "Walk the current spline without re-clicking"),
                 ("Ins Before / After","Insert points around the current selection"),
                 ("Spliney Panel",     "Jump from Geo into the full spliney editor"),]
            )
            cy += 2
            heading("Road / River editing details")
            two_col(
                [("Control dots",      "Zoom in and click yellow dots on the map to edit"),
                 ("Sample Y",          "Resample the current point from terrain"),
                 ("Auto Rot",          "Solve rotY from neighboring points"),
                 ("Delete Pt",         "Allowed while the spline keeps at least 2 points"),
                 ("Width",             "Per-point width is editable in the point inspector"),
                 ("Terrain fit",       "Good starting pass, then fine tune by hand"),],
                [("Fit Terrain",       "Resample all control-point heights to the terrain"),
                 ("Reverse Flow",      "River only: reverse node order and add 180 deg rotY"),
                 ("Flow direction",    "Rivers flow from the first point to the last point"),
                 ("Flow arrows",       "Map preview arrows show river direction"),
                 ("Roads vs rivers",   "Roads can hug terrain harder than rivers should"),
                 ("Manual cleanup",    "Rivers still usually need final Y and rot work"),]
            )
            cy += 2
            heading("When to use which tool")
            two_col(
                [("Quick placement",    "Use the Spliney panel when you know the first point"),
                 ("Trace from map",     "Use Geo -> Spliney when sketching from contours"),
                 ("Follow track yaw",   "Use Use Sel Rot when the spline should leave a node cleanly"),
                 ("New file needed",    "Use New JSON... before building into a new layer"),],
                [("Road pass",          "Terrain fit is usually a strong first pass"),
                 ("River pass",         "Terrain fit first, then smooth heights and widths manually"),
                 ("Flow mistakes",      "Use Reverse Flow if the river is running uphill"),
                 ("Existing splineys",  "Click any control dot to start editing old content"),]
            )

        # ================================================================
        elif page == 7:  # Geometry Tools
        # ================================================================
            heading("Geometry Tools panel  (Geo button) - seven tabs")
            para("All geometry tools use an orange preview on the map before writing. "
                 "Preview and Commit refuse unsafe arc and turnout geometry when the "
                 "current limits are exceeded.")
            cy += 4
            heading("Spliney  |  Arc  |  Parallel  |  Fit Arc")
            two_col(
                [("Spliney",           "Trace a guide path, then build a Road or River spliney"),
                 ("Arc",               "Generate a constant-radius curve from a selected node"),
                 ("Radius (m)",        "Arc radius; smaller values are sharper"),
                 ("Arc (degrees)",     "Total sweep angle for the preview"),
                 ("Parallel",          "Offset one or more tracks from a selected source chain"),
                 ("Fit Arc",           "Solve a constant-radius arc from the Grade chain"),],
                [("Warn radius",       "Highlights samples below the current minimum"),
                 ("Height change",     "Arc preview can also climb or fall"),
                 ("Segments",          "0 = auto, or enter a manual segment count"),
                 ("Side / N tracks",   "Parallel copies can go left, right, or both"),
                 ("Fit RMS",           "Fit Arc reports solve error against the source chain"),
                 ("Turnout chains",    "Fit Arc blocks if the source chain contains switches"),]
            )
            cy += 2
            heading("Add Node  |  Grade  |  Turnout")
            two_col(
                [("Add Node",          "Place nodes directly on the map from Geo"),
                 ("Placement Y",       "Use terrain, fixed Y, or inherit from the last node"),
                 ("Grade Set Start",   "Start an ordered chain from the selected node"),
                 ("Grade Add Node",    "Append more nodes to the chain"),
                 ("Smooth Grade",      "Interpolate Y and recalculate pitch across the chain"),
                 ("Apply Grade %",     "Force a constant grade and matching node pitch"),],
                [("Straighten XZ",     "Keep heights but straighten the chain in plan view"),
                 ("Turnout",           "Build a switch from the selected frog node"),
                 ("Leg minimum",       "Turnout safety blocks legs shorter than the safe minimum"),
                 ("Angle limits",      "Preferred diverge <= 12 deg, hard block above 15 deg"),
                 ("Diverge radius",    "Blocked if the solved branch radius is too tight"),
                 ("Warnings",          "Sharp geometry and odd speed combos are flagged"),]
            )
            cy += 2
            heading("Smooth vertical transitions")
            two_col(
                [("Start grade",       "Grade entering the selected chain"),
                 ("Hold grade",        "Constant grade between the two transition curves"),
                 ("End grade",         "Grade leaving the selected chain"),
                 ("Entry / Exit m",    "Length of each parabolic vertical curve"),],
                [("Read Current Ends", "Seed start/end grade from the current track"),
                 ("Preview",           "Open Profile with proposed curve in purple"),
                 ("Apply",             "Write Y and correctly signed rotX as one undo step"),
                 ("Mousewheel",        "Scroll long Geometry panels to reach every control"),]
            )
            cy += 2
            heading("Preview, blockers, and commit")
            two_col(
                [("Preview",           "Generates the orange ghost geometry"),
                 ("Commit",            "Writes the preview into the active graph layer"),
                 ("Clear",             "Discard the current preview"),
                 ("Blocked:",          "Unsafe previews show blocker lines in the panel"),
                 ("Radius warnings",   "Also draw warning markers on the map"),],
                [("Undo",              "Ctrl+Z reverts committed geometry edits"),
                 ("Guide traces",      "Draft-only until Build, Preview, or Clear"),
                 ("Turnout approach",  "Uses connected segments to infer the through route"),
                 ("Grade order",       "Chain order matters; build it in travel order"),
                 ("Hot reload",        "Successful commits reload the game graph"),]
            )

        # ================================================================
        elif page == 8:  # Keybinds
        # ================================================================
            heading("Keyboard Shortcuts")
            three_col([
                [("H",          "Heightmap mode"),
                 ("V",          "Vegetation mode"),
                 ("W",          "Water mode"),
                 ("S",          "Toggle hillshade"),
                 ("D",          "Toggle diff overlay"),
                 ("F",          "Fit view to tiles"),
                 ("R",          "Redraw / refresh"),
                 ("Ctrl+R",     "Reload from disk"),
                 ("Ctrl+-",     "Shrink UI scale"),
                 ("Ctrl+=",     "Grow UI scale"),
                 ("Ctrl+0",     "Reset UI scale"),
                 ("O",          "Toggle OSM overlay"),
                 ("T",          "Toggle track display"),
                 ("N",          "Toggle node dots"),
                 ("I",          "Toggle tile info tooltip"),
                 ("L",          "Load track graph JSON"),
                 ("B",          "Pick game/bridge folder"),
                 ("E",          "Toggle edit mode"),
                 ("?  /  F1",   "Open this help"),
                 ("Esc / Q",    "Quit / cancel / close"),],
                [("M",          "Toggle select mode (edit)"),
                 ("[  ]",       "Brush smaller / larger"),
                 ("-  =",       "Brush weaker / stronger"),
                 (",",          "Height clamp floor (edit)"),
                 (".",          "Height clamp ceil (edit)"),
                 ("0 – 7",      "Veg preset (edit mode)"),
                 ("B (edit)",   "Cycle brush type"),
                 ("Ctrl+C",     "Copy selection (terrain)"),
                 ("Ctrl+X",     "Export / Cut selection"),
                 ("Ctrl+V",     "Paste selection (terrain)"),
                 ("Ctrl+Z",     "Undo (terrain or mod)"),
                 ("Ctrl+S",     "Save mod project"),
                 ("Ctrl+click", "Create node at cursor"),
                 ("Delete",     "Delete selected node/seg"),
                 ("Esc",        "Cancel drag/connect/place"),],
                [("LMB",        "Select / place / click"),
                 ("RMB drag",   "Pan the map"),
                 ("Scroll",     "Zoom in / out"),
                 ("Ctrl+drag",  "Rubber-band select nodes"),
                 ("Shift+drag", "Add to group selection"),
                 ("Drag node",  "Move (2nd click = drag)"),
                 ("-> node",    "Snap: connect with segment"),
                 ("-> segment", "Cyan: insert into segment"),
                 ("Shift+->seg","Yellow: insert + turnout"),
                 ("MMB",        "Pan (middle mouse button)"),
                 ("Scroll panel","Mousewheel scrolls lists"),
                 ("<- -> Tab",  "Navigate help pages"),
                 ("Esc",        "Close any open panel"),],
            ])
            cy += 8
            heading("Page index")
            two_col(
                [("Page 1", "Overview"),
                 ("Page 2", "Navigation & View"),
                 ("Page 3", "Terrain Editing"),
                 ("Page 4", "Track Editing"),
                 ("Page 5", "Mod Tools"),],
                [("Page 6", "Mod Tools II"),
                 ("Page 7", "Splineys & Rivers"),
                 ("Page 8", "Geometry Tools"),
                 ("Page 9", "Keybinds  <- you are here"),]
            )

    # ------------------------------------------------------------------
    # Tile generation
    # ------------------------------------------------------------------

    def _gen_start(self):
        if self.gen_active:
            return
        self.gen_token = clean_mapbox_token(self.gen_token)
        if not self.gen_token or not self.gen_out_dir:
            self._set_status("Set Mapbox token and output folder first")
            return
        if not self.gen_queue:
            self._set_status("No tiles queued — click empty cells in the generate panel")
            return

        Path(self.gen_out_dir).mkdir(parents=True, exist_ok=True)
        queue = list(self.gen_queue)
        self.gen_active = True
        self.gen_failed.clear()
        run_saved_paths = []
        run_failed_tiles = []
        run_reload_misses = []
        auth_state = {'message': None}
        stop_event = threading.Event()

        def worker(gx, gy):
            if stop_event.is_set():
                return
            with self._gen_lock:
                self.gen_running[(gx, gy)] = "starting..."

            def prog(msg):
                with self._gen_lock:
                    self.gen_running[(gx, gy)] = msg

            try:
                path = Path(generate_tile(
                    gx, gy, self.gen_token, self.gen_out_dir,
                    use_nlcd=self.gen_use_nlcd,
                    nlcd_blur=self.gen_nlcd_blur,
                    veg_override=self.gen_veg_override,
                    progress_cb=prog,
                    origin_lat=self.map_origin_lat,
                    origin_lon=self.map_origin_lon,
                    tile_dimension_m=self.map_tile_dimension_m,
                    origin_e_bias=self.map_origin_e_bias,
                    origin_n_bias=self.map_origin_n_bias,
                ))
                tile = load_tile(path)
                with self._gen_lock:
                    if tile:
                        self.tiles[f'{tile.x},{tile.y}'] = tile
                    else:
                        run_reload_misses.append((gx, gy))
                    self.gen_done.add((gx, gy))
                    self.gen_running.pop((gx, gy), None)
                    self.gen_queue.discard((gx, gy))
                    run_saved_paths.append(path)
                print(f"Generated tile ({gx},{gy}) -> {path}")
            except MapboxAuthError as e:
                stop_event.set()
                auth_state['message'] = str(e)
                with self._gen_lock:
                    self.gen_failed.add((gx, gy))
                    self.gen_running[(gx, gy)] = f"FAILED: {e}"
                    run_failed_tiles.append((gx, gy))
                print(f"Tile ({gx},{gy}) failed: {e}")
            except Exception as e:
                with self._gen_lock:
                    self.gen_failed.add((gx, gy))
                    self.gen_running[(gx, gy)] = f"FAILED: {e}"
                    run_failed_tiles.append((gx, gy))
                print(f"Tile ({gx},{gy}) failed: {e}")

        def runner():
            with ThreadPoolExecutor(max_workers=self.gen_workers) as pool:
                futs = {pool.submit(worker, gx, gy): (gx, gy) for gx, gy in queue}
                for _future in as_completed(futs):
                    pass

            self.gen_active = False
            self._update_bounds()
            map_json = self._sync_map_manifest(self.gen_out_dir, create=True)

            saved_count = len(run_saved_paths)
            failed_count = len(run_failed_tiles)
            out_dir = str(Path(self.gen_out_dir))
            if auth_state['message']:
                self._set_status(auth_state['message'])
            elif failed_count:
                self._set_status(
                    f"Generation complete — saved {saved_count} tile(s) to {out_dir}, {failed_count} failed"
                )
            elif run_reload_misses:
                self._set_status(
                    f"Generation complete — saved {saved_count} tile(s) to {out_dir}; reload {len(run_reload_misses)} if needed"
                )
            else:
                suffix = "; Map.json updated" if map_json else ""
                self._set_status(
                    f"Generation complete — saved {saved_count} tile(s) to {out_dir}{suffix}"
                )

        threading.Thread(target=runner, daemon=True).start()

    def _set_status(self, msg, duration=120):
        self.status_msg   = msg
        self.status_timer = duration

    # ------------------------------------------------------------------
    # UI helpers
    # ------------------------------------------------------------------
    def _draw_button(self, surf, rect, label, active, hover=False, color=None):
        """Draw a pill-style toolbar button. Returns rect."""
        x, y, bw, bh = rect
        bg = BTN_ACTIVE if active else (BTN_HOVER_C if hover else BTN_INACTIVE)
        if color and active:
            bg = tuple(min(255, 18 + int(c * 0.48)) for c in color)
        pygame.draw.rect(surf, bg,          (x, y, bw, bh), border_radius=5)
        border = color if (active and color) else BTN_BORDER
        pygame.draw.rect(surf, border,      (x, y, bw, bh), 1, border_radius=5)
        if active and color:
            pygame.draw.rect(surf, color, (x, y+bh-3, bw, 3), border_radius=2)
        tc = color if (active and color) else (TEXT_COLOR if (active or hover) else TEXT_SOFT)
        tr, _ = self.font_big.render(label, tc)
        surf.blit(tr, (x + (bw - tr.get_width()) // 2, y + (bh - tr.get_height()) // 2))
        return rect

    def _draw_separator(self, surf, x, y, h):
        pygame.draw.line(surf, BTN_BORDER, (x, y + 6), (x, y + h - 6), 1)

    def _draw_minimap(self, surf, x, y, mw, mh):
        """Tiny overview map in corner showing pan position."""
        if not self.tiles:
            return
        pygame.draw.rect(surf, (10, 14, 20), (x, y, mw, mh))
        pygame.draw.rect(surf, BTN_BORDER,   (x, y, mw, mh), 1)
        span_x = max(1, self.max_x - self.min_x + 1)
        span_y = max(1, self.max_y - self.min_y + 1)
        cell_w = mw / span_x
        cell_h = mh / span_y
        for tile in list(self.tiles.values()):
            tx = tile.x - self.min_x
            ty = self.max_y - tile.y   # flip Y
            px = int(x + tx * cell_w)
            py = int(y + ty * cell_h)
            cw = max(1, int(cell_w) - 1)
            ch = max(1, int(cell_h) - 1)
            col = WARN_COLOR if tile.dirty else (30, 50, 70)
            pygame.draw.rect(surf, col, (px, py, cw, ch))

        # Viewport rect on minimap
        sw, sh = surf.get_size()
        content_top = PANEL_H + (TOOLBAR_H if self.edit_mode else 0)
        ts = self.tile_size * self.zoom
        # left/top of visible area in tile coords
        vl = (0 - self.pan_x) / ts + self.min_x
        vt = self.max_y - (content_top - self.pan_y) / ts
        vr = (sw - self.pan_x) / ts + self.min_x
        vb = self.max_y - (sh - self.pan_y) / ts
        rx = int(x + (vl - self.min_x) / span_x * mw)
        ry = int(y + (self.max_y - vt) / span_y * mh)  # flip back
        rw = max(2, int((vr - vl) / span_x * mw))
        rh = max(2, int((vt - vb) / span_y * mh))
        rx = max(x, min(rx, x + mw - 2))
        ry = max(y, min(ry, y + mh - 2))
        pygame.draw.rect(surf, ACCENT_COLOR, (rx, ry, rw, rh), 1)

    def _draw_properties_panel(self, surf, content_top):
        """Draw the unified properties panel showing selected node or segment.
        Stores clickable rects in self._prop_seg_rects for click handling."""
        self._prop_seg_rects = []  # [(rect, seg_id), ...]
        self._prop_action_rects = []  # [(rect, action_name), ...]
        if self.geo_panel:
            return

        node_id = self.sel_mod_node_id
        seg_id  = self.sel_mod_seg_id
        if not node_id and not seg_id:
            return

        mx0, my0 = pygame.mouse.get_pos()
        pw2 = 360
        px2 = surf.get_width() - pw2 - 10
        py2 = content_top + 10
        cx2 = px2 + 10
        row_h = 17
        is_mod = self.mod_project is not None

        # --- Collect data ---
        if node_id:
            li = self.sel_mod_layer_idx
            if li is not None and is_mod:
                layer = self.mod_project.layers[li]
                node  = layer.nodes.get(node_id) or                         self.mod_project.merged_nodes.get(node_id)
            else:
                node_state = self._get_track_node_state(node_id)
                node = {'id': node_state['id'], 'x': node_state['x'], 'y': node_state['y'], 'z': node_state['z'],
                        'rotX': 0, 'rotY': node_state.get('rotY', 0.0), 'rotZ': 0,
                        'flipSwitchStand': False, 'source': node_state.get('source', 'loaded')} if node_state else None
                layer = None
            if not node:
                return
            if is_mod:
                conn_segs = self.mod_project.segments_for_node(node_id)
            else:
                conn_segs = self._segments_for_track_node(node_id)

            layer_name = layer.label if layer else ('Live Bridge' if node.get('source') == 'bridge' else 'Loaded Graph')
            layer_color = layer.color if layer else ((80,220,80) if node.get('source') == 'bridge' else (120,190,255))
            fields = [
                ("ID",      node['id'],                              (220,230,240)),
                ("Layer",   layer_name,                             layer_color),
                ("X",       f"{node['x']:.2f}",                      (180,220,180)),
                ("Y",       f"{node['y']:.2f}",                      (180,220,180)),
                ("Z",       f"{node['z']:.2f}",                      (180,220,180)),
                ("RotY",    f"{node.get('rotY',0):.2f}",             (200,180,220)),
                ("Flip",    str(node.get('flipSwitchStand',False)),  (220,200,160)),
            ]
            n_rows = len(fields) + 1 + len(conn_segs) + (11 if is_mod else 0)  # 6 nudge rows + 2 button rows + gaps

        else:  # segment selected
            li = self.sel_mod_layer_idx
            if li is not None and is_mod:
                layer = self.mod_project.layers[li]
                seg   = layer.segments.get(seg_id) or                         self.mod_project.merged_segments.get(seg_id, {})
            else:
                seg = self._get_track_segment_state(seg_id) or {}
                layer = None
            conn_segs = None
            layer_name = layer.label if layer else ('Live Bridge' if seg.get('source') == 'bridge' else 'Loaded Graph')
            layer_color = layer.color if layer else ((80,220,80) if seg.get('source') == 'bridge' else (120,190,255))
            fields = [
                ("ID",         seg.get('id',''),         (220,230,240)),
                ("Layer",      layer_name,               layer_color),
                ("Start",      seg.get('startId',''),    (180,220,180)),
                ("End",        seg.get('endId',''),      (180,220,180)),
                ("Class",      seg.get('trackClass',''), (220,200,140)),
                ("Style",      seg.get('style',''),      (200,180,220)),
                ("Gauge",      normalize_track_gauge(
                    seg.get('gauge', 'Standard')
                ), (255,170,100)),
                ("Speed",      str(seg.get('speedLimit','')), (160,200,220)),
                ("Priority",   str(seg.get('priority','')),   (160,200,220)),
                ("GroupID",    seg.get('groupId',''),    (160,180,200)),
            ]
            n_rows = len(fields) + (8 if is_mod else 0)

        ph2 = 10 + n_rows * row_h + 10
        # Clamp panel so it never runs off the bottom of the window
        win_h = surf.get_height()
        py2 = min(py2, win_h - ph2 - 10)
        py2 = max(content_top + 4, py2)
        cy2_base = py2 + 8  # recomputed after clamp
        # Draw background
        panel_surf = pygame.Surface((pw2, ph2), pygame.SRCALPHA)
        panel_surf.fill((8, 11, 18, 220))
        surf.blit(panel_surf, (px2, py2))
        pygame.draw.rect(surf, (40,60,80), (px2, py2, pw2, ph2), 1, border_radius=4)

        cy2 = cy2_base

        # Editable fields
        # _prop_edit_key: which field is being typed into
        # _prop_edit_buf: current text buffer
        edit_key = getattr(self, '_prop_edit_key', None)
        edit_buf = getattr(self, '_prop_edit_buf', '')

        EDITABLE_NODE = {'X','Y','Z','RotY','RotX','RotZ','ID'}
        EDITABLE_SEG  = {'Speed','Priority','GroupID','ID'}
        BOOLEAN_FIELDS = {'Flip'}
        READONLY = {'Layer','Class','Style','Gauge','Start','End'}

        field_w = pw2 - 100
        for label, value, col in fields:
            self.font.render_to(surf, (cx2, cy2), label+":", (100,120,140))
            fx = cx2 + 80
            fw = field_w

            # Boolean toggle (True/False)
            if label in BOOLEAN_FIELDS:
                is_true = str(value).lower() == 'true'
                for opt, opt_col in [('True',(80,200,100)),('False',(200,80,80))]:
                    bw_b = self.font.get_rect(opt).width + 10
                    r_b  = pygame.Rect(fx, cy2-1, bw_b, row_h)
                    sel  = (opt == 'True') == is_true
                    hov  = r_b.collidepoint(mx0, my0) and is_mod
                    pygame.draw.rect(surf,
                        opt_col if sel else ((30,45,35) if opt=='True' else (45,25,25)),
                        r_b, border_radius=2)
                    if sel or hov:
                        pygame.draw.rect(surf, opt_col, r_b, 1, border_radius=2)
                    self.font.render_to(surf, (fx+5, cy2), opt,
                        (220,240,220) if sel else (100,120,100))
                    act_b = f"flip_{opt.lower()}"
                    self._prop_action_rects.append((r_b, act_b))
                    fx += bw_b + 4

            # Readonly label (Layer, Start, End, Class, Style)
            elif label in READONLY or not is_mod:
                self.font.render_to(surf, (fx, cy2), str(value), col)

            # Editable text box
            else:
                active = (edit_key == f"prop_{label}")
                disp   = (edit_buf + "_") if active else str(value)
                fr     = pygame.Rect(fx, cy2-2, fw, row_h)
                pygame.draw.rect(surf,
                    (30,50,70) if active else (18,26,38), fr, border_radius=2)
                pygame.draw.rect(surf,
                    (0,200,255) if active else (40,60,80), fr, 1, border_radius=2)
                self.font.render_to(surf, (fx+4, cy2), disp, col)
                self._prop_action_rects.append((fr, f"prop_edit_{label}"))

            cy2 += row_h

        # Connected segments list (node only)
        if node_id and conn_segs is not None:
            cy2 += 2
            pygame.draw.line(surf, (40,60,80), (cx2, cy2), (px2+pw2-10, cy2))
            cy2 += 3
            self.font.render_to(surf, (cx2, cy2),
                f"Connected segments ({len(conn_segs)}):", (100,120,140))
            cy2 += row_h
            for s in conn_segs:
                sid2   = s.get('id','')
                other  = s.get('endId','') if s.get('startId','') == node_id                          else s.get('startId','')
                tc2    = s.get('trackClass','')
                spd2   = s.get('speedLimit','')
                lbl2   = f"  {sid2}  →{other}  {tc2}  {spd2}mph"
                r2     = pygame.Rect(cx2, cy2-1, pw2-20, row_h)
                hov2   = r2.collidepoint(mx0, my0)
                is_sel = sid2 == self.sel_mod_seg_id
                if is_sel:
                    pygame.draw.rect(surf, (30,50,80), r2, border_radius=2)
                elif hov2:
                    pygame.draw.rect(surf, (20,30,50), r2, border_radius=2)
                col2 = (0,200,255) if is_sel else ((180,220,255) if hov2 else (160,180,200))
                self.font.render_to(surf, (cx2+4, cy2), lbl2, col2)
                self._prop_seg_rects.append((r2, sid2))
                cy2 += row_h

        # Action buttons (mod only)
        if is_mod:
            cy2 += 4
            if node_id:
                # Rotation nudge — two rows mirroring the existing tool's button grid
                # Left col = negative (CCW), right col = positive (CW)
                nudges = [(90,0.001),(45,0.01),(30,0.05),(15,0.1),(10,1),(5,5)]
                col_lx = cx2
                col_rx = cx2 + pw2 // 2 - 10
                self.font.render_to(surf, (col_lx, cy2), "RotY  ←CCW", (100,120,140))
                self.font.render_to(surf, (col_rx, cy2), "CW→",        (100,120,140))
                cy2 += 14
                for big, small in nudges:
                    for sign, bx3, val in [(-1, col_lx, big),  (-1, col_lx+36, small),
                                            (1,  col_rx, big),  ( 1, col_rx+36, small)]:
                        lbl3  = f"{'-' if sign<0 else '+'}{val}"
                        key3  = f"rotY_{'m' if sign<0 else 'p'}{str(val).replace('.','d')}"
                        bw3   = self.font.get_rect(lbl3).width + 6
                        r3    = pygame.Rect(bx3, cy2-1, bw3, row_h-1)
                        hov3  = r3.collidepoint(mx0,my0)
                        pygame.draw.rect(surf,(30,60,100) if hov3 else (20,35,55),r3,border_radius=2)
                        pygame.draw.rect(surf,(60,120,200),r3,1,border_radius=2)
                        self.font.render_to(surf,(bx3+3,cy2),lbl3,(180,210,255))
                        self._prop_action_rects.append((r3, key3))
                    cy2 += row_h
                cy2 += 4
                # Row 2: node operations
                bx3 = cx2
                actions = [("Flatten",  "node_flatten",  (60,120,180)),
                           ("Reverse",  "node_reverse",  (120,80,180)),
                           ("Merge",    "node_merge",    (180,120,0)),
                           ("Split",    "node_split",    (0,160,140)),
                           ("Copy XYZ", "node_copy",     (80,120,160)),
                           ("Paste Y",  "node_paste_y",  (60,100,140)),
                           ("Del Node", "del_node",      (180,60,60)),
                           ("Connect →","connect_node",  (0,140,180))]
            else:
                # ---- Segment inline editing ----

                # Track Class — full names, coloured by type
                self.font.render_to(surf, (cx2, cy2), "Gauge:", (100,120,140))
                bx3 = cx2 + 42
                cur_gauge = normalize_track_gauge(
                    seg.get('gauge', 'Standard')
                )
                gauge_choices = [
                    ('Std', 'Standard'),
                    ('3ft', 'Narrow'),
                    ('Dual', 'DualGauge'),
                    ('L', 'DualGauge_L'),
                    ('R', 'DualGauge_R'),
                    ('Trans', 'DualGauge_T'),
                ]
                for gauge_label, gauge_value in gauge_choices:
                    bw3 = self.font.get_rect(gauge_label).width + 8
                    r3 = pygame.Rect(bx3, cy2 - 1, bw3, row_h)
                    active3 = cur_gauge == gauge_value
                    hover3 = r3.collidepoint(mx0, my0)
                    color3 = (
                        (245, 64, 210)
                        if gauge_value == 'DualGauge_T'
                        else (110, 184, 255)
                        if gauge_value.startswith('DualGauge')
                        else (255, 122, 20)
                        if gauge_value == 'Narrow'
                        else (180, 160, 70)
                    )
                    pygame.draw.rect(
                        surf,
                        color3 if active3 else (
                            tuple(v // 2 for v in color3)
                            if hover3 else (20, 28, 36)
                        ),
                        r3,
                        border_radius=2,
                    )
                    pygame.draw.rect(surf, color3, r3, 1, border_radius=2)
                    self.font.render_to(
                        surf,
                        (bx3 + 4, cy2),
                        gauge_label,
                        (245, 245, 245) if active3 else (150, 170, 185),
                    )
                    self._prop_action_rects.append(
                        (r3, f"seg_gauge_{gauge_value}")
                    )
                    bx3 += bw3 + 3
                cy2 += row_h + 2

                self.font.render_to(surf, (cx2, cy2), "Class:", (100,120,140))
                bx3 = cx2 + 42
                cur_tc = seg.get('trackClass','')
                TC_COLS = {'Mainline':   (0,140,220),
                           'Branch':     (0,180,140),
                           'Industrial': (180,100,220)}
                for tc in ['Mainline','Branch','Industrial']:
                    bw3  = self.font.get_rect(tc[:5]).width + 6
                    r3   = pygame.Rect(bx3, cy2-1, bw3, row_h)
                    act3 = cur_tc == tc
                    hov3 = r3.collidepoint(mx0, my0)
                    col3 = TC_COLS.get(tc, (0,140,200))
                    pygame.draw.rect(surf,
                        col3 if act3 else (tuple(v//3 for v in col3) if not hov3
                                           else tuple(v//2 for v in col3)),
                        r3, border_radius=2)
                    if act3: pygame.draw.rect(surf, col3, r3, 1, border_radius=2)
                    self.font.render_to(surf, (bx3+3, cy2), tc[:5],
                        (240,250,240) if act3 else (140,160,180))
                    self._prop_action_rects.append((r3, f"seg_class_{tc}"))
                    bx3 += bw3 + 3
                cy2 += row_h + 2

                # Style
                self.font.render_to(surf, (cx2, cy2), "Style:", (100,120,140))
                bx3 = cx2 + 42
                cur_st = seg.get('style','Standard')
                ST_COLS = {'Standard':(0,120,180),'Yard':(140,140,60),
                           'Bridge':(160,100,60),'Tunnel':(100,80,160)}
                for st in ['Standard','Yard','Bridge','Tunnel']:
                    bw3  = self.font.get_rect(st).width + 8
                    r3   = pygame.Rect(bx3, cy2-1, bw3, row_h)
                    act3 = cur_st == st
                    hov3 = r3.collidepoint(mx0, my0)
                    col3 = ST_COLS.get(st, (0,120,180))
                    pygame.draw.rect(surf,
                        col3 if act3 else (tuple(v//3 for v in col3) if not hov3
                                           else tuple(v//2 for v in col3)),
                        r3, border_radius=2)
                    if act3: pygame.draw.rect(surf, col3, r3, 1, border_radius=2)
                    self.font.render_to(surf, (bx3+4, cy2), st,
                        (240,250,240) if act3 else (140,160,180))
                    self._prop_action_rects.append((r3, f"seg_style_{st}"))
                    bx3 += bw3 + 3
                cy2 += row_h + 2

                # Speed limit — finer nudge steps + current value
                self.font.render_to(surf, (cx2, cy2), "Speed:", (100,120,140))
                bx3 = cx2 + 42
                cur_spd = int(seg.get('speedLimit', 45))
                for lbl3, act3 in [("-25","spd_m25"),("-10","spd_m10"),("-5","spd_m5"),
                                    ("-1","spd_m1"),("+1","spd_p1"),("+5","spd_p5"),
                                    ("+10","spd_p10"),("+25","spd_p25")]:
                    bw3  = self.font.get_rect(lbl3).width + 6
                    r3   = pygame.Rect(bx3, cy2-1, bw3, row_h)
                    hov3 = r3.collidepoint(mx0, my0)
                    pygame.draw.rect(surf,(30,60,60) if hov3 else (18,36,36), r3, border_radius=2)
                    pygame.draw.rect(surf,(60,160,120), r3, 1, border_radius=2)
                    self.font.render_to(surf, (bx3+3, cy2), lbl3, (160,220,180))
                    self._prop_action_rects.append((r3, act3))
                    bx3 += bw3 + 3
                # Current speed display
                spd_r = pygame.Rect(bx3+2, cy2-2, 52, row_h)
                active_spd = self._prop_edit_key == 'prop_Speed'
                pygame.draw.rect(surf, (30,50,35) if active_spd else (18,28,20), spd_r, border_radius=2)
                pygame.draw.rect(surf, (0,200,255) if active_spd else (40,80,60), spd_r, 1, border_radius=2)
                spd_disp = (self._prop_edit_buf+"_") if active_spd else f"{cur_spd} mph"
                self.font.render_to(surf, (bx3+5, cy2), spd_disp, (220,200,140))
                self._prop_action_rects.append((spd_r, "prop_edit_Speed"))
                cy2 += row_h + 2

                # Priority + GroupID on one row
                self.font.render_to(surf, (cx2, cy2), "Pri:", (100,120,140))
                pri_r = pygame.Rect(cx2+28, cy2-2, 36, row_h)
                active_pri = self._prop_edit_key == 'prop_Priority'
                pygame.draw.rect(surf, (30,40,50) if active_pri else (18,24,32), pri_r, border_radius=2)
                pygame.draw.rect(surf, (0,200,255) if active_pri else (40,60,80), pri_r, 1, border_radius=2)
                pri_disp = (self._prop_edit_buf+"_") if active_pri else str(seg.get('priority','0'))
                self.font.render_to(surf, (cx2+31, cy2), pri_disp, (160,200,220))
                self._prop_action_rects.append((pri_r, "prop_edit_Priority"))

                self.font.render_to(surf, (cx2+72, cy2), "Group:", (100,120,140))
                grp_r = pygame.Rect(cx2+112, cy2-2, pw2-125, row_h)
                active_grp = self._prop_edit_key == 'prop_GroupID'
                pygame.draw.rect(surf, (30,40,50) if active_grp else (18,24,32), grp_r, border_radius=2)
                pygame.draw.rect(surf, (0,200,255) if active_grp else (40,60,80), grp_r, 1, border_radius=2)
                grp_disp = (self._prop_edit_buf+"_") if active_grp else seg.get('groupId','')
                self.font.render_to(surf, (cx2+115, cy2), grp_disp, (160,180,200))
                self._prop_action_rects.append((grp_r, "prop_edit_GroupID"))
                cy2 += row_h + 2

                bx3 = cx2
                actions = [("Del Seg","del_seg",(180,60,60)),
                           ("Reverse","seg_reverse",(120,80,180)),
                           ("Trestle","seg_trestle",(140,100,60))]

            panel_right = px2 + pw2 - 10
            bx3 = cx2
            for lbl3, act3, col3 in actions:
                bw3 = self.font.get_rect(lbl3).width + 12
                if bx3 + bw3 > panel_right:
                    bx3 = cx2
                    cy2 += row_h + 4
                r3  = pygame.Rect(bx3, cy2, bw3, row_h+2)
                hov3 = r3.collidepoint(mx0, my0)
                pygame.draw.rect(surf, col3 if hov3 else
                                 (col3[0]//2,col3[1]//2,col3[2]//2), r3, border_radius=3)
                pygame.draw.rect(surf, col3, r3, 1, border_radius=3)
                self.font.render_to(surf, (bx3+6, cy2+2), lbl3, (220,230,240))
                self._prop_action_rects.append((r3, act3))
                bx3 += bw3 + 6

    def _draw_node_properties(self, node, layer):
        """Legacy stub — real drawing now in _draw_properties_panel."""
        pass

    def _draw_segment_properties(self, seg, layer):
        """Legacy stub — real drawing now in _draw_properties_panel."""
        pass


    def _handle_progression_click(self, mx, my, content_top):
        """Handle clicks in the progression editor panel."""
        w, h = self.screen.get_size()
        pw   = min(w - 40, 1100)
        px   = (w - pw) // 2
        py   = content_top + 10
        ph   = h - content_top - STATUS_H - 20
        if not pygame.Rect(px, py, pw, ph).collidepoint(mx, my):
            return False

        # X close
        if pygame.Rect(px+pw-30, py+8, 22, 22).collidepoint(mx, my):
            self.prog_panel = False; return True

        pp = self.prog_project
        if not pp: return True

        # Init rects if not drawn yet
        if not hasattr(self, '_prog_sec_rects'):  self._prog_sec_rects  = []
        if not hasattr(self, '_prog_feat_rects'): self._prog_feat_rects = []
        if not hasattr(self, '_prog_action_rects'):self._prog_action_rects=[]

        for r, sid in self._prog_sec_rects:
            if r.collidepoint(mx, my):
                self.prog_sel_section = sid
                self.prog_sel_feature = None
                self._set_status(f"Section: {sid}  "
                                 f"{pp.sections[sid].display_name}")
                return True

        for r, fid in self._prog_feat_rects:
            if r.collidepoint(mx, my):
                self.prog_sel_feature = fid
                self.prog_sel_section = None
                self._set_status(f"Feature: {fid}  "
                                 f"{pp.features[fid].display_name}")
                return True

        for r, act in self._prog_action_rects:
            if r.collidepoint(mx, my):
                self._do_progression_action(act)
                return True

        return True

    def _do_progression_action(self, action):
        """Execute a progression editor action."""
        pp = self.prog_project
        if not pp: return
        if action == 'prog_save':
            pp.save()
            self._set_status("Progressions saved")
        elif action == 'prog_add_section':
            try:
                sid  = ask_string(self.screen, "Section ID", "Enter section ID:") or ""
                name = ask_string(self.screen, "Display Name", "Display name:") or sid
                cost = ask_integer(self.screen, "Cost", "Delivery cost ($):",
                                               initialvalue=1000) or 1000
                prereq  = ask_string(self.screen, "Prerequisite",
                    "Prerequisite section ID (blank=none):") or ""
                feat_id = ask_string(self.screen, "Feature ID",
                    "Feature to enable (blank=none):") or ""
                if sid:
                    pp.add_section(sid.strip(), name.strip(),
                                   [prereq.strip()] if prereq.strip() else [],
                                   cost, feat_id.strip())
                    self._set_status(f"Added section: {sid}")
            except Exception as ex:
                self._set_status(f"Failed: {ex}")
        elif action == 'prog_add_feature':
            try:
                fid  = ask_string(self.screen, "Feature ID", "Enter feature ID:") or ""
                name = ask_string(self.screen, "Display Name", "Display name:") or fid
                if fid:
                    pp.add_feature(fid.strip(), name.strip())
                    self._set_status(f"Added feature: {fid}")
            except Exception as ex:
                self._set_status(f"Failed: {ex}")
        elif action == 'prog_del_section' and self.prog_sel_section:
            pp.delete_section(self.prog_sel_section)
            self._set_status(f"Deleted section: {self.prog_sel_section}")
            self.prog_sel_section = None
        elif action == 'prog_del_feature' and self.prog_sel_feature:
            pp.delete_feature(self.prog_sel_feature)
            self._set_status(f"Deleted feature: {self.prog_sel_feature}")
            self.prog_sel_feature = None

    def _selected_area_obj(self):
        if not self.prog_project or not self.area_sel_id:
            return None
        return self.prog_project.areas.get(self.area_sel_id)

    def _selected_industry_obj(self):
        area = self._selected_area_obj()
        if not area or self.area_sel_industry is None:
            return None
        return area.industries.get(self.area_sel_industry)

    def _selected_component_entry(self):
        ind = self._selected_industry_obj()
        if not ind or self.area_sel_component is None:
            return None
        comp = ind.components.get(self.area_sel_component)
        return comp if isinstance(comp, dict) else None

    def _town_layer_label(self, filename: str) -> str:
        stem = Path(filename).stem
        if stem.lower().startswith("town_"):
            stem = stem[5:]
        return stem.replace("_", " ").strip().title() or filename

    def _next_town_layer_color(self):
        town_count = sum(1 for layer in self.mod_project.layers
                         if layer.layer_type == LAYER_TOWN)
        return TOWN_PALETTE[town_count % len(TOWN_PALETTE)]

    def _ensure_town_layer(self, filename: str):
        if not self.mod_project or not self.mod_project.folder:
            raise RuntimeError("Load a mod folder first")
        active_source = self.mod_project.active_source
        definition = (
            active_source.get("definition", {})
            if active_source else {}
        )
        if isinstance(definition, dict) and definition.get("FuseDataFiles"):
            source_idx = (
                self.mod_project.sources.index(active_source)
                if active_source in self.mod_project.sources else None
            )
            for index, layer in enumerate(self.mod_project.layers):
                if (layer.is_fuse_native
                        and not layer.read_only
                        and (source_idx is None
                             or getattr(layer, "source_idx", None) == source_idx)):
                    return index, layer
            raise RuntimeError(
                "The native FUSE package has no editable FuseDataFiles layer"
            )
        name = (filename or "").strip()
        if not name:
            raise ValueError("Town file name is required")
        if not name.lower().endswith(".json"):
            name += ".json"
        for idx, layer in enumerate(self.mod_project.layers):
            if layer.path.name.lower() == name.lower():
                return idx, layer
        path = Path(self.mod_project.folder) / name
        path.parent.mkdir(parents=True, exist_ok=True)
        if not path.exists():
            _save_json(path, {
                "areas": {},
                "tracks": {"nodes": {}, "segments": {}, "spans": {}},
            })
        layer = Layer(path, LAYER_TOWN, self._next_town_layer_color(),
                      self._town_layer_label(name), visible=True)
        layer.load()
        self.mod_project.layers.append(layer)

        self.mod_project._rebuild_merge()
        self._mark_measure_cache_dirty()
        return len(self.mod_project.layers) - 1, layer

    def _mark_area_layer_dirty(self, layer_idx: int):
        if self.mod_project and 0 <= layer_idx < len(self.mod_project.layers):
            self.mod_project.layers[layer_idx].dirty = True
            self._area_dirty_layers.add(layer_idx)

    def _sync_area_to_layer(self, area_id: str):
        pp = self.prog_project
        if not pp or area_id not in pp.areas:
            return None
        layer_idx = pp.area_layer.get(area_id)
        if layer_idx is None or not self.mod_project:
            return None
        layer = self.mod_project.layers[layer_idx]
        payload = pp.areas[area_id].to_dict()
        if layer.is_fuse_native:
            area_payload = copy.deepcopy(payload)
            industries_payload = area_payload.pop("industries", {}) or {}
            area_payload = {
                key: value for key, value in area_payload.items()
                if key in {
                    "name", "position", "radius", "tagColor", "order",
                    "spanIds", "groupId",
                }
                and not (key == "groupId"
                         and not str(value or "").strip())
            }
            layer.raw_collection("areas", create=True)[area_id] = area_payload
            native_industries = layer.raw_collection(
                "industries", create=True
            )
            authored_for_area = {
                industry_id for industry_id, industry in native_industries.items()
                if isinstance(industry, dict)
                and str(industry.get("areaId", "")) == area_id
            }
            for industry_id in authored_for_area - set(industries_payload):
                native_industries.pop(industry_id, None)
            for industry_id, industry in industries_payload.items():
                native_industries[industry_id] = self._native_industry_payload(
                    area_id,
                    area_payload,
                    industry_id,
                    industry,
                )
        else:
            layer.raw_collection("areas", create=True)[area_id] = payload
        layer.areas[area_id] = payload
        self._mark_area_layer_dirty(layer_idx)

        self.mod_project._rebuild_merge()
        self._mark_measure_cache_dirty()
        return layer_idx

    def _delete_area_from_layer(self, area_id: str):
        pp = self.prog_project
        if not pp or not self.mod_project:
            return None
        layer_idx = pp.area_layer.get(area_id)
        if layer_idx is None:
            return None
        layer = self.mod_project.layers[layer_idx]
        areas = layer.raw_collection("areas", create=True)
        if layer.is_fuse_native:
            areas.pop(area_id, None)
            industries = layer.raw_collection("industries", create=True)
            for industry_id, industry in list(industries.items()):
                if (isinstance(industry, dict)
                        and str(industry.get("areaId", "")) == area_id):
                    industries.pop(industry_id, None)
        else:
            areas[area_id] = None
        layer.areas[area_id] = None
        self._mark_area_layer_dirty(layer_idx)

        self.mod_project._rebuild_merge()
        self._mark_measure_cache_dirty()
        return layer_idx

    @staticmethod
    def _native_industry_payload(area_id: str, area_payload: dict,
                                 industry_id: str, industry: dict) -> dict:
        source = copy.deepcopy(industry or {})
        area_position = area_payload.get("position") or {}
        local_position = source.pop("localPosition", {}) or {}
        position = {
            axis: float(area_position.get(axis, 0.0))
                  + float(local_position.get(axis, 0.0))
            for axis in ("x", "y", "z")
        }
        components = {}
        component_keys = {
            "remove", "partial", "type", "name", "trackSpanIds",
            "trackSpanPatch", "carTypeFilter", "loadId",
            "convertedLoadId", "sharedStorage", "storageChangeRate",
            "maxStorage", "carTransferRate", "costPerUnit",
            "notBeforeHour", "notAfterHour", "fillPercentage",
            "bookReasons", "title", "orderAroundEmpties",
            "orderAroundLoaded", "inputSpanIds", "outputSpanIds",
            "inputTermsPerDay", "outputTermsPerDay", "idealCars",
            "teamProfiles", "canOverhaul", "passengerStopId",
            "timetableCode", "basePopulation", "neighborIds", "branch",
            "branchDefinitions", "carLoadPeriod", "carLengthFeet", "fields",
        }
        id_reference_keys = {
            "loadId", "convertedLoadId", "passengerStopId",
        }
        for component_id, component in (source.pop("components", {}) or {}).items():
            if not isinstance(component, dict):
                continue
            native = copy.deepcopy(component)
            if "trackSpans" in native and "trackSpanIds" not in native:
                native["trackSpanIds"] = native.pop("trackSpans")
            fields = native.get("fields")
            if not isinstance(fields, dict):
                fields = {}
            for key in list(native):
                if key not in component_keys:
                    fields[key] = native.pop(key)
            for key in id_reference_keys:
                if key in native and not str(native[key] or "").strip():
                    native.pop(key)
            if fields:
                native["fields"] = fields
            elif "fields" in native:
                native.pop("fields")
            if not native.get("partial") and not native.get("remove"):
                native["type"] = str(native.get("type") or "loader")
                native["name"] = str(native.get("name") or component_id)
            components[component_id] = native

        result = {
            "name": str(source.pop("name", None) or industry_id),
            "areaId": area_id,
            "position": position,
            "rotation": source.pop(
                "rotation", {"x": 0.0, "y": 0.0, "z": 0.0}
            ),
            "usesContract": bool(source.pop("usesContract", False)),
            "components": components,
        }
        for key in ("order", "mergeComponents", "replaceComponents"):
            if key in source:
                result[key] = source[key]
        return result

    def _ask_json_object(self, title: str, prompt: str, initial_obj: dict):
        text = json.dumps(initial_obj, indent=2)
        while True:
            raw = ask_text(self.screen, title, prompt, text)
            if raw is None:
                return None
            try:
                parsed = json.loads(raw)
            except Exception as ex:
                self._set_status(f"{title} JSON parse failed: {ex}")
                if not ask_yes_no(self.screen, "Invalid JSON",
                                  f"Could not parse JSON: {ex}. Edit it again?"):
                    return None
                text = raw
                continue
            if not isinstance(parsed, dict):
                self._set_status(f"{title} must be a JSON object")
                if not ask_yes_no(self.screen, "JSON Object Required",
                                  "That JSON was valid, but it was not an object. Edit it again?"):
                    return None
                text = raw
                continue
            return parsed

    def _normalize_area_payload(self, payload: dict, area_id: str, default_name: str):
        data = dict(payload or {})
        data.pop("industries", None)
        pos = data.get("position")
        if not isinstance(pos, dict):
            pos = {}
        tag = data.get("tagColor", [0.5, 0.5, 0.5])
        if not isinstance(tag, list):
            tag = list(tag) if isinstance(tag, tuple) else [0.5, 0.5, 0.5]
        tag = list(tag[:3])
        while len(tag) < 3:
            tag.append(0.5)
        data["name"] = str(data.get("name") or default_name or area_id)
        data["position"] = {
            "x": float(pos.get("x", 0.0)),
            "y": float(pos.get("y", 0.0)),
            "z": float(pos.get("z", 0.0)),
        }
        data["radius"] = float(data.get("radius", 500.0))
        data["order"] = int(data.get("order", 0))
        data["tagColor"] = [float(tag[0]), float(tag[1]), float(tag[2])]
        return data

    def _normalize_industry_payload(self, payload: dict, industry_id: str):
        data = dict(payload or {})
        pos = data.get("localPosition")
        if not isinstance(pos, dict):
            pos = {}
        comps = data.get("components", {})
        if comps is None:
            comps = {}
        if not isinstance(comps, dict):
            raise ValueError("components must be a JSON object")
        uses_contract = data.get("usesContract", False)
        if isinstance(uses_contract, str):
            uses_contract = uses_contract.strip().lower() in ("1", "true", "yes", "y", "on")
        data["name"] = str(data.get("name") or industry_id)
        data["localPosition"] = {
            "x": float(pos.get("x", 0.0)),
            "y": float(pos.get("y", 0.0)),
            "z": float(pos.get("z", 0.0)),
        }
        data["usesContract"] = bool(uses_contract)
        data["components"] = comps
        return data

    def _normalize_component_payload(self, payload: dict, component_id: str, component_type: str = ""):
        data = dict(payload or {})
        if component_type and not data.get("type"):
            data["type"] = component_type
        if not data.get("name"):
            fallback = component_id if component_id != "" else "Component"
            data["name"] = fallback
        spans = data.get("trackSpans", [])
        if spans is None:
            spans = []
        elif isinstance(spans, str):
            spans = [spans]
        elif not isinstance(spans, list):
            raise ValueError("trackSpans must be a JSON array")
        data["trackSpans"] = spans
        return data

    def _component_template_for_type(self, component_type: str, industry_name: str = ""):
        ctype = (component_type or "").strip()
        short = ctype.split(".")[-1] if ctype else "Component"
        base = {
            "type": ctype,
            "name": industry_name or short,
            "trackSpans": [],
            "carTypeFilter": "*",
            "sharedStorage": True,
        }
        if ctype == "AlinasMapMod.PaxStationComponent":
            base.update({
                "timetableCode": "",
                "basePopulation": 50,
                "loadId": "passengers",
                "branch": "Main",
                "neighborIds": [],
            })
        elif ctype == "Model.Ops.FormulaicIndustryComponent":
            base.update({
                "carTypeFilter": "",
                "inputTermsPerDay": {},
                "outputTermsPerDay": {},
            })
        elif ctype == "Model.Ops.IndustryLoader":
            base.update({
                "loadId": "",
                "storageChangeRate": 0.0,
                "maxStorage": 0.0,
                "orderAroundEmpties": True,
                "carTransferRate": 0.0,
                "orderAroundLoaded": True,
            })
        elif ctype == "Model.Ops.IndustryUnloader":
            base.update({
                "loadId": "",
                "storageChangeRate": 0.0,
                "maxStorage": 0.0,
                "orderAroundEmpties": False,
                "carTransferRate": 0.0,
                "orderAroundLoaded": False,
            })
        elif ctype == "Model.Ops.InterchangedIndustryLoader":
            base.update({"loadId": ""})
        elif ctype == "Model.Ops.RepairTrack":
            base.update({
                "loadId": "repair-parts",
                "canOverhaul": True,
            })
        elif ctype == "Model.Ops.TeamTrack":
            base.update({
                "idealCars": 2.0,
                "teamProfiles": {},
            })
        return base

    def _add_area_dialog(self):
        pp = self.prog_project
        if not pp:
            return
        aid = ask_string(self.screen, "Area ID", "Enter area ID:") or ""
        aid = aid.strip()
        if not aid:
            return
        if aid in pp.areas:
            self._set_status(f"Area already exists: {aid}")
            return
        target = ask_string(self.screen, "Town File",
                            "File to save this area in:",
                            initialvalue=f"town_{aid}.json") or ""
        target = target.strip()
        if not target:
            return
        w2, h2 = self.screen.get_size()
        ux, uz = self.screen_to_unity(w2 // 2, h2 // 2)
        uy = self._sample_terrain_y(ux, uz)
        payload = self._ask_json_object(
            "New Area",
            "Edit the new town/area JSON object.",
            {
                "name": aid,
                "position": {"x": ux, "y": uy, "z": uz},
                "radius": 500,
                "order": 0,
                "tagColor": [0.5, 0.5, 0.5],
            },
        )
        if payload is None:
            return
        from mod_project import Area
        area_data = self._normalize_area_payload(payload, aid, aid)
        area_data["industries"] = {}
        pp.areas[aid] = Area(aid, area_data)
        layer_idx, _layer = self._ensure_town_layer(target)
        pp.area_layer[aid] = layer_idx
        self._sync_area_to_layer(aid)
        self.area_sel_id = aid
        self.area_sel_industry = None
        self.area_sel_component = None
        self._set_status(f"Added area: {aid}")

    def _edit_area_dialog(self):
        pp = self.prog_project
        area = self._selected_area_obj()
        if not pp or not area:
            return
        payload = area.to_dict()
        payload.pop("industries", None)
        edited = self._ask_json_object(
            "Edit Area",
            "Edit the selected town/area JSON object.",
            payload,
        )
        if edited is None:
            return
        from mod_project import Area
        full = area.to_dict()
        full.update(self._normalize_area_payload(edited, area.id, area.name))
        pp.areas[area.id] = Area(area.id, full)
        self._sync_area_to_layer(area.id)
        self._set_status(f"Updated area: {area.id}")

    def _add_industry_dialog(self):
        area = self._selected_area_obj()
        if not area:
            self._set_status("Select an area first")
            return
        iid = ask_string(self.screen, "Industry ID", "Enter industry ID:") or ""
        iid = iid.strip()
        if not iid:
            return
        if iid in area.industries:
            self._set_status(f"Industry already exists: {iid}")
            return
        payload = self._ask_json_object(
            "New Industry",
            "Edit the new industry JSON object.",
            {
                "name": iid.replace("-", " ").title(),
                "localPosition": {"x": 0.0, "y": 0.0, "z": 0.0},
                "usesContract": False,
                "components": {},
            },
        )
        if payload is None:
            return
        from mod_project import AreaIndustry
        area.industries[iid] = AreaIndustry(iid, self._normalize_industry_payload(payload, iid))
        self._sync_area_to_layer(area.id)
        self.area_sel_industry = iid
        self.area_sel_component = None
        self._set_status(f"Added industry: {iid}")

    def _edit_industry_dialog(self):
        area = self._selected_area_obj()
        ind = self._selected_industry_obj()
        if not area or not ind:
            self._set_status("Select an industry first")
            return
        edited = self._ask_json_object(
            "Edit Industry",
            "Edit the selected industry JSON object.",
            ind.to_dict(),
        )
        if edited is None:
            return
        from mod_project import AreaIndustry
        area.industries[ind.id] = AreaIndustry(ind.id, self._normalize_industry_payload(edited, ind.id))
        self._sync_area_to_layer(area.id)
        if self.area_sel_component not in area.industries[ind.id].components:
            self.area_sel_component = None
        self._set_status(f"Updated industry: {ind.id}")

    def _delete_selected_industry(self):
        area = self._selected_area_obj()
        ind = self._selected_industry_obj()
        if not area or not ind:
            self._set_status("Select an industry first")
            return
        if not ask_yes_no(self.screen, "Delete Industry",
                          f"Delete industry '{ind.id}' from '{area.name}'?"):
            return
        area.industries.pop(ind.id, None)
        self._sync_area_to_layer(area.id)
        self._set_status(f"Deleted industry: {ind.id}")
        self.area_sel_industry = None
        self.area_sel_component = None

    def _add_component_dialog(self):
        area = self._selected_area_obj()
        ind = self._selected_industry_obj()
        if not area or not ind:
            self._set_status("Select an industry first")
            return
        cid = ask_string(self.screen, "Component ID",
                         "Enter component ID (e.g. platform, formula, loader):") or ""
        cid = cid.strip()
        if not cid:
            return
        if cid in ind.components:
            self._set_status(f"Component already exists: {cid}")
            return
        ctype = ask_string(
            self.screen,
            "Component Type",
            "Type (PaxStation, Loader, Unloader, Formulaic, Interchange, TeamTrack, etc.):",
        ) or ""
        ctype = ctype.strip()
        if not ctype:
            return
        template = self._component_template_for_type(ctype, ind.name)
        edited = self._ask_json_object(
            "New Component",
            "Edit the new component JSON object.",
            template,
        )
        if edited is None:
            return
        ind.components[cid] = self._normalize_component_payload(edited, cid, ctype)
        self._sync_area_to_layer(area.id)
        self.area_sel_component = cid
        self._set_status(f"Added component: {cid}")

    def _edit_component_dialog(self):
        area = self._selected_area_obj()
        ind = self._selected_industry_obj()
        comp = self._selected_component_entry()
        if not area or not ind or comp is None:
            self._set_status("Select a component first")
            return
        cid = self.area_sel_component
        edited = self._ask_json_object(
            "Edit Component",
            "Edit the selected component JSON object.",
            comp,
        )
        if edited is None:
            return
        ind.components[cid] = self._normalize_component_payload(
            edited, cid, comp.get("type", "")
        )
        self._sync_area_to_layer(area.id)
        self._set_status(f"Updated component: {cid or '<root>'}")

    def _delete_selected_component(self):
        area = self._selected_area_obj()
        ind = self._selected_industry_obj()
        if not area or not ind or self.area_sel_component is None:
            self._set_status("Select a component first")
            return
        cid = self.area_sel_component
        label = cid or "<root>"
        if not ask_yes_no(self.screen, "Delete Component",
                          f"Delete component '{label}' from industry '{ind.id}'?"):
            return
        ind.components.pop(cid, None)
        self._sync_area_to_layer(area.id)
        self._set_status(f"Deleted component: {label}")
        self.area_sel_component = None

    def _handle_area_click(self, mx, my, content_top):
        """Handle clicks in the area editor panel."""
        w, h = self.screen.get_size()
        pw = min(w - 40, 1100)
        px = (w - pw) // 2
        py = content_top + 10
        ph = h - content_top - STATUS_H - 20
        if not pygame.Rect(px, py, pw, ph).collidepoint(mx, my):
            return False

        if pygame.Rect(px + pw - 30, py + 8, 22, 22).collidepoint(mx, my):
            self.area_panel = False
            return True

        pp = self.prog_project
        if not pp:
            return True

        if not hasattr(self, "_area_list_rects"):
            self._area_list_rects = []
        if not hasattr(self, "_area_ind_rects"):
            self._area_ind_rects = []
        if not hasattr(self, "_area_comp_rects"):
            self._area_comp_rects = []
        if not hasattr(self, "_area_action_rects"):
            self._area_action_rects = []

        for rect, aid in self._area_list_rects:
            if rect.collidepoint(mx, my):
                self.area_sel_id = aid
                self.area_sel_industry = None
                self.area_sel_component = None
                area = pp.areas[aid]
                self._set_status(
                    f"Area: {area.name}  pos=({area.x:.0f},{area.y:.0f},{area.z:.0f})  "
                    f"radius={area.radius:.0f}"
                )
                return True

        for rect, iid in self._area_ind_rects:
            if rect.collidepoint(mx, my):
                self.area_sel_industry = iid
                self.area_sel_component = None
                ind = self._selected_industry_obj()
                if ind:
                    self._set_status(f"Industry: {ind.name}  ({iid})")
                return True

        for rect, cid in self._area_comp_rects:
            if rect.collidepoint(mx, my):
                self.area_sel_component = cid
                comp = self._selected_component_entry()
                if comp:
                    self._set_status(
                        f"Component: {(cid or '<root>')}  ({comp.get('type', 'unknown')})"
                    )
                return True

        for rect, act in self._area_action_rects:
            if rect.collidepoint(mx, my):
                self._do_area_action(act)
                return True

        return True

    def _do_area_action(self, action):
        """Execute an area editor action."""
        pp = self.prog_project
        if not pp or not self.mod_project:
            return
        if action == "area_save":
            dirty_layers = sorted(self._area_dirty_layers)
            if dirty_layers:
                for layer_idx in dirty_layers:
                    self.mod_project.layers[layer_idx].save()
                self._area_dirty_layers.clear()
                if len(dirty_layers) == 1:
                    self._set_status(
                        f"Saved town layer: {self.mod_project.layers[dirty_layers[0]].path.name}"
                    )
                else:
                    self._set_status(f"Saved {len(dirty_layers)} town layers")
            elif self.area_sel_id and self.area_sel_id in pp.area_layer:
                layer_idx = pp.area_layer[self.area_sel_id]
                layer = self.mod_project.layers[layer_idx]
                layer.save()
                self._set_status(f"Saved town layer: {layer.path.name}")
            else:
                self._set_status("No town changes to save")
        elif action == "area_goto" and self.area_sel_id in pp.areas:
            area = pp.areas[self.area_sel_id]
            sx, sy = self.unity_to_screen(area.x, area.z)
            w2, h2 = self.screen.get_size()
            self.pan_x += w2 // 2 - sx
            self.pan_y += h2 // 2 - sy
            self.area_panel = False
            self._set_status(f"Panned to {area.name}")
        elif action == "area_add":
            self._add_area_dialog()
        elif action == "area_edit":
            self._edit_area_dialog()
        elif action == "area_del" and self.area_sel_id:
            area = self._selected_area_obj()
            if area and ask_yes_no(self.screen, "Delete Area",
                                   f"Delete area '{area.name}' ({area.id})?"):
                aid = area.id
                self._delete_area_from_layer(aid)
                pp.areas.pop(aid, None)
                pp.area_layer.pop(aid, None)
                self.area_sel_id = None
                self.area_sel_industry = None
                self.area_sel_component = None
                self._set_status(f"Deleted area: {aid}")
        elif action == "industry_add":
            self._add_industry_dialog()
        elif action == "industry_edit":
            self._edit_industry_dialog()
        elif action == "industry_del":
            self._delete_selected_industry()
        elif action == "comp_add":
            self._add_component_dialog()
        elif action == "comp_edit":
            self._edit_component_dialog()
        elif action == "comp_del":
            self._delete_selected_component()


    # ------------------------------------------------------------------
    # Track Spans editor
    # ------------------------------------------------------------------
    def _draw_spans_panel(self, surf, content_top):
        """Draw the track spans editor panel."""
        if not self.span_panel or not self.mod_project:
            return
        w, h  = surf.get_size()
        pw    = min(w - 40, 900)
        ph    = min(h - content_top - STATUS_H - 20, 600)
        px    = (w - pw) // 2
        py    = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 186))
        surf.blit(overlay, (0, content_top))
        panel_rect = pygame.Rect(px, py, pw, ph)
        pygame.draw.rect(surf, PANEL_ELEVATED_BG, panel_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, panel_rect, 1, border_radius=12)
        header_h = 98
        header_rect = pygame.Rect(px, py, pw, header_h)
        pygame.draw.rect(surf, PANEL_HEADER_BG, header_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, header_rect, 1, border_radius=12)
        pygame.draw.rect(surf, ACCENT_COLOR, (px, py + header_h - 4, pw, 4), border_radius=2)

        # X close
        xbtn = pygame.Rect(px+pw-30, py+8, 22, 22)
        hx   = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180,60,60) if hx else (80,40,40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px+pw-24, py+11), "✕", (220,200,200))

        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236,216,216))
        cx = px + 16
        cy = py + 14
        self.font_big.render_to(surf, (cx, cy), "Track Spans Editor", (0,212,255))
        cy += 20

        # Collect all spans from all layers
        all_spans = {}  # span_id -> (span_dict, layer_idx)
        for li, layer in enumerate(self.mod_project.layers):
            for sid, sv in layer.spans.items():
                if sv:
                    all_spans[sid] = (sv, li)
        # Also get from merged
        for sid, sv in {}.items():
            pass

        self.font.render_to(surf, (cx, cy),
            f"{len(all_spans)} spans  (industries use spans to locate loading areas)",
            (140,160,180))
        cy += 24
        pygame.draw.rect(surf, (180,60,60) if hx else (80,40,40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236,216,216))

        # Action buttons
        bx2 = cx
        self._span_rects = []
        for lbl2, act2, col2 in [
                ("+ New Span", "span_new",  (0,140,180)),
                ("Save",       "span_save", (220,140,0)),
        ]:
            bw2 = self.font_big.get_rect(lbl2).width + 16
            r2  = pygame.Rect(bx2, cy, bw2, 24)
            hv2 = r2.collidepoint(mx0, my0)
            pygame.draw.rect(surf, col2 if hv2 else tuple(v//2 for v in col2),
                             r2, border_radius=4)
            pygame.draw.rect(surf, col2, r2, 1, border_radius=4)
            self.font_big.render_to(surf, (bx2+8, cy+5), lbl2, (220,230,240))
            self._span_rects.append((r2, act2))
            bx2 += bw2 + 8
        cy = header_rect.bottom + 14

        # Split: span list left, detail right
        col_gap = 12
        list_w  = max(250, (pw - 32 - col_gap) // 3)
        det_x   = cx + list_w + col_gap
        det_w   = px + pw - det_x - 16
        row_h   = 18
        card_h  = py + ph - cy - 12
        list_card = pygame.Rect(cx, cy, list_w, card_h)
        detail_card = pygame.Rect(det_x, cy, det_w, card_h)
        for rect, title, accent in [
            (list_card, "SPANS", ACCENT_COLOR),
            (detail_card, "DETAILS", (110, 180, 255)),
        ]:
            pygame.draw.rect(surf, PANEL_SECTION_BG, rect, border_radius=10)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, rect, 1, border_radius=10)
            pygame.draw.rect(
                surf,
                PANEL_SECTION_ALT,
                (rect.x, rect.y, rect.width, 32),
                border_top_left_radius=10,
                border_top_right_radius=10,
            )
            pygame.draw.rect(surf, accent, (rect.x, rect.y + 28, rect.width, 3), border_radius=2)
            self.font_big.render_to(surf, (rect.x + 10, rect.y + 8), title, accent)

        list_top = list_card.y + 40
        detail_top = detail_card.y + 40
        max_rows = max(5, (list_card.bottom - list_top - 12) // row_h)

        for i, (sid, (sv, li)) in enumerate(list(all_spans.items())[:max_rows]):
            ry     = list_top + i * row_h
            is_sel = sid == self.span_sel_id
            r_s    = pygame.Rect(list_card.x + 8, ry, list_card.width - 16, row_h - 1)
            layer  = self.mod_project.layers[li]
            if is_sel:
                pygame.draw.rect(surf, ROW_ACTIVE_BG, r_s, border_radius=4)
                pygame.draw.rect(surf, ROW_ACTIVE_BORDER, r_s, 1, border_radius=4)
            elif r_s.collidepoint(mx0, my0):
                pygame.draw.rect(surf, ROW_HOVER_BG, r_s, border_radius=4)
            else:
                pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, r_s, border_radius=4)
            col_s  = (0,200,255) if is_sel else layer.color
            self.font.render_to(surf, (r_s.x + 6, ry+2), sid[:28], col_s)
            self._span_rects.append((r_s, f"span_sel:{sid}"))
        if not all_spans:
            self.font.render_to(surf, (list_card.x + 12, list_top), "No spans found in loaded layers", TEXT_MUTED)

        # Detail panel for selected span
        if self.span_sel_id and self.span_sel_id in all_spans:
            sv, li = all_spans[self.span_sel_id]
            layer  = self.mod_project.layers[li]
            dy     = detail_top

            self.font.render_to(surf, (det_x, dy), f"ID: {self.span_sel_id}", (220,230,240))
            dy += row_h + 2
            self.font.render_to(surf, (det_x, dy), f"Layer: {layer.label}", layer.color)
            dy += row_h + 4

            for half in ('upper', 'lower'):
                hd   = sv.get(half, {}) or {}
                col_h= (180,220,180) if half == 'upper' else (180,180,220)
                self.font.render_to(surf, (det_x, dy), half.upper()+":", (100,120,140))
                dy += row_h

                # Segment ID field
                seg_id = hd.get('segmentId', '')
                dist   = hd.get('distance', 0)
                end    = hd.get('end', 'Start')

                self.font.render_to(surf, (det_x+8, dy), "Segment:", (100,120,140))
                seg_r = pygame.Rect(det_x+80, dy-2, det_w-90, 15)
                act_k = f"span_{half}_seg"
                active= getattr(self,'_span_edit_key','') == act_k
                pygame.draw.rect(surf,(30,50,70) if active else (18,26,38), seg_r, border_radius=2)
                pygame.draw.rect(surf,(0,200,255) if active else (40,60,80), seg_r, 1, border_radius=2)
                buf = getattr(self,'_span_edit_buf','') if active else seg_id
                self.font.render_to(surf,(det_x+84, dy), buf + ("_" if active else ""), col_h)
                self._span_rects.append((seg_r, f"span_field:{act_k}:{seg_id}"))
                dy += row_h

                self.font.render_to(surf, (det_x+8, dy), "Distance:", (100,120,140))
                dist_r = pygame.Rect(det_x+80, dy-2, 80, 15)
                act_dk = f"span_{half}_dist"
                active_d = getattr(self,'_span_edit_key','') == act_dk
                pygame.draw.rect(surf,(30,50,70) if active_d else (18,26,38), dist_r, border_radius=2)
                pygame.draw.rect(surf,(0,200,255) if active_d else (40,60,80), dist_r, 1, border_radius=2)
                buf_d = getattr(self,'_span_edit_buf','') if active_d else str(dist)
                self.font.render_to(surf,(det_x+84, dy), buf_d + ("_" if active_d else ""), col_h)
                self._span_rects.append((dist_r, f"span_field:{act_dk}:{dist}"))
                dy += row_h

                # End toggle
                self.font.render_to(surf, (det_x+8, dy), "End:", (100,120,140))
                for opt in ('Start', 'End'):
                    ew   = self.font.get_rect(opt).width + 10
                    er   = pygame.Rect(det_x+80 + (['Start','End'].index(opt))*50, dy-1, ew, 15)
                    sel  = end == opt
                    hov  = er.collidepoint(mx0, my0)
                    ec   = (0,180,100) if opt=='Start' else (100,120,220)
                    pygame.draw.rect(surf, ec if sel else (20,30,40), er, border_radius=2)
                    if sel or hov: pygame.draw.rect(surf, ec, er, 1, border_radius=2)
                    self.font.render_to(surf,(er.x+5, dy), opt, (220,240,220) if sel else (120,140,120))
                    self._span_rects.append((er, f"span_end:{half}:{opt}"))
                dy += row_h + 4

            # Delete span button
            del_r = pygame.Rect(det_x, dy, 80, 20)
            hd2   = del_r.collidepoint(mx0, my0)
            pygame.draw.rect(surf,(180,60,60) if hd2 else (80,30,30), del_r, border_radius=3)
            pygame.draw.rect(surf,(220,80,80), del_r, 1, border_radius=3)
            self.font.render_to(surf,(det_x+6, dy+3), "Del Span", (220,200,200))
            self._span_rects.append((del_r, "span_delete"))
        else:
            self.font.render_to(surf, (detail_card.x + 12, detail_top), "Select a span to inspect or edit it.", TEXT_MUTED)

    def _handle_span_click(self, mx, my, content_top):
        """Handle clicks in the spans panel."""
        w, h = self.screen.get_size()
        pw   = min(w-40, 900)
        ph   = min(h - content_top - STATUS_H - 20, 600)
        px   = (w-pw)//2
        py   = content_top + 10
        if not pygame.Rect(px, py, pw, ph).collidepoint(mx, my):
            return False

        # X close
        if pygame.Rect(px+pw-30, py+8, 22, 22).collidepoint(mx, my):
            self.span_panel = False; return True

        if not hasattr(self, '_span_rects'):
            return True

        for r, act in self._span_rects:
            if not r.collidepoint(mx, my):
                continue
            if act.startswith('span_sel:'):
                self.span_sel_id      = act[9:]
                self._span_edit_key   = None
                self._span_edit_buf   = ''
                return True
            elif act.startswith('span_field:'):
                parts = act.split(':', 2)
                self._span_edit_key = parts[1]
                self._span_edit_buf = parts[2]
                return True
            elif act.startswith('span_end:'):
                _, half, opt = act.split(':')
                self._apply_span_field(half + '_end', opt)
                return True
            elif act == 'span_new':
                self._new_span_dialog()
                return True
            elif act == 'span_save':
                self._save_all_spans()
                return True
            elif act == 'span_delete':
                self._delete_selected_span()
                return True
        return True

    def _handle_span_keydown(self, event) -> bool:
        """Keyboard for span field editing."""
        key = getattr(self, '_span_edit_key', None)
        if not key:
            return False
        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self._apply_span_field(key, getattr(self, '_span_edit_buf', ''))
            self._span_edit_key = None
            self._span_edit_buf = ''
            return True
        elif event.key == pygame.K_ESCAPE:
            self._span_edit_key = None
            self._span_edit_buf = ''
            return True
        elif event.key == pygame.K_BACKSPACE:
            self._span_edit_buf = getattr(self,'_span_edit_buf','')[:-1]
            return True
        elif event.unicode:
            self._span_edit_buf = getattr(self,'_span_edit_buf','') + event.unicode
            return True
        return False

    def _apply_span_field(self, key: str, value: str):
        """Write an edited span field back to the layer."""
        if not self.span_sel_id or not self.mod_project:
            return
        # Find which layer owns this span
        for li, layer in enumerate(self.mod_project.layers):
            if self.span_sel_id in layer.spans:
                sv = dict(layer.spans[self.span_sel_id])
                upper = dict(sv.get('upper', {}))
                lower = dict(sv.get('lower', {}))
                if   key == 'span_upper_seg':  upper['segmentId'] = value.strip()
                elif key == 'span_lower_seg':  lower['segmentId'] = value.strip()
                elif key == 'span_upper_dist':
                    try: upper['distance'] = float(value)
                    except ValueError: return
                elif key == 'span_lower_dist':
                    try: lower['distance'] = float(value)
                    except ValueError: return
                elif key == 'upper_end':  upper['end'] = value
                elif key == 'lower_end':  lower['end'] = value
                sv['upper'] = upper
                sv['lower'] = lower
                span_set(layer, self.span_sel_id,
                         upper.get('segmentId',''), upper.get('distance',0), upper.get('end','Start'),
                         lower.get('segmentId',''), lower.get('distance',0), lower.get('end','End'))
                self._set_status(f"Span {self.span_sel_id} updated")
                return

    def _save_all_spans(self):
        """Save all dirty layers containing spans."""
        if not self.mod_project:
            return
        saved = 0
        for layer in self.mod_project.layers:
            if layer.dirty and layer.spans:
                layer.save()
                saved += 1
        self._set_status(f"Saved {saved} layer(s) with spans")

    def _delete_selected_span(self):
        """Delete the selected span from its layer."""
        if not self.span_sel_id or not self.mod_project:
            return
        for layer in self.mod_project.layers:
            if self.span_sel_id in layer.spans:
                self._push_undo(f"delete span {self.span_sel_id}")
                span_delete(layer, self.span_sel_id)
                layer.save()
                self._set_status(f"Deleted span: {self.span_sel_id}")
                self.span_sel_id = None
                return

    def _new_span_dialog(self):
        """Create a new span via dialog."""
        if not self.mod_project:
            return
        try:
            sid  = ask_string(self.screen, "Span ID", "Enter span ID:") or ""
            seg  = ask_string(self.screen, "Segment", "Segment ID for upper/lower:") or ""
            if sid and seg:
                graph = self.mod_project.get_graph_layer()
                if graph:
                    self._push_undo(f"create span {sid}")
                    span_set(graph, sid.strip(), seg.strip(), 0.0, 'Start',
                             seg.strip(), 0.0, 'End')
                    self.span_sel_id = sid.strip()
                    self._set_status(f"Created span: {sid}")
        except Exception as ex:
            self._set_status(f"Failed: {ex}")


    # ------------------------------------------------------------------
    # Dedicated spliney panel
    # ------------------------------------------------------------------

    def _suggest_spliney_layer_name(self, style: str) -> str:
        base = 'roads.json' if str(style).lower() != 'river' else 'rivers.json'
        if not self.mod_project:
            return base
        active_source = self.mod_project.active_source or self.mod_project._default_edit_source()
        if not active_source or not active_source.get('folder'):
            return base
        folder = Path(active_source['folder'])
        candidate = folder / base
        if not candidate.exists():
            return base
        stem = candidate.stem
        idx = 2
        while True:
            name = f"{stem}-{idx}.json"
            if not (folder / name).exists():
                return name
            idx += 1

    def _create_spliney_layer_dialog(self):
        if not self.mod_project:
            self._set_status("Load a mod first")
            return
        active_source = self.mod_project.active_source or self.mod_project._default_edit_source()
        if not active_source or active_source.get('is_base_game'):
            self._set_status("No writable mod source is active")
            return
        style = 'River' if str(self.geo_spline_style).lower() == 'river' else 'Road'
        suggested = self._suggest_spliney_layer_name(style)
        rel_path = ask_string(
            self.screen,
            "Spliney JSON",
            f"New JSON file for {style.lower()} splineys:",
            suggested,
        ) or ""
        rel_path = rel_path.strip()
        if not rel_path:
            return
        layer = self.mod_project.ensure_json_layer(
            rel_path,
            target='game-graph',
            template={'splineys': {}},
            make_active=True,
        )
        if not layer:
            self._set_status("Could not create that JSON layer")
            return
        self.spliney_target_path = str(layer.path)
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._set_status(f"Created {layer.path.name} and registered it in mixintos")

    def _place_spliney_seed_at(self, sx: float, sy: float):
        if not self.mod_project:
            self._set_status("Load a mod first")
            return
        style = 'River' if str(self.geo_spline_style).lower() == 'river' else 'Road'
        defaults = self._spliney_style_defaults(style)
        width = float(self.geo_spline_width) if float(self.geo_spline_width) > 0.0 else float(defaults['width'])
        if width <= 0.0:
            self._set_status("Spline width must be 0 or greater than 0")
            return
        try:
            seed_length = float(self.spliney_seed_length)
        except (TypeError, ValueError):
            self._set_status("Seed length must be a number")
            return
        if seed_length <= 0.0:
            self._set_status("Seed length must be greater than 0")
            return
        layer = self._spliney_target_layer(style)
        if layer is None or layer.read_only:
            self._set_status(f"No writable layer found for {style.lower()} splineys")
            return

        ux, uz = self.screen_to_unity(sx, sy)
        heading = self._spliney_seed_heading()
        points = self._spliney_seed_points(ux, uz, heading, seed_length, width)
        fit_meta = {}
        if style == 'River':
            points, fit_meta = self._fit_flowy_points_to_terrain(points, style=style)
        spliney_id = next_spliney_id(layer, prefix=style)
        spliney_add_road(layer, spliney_id, defaults['profile'], points, style=style)
        layer.save()
        self.mod_project._rebuild_merge()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))

        layer_idx = next((i for i, lyr in enumerate(self.mod_project.layers) if lyr is layer), None)
        if layer_idx is not None:
            self.mod_project.set_active_layer(layer_idx)
            self._set_selected_spliney_point(spliney_id, layer_idx, 1)
        self.spliney_target_path = str(layer.path)
        extra_text = ""
        if style == 'River':
            extras = [f"drop {float(fit_meta.get('drop_m', 0.0)):.1f} m"]
            if fit_meta.get('reversed_flow'):
                extras.append("flow reversed")
            extra_text = f", {', '.join(extras)}"
        self._set_status(
            f"Placed {style.lower()} spliney {spliney_id} in {layer.label}  "
            f"({seed_length:.1f} m seed, {defaults['profile']}{extra_text})"
        )

    def _delete_selected_flowy_spliney(self):
        if not self.sel_spliney_id or self.sel_spliney_layer is None or not self.mod_project:
            return
        if not (0 <= self.sel_spliney_layer < len(self.mod_project.layers)):
            return
        layer = self.mod_project.layers[self.sel_spliney_layer]
        spl = layer.splineys.get(self.sel_spliney_id)
        if not spl or 'FlowyThing' not in str(spl.get('handler', '')):
            self._set_status("Select a road or river spliney first")
            return
        spliney_delete(layer, self.sel_spliney_id)
        layer.save()
        self.mod_project._rebuild_merge()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        deleted_id = self.sel_spliney_id
        self.sel_spliney_id = None
        self.sel_spliney_pt = -1
        self.sel_spliney_layer = None
        self._set_status(f"Deleted spliney {deleted_id}")

    def _goto_selected_flowy_spliney(self):
        if not self.sel_spliney_id or self.sel_spliney_layer is None or not self.mod_project:
            return
        if not (0 <= self.sel_spliney_layer < len(self.mod_project.layers)):
            return
        layer = self.mod_project.layers[self.sel_spliney_layer]
        spl = layer.splineys.get(self.sel_spliney_id)
        if not spl:
            return
        pts = spl.get('points', [])
        if not pts:
            return
        focus_pt = pts[min(max(self.sel_spliney_pt, 0), len(pts) - 1)]
        pos = focus_pt.get('position', {})
        sx2, sy2 = self.unity_to_screen(pos.get('x', 0), pos.get('z', 0))
        w2, h2 = self.screen.get_size()
        self.pan_x += w2 // 2 - sx2
        self.pan_y += h2 // 2 - sy2
        self.spliney_panel = False
        self._set_status(f"Panned to {self.sel_spliney_id}[{max(self.sel_spliney_pt, 0)}]")

    def _selected_flowy_entry(self):
        if not self.sel_spliney_id or self.sel_spliney_layer is None or not self.mod_project:
            return None, None
        if not (0 <= self.sel_spliney_layer < len(self.mod_project.layers)):
            return None, None
        layer = self.mod_project.layers[self.sel_spliney_layer]
        spl = layer.splineys.get(self.sel_spliney_id)
        if not spl or 'FlowyThing' not in str(spl.get('handler', '')):
            return None, None
        return layer, spl

    def _clear_spliney_range_selection(self):
        self.sel_spliney_range_id = None
        self.sel_spliney_range_layer = None
        self.sel_spliney_range_anchor = -1

    def _current_spliney_range_state(self) -> dict:
        anchor_id = getattr(self, 'sel_spliney_range_id', None)
        anchor_layer = getattr(self, 'sel_spliney_range_layer', None)
        anchor_idx = int(getattr(self, 'sel_spliney_range_anchor', -1))
        if (not self.sel_spliney_id or self.sel_spliney_layer is None or
                anchor_id != self.sel_spliney_id or anchor_layer != self.sel_spliney_layer):
            return {'anchor': None, 'current': self.sel_spliney_pt, 'start': None, 'end': None, 'ready': False}
        layer, spl = self._selected_flowy_entry()
        pts = spl.get('points', []) if spl else []
        current_idx = int(self.sel_spliney_pt)
        if (anchor_idx < 0 or current_idx < 0 or
                anchor_idx >= len(pts) or current_idx >= len(pts)):
            self._clear_spliney_range_selection()
            return {'anchor': None, 'current': self.sel_spliney_pt, 'start': None, 'end': None, 'ready': False}
        return {
            'anchor': anchor_idx,
            'current': current_idx,
            'start': min(anchor_idx, current_idx),
            'end': max(anchor_idx, current_idx),
            'ready': anchor_idx != current_idx,
        }

    def _set_selected_spliney_point(self, spliney_id: str, layer_idx: int, point_idx: int,
                                    preserve_range: bool = False):
        if (not preserve_range or spliney_id != self.sel_spliney_id or
                layer_idx != self.sel_spliney_layer):
            self._clear_spliney_range_selection()
        self.sel_spliney_layer = layer_idx
        self.sel_spliney_id = spliney_id
        self.sel_spliney_pt = int(point_idx)

    def _toggle_spliney_range_anchor(self):
        if not self.sel_spliney_id or self.sel_spliney_layer is None:
            self._set_status("Select a spliney point first")
            return
        state = self._current_spliney_range_state()
        if state.get('ready'):
            self._clear_spliney_range_selection()
            self._set_status("Spliney range cleared")
            return
        anchor_idx = state.get('anchor')
        if anchor_idx == self.sel_spliney_pt:
            self._clear_spliney_range_selection()
            self._set_status("Spliney range start cleared")
            return
        self.sel_spliney_range_id = self.sel_spliney_id
        self.sel_spliney_range_layer = self.sel_spliney_layer
        self.sel_spliney_range_anchor = int(self.sel_spliney_pt)
        self._set_status(
            f"Spliney range start set at {self.sel_spliney_id}[{self.sel_spliney_pt}]  "
            f"shift-click another point or use Prev/Next, then Fill Width or Grade"
        )

    def _spl_fill_width_range(self):
        li = self.sel_spliney_layer
        if li is None or not self.mod_project or not self.sel_spliney_id:
            return
        state = self._current_spliney_range_state()
        if not state.get('ready'):
            self._set_status("Mark a spliney range first")
            return
        layer = self.mod_project.layers[li]
        spl = layer.splineys.get(self.sel_spliney_id)
        if not spl:
            return
        pts = copy.deepcopy(spl.get('points', []))
        if not pts:
            return
        width_value = None
        if getattr(self, '_spl_edit_key', '') == 'spl_width' and str(getattr(self, '_spl_edit_buf', '')).strip():
            try:
                width_value = float(self._spl_edit_buf)
            except ValueError:
                width_value = None
        if width_value is None:
            try:
                width_value = float(pts[self.sel_spliney_pt].get('width'))
            except (TypeError, ValueError):
                width_value = None
        if width_value is None:
            self._set_status("Current point needs a valid width first")
            return
        start_idx = int(state['start'])
        end_idx = int(state['end'])
        for idx in range(start_idx, end_idx + 1):
            pt = dict(pts[idx])
            pt['width'] = float(width_value)
            pts[idx] = pt
        updated = dict(spl)
        updated['points'] = pts
        self._save_flowy_spliney(layer, self.sel_spliney_id, updated)
        self._set_status(
            f"{self.sel_spliney_id}[{start_idx}..{end_idx}] width -> {float(width_value):.2f} m"
        )

    def _current_spliney_grade_pct(self) -> float:
        if getattr(self, '_spl_edit_key', '') == 'spl_grade_pct':
            raw = str(getattr(self, '_spl_edit_buf', '')).strip()
            if raw:
                try:
                    return float(raw)
                except ValueError:
                    pass
        return float(getattr(self, 'spliney_grade_pct', 0.0))

    def _selected_spliney_grade_span(self):
        layer, spl = self._selected_flowy_entry()
        if not layer or not spl:
            self._set_status("Select a road or river spliney first")
            return None
        points = copy.deepcopy(spl.get('points', []))
        if len(points) < 2:
            self._set_status("Spliney needs at least 2 points")
            return None
        range_state = self._current_spliney_range_state()
        if range_state.get('ready'):
            start_idx = int(range_state['start'])
            end_idx = int(range_state['end'])
        elif range_state.get('anchor') is not None:
            self._set_status(
                "Mark a second point for the spliney range, or clear the start to grade the whole spliney"
            )
            return None
        else:
            start_idx = 0
            end_idx = len(points) - 1

        nodes = []
        for idx in range(start_idx, end_idx + 1):
            pt = points[idx]
            if not isinstance(pt, dict):
                continue
            pos = dict(pt.get('position', {}) or {})
            try:
                nodes.append({
                    'id': idx,
                    'x': float(pos.get('x', 0.0)),
                    'y': float(pos.get('y', 0.0)),
                    'z': float(pos.get('z', 0.0)),
                })
            except (TypeError, ValueError):
                continue
        if len(nodes) < 2:
            self._set_status("Selected spliney span needs at least 2 valid points")
            return None
        return layer, spl, points, nodes, start_idx, end_idx

    def _apply_spliney_y_results(self, layer, spliney_id: str, spl: dict, points: list, results: list):
        updated_points = list(points)
        touched = []
        for idx, new_y in results:
            if not (0 <= idx < len(updated_points)):
                continue
            pt = updated_points[idx]
            if not isinstance(pt, dict):
                continue
            pt = dict(pt)
            pos = dict(pt.get('position', {}) or {})
            pos['y'] = float(new_y)
            pt['position'] = pos
            updated_points[idx] = pt
            touched.append(idx)
        if touched:
            self._solve_spliney_rotation_span(updated_points, min(touched), max(touched))
        updated = dict(spl)
        updated['points'] = updated_points
        self._save_flowy_spliney(layer, spliney_id, updated)
        return updated_points

    def _spliney_span_length(self, nodes: list) -> float:
        import math as _math

        return sum(
            _math.sqrt(
                (nodes[i]['x'] - nodes[i - 1]['x']) ** 2 +
                (nodes[i]['z'] - nodes[i - 1]['z']) ** 2
            )
            for i in range(1, len(nodes))
        )

    def _spl_smooth_grade_range(self):
        span = self._selected_spliney_grade_span()
        if not span:
            return
        layer, spl, points, nodes, start_idx, end_idx = span
        results = smooth_grade(nodes, fix_first=True, fix_last=True)
        if len(results) < 2:
            self._set_status("Not enough valid spliney points to smooth")
            return
        self._apply_spliney_y_results(layer, self.sel_spliney_id, spl, points, results)
        total_dist = self._spliney_span_length(nodes)
        y_vals = [float(y) for _, y in results]
        self._set_status(
            f"Grade smoothed: {self.sel_spliney_id}[{start_idx}..{end_idx}]  "
            f"{total_dist:.1f} m  Y {y_vals[0]:.2f}->{y_vals[-1]:.2f} m"
        )

    def _spl_apply_grade_range(self):
        span = self._selected_spliney_grade_span()
        if not span:
            return
        layer, spl, points, nodes, start_idx, end_idx = span
        grade_pct = self._current_spliney_grade_pct()
        results = apply_grade_from_start(nodes, grade_pct=grade_pct, fix_first=True)
        if len(results) < 2:
            self._set_status("Not enough valid spliney points to grade")
            return
        self._apply_spliney_y_results(layer, self.sel_spliney_id, spl, points, results)
        total_dist = self._spliney_span_length(nodes)
        y_vals = [float(y) for _, y in results]
        rise = y_vals[-1] - y_vals[0]
        self._set_status(
            f"Grade applied: {self.sel_spliney_id}[{start_idx}..{end_idx}]  "
            f"{grade_pct:+.2f}% over {total_dist:.1f} m  rise/fall {rise:+.2f} m"
        )

    def _spl_auto_pitch_range(self):
        span = self._selected_spliney_grade_span()
        if not span:
            return
        layer, spl, points, nodes, start_idx, end_idx = span
        self._solve_spliney_rotation_span(points, start_idx, end_idx)
        updated = dict(spl)
        updated['points'] = points
        self._save_flowy_spliney(layer, self.sel_spliney_id, updated)
        self._set_status(
            f"Pitch solved: {self.sel_spliney_id}[{start_idx}..{end_idx}]  "
            f"{self._spliney_span_length(nodes):.1f} m"
        )

    def _save_flowy_spliney(self, layer, spliney_id: str, spl: dict):
        if 'splineys' not in layer._raw:
            layer._raw['splineys'] = {}
        layer._raw['splineys'][spliney_id] = copy.deepcopy(spl)
        layer.splineys[spliney_id] = copy.deepcopy(spl)
        layer.dirty = True
        layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))

    def _flowy_direction_meta(self, spl: dict) -> dict:
        pts = spl.get('points', []) if spl else []
        if not pts:
            return {'start_y': 0.0, 'end_y': 0.0, 'drop_m': 0.0}
        start_y = float(pts[0].get('position', {}).get('y', 0.0))
        end_y = float(pts[-1].get('position', {}).get('y', 0.0))
        return {
            'start_y': start_y,
            'end_y': end_y,
            'drop_m': start_y - end_y,
        }

    def _fit_selected_flowy_to_terrain(self):
        layer, spl = self._selected_flowy_entry()
        if not layer or not spl:
            self._set_status("Select a road or river spliney first")
            return
        points = copy.deepcopy(spl.get('points', []))
        if len(points) < 2:
            self._set_status("Spliney needs at least 2 points")
            return
        style = str(spl.get('style', 'Road'))
        fitted_points, fit_meta = self._fit_flowy_points_to_terrain(
            points, style=style, normalize_rotations=True
        )
        updated = dict(spl)
        updated['points'] = fitted_points
        self._save_flowy_spliney(layer, self.sel_spliney_id, updated)
        message = f"Terrain fit applied to {self.sel_spliney_id}"
        if style == 'River':
            if fit_meta.get('reversed_flow'):
                if (self.sel_spliney_range_id == self.sel_spliney_id and
                        self.sel_spliney_range_layer == self.sel_spliney_layer and
                        self.sel_spliney_range_anchor >= 0):
                    self.sel_spliney_range_anchor = max(
                        0, len(fitted_points) - 1 - self.sel_spliney_range_anchor
                    )
                self.sel_spliney_pt = max(0, len(fitted_points) - 1 - self.sel_spliney_pt)
                message += " (flow reversed)"
            message += f"  drop {float(fit_meta.get('drop_m', 0.0)):.1f} m"
        self._set_status(message)

    def _reverse_selected_flowy(self):
        layer, spl = self._selected_flowy_entry()
        if not layer or not spl:
            self._set_status("Select a road or river spliney first")
            return
        if str(spl.get('style', 'Road')).lower() != 'river':
            self._set_status("Reverse Flow is only used for river splineys")
            return
        points = copy.deepcopy(spl.get('points', []))
        if len(points) < 2:
            self._set_status("Spliney needs at least 2 points")
            return
        points = self._reverse_flowy_points(points)
        updated = dict(spl)
        updated['points'] = points
        if (self.sel_spliney_range_id == self.sel_spliney_id and
                self.sel_spliney_range_layer == self.sel_spliney_layer and
                self.sel_spliney_range_anchor >= 0):
            self.sel_spliney_range_anchor = max(0, len(points) - 1 - self.sel_spliney_range_anchor)
        if self.sel_spliney_pt >= 0:
            self.sel_spliney_pt = max(0, len(points) - 1 - self.sel_spliney_pt)
        self._save_flowy_spliney(layer, self.sel_spliney_id, updated)
        flow_meta = self._flowy_direction_meta(updated)
        self._set_status(
            f"{self.sel_spliney_id} reversed  drop {float(flow_meta.get('drop_m', 0.0)):.1f} m"
        )

    def _draw_spliney_panel(self, surf, content_top):
        if not self.spliney_panel or not self.mod_project:
            return
        w, h = surf.get_size()
        pw = min(w - 40, 980)
        ph = min(h - content_top - STATUS_H - 20, 620)
        px = (w - pw) // 2
        py = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 192))
        surf.blit(overlay, (0, content_top))

        panel_rect = pygame.Rect(px, py, pw, ph)
        pygame.draw.rect(surf, PANEL_ELEVATED_BG, panel_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, panel_rect, 1, border_radius=12)
        header_h = 136
        header_rect = pygame.Rect(px, py, pw, header_h)
        pygame.draw.rect(surf, PANEL_HEADER_BG, header_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, header_rect, 1, border_radius=12)
        pygame.draw.rect(surf, ACCENT_COLOR, (px, py + header_h - 4, pw, 4), border_radius=2)

        self._spliney_rects = []
        xbtn = pygame.Rect(px + pw - 30, py + 8, 22, 22)
        hov_x = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180, 60, 60) if hov_x else (80, 40, 40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220, 80, 80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px + pw - 24, py + 11), "x", (220, 200, 200))
        self._spliney_rects.append((xbtn, 'close'))

        cx = px + 16
        cy = py + 14
        self.font_big.render_to(surf, (cx, cy), "Road / River Splineys", (0, 212, 255))
        cy += 20

        all_flowy = self._flowy_splineys()
        active_source = self.mod_project.active_source or self.mod_project._default_edit_source()
        source_name = active_source.get('name', 'No source') if active_source else 'No source'
        self.font.render_to(
            surf,
            (cx, cy),
            f"{len(all_flowy)} flowy splineys   Source: {source_name}",
            (140, 160, 180),
        )
        cy += 18

        defaults = self._spliney_style_defaults(self.geo_spline_style)
        width_value = float(self.geo_spline_width) if float(self.geo_spline_width) > 0.0 else float(defaults['width'])
        heading_value = self._spliney_seed_heading()
        heading_mode = (
            f"selected node {self.sel_mod_node_id}"
            if self.spliney_use_selected_heading and self.sel_mod_node_id and self.mod_project
               and self.mod_project.merged_nodes.get(self.sel_mod_node_id)
            else "panel heading"
        )
        self.font.render_to(
            surf,
            (cx, cy),
            f"Profile: {defaults['profile']}   Width: {width_value:.1f} m   Seed: {float(self.spliney_seed_length):.1f} m",
            (120, 140, 160),
        )
        cy += 18
        self.font.render_to(
            surf,
            (cx, cy),
            f"Heading: {heading_value:.1f}° from {heading_mode}. Click map in place mode to seed 2 points automatically.",
            (120, 140, 160),
        )
        cy += 22

        def choice_chip(x, y, label, selected, action, color):
            bw = self.font.get_rect(label).width + 12
            rect = pygame.Rect(x, y, bw, 18)
            hov = rect.collidepoint(mx0, my0)
            fill = color if selected else ((36, 54, 78) if hov else (20, 28, 40))
            pygame.draw.rect(surf, fill, rect, border_radius=4)
            pygame.draw.rect(surf, color if selected or hov else (60, 74, 92), rect, 1, border_radius=4)
            self.font.render_to(
                surf,
                (x + 6, y + 3),
                label,
                (228, 236, 242) if selected else (140, 160, 180),
            )
            self._spliney_rects.append((rect, action))
            return rect.right + 6

        row_y = py + 94
        self.font.render_to(surf, (cx, row_y + 3), "Type:", (100, 120, 140))
        bx = cx + 38
        for opt in ('Road', 'River'):
            bx = choice_chip(bx, row_y, opt, self.geo_spline_style == opt, f'spl_style:{opt}',
                             (0, 160, 120) if opt == 'Road' else (0, 130, 190))

        def number_field(x, y, label, key, value, width=72):
            self.font.render_to(surf, (x, y + 3), label + ":", (100, 120, 140))
            rect = pygame.Rect(x + 56, y, width, 18)
            active = self._spliney_edit_key == key
            disp = (self._spliney_edit_buf if active else str(value)) + ("_" if active else "")
            pygame.draw.rect(surf, (30, 50, 70) if active else (18, 26, 38), rect, border_radius=3)
            pygame.draw.rect(surf, (0, 200, 255) if active else (40, 60, 80), rect, 1, border_radius=3)
            self.font.render_to(surf, (rect.x + 4, y + 3), disp, (180, 220, 180))
            self._spliney_rects.append((rect, f'spl_field:{key}:{value}'))
            return rect.right

        bx += 14
        bx = number_field(bx, row_y, "Width", 'geo_spline_width', self.geo_spline_width, width=78) + 14
        bx = number_field(bx, row_y, "Seed", 'spliney_seed_length', self.spliney_seed_length, width=72) + 14
        bx = number_field(bx, row_y, "Heading", 'spliney_place_rotY', self.spliney_place_rotY, width=72) + 14
        choice_chip(
            bx,
            row_y,
            "Use Sel Rot",
            bool(self.spliney_use_selected_heading),
            'spl_toggle_sel_heading',
            (110, 140, 220),
        )

        layers_y = header_rect.bottom - 36
        self.font.render_to(surf, (cx, layers_y + 3), "Target:", (100, 120, 140))
        bx = cx + 50
        candidates = self._spliney_candidate_layers(self.geo_spline_style)
        _, selected_layer = self._selected_spliney_target_layer(self.geo_spline_style)
        if selected_layer is None and candidates:
            selected_layer = candidates[0][1]
            self.spliney_target_path = str(selected_layer.path)
        for li, layer in candidates:
            label = layer.path.name
            bw = self.font.get_rect(label).width + 14
            if bx + bw > px + pw - 150:
                layers_y += 22
                bx = cx + 50
            rect = pygame.Rect(bx, layers_y, bw, 18)
            selected = selected_layer is layer
            hov = rect.collidepoint(mx0, my0)
            fill = layer.color if selected else ((34, 46, 60) if hov else (18, 26, 38))
            pygame.draw.rect(surf, fill, rect, border_radius=4)
            pygame.draw.rect(surf, layer.color if selected or hov else (52, 64, 82), rect, 1, border_radius=4)
            self.font.render_to(
                surf,
                (rect.x + 6, rect.y + 3),
                label,
                (228, 236, 242) if selected else (140, 160, 180),
            )
            self._spliney_rects.append((rect, f'spl_target:{li}'))
            bx = rect.right + 6
        new_rect = pygame.Rect(px + pw - 124, header_rect.bottom - 34, 108, 22)
        hov_new = new_rect.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (40, 120, 180) if hov_new else (22, 58, 88), new_rect, border_radius=4)
        pygame.draw.rect(surf, (70, 170, 240), new_rect, 1, border_radius=4)
        self.font.render_to(surf, (new_rect.x + 9, new_rect.y + 5), "New JSON...", (224, 236, 244))
        self._spliney_rects.append((new_rect, 'spl_new_json'))

        cy = header_rect.bottom + 14
        place_r = pygame.Rect(cx, cy, 140, 24)
        placing = self.spliney_place_mode
        pygame.draw.rect(surf, (0, 160, 90) if placing else (0, 84, 48), place_r, border_radius=5)
        pygame.draw.rect(surf, (0, 220, 110), place_r, 1, border_radius=5)
        self.font_big.render_to(
            surf,
            (place_r.x + 8, place_r.y + 5),
            "● Placing..." if placing else "Place on Map",
            (220, 255, 220),
        )
        self._spliney_rects.append((place_r, 'spl_place_toggle'))

        self.font.render_to(
            surf,
            (place_r.right + 14, cy + 4),
            "First click creates a new spliney and seeds the first two control points.",
            (120, 140, 160),
        )
        cy += 34

        col_gap = 12
        list_w = max(280, (pw - 32 - col_gap) // 3)
        det_x = cx + list_w + col_gap
        det_w = px + pw - det_x - 16
        row_h = 18
        card_h = py + ph - cy - 12
        list_card = pygame.Rect(cx, cy, list_w, card_h)
        detail_card = pygame.Rect(det_x, cy, det_w, card_h)
        for rect, title, accent in [
            (list_card, "SPLINEYS", ACCENT_COLOR),
            (detail_card, "DETAILS", (110, 180, 255)),
        ]:
            pygame.draw.rect(surf, PANEL_SECTION_BG, rect, border_radius=10)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, rect, 1, border_radius=10)
            pygame.draw.rect(
                surf,
                PANEL_SECTION_ALT,
                (rect.x, rect.y, rect.width, 32),
                border_top_left_radius=10,
                border_top_right_radius=10,
            )
            pygame.draw.rect(surf, accent, (rect.x, rect.y + 28, rect.width, 3), border_radius=2)
            self.font_big.render_to(surf, (rect.x + 10, rect.y + 8), title, accent)

        list_top = list_card.y + 40
        detail_top = detail_card.y + 40
        max_rows = max(6, (list_card.bottom - list_top - 12) // row_h)

        selected_entry = None
        for sid, spl, li in all_flowy:
            if sid == self.sel_spliney_id and li == self.sel_spliney_layer:
                selected_entry = (sid, spl, li)
                break

        for i, (sid, spl, li) in enumerate(all_flowy[:max_rows]):
            ry = list_top + i * row_h
            is_sel = sid == self.sel_spliney_id and li == self.sel_spliney_layer
            row_rect = pygame.Rect(list_card.x + 8, ry, list_card.width - 16, row_h - 1)
            layer = self.mod_project.layers[li]
            if is_sel:
                pygame.draw.rect(surf, ROW_ACTIVE_BG, row_rect, border_radius=4)
                pygame.draw.rect(surf, ROW_ACTIVE_BORDER, row_rect, 1, border_radius=4)
            elif row_rect.collidepoint(mx0, my0):
                pygame.draw.rect(surf, ROW_HOVER_BG, row_rect, border_radius=4)
            else:
                pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, row_rect, border_radius=4)
            style = str(spl.get('style', 'Road'))
            label = f"{sid[:18]}  [{style[:5]}]"
            self.font.render_to(surf, (row_rect.x + 6, ry + 2), label, (0, 200, 255) if is_sel else layer.color)
            self._spliney_rects.append((row_rect, f'spl_sel:{li}:{sid}'))

        if not all_flowy:
            self.font.render_to(surf, (list_card.x + 12, list_top), "No road or river splineys in the active mod source", TEXT_MUTED)

        if selected_entry is not None:
            sid, spl, li = selected_entry
            layer = self.mod_project.layers[li]
            pts = spl.get('points', [])
            style_name = str(spl.get('style', 'Road'))
            flow_meta = self._flowy_direction_meta(spl)
            dy = detail_top
            poly_pts = [
                {'x': pt.get('position', {}).get('x', 0.0), 'z': pt.get('position', {}).get('z', 0.0)}
                for pt in pts
            ]
            approx_len = alignment_polyline_length(poly_pts) if len(poly_pts) >= 2 else 0.0
            width_samples = []
            for pt in pts:
                try:
                    width_samples.append(float(pt.get('width', 0.0)))
                except (TypeError, ValueError):
                    pass
            width_text = f"{width_samples[0]:.1f} m" if width_samples else "n/a"
            for label, value, color in [
                ("ID", sid, (220, 230, 240)),
                ("Layer", layer.label, layer.color),
                ("Style", style_name, (180, 220, 180)),
                ("Profile", str(spl.get('profile', '')), (180, 220, 180)),
                ("Points", str(len(pts)), (180, 220, 180)),
                ("Width", width_text, (180, 220, 180)),
                ("Approx Len", f"{approx_len:.1f} m", (180, 220, 180)),
            ]:
                self.font.render_to(surf, (det_x, dy), label + ":", (100, 120, 140))
                self.font.render_to(surf, (det_x + 82, dy), value, color)
                dy += row_h
            if style_name == 'River' and pts:
                drop_m = float(flow_meta.get('drop_m', 0.0))
                flow_color = (120, 210, 255) if drop_m >= 0.0 else (255, 140, 120)
                self.font.render_to(surf, (det_x, dy), "Flow:", (100, 120, 140))
                self.font.render_to(
                    surf,
                    (det_x + 82, dy),
                    (
                        f"P0 -> P{len(pts) - 1}   "
                        f"Y {float(flow_meta.get('start_y', 0.0)):.1f} -> "
                        f"{float(flow_meta.get('end_y', 0.0)):.1f}   "
                        f"Drop {drop_m:.1f} m"
                    ),
                    flow_color,
                )
                dy += row_h
            if pts:
                focus_idx = min(max(self.sel_spliney_pt, 0), len(pts) - 1)
                pos = pts[focus_idx].get('position', {})
                rot = pts[focus_idx].get('rotation', {})
                dy += 4
                self.font.render_to(surf, (det_x, dy), f"Selected point: [{focus_idx}]", (0, 200, 255))
                dy += row_h
                self.font.render_to(
                    surf,
                    (det_x, dy),
                    f"Pos ({pos.get('x', 0):.1f}, {pos.get('y', 0):.1f}, {pos.get('z', 0):.1f})   RotY {rot.get('y', 0):.1f}°",
                    (180, 220, 180),
                )
                dy += row_h
            dy += 8

            button_specs = [
                (pygame.Rect(det_x, dy, 64, 20), 'spl_goto', (0, 70, 100), (0, 160, 200), "Go To"),
                (pygame.Rect(det_x + 74, dy, 70, 20), 'spl_del', (80, 30, 30), (220, 80, 80), "Delete"),
                (pygame.Rect(det_x + 154, dy, 90, 20), 'spl_fit_terrain', (0, 92, 70), (0, 180, 140), "Fit Terrain"),
            ]
            if style_name == 'River':
                button_specs.append(
                    (pygame.Rect(det_x + 254, dy, 98, 20), 'spl_reverse_flow', (40, 84, 120), (110, 190, 255), "Reverse Flow")
                )
            for rect, action, base, border, label in button_specs:
                hov = rect.collidepoint(mx0, my0)
                pygame.draw.rect(surf, tuple(min(255, c + 24) for c in base) if hov else base, rect, border_radius=4)
                pygame.draw.rect(surf, border, rect, 1, border_radius=4)
                self.font.render_to(surf, (rect.x + 8, rect.y + 3), label, (220, 230, 240))
                self._spliney_rects.append((rect, action))
            dy += 30
            self.font.render_to(
                surf,
                (det_x, dy),
                "Click a control dot on the map to open the point editor. River arrows on the map show downstream.",
                TEXT_MUTED,
            )
        else:
            self.font.render_to(
                surf,
                (detail_card.x + 12, detail_top),
                "Select a road/river spliney from the list or click one on the map.",
                TEXT_MUTED,
            )

    def _handle_spliney_panel_click(self, mx, my, content_top):
        w, h = self.screen.get_size()
        pw = min(w - 40, 980)
        ph = min(h - content_top - STATUS_H - 20, 620)
        px = (w - pw) // 2
        py = content_top + 10
        if not pygame.Rect(px, py, pw, ph).collidepoint(mx, my):
            return False
        for rect, action in getattr(self, '_spliney_rects', []):
            if not rect.collidepoint(mx, my):
                continue
            if action == 'close':
                self.spliney_panel = False
                self.spliney_place_mode = False
                return True
            if action.startswith('spl_style:'):
                self.geo_spline_style = action.split(':', 1)[1]
                self._spliney_edit_key = ''
                self._spliney_edit_buf = ''
                if not self._selected_spliney_target_layer(self.geo_spline_style)[1]:
                    layer = self._spliney_target_layer(self.geo_spline_style)
                    if layer is not None:
                        self.spliney_target_path = str(layer.path)
                return True
            if action.startswith('spl_target:'):
                li = int(action.split(':', 1)[1])
                if 0 <= li < len(self.mod_project.layers):
                    self.spliney_target_path = str(self.mod_project.layers[li].path)
                return True
            if action.startswith('spl_field:'):
                _, key, value = action.split(':', 2)
                self._spliney_edit_key = key
                self._spliney_edit_buf = value
                return True
            if action == 'spl_toggle_sel_heading':
                self.spliney_use_selected_heading = not self.spliney_use_selected_heading
                return True
            if action == 'spl_new_json':
                self._create_spliney_layer_dialog()
                return True
            if action == 'spl_place_toggle':
                self.spliney_place_mode = not self.spliney_place_mode
                return True
            if action.startswith('spl_sel:'):
                _, li_txt, sid = action.split(':', 2)
                li = int(li_txt)
                self._set_selected_spliney_point(sid, li, 0)
                if 0 <= li < len(self.mod_project.layers):
                    self.spliney_target_path = str(self.mod_project.layers[li].path)
                return True
            if action == 'spl_goto':
                self._goto_selected_flowy_spliney()
                return True
            if action == 'spl_del':
                self._delete_selected_flowy_spliney()
                return True
            if action == 'spl_fit_terrain':
                self._fit_selected_flowy_to_terrain()
                return True
            if action == 'spl_reverse_flow':
                self._reverse_selected_flowy()
                return True
        return True

    def _handle_spliney_panel_keydown(self, event) -> bool:
        if not self._spliney_edit_key:
            return False
        key = self._spliney_edit_key
        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self._apply_spliney_panel_field(key, self._spliney_edit_buf)
            self._spliney_edit_key = ''
            self._spliney_edit_buf = ''
            return True
        if event.key == pygame.K_ESCAPE:
            self._spliney_edit_key = ''
            self._spliney_edit_buf = ''
            return True
        if event.key == pygame.K_BACKSPACE:
            self._spliney_edit_buf = self._spliney_edit_buf[:-1]
            return True
        if event.unicode and event.unicode.isprintable():
            self._spliney_edit_buf += event.unicode
            return True
        return False

    def _apply_spliney_panel_field(self, key: str, value: str):
        try:
            if key == 'geo_spline_width':
                width = float(value)
                if width < 0.0:
                    raise ValueError
                self.geo_spline_width = width
            elif key == 'spliney_seed_length':
                seed = float(value)
                if seed <= 0.0:
                    raise ValueError
                self.spliney_seed_length = seed
            elif key == 'spliney_place_rotY':
                self.spliney_place_rotY = float(value) % 360.0
            else:
                return
        except ValueError:
            self._set_status(f"Invalid value for {key}")
            return
        self._set_status(f"{key} = {value}")


    # ------------------------------------------------------------------
    # Items 11-15: Spliney editing, Scenery, Copy/Paste, Trestle
    # ------------------------------------------------------------------

    def pick_spliney_point(self, sx: float, sy: float, radius_px: float = 10.0):
        """Return (spliney_id, point_idx, layer_idx) of nearest spliney ctrl point."""
        if not self.mod_project:
            return None, -1, None
        best_d = radius_px
        best_spl = best_pt = best_li = None
        for li, layer in enumerate(self.mod_project.layers):
            if not layer.visible:
                continue
            for sid, spl in layer.splineys.items():
                if not spl or 'points' not in spl:
                    continue
                for pi, pt in enumerate(spl['points']):
                    if not isinstance(pt, dict):
                        continue
                    pos = pt.get('position', {})
                    snx, sny = self.unity_to_screen(pos.get('x',0), pos.get('z',0))
                    d = ((snx-sx)**2+(sny-sy)**2)**0.5
                    if d < best_d:
                        best_d = d; best_spl = sid; best_pt = pi; best_li = li
        return best_spl, best_pt if best_spl else -1, best_li

    def _commit_spliney_drag(self, spliney_id: str, layer_idx: int,
                              pt_idx: int, new_ux: float, new_uz: float):
        """Move a spliney control point and save."""
        if not self.mod_project:
            return
        layer = self.mod_project.layers[layer_idx]
        spl   = layer.splineys.get(spliney_id)
        if not spl or pt_idx < 0 or pt_idx >= len(spl.get('points',[])):
            return
        old_pt = spl['points'][pt_idx]
        old_rotY = old_pt.get('rotation',{}).get('y',0)
        new_y    = self._sample_terrain_y(new_ux, new_uz) or old_pt['position'].get('y',0)
        width    = old_pt.get('width', None)
        spliney_set_point(layer, spliney_id, pt_idx,
                          new_ux, new_y, new_uz,
                          0, old_rotY, 0, width)
        layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._set_status(f"{spliney_id}[{pt_idx}] → ({new_ux:.1f},{new_y:.1f},{new_uz:.1f})")

    # 13 — Copy/Paste coordinates
    def copy_node_coords(self):
        """Copy selected node position/rotation to clipboard."""
        nid = self.sel_mod_node_id
        if not nid or not self.mod_project:
            self._set_status("No node selected")
            return
        node = self.mod_project.merged_nodes.get(nid, {})
        if node:
            self._coord_clipboard = {
                'x': node.get('x',0), 'y': node.get('y',0), 'z': node.get('z',0),
                'rotY': node.get('rotY',0)
            }
            self._set_status(
                f"Copied coords: ({node['x']:.2f}, {node['y']:.2f}, {node['z']:.2f})")

    def paste_node_height(self):
        """Paste only the Y value from clipboard onto selected node."""
        nid = self.sel_mod_node_id
        if not nid or not self.mod_project:
            self._set_status("No node selected")
            return
        if not self._coord_clipboard:
            self._set_status("Nothing in clipboard")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        self._push_undo(f"paste height {nid}")
        node = dict(self.mod_project.merged_nodes.get(nid, {}))
        if node:
            new_y = self._coord_clipboard['y']
            graph.set_node(nid, node['x'], new_y, node['z'],
                           node.get('rotX',0), node.get('rotY',0),
                           node.get('rotZ',0), node.get('flipSwitchStand',False))

            self.mod_project._rebuild_merge()
            self._mark_measure_cache_dirty()
            graph.save()
            if self.bridge: self.bridge.reload_tracks(str(graph.path))
            self._set_status(f"{nid} Y → {new_y:.3f}")

    def paste_node_all(self):
        """Paste full position from clipboard onto selected node."""
        nid = self.sel_mod_node_id
        if not nid or not self.mod_project:
            self._set_status("No node selected")
            return
        if not self._coord_clipboard:
            self._set_status("Nothing in clipboard")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        self._push_undo(f"paste coords {nid}")
        node = dict(self.mod_project.merged_nodes.get(nid, {}))
        cb   = self._coord_clipboard
        if node:
            graph.set_node(nid, cb['x'], cb['y'], cb['z'],
                           node.get('rotX',0), cb.get('rotY', node.get('rotY',0)),
                           node.get('rotZ',0), node.get('flipSwitchStand',False))

            self.mod_project._rebuild_merge()
            self._mark_measure_cache_dirty()
            graph.save()
            if self.bridge: self.bridge.reload_tracks(str(graph.path))
            self._set_status(f"{nid} → ({cb['x']:.1f},{cb['y']:.1f},{cb['z']:.1f})")

    # 15 — Create trestle from segment
    def create_trestle_from_sel_segment(self):
        """Wrap selected segment in an AutoTrestleBuilder spliney."""
        sid = self.sel_mod_seg_id
        if not sid or not self.mod_project:
            self._set_status("Select a segment first")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        seg = self.mod_project.merged_segments.get(sid, {})
        self._push_undo(f"trestle {sid}")
        prefix = self.mod_project.definition.get('id','TRS').replace('.','_')[:6]
        trs_id = create_trestle_from_segment(
            graph, seg, self.mod_project.merged_nodes, prefix)
        if trs_id:
            graph.save()
            self._set_status(f"Created trestle: {trs_id}")
        else:
            self._mod_undo_stack.pop()
            self._set_status("Trestle failed — segment nodes not found")

    def _fit_selected_trestle_to_track(self):
        """Refit the selected AutoTrestle to its matching track Bezier."""
        if not self.mod_project or not self.sel_spliney_id:
            self._set_status("Select a trestle control point first")
            return
        layer_idx = self.sel_spliney_layer
        if layer_idx is None or not (0 <= layer_idx < len(self.mod_project.layers)):
            self._set_status("The selected trestle layer is unavailable")
            return
        layer = self.mod_project.layers[layer_idx]
        spliney = layer.splineys.get(self.sel_spliney_id)
        if not spliney or 'AutoTrestle' not in str(spliney.get('handler', '')):
            self._set_status("The selected spliney is not an AutoTrestle")
            return
        points = [
            point for point in spliney.get('points', [])
            if isinstance(point, dict) and isinstance(point.get('position'), dict)
        ]
        if len(points) < 2:
            self._set_status("The trestle needs at least two points")
            return

        start = points[0]['position']
        end = points[-1]['position']

        def endpoint_distance(point, node):
            return math.sqrt(
                (float(point.get('x', 0.0)) - float(node.get('x', 0.0))) ** 2
                + (float(point.get('y', 0.0)) - float(node.get('y', 0.0))) ** 2
                + (float(point.get('z', 0.0)) - float(node.get('z', 0.0))) ** 2
            )

        def score_segment(segment):
            node_a = self.mod_project.merged_nodes.get(segment.get('startId', ''))
            node_b = self.mod_project.merged_nodes.get(segment.get('endId', ''))
            if not node_a or not node_b:
                return None
            direct = (
                endpoint_distance(start, node_a),
                endpoint_distance(end, node_b),
            )
            reverse = (
                endpoint_distance(start, node_b),
                endpoint_distance(end, node_a),
            )
            chosen = direct if sum(direct) <= sum(reverse) else reverse
            return max(chosen), sum(chosen)

        candidates = []
        for segment_id, segment in self.mod_project.merged_segments.items():
            if not segment or segment.get('deleted'):
                continue
            score = score_segment(segment)
            if score is not None:
                candidates.append((score[0], score[1], segment_id, segment))
        if not candidates:
            self._set_status("No track segments are available for this trestle")
            return

        candidates.sort(key=lambda item: (item[0], item[1]))
        selected_candidate = next(
            (
                item for item in candidates
                if item[2] == self.sel_mod_seg_id and item[0] <= 25.0
            ),
            None,
        )
        best = selected_candidate or candidates[0]
        max_endpoint_distance, _total_distance, segment_id, segment = best
        if max_endpoint_distance > 25.0:
            self._set_status(
                "No matching track found within 25 m of both trestle ends"
            )
            return

        self._push_undo(f"fit trestle {self.sel_spliney_id} to {segment_id}")
        if not fit_trestle_to_segment(
            layer,
            self.sel_spliney_id,
            segment,
            self.mod_project.merged_nodes,
        ):
            self._mod_undo_stack.pop()
            self._set_status("Could not fit the trestle to that segment")
            return
        layer.save()
        self.mod_project._rebuild_merge()
        fitted = layer.splineys[self.sel_spliney_id].get('points', [])
        self.sel_spliney_pt = min(self.sel_spliney_pt, len(fitted) - 1)
        self._set_status(
            f"{self.sel_spliney_id} fitted to {segment_id} "
            f"with {len(fitted)} Bezier samples"
        )

    # ------------------------------------------------------------------
    # Scenery panel
    # ------------------------------------------------------------------

    def _draw_spliney_props(self, surf, content_top):
        """Draw properties panel for selected spliney control point."""
        if not self.sel_spliney_id or not self.mod_project:
            return
        li = self.sel_spliney_layer
        if li is None or li >= len(self.mod_project.layers):
            return
        layer = self.mod_project.layers[li]
        spl   = layer.splineys.get(self.sel_spliney_id)
        if not spl or self.sel_spliney_pt < 0:
            return
        pts = spl.get('points', [])
        if self.sel_spliney_pt >= len(pts):
            return
        pt = pts[self.sel_spliney_pt]
        if not isinstance(pt, dict):
            return
        pos = pt.get('position', {})
        rot = pt.get('rotation', {})
        width = pt.get('width', None)
        range_state = self._current_spliney_range_state()
        rot_axis = str(getattr(self, '_spl_rot_axis', 'y') or 'y').lower()
        if rot_axis not in ('x', 'y', 'z'):
            rot_axis = 'y'
            self._spl_rot_axis = 'y'

        mx0, my0 = pygame.mouse.get_pos()
        pw2  = 380
        px2  = 10
        py2  = content_top + 10
        cx2  = px2 + 10
        row_h = 17
        is_mod = True

        edit_key = getattr(self, '_spl_edit_key', '')
        edit_buf = getattr(self, '_spl_edit_buf', '')
        text_w = pw2 - 20
        header_text = f"Spliney Point: {self.sel_spliney_id}[{self.sel_spliney_pt}]"
        layer_text = f"Layer: {layer.label}  Handler: {spl.get('handler','').split('.')[-1]}"
        is_trestle = 'AutoTrestle' in str(spl.get('handler', ''))
        tools_text = (
            "Trestle tools. Fit Trestle to Track finds the matching rail segment "
            "and resamples its exact 3D Bezier, including grade and pitch."
            if is_trestle else
            "Road/river point tools. Zoom in and click a control dot to edit another point. "
            "Use Grade % with Smooth Grade or Apply Grade to reshape elevation, or Auto Pitch to tilt along the span."
        )
        if range_state.get('ready'):
            range_text = (
                f"Spliney range: {self.sel_spliney_id}[{range_state['start']}..{range_state['end']}]"
                "  Set Width, then Fill Width, or use Grade tools."
            )
            range_col = (255, 210, 110)
        elif range_state.get('anchor') is not None:
            range_text = (
                f"Spliney range start: {self.sel_spliney_id}[{range_state['anchor']}]"
                "  shift-click another point or use Prev/Next."
            )
            range_col = (255, 210, 110)
        else:
            range_text = (
                "Spliney range: Mark Start, then shift-click another point to fill widths or grade between them. "
                "Without a marked range, grade tools affect the whole spliney."
            )
            range_col = (120, 140, 160)

        field_rows = 7 + (1 if width is not None else 0)
        nudges = [(90,0.001),(45,0.01),(30,0.05),(15,0.1),(10,1),(5,5)]
        action_rows = 5 if is_trestle else 4

        def wrap_lines_local(font_obj, text, max_w):
            text = str(text)
            words = text.split()
            if not words:
                return [""]
            lines = []
            line = ""
            for word in words:
                test = (line + " " + word).strip()
                if line and font_obj.get_rect(test).width > max_w:
                    lines.append(line)
                    line = word
                else:
                    line = test
            if line:
                lines.append(line)
            return lines or [text]

        header_lines = wrap_lines_local(self.font_big, header_text, text_w)
        layer_lines = wrap_lines_local(self.font, layer_text, text_w)
        tools_lines = wrap_lines_local(self.font, tools_text, text_w)
        range_lines = wrap_lines_local(self.font, range_text, text_w)
        ph2 = (
            8 +
            len(header_lines) * 18 +
            (len(layer_lines) + len(tools_lines) + len(range_lines)) * row_h +
            field_rows * row_h +
            14 + row_h + 4 + len(nudges) * row_h + 4 +
            action_rows * (row_h + 6) +
            8
        )
        bg  = pygame.Surface((pw2, ph2), pygame.SRCALPHA)
        bg.fill((8,11,18,230))
        surf.blit(bg, (px2, py2))
        pygame.draw.rect(surf,(40,60,80),(px2,py2,pw2,ph2),1,border_radius=4)
        self._spl_panel_rect = pygame.Rect(px2, py2, pw2, ph2)

        self._spl_prop_rects = []
        cy2 = py2 + 8

        def spl_field(label, key, value, col):
            nonlocal cy2
            surf.blit(surf, (0,0), (0,0,0,0))  # no-op
            self.font.render_to(surf,(cx2,cy2),label+":",(100,120,140))
            fx  = cx2+80; fw = pw2-92
            active = edit_key == key
            disp   = (edit_buf+"_") if active else str(value)
            fr = pygame.Rect(fx,cy2-2,fw,row_h)
            pygame.draw.rect(surf,(30,50,70) if active else (18,26,38),fr,border_radius=2)
            pygame.draw.rect(surf,(0,200,255) if active else (40,60,80),fr,1,border_radius=2)
            self.font.render_to(surf,(fx+4,cy2),disp,col)
            self._spl_prop_rects.append((fr,key,value,True))
            cy2 += row_h

        # Header
        for line in header_lines:
            self.font_big.render_to(surf, (cx2, cy2), line, (0,200,255))
            cy2 += 18
        for line in layer_lines:
            self.font.render_to(surf, (cx2, cy2), line, layer.color)
            cy2 += row_h
        for line in tools_lines:
            self.font.render_to(surf, (cx2, cy2), line, (120, 140, 160))
            cy2 += row_h
        for line in range_lines:
            self.font.render_to(surf, (cx2, cy2), line, range_col)
            cy2 += row_h

        spl_field("X",    "spl_x",    f"{pos.get('x',0):.3f}",  (180,220,180))
        spl_field("Y",    "spl_y",    f"{pos.get('y',0):.3f}",  (180,220,180))
        spl_field("Z",    "spl_z",    f"{pos.get('z',0):.3f}",  (180,220,180))
        spl_field("RotX", "spl_rotX", f"{rot.get('x',0):.3f}",  (200,180,220))
        spl_field("RotY", "spl_rotY", f"{rot.get('y',0):.3f}",  (200,180,220))
        spl_field("RotZ", "spl_rotZ", f"{rot.get('z',0):.3f}",  (200,180,220))
        if width is not None:
            spl_field("Width","spl_width",f"{width:.2f}",        (220,200,160))
        spl_field("Grade%", "spl_grade_pct", f"{float(getattr(self, 'spliney_grade_pct', 0.0)):+.3f}", (220, 210, 150))

        # Rotation axis picker + nudge row
        axis_x = cx2
        self.font.render_to(surf, (axis_x, cy2), "Rotate:", (100,120,140))
        axis_x += 52
        for axis, axis_col in [('x', (220,140,140)), ('y', (120,200,255)), ('z', (180,150,220))]:
            label = axis.upper()
            bw_axis = self.font.get_rect(label).width + 12
            axis_rect = pygame.Rect(axis_x, cy2 - 1, bw_axis, row_h)
            active_axis = rot_axis == axis
            hov_axis = axis_rect.collidepoint(mx0, my0)
            fill_axis = axis_col if active_axis else ((30,50,60) if hov_axis else (18,26,38))
            border_axis = axis_col if active_axis or hov_axis else (60, 68, 80)
            txt_axis = (220,230,240) if active_axis else (150,165,182)
            pygame.draw.rect(surf, fill_axis, axis_rect, border_radius=3)
            pygame.draw.rect(surf, border_axis, axis_rect, 1, border_radius=3)
            self.font.render_to(surf, (axis_rect.x + 6, cy2), label, txt_axis)
            self._spl_prop_rects.append((axis_rect, f"spl_rot_axis:{axis}", None, True))
            axis_x = axis_rect.right + 6
        cy2 += row_h + 4

        col_lx = cx2; col_rx = cx2 + pw2//2 - 10
        self.font.render_to(surf,(col_lx,cy2),"RotY  ←",(100,120,140))
        self.font.render_to(surf,(col_rx,cy2),"→",(100,120,140))
        pygame.draw.rect(surf, (8,11,18,230), (col_lx - 2, cy2 - 1, pw2 - 20, 14))
        self.font.render_to(surf, (col_lx, cy2), f"Rot{rot_axis.upper()}  -", (100,120,140))
        self.font.render_to(surf, (col_rx, cy2), "+", (100,120,140))
        cy2 += 14
        for big, small in nudges:
            for sign, bx3, val in [(-1,col_lx,big),(-1,col_lx+36,small),
                                    (1,col_rx,big),(1,col_rx+36,small)]:
                lbl3 = f"{'-' if sign<0 else '+'}{val}"
                key3 = f"splrot_{rot_axis}_{'m' if sign<0 else 'p'}{str(val).replace('.','d')}"
                bw3  = self.font.get_rect(lbl3).width+6
                r3   = pygame.Rect(bx3,cy2-1,bw3,row_h-1)
                hov3 = r3.collidepoint(mx0,my0)
                pygame.draw.rect(surf,(30,60,100) if hov3 else (20,35,55),r3,border_radius=2)
                pygame.draw.rect(surf,(60,120,200),r3,1,border_radius=2)
                self.font.render_to(surf,(bx3+3,cy2),lbl3,(180,210,255))
                self._spl_prop_rects.append((r3,key3,None,True))
            cy2 += row_h
        cy2 += 4

        def draw_action_row(buttons):
            nonlocal cy2
            bx2 = cx2
            for lbl3, act3, col3, enabled in buttons:
                bw3 = self.font.get_rect(lbl3).width + 12
                r3 = pygame.Rect(bx2, cy2, bw3, row_h + 2)
                hov3 = enabled and r3.collidepoint(mx0, my0)
                fill = col3 if hov3 else tuple(max(20, v // 2) for v in col3)
                if not enabled:
                    fill = (28, 32, 38)
                border = col3 if enabled else (60, 68, 80)
                txt_col = (220,230,240) if enabled else (90, 98, 108)
                pygame.draw.rect(surf, fill, r3, border_radius=3)
                pygame.draw.rect(surf, border, r3, 1, border_radius=3)
                self.font.render_to(surf, (bx2 + 6, cy2 + 2), lbl3, txt_col)
                self._spl_prop_rects.append((r3, act3, None, enabled))
                bx2 += bw3 + 6
            cy2 += row_h + 6

        draw_action_row([
            ("Prev Pt", "spl_prev", (60,120,180), self.sel_spliney_pt > 0),
            ("Next Pt", "spl_next", (60,120,180), self.sel_spliney_pt + 1 < len(pts)),
            ("Sample Y", "spl_sampleY", (0,140,120), True),
        ])
        draw_action_row([
            ("Auto Rot", "spl_auto_rot", (140,120,220), len(pts) >= 2),
            ("Ins Before", "spl_ins_before", (180,120,60), self.sel_spliney_pt > 0),
            ("Ins After", "spl_ins_after", (180,120,60), self.sel_spliney_pt + 1 < len(pts)),
            ("Delete Spliney" if len(pts) <= 2 else "Delete Pt", "spl_del_pt", (160,80,80), len(pts) >= 2),
        ])
        if is_trestle:
            draw_action_row([
                (
                    "Fit Trestle to Track",
                    "spl_fit_trestle",
                    (160, 115, 45),
                    len(pts) >= 2,
                ),
            ])
        draw_action_row([
            (
                "Clear Range" if range_state.get('ready')
                else ("Clear Start" if range_state.get('anchor') is not None else "Mark Start"),
                "spl_range_anchor",
                (220, 150, 60),
                True,
            ),
            ("Fill Width", "spl_fill_width_range", (0, 150, 110), bool(range_state.get('ready'))),
        ])
        grade_enabled = len(pts) >= 2 and (range_state.get('ready') or range_state.get('anchor') is None)
        draw_action_row([
            ("Smooth Grade", "spl_smooth_grade", (0, 135, 160), grade_enabled),
            (f"Apply {self._current_spliney_grade_pct():+.2f}%", "spl_apply_grade", (180, 120, 50), grade_enabled),
            ("Auto Pitch", "spl_auto_pitch", (90, 130, 200), grade_enabled),
        ])

    def _handle_spliney_props_click(self, mx, my, content_top) -> bool:
        """Handle clicks in the spliney properties panel."""
        # Check if click is inside panel bounds
        if not self.sel_spliney_id: return False
        panel_rect = getattr(self, '_spl_panel_rect', None)
        if panel_rect is None or not panel_rect.collidepoint(mx, my):
            return False
        for entry in getattr(self,'_spl_prop_rects',[]):
            if len(entry) == 4:
                r, key, val, enabled = entry
            else:
                r, key, val = entry
                enabled = True
            if not enabled:
                continue
            if not r.collidepoint(mx,my): continue
            if key == 'spl_prev':
                self.sel_spliney_pt = max(0, self.sel_spliney_pt-1)
                self._spl_edit_key = ''
            elif key == 'spl_next':
                li = self.sel_spliney_layer
                if li is not None:
                    spl = self.mod_project.layers[li].splineys.get(self.sel_spliney_id) or {}
                    n_pts = len(spl.get('points',[]))
                    self.sel_spliney_pt = min(n_pts-1, self.sel_spliney_pt+1)
                self._spl_edit_key = ''
            elif key == 'spl_sampleY':
                self._spl_sample_terrain()
            elif key == 'spl_auto_rot':
                self._spl_auto_rotY()
            elif key == 'spl_ins_before':
                self._spl_insert_point(after=False)
            elif key == 'spl_ins_after':
                self._spl_insert_point(after=True)
            elif key == 'spl_del_pt':
                self._spl_delete_point()
            elif key == 'spl_fit_trestle':
                self._fit_selected_trestle_to_track()
            elif key == 'spl_range_anchor':
                self._toggle_spliney_range_anchor()
            elif key == 'spl_fill_width_range':
                self._spl_fill_width_range()
            elif key == 'spl_smooth_grade':
                self._spl_smooth_grade_range()
            elif key == 'spl_apply_grade':
                self._spl_apply_grade_range()
            elif key == 'spl_auto_pitch':
                self._spl_auto_pitch_range()
            elif key.startswith('spl_rot_axis:'):
                self._spl_rot_axis = key.split(':', 1)[1]
            elif key.startswith('splrot_'):
                self._spl_nudge_rotation(key)
            elif key.startswith('spl_'):
                # Activate field for editing
                if key in ('spl_rotX', 'spl_rotY', 'spl_rotZ'):
                    self._spl_rot_axis = key[-1].lower()
                self._spl_edit_key = key
                self._spl_edit_buf = str(val).strip()
            return True
        return True

    def _handle_spliney_props_keydown(self, event) -> bool:
        if not getattr(self,'_spl_edit_key',''):
            return False
        key = self._spl_edit_key
        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self._commit_spl_field(key, self._spl_edit_buf)
            self._spl_edit_key = ''
            self._spl_edit_buf = ''
            return True
        elif event.key == pygame.K_ESCAPE:
            self._spl_edit_key = ''
            self._spl_edit_buf = ''
            return True
        elif event.key == pygame.K_BACKSPACE:
            self._spl_edit_buf = self._spl_edit_buf[:-1]
            return True
        elif event.unicode and event.unicode in '0123456789.-':
            self._spl_edit_buf += event.unicode
            return True
        return False

    def _commit_spl_field(self, key: str, value: str):
        """Apply edited spliney field."""
        if key == 'spl_grade_pct':
            try:
                self.spliney_grade_pct = float(value)
            except ValueError:
                return
            self._set_status(f"Spliney grade target = {self.spliney_grade_pct:+.2f}%")
            return
        li = self.sel_spliney_layer
        if li is None or not self.mod_project: return
        layer = self.mod_project.layers[li]
        spl   = layer.splineys.get(self.sel_spliney_id)
        if not spl: return
        pts = list(spl.get('points',[]))
        pi  = self.sel_spliney_pt
        if pi >= len(pts): return
        pt  = dict(pts[pi])
        pos = dict(pt.get('position',{}))
        rot = dict(pt.get('rotation',{}))
        try:
            v = float(value)
            if   key=='spl_x':     pos['x'] = v
            elif key=='spl_y':     pos['y'] = v
            elif key=='spl_z':     pos['z'] = v
            elif key=='spl_rotX':  rot['x'] = v
            elif key=='spl_rotY':  rot['y'] = v % 360
            elif key=='spl_rotZ':  rot['z'] = v
            elif key=='spl_width': pt['width'] = v
        except ValueError:
            return
        pt['position'] = pos; pt['rotation'] = rot
        pts[pi] = pt; spl['points'] = pts
        if 'splineys' in layer._raw:
            layer._raw['splineys'][self.sel_spliney_id] = spl
        layer.dirty = True; layer.save()
        if key in ('spl_rotX', 'spl_rotY', 'spl_rotZ'):
            self._spl_rot_axis = key[-1].lower()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._set_status(f"{self.sel_spliney_id}[{pi}]  {key}={value}")

    def _spl_nudge_rotation(self, action: str):
        li = self.sel_spliney_layer
        if li is None or not self.mod_project: return
        layer = self.mod_project.layers[li]
        spl   = layer.splineys.get(self.sel_spliney_id)
        if not spl: return
        pts = list(spl.get('points',[]))
        pi  = self.sel_spliney_pt
        if pi >= len(pts): return
        pt  = dict(pts[pi])
        rot = dict(pt.get('rotation',{}))
        raw = action[len('splrot_'):]
        try:
            axis, signed = raw.split('_', 1)
        except ValueError:
            return
        axis = axis.lower()
        if axis not in ('x', 'y', 'z'):
            return
        sign  = -1 if signed[0]=='m' else 1
        try:
            delta = sign * float(signed[1:].replace('d','.'))
        except Exception:
            return
        cur_val = float(rot.get(axis, 0.0) or 0.0)
        new_val = cur_val + delta
        rot[axis] = new_val % 360 if axis == 'y' else new_val
        self._spl_rot_axis = axis
        pt['rotation'] = rot; pts[pi] = pt; spl['points'] = pts
        if 'splineys' in layer._raw:
            layer._raw['splineys'][self.sel_spliney_id] = spl
        layer.dirty = True; layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._set_status(f"{self.sel_spliney_id}[{pi}]  rotY={rot['y']:.2f}°")

        self._set_status(f"{self.sel_spliney_id}[{pi}]  rot{axis.upper()}={rot[axis]:.2f}")

    def _spl_sample_terrain(self):
        li = self.sel_spliney_layer
        if li is None or not self.mod_project: return
        layer = self.mod_project.layers[li]
        spl   = layer.splineys.get(self.sel_spliney_id)
        if not spl: return
        pts = list(spl.get('points',[]))
        pi  = self.sel_spliney_pt
        if pi >= len(pts): return
        pt  = dict(pts[pi]); pos = dict(pt.get('position',{}))
        new_y = self._sample_terrain_y(pos.get('x',0), pos.get('z',0))
        if new_y:
            pos['y'] = new_y; pt['position'] = pos
            pts[pi] = pt; spl['points'] = pts
            self._solve_spliney_rotation_span(pts, pi, pi)
            if 'splineys' in layer._raw:
                layer._raw['splineys'][self.sel_spliney_id] = spl
            layer.dirty = True; layer.save()
            if self.bridge:
                self.bridge.reload_tracks(str(layer.path))
            self._set_status(f"{self.sel_spliney_id}[{pi}]  Y sampled → {new_y:.2f}")

    def _spl_auto_rotY(self):
        li = self.sel_spliney_layer
        if li is None or not self.mod_project:
            return
        layer = self.mod_project.layers[li]
        spl = layer.splineys.get(self.sel_spliney_id)
        if not spl:
            return
        pts = list(spl.get('points', []))
        pi = self.sel_spliney_pt
        if pi < 0 or pi >= len(pts) or len(pts) < 2:
            return

        self._solve_spliney_point_rotation(pts, pi)
        rot_y = float(dict(pts[pi].get('rotation', {}) or {}).get('y', 0.0))
        spl['points'] = pts
        if 'splineys' in layer._raw:
            layer._raw['splineys'][self.sel_spliney_id] = spl
        layer.dirty = True
        layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._set_status(f"{self.sel_spliney_id}[{pi}]  auto rotY={rot_y:.2f}°")

    def _spl_insert_point(self, after: bool = True):
        li = self.sel_spliney_layer
        if li is None or not self.mod_project:
            return
        layer = self.mod_project.layers[li]
        spl = layer.splineys.get(self.sel_spliney_id)
        if not spl:
            return
        pts = list(spl.get('points', []))
        pi = self.sel_spliney_pt
        if pi < 0 or pi >= len(pts):
            return
        if after:
            if pi + 1 >= len(pts):
                self._set_status("Insert After needs a next point")
                return
            a = pts[pi]
            b = pts[pi + 1]
        else:
            if pi <= 0:
                self._set_status("Insert Before needs a previous point")
                return
            a = pts[pi - 1]
            b = pts[pi]

        pos_a = dict(a.get('position', {}))
        pos_b = dict(b.get('position', {}))
        x = (float(pos_a.get('x', 0.0)) + float(pos_b.get('x', 0.0))) / 2.0
        z = (float(pos_a.get('z', 0.0)) + float(pos_b.get('z', 0.0))) / 2.0
        y = self._sample_terrain_y(x, z)
        if not y:
            y = (float(pos_a.get('y', 0.0)) + float(pos_b.get('y', 0.0))) / 2.0
        rot_y = self._spliney_heading_deg(pos_a, pos_b)
        rot_x = (float(a.get('rotation', {}).get('x', 0.0)) + float(b.get('rotation', {}).get('x', 0.0))) / 2.0
        rot_z = (float(a.get('rotation', {}).get('z', 0.0)) + float(b.get('rotation', {}).get('z', 0.0))) / 2.0
        width_a = a.get('width')
        width_b = b.get('width')
        if width_a is not None and width_b is not None:
            width = (float(width_a) + float(width_b)) / 2.0
        else:
            width = width_a if width_a is not None else width_b

        new_point = {
            'position': {'x': x, 'y': float(y), 'z': z},
            'rotation': {'x': rot_x, 'y': rot_y, 'z': rot_z},
        }
        if width is not None:
            new_point['width'] = float(width)

        new_idx = spliney_insert_point(layer, self.sel_spliney_id, pi, new_point, after=after)
        if new_idx < 0:
            self._set_status("Insert point failed")
            return
        layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._clear_spliney_range_selection()
        self.sel_spliney_pt = new_idx
        self._set_status(f"{self.sel_spliney_id}[{new_idx}] inserted")

    def _spl_delete_point(self):
        li = self.sel_spliney_layer
        if li is None or not self.mod_project:
            return
        layer = self.mod_project.layers[li]
        spl = layer.splineys.get(self.sel_spliney_id)
        pts = spl.get('points', []) if spl else []
        pi = self.sel_spliney_pt
        if len(pts) <= 2:
            self._delete_selected_flowy_spliney()
            return
        if not spliney_delete_point(layer, self.sel_spliney_id, pi):
            self._set_status("Delete point failed")
            return
        layer.save()
        if self.bridge:
            self.bridge.reload_tracks(str(layer.path))
        self._clear_spliney_range_selection()
        self.sel_spliney_pt = max(0, min(pi, len(pts) - 2))
        self._set_status(f"{self.sel_spliney_id}[{pi}] deleted")

    def _draw_scenery_panel(self, surf, content_top):
        """Scenery / building placement panel."""
        if not self.scenery_panel or not self.mod_project:
            return
        w, h  = surf.get_size()
        pw    = min(w-40, 860)
        ph    = min(h - content_top - STATUS_H - 20, 560)
        px    = (w-pw)//2
        py    = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h-content_top-STATUS_H), pygame.SRCALPHA)
        overlay.fill((0,0,0,200))
        surf.blit(overlay, (0, content_top))
        pygame.draw.rect(surf, (16,21,30), (px,py,pw,ph), border_radius=8)
        pygame.draw.rect(surf, (40,60,80), (px,py,pw,ph), 1, border_radius=8)

        xbtn = pygame.Rect(px+pw-30, py+8, 22, 22)
        pygame.draw.rect(surf, (180,60,60) if xbtn.collidepoint(mx0,my0) else (80,40,40),
                         xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px+pw-24,py+11), "✕", (220,200,200))

        cx = px+16; cy = py+14
        self.font_big.render_to(surf, (cx, cy), "Scenery / Building Placement", (0,212,255))
        cy += 22

        self._scenery_rects = []

        # Collect the final merged view. A null in the writable mixinto must
        # hide scenery from an earlier layer instead of leaving it in the list.
        all_sc = {}
        for sid2, sv in self.mod_project.merged_scenery.items():
            source_layer_idx = 0
            for li in range(len(self.mod_project.layers) - 1, -1, -1):
                layer = self.mod_project.layers[li]
                if layer.visible and isinstance(layer.scenery.get(sid2), dict):
                    source_layer_idx = li
                    break
            all_sc[sid2] = (sv, source_layer_idx)

        self.font.render_to(surf, (cx, cy),
            f"{len(all_sc)} objects placed", (140,160,180))
        cy += 18

        # Top row: model input + place button
        self.font.render_to(surf, (cx, cy), "Model ID:", (100,120,140))
        mod_r = pygame.Rect(cx+68, cy-2, 240, 16)
        active_m = getattr(self,'_scenery_edit_model', False)
        pygame.draw.rect(surf, (30,50,70) if active_m else (18,26,38), mod_r, border_radius=2)
        pygame.draw.rect(surf, (0,200,255) if active_m else (40,60,80), mod_r, 1, border_radius=2)
        m_disp = (getattr(self,'_scenery_model_buf', self.scenery_place_model) +
                  ("_" if active_m else ""))
        self.font.render_to(surf, (cx+71, cy), m_disp or "<click to type>",
                            (180,220,180) if self.scenery_place_model else (80,100,80))
        self._scenery_rects.append((mod_r, 'model_edit'))

        # RotY nudge
        bx3 = cx + 320
        self.font.render_to(surf, (bx3, cy), "RotY:", (100,120,140))
        bx3 += 36
        for lbl3, act3 in [("-90","srotY_m90"),("-45","srotY_m45"),
                            ("+45","srotY_p45"),("+90","srotY_p90")]:
            bw3 = self.font.get_rect(lbl3).width + 6
            r3  = pygame.Rect(bx3, cy-1, bw3, 16)
            hov3 = r3.collidepoint(mx0, my0)
            pygame.draw.rect(surf,(30,60,100) if hov3 else (18,35,55),r3,border_radius=2)
            pygame.draw.rect(surf,(60,120,200),r3,1,border_radius=2)
            self.font.render_to(surf,(bx3+3,cy),lbl3,(180,210,255))
            self._scenery_rects.append((r3, act3))
            bx3 += bw3 + 3
        self.font.render_to(surf,(bx3+4,cy),f"{self.scenery_place_rotY:.0f}°",(200,180,140))
        cy += 20

        # Uniform scale controls
        self.font.render_to(surf, (cx, cy), "Scale:", (100,120,140))
        bx_scale = cx + 44
        for lbl_scale, act_scale in [
                ("-0.1", "sscale_m"),
                ("Reset", "sscale_reset"),
                ("+0.1", "sscale_p")]:
            bw_scale = self.font.get_rect(lbl_scale).width + 8
            r_scale = pygame.Rect(bx_scale, cy-1, bw_scale, 16)
            hov_scale = r_scale.collidepoint(mx0, my0)
            pygame.draw.rect(
                surf,
                (60, 50, 90) if hov_scale else (30, 25, 45),
                r_scale,
                border_radius=2,
            )
            pygame.draw.rect(surf, (130, 100, 190), r_scale, 1, border_radius=2)
            self.font.render_to(
                surf, (bx_scale+4, cy), lbl_scale, (220, 190, 255)
            )
            self._scenery_rects.append((r_scale, act_scale))
            bx_scale += bw_scale + 4
        self.font.render_to(
            surf,
            (bx_scale+4, cy),
            f"{self.scenery_place_scale:.2f}x",
            (220, 190, 255),
        )
        cy += 20

        # Place button + place mode status
        place_r = pygame.Rect(cx, cy, 140, 22)
        pm      = self.scenery_place_mode
        pygame.draw.rect(surf, (0,160,80) if pm else (0,80,40), place_r, border_radius=4)
        pygame.draw.rect(surf, (0,220,100), place_r, 1, border_radius=4)
        self.font_big.render_to(surf,(cx+6,cy+4),
            "● Placing..." if pm else "Place on Map", (220,255,220))
        self._scenery_rects.append((place_r,'place_toggle'))

        # Known models quick-pick
        KNOWN = ['freight-house-general','mcgee-lumberco','paperboard-finishing-house',
                 'roadmaster-office','store-5','brick-substation-medium',
                 'branch_line_engine_house_1_track','kvrr_station_7','CLB_Shed02']
        bx4 = cx + 154
        for mdl in KNOWN:
            short = mdl[:10]
            bw4   = self.font.get_rect(short).width + 8
            if bx4 + bw4 > px + pw - 16:
                break
            r4    = pygame.Rect(bx4, cy+1, bw4, 20)
            sel4  = self.scenery_place_model == mdl
            hov4  = r4.collidepoint(mx0,my0)
            pygame.draw.rect(surf,(40,80,60) if sel4 else (20,35,25) if not hov4 else (30,55,40),
                             r4, border_radius=2)
            if sel4: pygame.draw.rect(surf,(0,180,80),r4,1,border_radius=2)
            self.font.render_to(surf,(bx4+4,cy+4),short,
                (200,255,200) if sel4 else (100,140,100))
            self._scenery_rects.append((r4, f'model_pick:{mdl}'))
            bx4 += bw4 + 4
        cy += 28

        # Scenery list + detail
        list_w  = pw//3
        det_x   = px + list_w + 8
        det_w   = pw - list_w - 24
        row_h   = 16

        pygame.draw.line(surf,(40,60,80),(cx,cy),(cx+pw-32,cy))
        cy += 4
        self.font.render_to(surf,(cx,cy),"PLACED OBJECTS",(100,120,140))
        self.font.render_to(surf,(det_x,cy),"SELECTED",(100,120,140))
        cy += 14
        max_rows = (py+ph-cy-20)//row_h

        for i,(sid2,(sv,li)) in enumerate(list(all_sc.items())[:max_rows]):
            ry     = cy + i*row_h
            is_sel = sid2 == self.sel_scenery_id
            r_sc   = pygame.Rect(cx, ry, list_w-8, row_h-1)
            layer  = self.mod_project.layers[li]
            if is_sel:
                pygame.draw.rect(surf,(30,50,80),r_sc,border_radius=2)
            elif r_sc.collidepoint(mx0,my0):
                pygame.draw.rect(surf,(20,30,50),r_sc,border_radius=2)
            col_sc = (0,200,255) if is_sel else layer.color
            self.font.render_to(surf,(cx+4,ry+2),
                f"{sv.get('modelIdentifier','?')[:22]}", col_sc)
            self._scenery_rects.append((r_sc,f'sc_sel:{sid2}'))

        # Detail for selected scenery
        if self.sel_scenery_id and self.sel_scenery_id in all_sc:
            sv2, li2 = all_sc[self.sel_scenery_id]
            dy = cy
            pos2 = sv2.get('position',{})
            rot2 = sv2.get('rotation',{})
            sc2  = sv2.get('scale',{})
            for lbl3, val3 in [
                    ("ID",    self.sel_scenery_id),
                    ("Model", sv2.get('modelIdentifier','')),
                    ("X",     f"{pos2.get('x',0):.2f}"),
                    ("Y",     f"{pos2.get('y',0):.2f}"),
                    ("Z",     f"{pos2.get('z',0):.2f}"),
                    ("RotY",  f"{rot2.get('y',0):.2f}"),
                    ("Scale", f"{sc2.get('x',1):.2f}"),
            ]:
                self.font.render_to(surf,(det_x,dy),lbl3+":",(100,120,140))
                self.font.render_to(surf,(det_x+60,dy),str(val3),(180,210,180))
                dy += row_h

            dy += 4
            del_r = pygame.Rect(det_x, dy, 70, 18)
            hd3   = del_r.collidepoint(mx0,my0)
            pygame.draw.rect(surf,(180,60,60) if hd3 else (80,30,30),del_r,border_radius=3)
            pygame.draw.rect(surf,(220,80,80),del_r,1,border_radius=3)
            self.font.render_to(surf,(det_x+5,dy+2),"Del Object",(220,200,200))
            self._scenery_rects.append((del_r,'sc_del'))

            goto_r = pygame.Rect(det_x+80, dy, 60, 18)
            hg     = goto_r.collidepoint(mx0,my0)
            pygame.draw.rect(surf,(0,100,140) if hg else (0,50,70),goto_r,border_radius=3)
            pygame.draw.rect(surf,(0,160,200),goto_r,1,border_radius=3)
            self.font.render_to(surf,(det_x+86,dy+2),"Go To",(180,220,240))
            self._scenery_rects.append((goto_r,'sc_goto'))

    def _handle_scenery_click(self, mx, my, content_top):
        w, h = self.screen.get_size()
        pw = min(w-40,860); ph = min(h-content_top-STATUS_H-20,560)
        px = (w-pw)//2;     py = content_top+10
        if not pygame.Rect(px,py,pw,ph).collidepoint(mx,my):
            return False
        if pygame.Rect(px+pw-30,py+8,22,22).collidepoint(mx,my):
            self.scenery_panel = False; return True
        for r, act in getattr(self,'_scenery_rects',[]):
            if not r.collidepoint(mx,my): continue
            if act == 'model_edit':
                self._scenery_edit_model = True
                self._scenery_model_buf  = self.scenery_place_model
            elif act.startswith('model_pick:'):
                self.scenery_place_model = act[len('model_pick:'):]
                self._scenery_edit_model = False
            elif act == 'place_toggle':
                self.scenery_place_mode = not self.scenery_place_mode
                if self.scenery_place_mode and not self.scenery_place_model:
                    self._set_status("Set a Model ID first")
                    self.scenery_place_mode = False
            elif act.startswith('srotY_'):
                delta = {'srotY_m90':-90,'srotY_m45':-45,
                         'srotY_p45': 45,'srotY_p90': 90}.get(act,0)
                self.scenery_place_rotY = (self.scenery_place_rotY+delta)%360
            elif act == 'sscale_m':
                self.scenery_place_scale = max(
                    0.1, round(self.scenery_place_scale - 0.1, 2)
                )
            elif act == 'sscale_p':
                self.scenery_place_scale = min(
                    10.0, round(self.scenery_place_scale + 0.1, 2)
                )
            elif act == 'sscale_reset':
                self.scenery_place_scale = 1.0
            elif act.startswith('sc_sel:'):
                self.sel_scenery_id = act[7:]
                self.sel_scenery_layer = None
                for li in range(len(self.mod_project.layers) - 1, -1, -1):
                    layer = self.mod_project.layers[li]
                    if (
                        layer.visible
                        and isinstance(layer.scenery.get(self.sel_scenery_id), dict)
                    ):
                        self.sel_scenery_layer = li
                        break
            elif act == 'sc_del':
                self._delete_selected_scenery()
            elif act == 'sc_goto':
                self._goto_selected_scenery()
            return True
        return True

    def _handle_scenery_keydown(self, event) -> bool:
        if not getattr(self,'_scenery_edit_model', False):
            return False
        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self.scenery_place_model = getattr(self,'_scenery_model_buf','')
            self._scenery_edit_model = False
            return True
        elif event.key == pygame.K_ESCAPE:
            self._scenery_edit_model = False
            return True
        elif event.key == pygame.K_BACKSPACE:
            self._scenery_model_buf = getattr(self,'_scenery_model_buf','')[:-1]
            return True
        elif event.unicode:
            self._scenery_model_buf = getattr(self,'_scenery_model_buf','') + event.unicode
            return True
        return False

    def _place_scenery_at(self, sx: float, sy: float):
        """Place a scenery object at screen position."""
        if not self.mod_project or not self.scenery_place_model:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        ux, uz = self.screen_to_unity(sx, sy)
        uy = self._sample_terrain_y(ux, uz)
        existing_ids = set(self.mod_project.merged_scenery) | set(graph.scenery)
        sid2 = next_scenery_id(graph, existing_ids)
        self._push_undo(f"place scenery {sid2}")
        scenery_set(
            graph,
            sid2,
            self.scenery_place_model,
            ux,
            uy,
            uz,
            rotY=self.scenery_place_rotY,
            scale_x=self.scenery_place_scale,
            scale_y=self.scenery_place_scale,
            scale_z=self.scenery_place_scale,
        )
        self._commit_mod_layer_edit(graph)
        self.sel_scenery_id   = sid2
        self.sel_scenery_layer= next(i for i,l in enumerate(self.mod_project.layers)
                                     if l is graph)
        self._set_status(
            f"Placed {self.scenery_place_model} as {sid2}  "
            f"Y {self.scenery_place_rotY:.0f} deg  scale {self.scenery_place_scale:.2f}"
        )

    def _delete_selected_scenery(self):
        if not self.sel_scenery_id or not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if graph is None:
            self._set_status("No writable game-graph layer for scenery deletion")
            return
        scenery_id = self.sel_scenery_id
        self._push_undo(f"delete scenery {scenery_id}")
        scenery_delete(graph, scenery_id)
        self._commit_mod_layer_edit(graph)
        self._set_status(f"Deleted {scenery_id}")
        self.sel_scenery_id = None
        self.sel_scenery_layer = None

    def _goto_selected_scenery(self):
        if not self.sel_scenery_id or not self.mod_project:
            return
        sv = self.mod_project.merged_scenery.get(self.sel_scenery_id)
        if isinstance(sv, dict):
            pos = sv.get('position',{})
            sx2, sy2 = self.unity_to_screen(pos.get('x',0), pos.get('z',0))
            w2, h2   = self.screen.get_size()
            self.pan_x += w2//2 - sx2
            self.pan_y += h2//2 - sy2
            self.scenery_panel = False
            self._set_status(f"Panned to {self.sel_scenery_id}")


    # ==================================================================
    # 16. Move Group
    # ==================================================================

    def _draw_group_rubber_band(self):
        """Draw rubber-band selection box during Ctrl+drag."""
        if not self.group_box_start or not self.group_box_end:
            return
        x0,y0 = self.group_box_start; x1,y1 = self.group_box_end
        r = pygame.Rect(min(x0,x1), min(y0,y1), abs(x1-x0), abs(y1-y0))
        s = pygame.Surface((r.width, r.height), pygame.SRCALPHA)
        s.fill((0,150,255,30))
        self.screen.blit(s, (r.x, r.y))
        pygame.draw.rect(self.screen, (0,180,255), r, 1)
        # Draw selected node highlights
        if self.group_sel_ids and self.mod_project:
            for nid in self.group_sel_ids:
                n = self.mod_project.merged_nodes.get(nid)
                if n:
                    sx2,sy2 = self.unity_to_screen(n['x'],n['z'])
                    pygame.draw.circle(self.screen,(0,180,255),(sx2,sy2),7,2)

    def _draw_group_panel(self, surf, content_top):
        """Move Group panel — translate and rotate selected nodes."""
        if not self.group_panel or not self.mod_project:
            return
        w,h = surf.get_size()
        pw=380; ph=260
        px=10; py=content_top+10
        mx0,my0 = pygame.mouse.get_pos()

        bg = pygame.Surface((pw,ph), pygame.SRCALPHA)
        bg.fill((8,11,18,235))
        surf.blit(bg,(px,py))
        pygame.draw.rect(surf,(40,80,120),(px,py,pw,ph),1,border_radius=6)
        self._group_rects = []

        xbtn = pygame.Rect(px+pw-28,py+6,22,22)
        pygame.draw.rect(surf,(180,60,60) if xbtn.collidepoint(mx0,my0) else (80,40,40),xbtn,border_radius=4)
        self.font_big.render_to(surf,(px+pw-22,py+9),"✕",(220,200,200))
        self._group_rects.append((xbtn,'close'))

        cx=px+12; cy=py+10
        self.font_big.render_to(surf,(cx,cy),"Move Group",(0,200,255))
        cy+=20

        n_sel = len(self.group_sel_ids)
        col_info = (0,200,100) if n_sel>0 else (180,80,80)
        self.font.render_to(surf,(cx,cy),
            f"{n_sel} node{'s' if n_sel!=1 else ''} selected  "
            f"(Ctrl+drag to rubber-band select)", col_info)
        cy+=18

        row_h=18
        def num_field(label, key, val):
            nonlocal cy
            self.font.render_to(surf,(cx,cy),label+":",(100,120,140))
            active = self._group_edit==key
            disp = (self._group_buf+"_") if active else val
            r = pygame.Rect(cx+70,cy-2,pw-84,row_h)
            pygame.draw.rect(surf,(30,50,70) if active else (18,26,38),r,border_radius=2)
            pygame.draw.rect(surf,(0,200,255) if active else (40,60,80),r,1,border_radius=2)
            self.font.render_to(surf,(cx+74,cy),disp,(200,220,200))
            self._group_rects.append((r,f'edit:{key}'))
            cy+=row_h

        num_field("ΔX (m)",  'dx',  self._group_dx)
        num_field("ΔY (m)",  'dy',  self._group_dy)
        num_field("ΔZ (m)",  'dz',  self._group_dz)
        num_field("Rot° Y",  'rot', self._group_rot)
        cy+=4

        bx3=cx
        for lbl3,act3,col3,ena3 in [
                ("Apply","group_apply",(0,200,100), n_sel>0),
                ("Clear Sel","group_clear",(100,100,180), n_sel>0),
                ("Sel All","group_all",(80,120,180), bool(self.mod_project)),
        ]:
            bw3=self.font_big.get_rect(lbl3).width+14
            r3=pygame.Rect(bx3,cy,bw3,22)
            hov3=r3.collidepoint(mx0,my0) and ena3
            pygame.draw.rect(surf,col3 if ena3 else (30,35,40),r3,border_radius=4)
            pygame.draw.rect(surf,col3 if ena3 else (50,55,60),r3,1,border_radius=4)
            self.font_big.render_to(surf,(bx3+7,cy+4),lbl3,(220,230,240) if ena3 else (80,90,100))
            self._group_rects.append((r3,act3))
            bx3+=bw3+6

    def _handle_group_panel_click(self,mx,my,content_top)->bool:
        px=10; py=content_top+10; pw=380; ph=260
        if not pygame.Rect(px,py,pw,ph).collidepoint(mx,my): return False
        for r,act in self._group_rects:
            if not r.collidepoint(mx,my): continue
            if act=='close':
                self.group_panel=False
            elif act.startswith('edit:'):
                key=act[5:]
                self._group_edit=key
                self._group_buf=getattr(self,f'_group_{key}','0')
            elif act=='group_apply':
                self._apply_group_move()
            elif act=='group_clear':
                self.group_sel_ids=set()
            elif act=='group_all':
                if self.mod_project:
                    self.group_sel_ids=set(self.mod_project.merged_nodes.keys())
            return True
        return True

    def _handle_group_keydown(self,event)->bool:
        if not self._group_edit: return False
        if event.key in (pygame.K_RETURN,pygame.K_KP_ENTER):
            setattr(self,f'_group_{self._group_edit}',self._group_buf)
            self._group_edit=''; self._group_buf=''
            return True
        elif event.key==pygame.K_ESCAPE:
            self._group_edit=''; self._group_buf=''; return True
        elif event.key==pygame.K_BACKSPACE:
            self._group_buf=self._group_buf[:-1]; return True
        elif event.unicode and event.unicode in '0123456789.-':
            self._group_buf+=event.unicode; return True
        return False

    def _apply_group_move(self):
        if not self.group_sel_ids or not self.mod_project: return
        try:
            dx=float(self._group_dx); dy=float(self._group_dy)
            dz=float(self._group_dz); rot=float(self._group_rot)
        except ValueError:
            self._set_status("Invalid number in Move Group fields"); return
        graph=self.mod_project.get_graph_layer()
        if not graph: return
        self._push_undo(f"move group {len(self.group_sel_ids)} nodes")
        updated=move_group(graph,list(self.group_sel_ids),dx,dy,dz,rot)

        self.mod_project._rebuild_merge()
        self._mark_measure_cache_dirty()
        graph.save()
        if self.bridge: self.bridge.reload_tracks(str(graph.path))
        self._set_status(f"Moved {len(updated)} nodes  Δ({dx},{dy},{dz})  rot={rot}°")
        self._group_dx=self._group_dy=self._group_dz=self._group_rot='0'

    # ==================================================================
    # 17. Calculators
    # ==================================================================

    def _handle_measure_action(self, action: str):
        selected = self._get_track_node_state(self.sel_mod_node_id)
        if action == 'set_start':
            if not selected:
                self._set_status('Select a track node first')
                return
            self.measure_start_node_id = selected['id']
            self._set_status(f"Measure start = {selected['id']}")
            return
        if action == 'set_end':
            if not selected:
                self._set_status('Select a track node first')
                return
            self.measure_end_node_id = selected['id']
            self._set_status(f"Measure end = {selected['id']}")
            return
        if action == 'swap':
            self.measure_start_node_id, self.measure_end_node_id = (
                self.measure_end_node_id, self.measure_start_node_id)
            self._set_status('Measure start/end swapped')
            return
        if action == 'clear':
            self.measure_start_node_id = None
            self.measure_end_node_id = None
            self.measure_baseline_start_id = None
            self.measure_baseline_end_id = None
            self._set_status('Measure pair cleared')
            return
        if action == 'set_origin':
            if not selected:
                self._set_status('Select a track node first')
                return
            self.station_origin_node_id = selected['id']
            self._set_status(f"Station origin = {selected['id']}")
            return
        if action == 'clear_origin':
            self.station_origin_node_id = None
            self._set_status('Station origin cleared')
            return
        if action == 'use_baseline':
            if (not self.measure_start_node_id or not self.measure_end_node_id or
                    self.measure_start_node_id == self.measure_end_node_id):
                self._set_status('Set two different measure nodes first')
                return
            self.measure_baseline_start_id = self.measure_start_node_id
            self.measure_baseline_end_id = self.measure_end_node_id
            line = self._construction_line()
            if line:
                self._set_status(
                    f"Baseline = {line['start']['id']} -> {line['end']['id']}  {line['heading']:.1f} deg")
            else:
                self._set_status('Baseline needs two valid nodes')
            return
        if action == 'clear_baseline':
            self.measure_baseline_start_id = None
            self.measure_baseline_end_id = None
            self._set_status('Baseline cleared')
            return
        if action == 'toggle_bearing':
            self.measure_bearing_lock = not self.measure_bearing_lock
            if self.measure_bearing_lock and not self._construction_line():
                self._set_status('Bearing lock armed - set a baseline to use it')
            else:
                self._set_status(f"Bearing lock {'ON' if self.measure_bearing_lock else 'OFF'}")
            return
        if action == 'toggle_distance':
            self.measure_distance_lock = not self.measure_distance_lock
            if self.measure_distance_lock and not self._construction_line():
                self._set_status('Distance lock armed - set a baseline to use it')
            else:
                self._set_status(
                    f"Distance lock {'ON' if self.measure_distance_lock else 'OFF'}  step {self._measure_step_m():.1f} m")
            return

    def _draw_calc_panel(self, surf, content_top):
        if not self.calc_panel:
            return
        w, h = surf.get_size()
        pw = min(w - 40, 780)
        ph = min(h - content_top - STATUS_H - 20, 520)
        px = (w - pw) // 2
        py = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 200))
        surf.blit(overlay, (0, content_top))
        pygame.draw.rect(surf, (14, 19, 28), (px, py, pw, ph), border_radius=8)
        pygame.draw.rect(surf, (40, 70, 100), (px, py, pw, ph), 1, border_radius=8)
        self._calc_rects = []

        xbtn = pygame.Rect(px + pw - 30, py + 8, 22, 22)
        pygame.draw.rect(surf, (180, 60, 60) if xbtn.collidepoint(mx0, my0) else (80, 40, 40), xbtn, border_radius=4)
        self.font_big.render_to(surf, (px + pw - 24, py + 11), 'X', (220, 200, 200))
        self._calc_rects.append((xbtn, 'close'))

        cx = px + 16
        cy = py + 12
        self.font_big.render_to(surf, (cx, cy), 'Track Tools', (0, 212, 255))
        cy += 22

        for mode, lbl, col in [
            ('measure', 'Measure', (0, 212, 255)),
            ('crossover', 'Crossover', (0, 160, 220)),
            ('curved_to', 'Curved Turnout', (220, 140, 0)),
            ('grade', 'Grade / Slope', (0, 200, 120)),
        ]:
            bw = self.font_big.get_rect(lbl).width + 16
            r = pygame.Rect(cx, cy, bw, 22)
            act = self.calc_mode == mode
            hov = r.collidepoint(mx0, my0)
            fill = col if act else (tuple(max(20, v // 2) for v in col) if hov else tuple(max(18, v // 3) for v in col))
            pygame.draw.rect(surf, fill, r, border_radius=4)
            if act:
                pygame.draw.rect(surf, col, r, 1, border_radius=4)
            self.font_big.render_to(surf, (r.x + 8, r.y + 4), lbl, (220, 230, 240) if act else (140, 160, 180))
            self._calc_rects.append((r, f'tab:{mode}'))
            cx += bw + 6
        cy += 30
        cx = px + 16

        row_h = 20

        def inp(label, key, default=''):
            nonlocal cy
            self.font.render_to(surf, (cx, cy), label + ':', (100, 130, 160))
            active = self._calc_edit == key
            val = self._calc_inputs.get(key, default)
            disp = (self._calc_buf + '_') if active else val
            r = pygame.Rect(cx + 180, cy - 2, 120, row_h - 2)
            pygame.draw.rect(surf, (30, 50, 70) if active else (18, 26, 38), r, border_radius=2)
            pygame.draw.rect(surf, (0, 200, 255) if active else (40, 60, 80), r, 1, border_radius=2)
            self.font.render_to(surf, (cx + 184, cy), disp, (200, 220, 200))
            self._calc_rects.append((r, f'inp:{key}:{default}'))
            cy += row_h

        def result(label, val, col=(0, 220, 160), big=True):
            nonlocal cy
            self.font.render_to(surf, (cx, cy), label + ':', (100, 130, 160))
            if big:
                self.font_big.render_to(surf, (cx + 180, cy), str(val), col)
                cy += row_h + 2
            else:
                self.font.render_to(surf, (cx + 180, cy), str(val), col)
                cy += row_h

        def divider():
            nonlocal cy
            cy += 4
            pygame.draw.line(surf, (40, 60, 80), (cx, cy), (px + pw - 16, cy))
            cy += 6

        def button_row(specs):
            nonlocal cy
            bx = cx
            for label, action, color, enabled, active in specs:
                bw = max(96, self.font_big.get_rect(label).width + 18)
                r = pygame.Rect(bx, cy, bw, 22)
                hov = r.collidepoint(mx0, my0)
                if not enabled:
                    fill = (26, 30, 38)
                    border = (52, 60, 72)
                    text_col = (90, 102, 116)
                elif active:
                    fill = tuple(min(255, 18 + int(c * 0.32)) for c in color)
                    border = color
                    text_col = color
                elif hov:
                    fill = tuple(max(18, c // 2) for c in color)
                    border = color
                    text_col = (220, 230, 240)
                else:
                    fill = (20, 28, 40)
                    border = tuple(max(36, c // 3) for c in color)
                    text_col = (170, 190, 210)
                pygame.draw.rect(surf, fill, r, border_radius=4)
                pygame.draw.rect(surf, border, r, 1, border_radius=4)
                self.font_big.render_to(surf, (r.x + 8, r.y + 4), label, text_col)
                if enabled:
                    self._calc_rects.append((r, f'btn:{action}'))
                bx = r.right + 6
            cy += 28

        import math as _mc

        if self.calc_mode == 'measure':
            self._calc_inputs.setdefault('ms_step', '25.0')
            selected = self._get_track_node_state(self.sel_mod_node_id)
            measure = self._measure_between_nodes(self.measure_start_node_id, self.measure_end_node_id)
            line = self._construction_line()
            origin = self._get_track_node_state(self.station_origin_node_id)
            self.font.render_to(
                surf,
                (cx, cy),
                'Keep this panel open, click the map to select nodes, then use Set Start / Set End.',
                (120, 140, 160),
            )
            cy += 18
            divider()
            button_row([
                ('Set Start', 'set_start', (0, 180, 255), bool(selected), False),
                ('Set End', 'set_end', (0, 220, 180), bool(selected), False),
                ('Swap', 'swap', (180, 180, 255), bool(self.measure_start_node_id and self.measure_end_node_id), False),
                ('Clear', 'clear', (160, 120, 120), bool(self.measure_start_node_id or self.measure_end_node_id), False),
            ])
            button_row([
                ('Set Origin', 'set_origin', (255, 180, 0), bool(selected), False),
                ('Clear Origin', 'clear_origin', (120, 100, 80), bool(self.station_origin_node_id), False),
                ('Use Pair As Baseline', 'use_baseline', (0, 220, 255), bool(self.measure_start_node_id and self.measure_end_node_id and self.measure_start_node_id != self.measure_end_node_id), bool(line)),
                ('Clear Baseline', 'clear_baseline', (90, 130, 170), bool(line), False),
            ])
            button_row([
                ('Bearing Lock', 'toggle_bearing', (0, 220, 220), True, self.measure_bearing_lock),
                ('Distance Lock', 'toggle_distance', (255, 190, 0), True, self.measure_distance_lock),
            ])
            inp('Distance step (m)', 'ms_step', '25.0')
            divider()
            result('Selected node', selected['id'] if selected else 'Click a node on the map', (220, 230, 240), big=False)
            result('Measure start', self.measure_start_node_id or '--', (0, 180, 255), big=False)
            result('Measure end', self.measure_end_node_id or '--', (0, 220, 180), big=False)
            result('Station origin', (origin['id'] + '   ' + self._format_station_readout(0.0)) if origin else '--', (255, 190, 80), big=False)
            result('Baseline', (f"{line['start']['id']} -> {line['end']['id']}   {line['heading']:.1f} deg") if line else '--', (0, 220, 255), big=False)
            divider()
            if measure:
                along_text = (f"{measure['along_track_m']:.1f} m   ({measure['path_segment_count']} segs)"
                              if measure['along_track_m'] is not None else 'Not connected')
                along_col = (0, 220, 160) if measure['along_track_m'] is not None else (220, 120, 120)
                grade_text = (f"{abs(measure['avg_grade_pct']):.2f}%"
                              if measure['avg_grade_pct'] is not None else '--')
                result('Direct X/Z', f"{measure['direct_xz_m']:.1f} m")
                result('Direct 3D', f"{measure['direct_3d_m']:.1f} m")
                result('Along track', along_text, along_col)
                result('Delta Y', f"{measure['delta_y_m']:+.2f} m", (255, 200, 120))
                result('Average grade', grade_text, (220, 220, 120))
                result('Bearing', f"{measure['heading_deg']:.1f} deg" if measure['heading_deg'] is not None else '--')
                result('Start station', self._format_station_readout(measure['start_station_m']), (255, 200, 80), big=False)
                result('End station', self._format_station_readout(measure['end_station_m']), (255, 200, 80), big=False)
            else:
                self.font.render_to(
                    surf,
                    (cx, cy),
                    'Set start and end nodes to measure direct distance, along-track mileage, and stationing.',
                    (130, 150, 170),
                )

        elif self.calc_mode == 'crossover':
            self.font.render_to(surf, (cx, cy), 'Calculates geometry for a standard crossover between two parallel tracks.', (120, 140, 160))
            cy += 18
            divider()
            inp('Track separation (m)', 'xo_sep', '5.0')
            inp('Crossover angle (deg)', 'xo_ang', '10.0')
            inp('Main track class', 'xo_cls', 'Mainline')
            divider()
            try:
                sep = float(self._calc_inputs.get('xo_sep', '5.0'))
                ang = float(self._calc_inputs.get('xo_ang', '10.0'))
                if ang > 0:
                    length = sep / (_mc.sin(_mc.radians(ang)))
                    leg = sep / (2 * _mc.tan(_mc.radians(ang / 2)))
                    result('Crossing length (m)', f'{length:.2f}')
                    result('Each leg length (m)', f'{leg:.2f}')
                    result('Node spacing (m)', f'{leg:.2f}')
                    result('Total nodes', '4 (2 crossover + 2 ends per track)')
                    result('Total segments', '6')
                    cy += 8
                    self.font.render_to(surf, (cx, cy), "Tip: place 2 nodes on each track at 'leg length' spacing, then connect diagonally.", (120, 150, 140))
            except Exception:
                pass

        elif self.calc_mode == 'curved_to':
            self.font.render_to(surf, (cx, cy), 'Radius and arc for a curved turnout that avoids kinking at the frog.', (120, 140, 160))
            cy += 18
            divider()
            inp('Through radius (m)', 'ct_rad', '200.0')
            inp('Gauge (m)', 'ct_gauge', '1.435')
            inp('Diverge angle (deg)', 'ct_div', '10.0')
            inp('Desired leg length (m)', 'ct_leg', '30.0')
            divider()
            try:
                R = float(self._calc_inputs.get('ct_rad', '200.0'))
                G = float(self._calc_inputs.get('ct_gauge', '1.435'))
                D = float(self._calc_inputs.get('ct_div', '10.0'))
                L = float(self._calc_inputs.get('ct_leg', '30.0'))
                R_div = R / (_mc.sin(_mc.radians(D)) + 1e-9)
                arc = 2 * _mc.pi * R_div * D / 360
                offset = L * _mc.sin(_mc.radians(D))
                result('Diverge radius (m)', f'{R_div:.1f}')
                result('Arc length (m)', f'{arc:.2f}')
                result('Lateral offset (m)', f'{offset:.3f}')
                result('Minimum leg (m)', f"{G / _mc.sin(_mc.radians(D) + 1e-9):.1f}")
                result('Set in editor', f'angle={D:.1f} deg  leg={L:.0f} m')
            except Exception:
                pass

        elif self.calc_mode == 'grade':
            self.font.render_to(surf, (cx, cy), 'Convert between run, rise, percentage, and ratio for track grades.', (120, 140, 160))
            cy += 18
            divider()
            inp('Horizontal run (m)', 'gr_run', '100.0')
            inp('Vertical rise (m)', 'gr_rise', '3.0')
            divider()
            try:
                run = float(self._calc_inputs.get('gr_run', '100.0'))
                rise = float(self._calc_inputs.get('gr_rise', '3.0'))
                if run > 0:
                    pct = rise / run * 100
                    ratio = run / rise if rise else 9999
                    angle = _mc.degrees(_mc.atan(rise / run))
                    length = _mc.sqrt(run * run + rise * rise)
                    result('Grade %', f'{pct:.2f}%')
                    result('Ratio', f'1:{ratio:.1f}')
                    result('Angle', f'{angle:.3f} deg')
                    result('Track length (m)', f'{length:.2f}')
                    cy += 8
                    ok = 'OK' if pct < 3.5 else ('Steep' if pct < 6 else 'Very steep')
                    col_g = (0, 220, 100) if pct < 3.5 else ((220, 180, 0) if pct < 6 else (220, 60, 60))
                    self.font_big.render_to(surf, (cx, cy), ok, col_g)
            except Exception:
                pass

    def _handle_calc_click(self, mx, my, content_top) -> bool:
        w, h = self.screen.get_size()
        pw = min(w - 40, 780)
        ph = min(h - content_top - STATUS_H - 20, 520)
        px = (w - pw) // 2
        py = content_top + 10
        if not pygame.Rect(px, py, pw, ph).collidepoint(mx, my):
            return False
        for r, act in self._calc_rects:
            if not r.collidepoint(mx, my):
                continue
            if act == 'close':
                self.calc_panel = False
            elif act.startswith('tab:'):
                self.calc_mode = act[4:]
            elif act.startswith('inp:'):
                parts = act.split(':', 2)
                key = parts[1]
                default = parts[2] if len(parts) > 2 else ''
                self._calc_edit = key
                self._calc_buf = self._calc_inputs.get(key, default)
            elif act.startswith('btn:'):
                self._handle_measure_action(act[4:])
            return True
        return True

    def _handle_calc_keydown(self, event) -> bool:
        if not self._calc_edit:
            return False
        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            self._calc_inputs[self._calc_edit] = self._calc_buf
            self._calc_edit = ''
            self._calc_buf = ''
            return True
        if event.key == pygame.K_ESCAPE:
            self._calc_edit = ''
            self._calc_buf = ''
            return True
        if event.key == pygame.K_BACKSPACE:
            self._calc_buf = self._calc_buf[:-1]
            return True
        if event.unicode:
            self._calc_buf += event.unicode
            return True
        return False

    # ==================================================================
    # 18. Mandela editor
    # ==================================================================








    def _mandela_catalog_path(self):
        """Best-effort location for the base-game dumped-mandelas.txt."""
        base_game_dump = Path(r"C:\Steam\steamapps\common\Railroader\dumped-mandelas.txt")
        candidates = [base_game_dump]
        if self._mandela_base_path and Path(self._mandela_base_path) != base_game_dump:
            candidates.append(Path(self._mandela_base_path))
        bridge_game_dir = getattr(getattr(self, 'bridge', None), 'game_dir', None)
        if bridge_game_dir:
            candidates.append(Path(bridge_game_dir) / "dumped-mandelas.txt")
        if preferred_railroader_path:
            try:
                pref = preferred_railroader_path()
            except Exception:
                pref = None
            if pref:
                candidates.append(Path(pref) / "dumped-mandelas.txt")
        candidates.append(
            Path.home() / ".steam" / "debian-installation" / "steamapps" / "common" / "Railroader" / "dumped-mandelas.txt"
        )
        seen = set()
        for candidate in candidates:
            if not candidate:
                continue
            key = os.fspath(candidate)
            if key in seen:
                continue
            seen.add(key)
            if candidate.exists():
                return candidate
        return candidates[0] if candidates else None

    def _reload_mandela_base_paths(self):
        """Load base-game Mandela prefab paths from dumped-mandelas.txt."""
        self._mandela_base_paths = []
        self._mandela_base_error = ""
        path = self._mandela_catalog_path()
        self._mandela_base_path = path
        if path is None or not path.exists():
            self._mandela_base_error = "Base prefab list not found"
            return False
        try:
            seen = set()
            for raw in path.read_text(encoding="utf-8", errors="ignore").splitlines():
                item = raw.strip()
                if not item or item in seen:
                    continue
                seen.add(item)
                self._mandela_base_paths.append(item)
            if not self._mandela_base_paths:
                self._mandela_base_error = f"No prefab paths found in {path.name}"
                return False
            return True
        except Exception as ex:
            self._mandela_base_error = str(ex)
            return False

    def _matching_mandela_base_paths(self, query, limit=5):
        """Return filtered base-game prefab paths from dumped-mandelas.txt."""
        paths = self._mandela_base_paths or []
        if not paths:
            return []
        query = (query or "").strip().lower()
        if not query:
            return paths[:limit]
        parts = [part for part in query.replace("\\", "/").split() if part]
        matches = []
        for item in paths:
            lower = item.lower()
            if all(part in lower for part in parts):
                matches.append(item)
            if len(matches) >= limit:
                break
        return matches

    def _pick_mandela_base_path(self):
        """Choose a prefab path from the base-game dumped-mandelas.txt."""
        if not self._mandela_base_paths:
            if not self._reload_mandela_base_paths():
                detail = f": {self._mandela_base_error}" if self._mandela_base_error else ""
                self._set_status("Could not load dumped-mandelas.txt" + detail)
                return
        choice = ask_choice_list(
            self.screen,
            "Base Mandela Prefabs",
            self._mandela_base_paths,
            prompt="Pick a base-game prefab path from dumped-mandelas.txt",
            initial_filter=self.mandela_place_type,
        )
        if choice:
            self.mandela_place_type = choice
            self._set_status(f"Base-game Mandela prefab selected: {choice}")

    def _mandela_enabled_label(self):
        labels = {
            'default': 'Enabled: Default',
            'enabled': 'Enabled: Force On',
            'disabled': 'Enabled: Disabled',
        }
        return labels.get(self.mandela_enabled_mode, 'Enabled: Default')

    def _mandela_enabled_args(self):
        if self.mandela_enabled_mode == 'disabled':
            return False, False
        if self.mandela_enabled_mode == 'enabled':
            return True, True
        return True, False

    def _current_selected_mandela(self):
        if not self.sel_mandela_id or not self.mod_project:
            return None
        for li, layer in enumerate(self.mod_project.layers):
            entry = (layer._raw.get('mandelas') or {}).get(self.sel_mandela_id)
            if entry is not None:
                return self.sel_mandela_id, entry, li, layer
        return None

    def _next_mandela_copy_path(self, base_path):
        existing = {mid for mid, _, _, _ in self._collect_mandela_entries()}
        parent, _, leaf = base_path.rpartition('/')
        leaf = leaf or base_path or 'Mandela'
        candidate_leaf = f"{leaf}_copy"
        candidate = f"{parent}/{candidate_leaf}" if parent else candidate_leaf
        index = 2
        while candidate in existing:
            candidate_leaf = f"{leaf}_copy_{index}"
            candidate = f"{parent}/{candidate_leaf}" if parent else candidate_leaf
            index += 1
        return candidate

    def _load_mandela_draft_from_entry(self, mandela_id, entry, duplicate=False):
        self.mandela_target_path = self._next_mandela_copy_path(mandela_id) if duplicate else mandela_id
        self.mandela_place_type = entry.get('instantiateFrom', '') or ''

        rotation = entry.get('localRotation') or {}
        scale = entry.get('localScale') or {}
        self.mandela_rotation = {
            'x': float(rotation.get('x', 0.0) or 0.0),
            'y': float(rotation.get('y', 0.0) or 0.0),
            'z': float(rotation.get('z', 0.0) or 0.0),
        }
        self.mandela_scale = {
            'x': float(scale.get('x', 1.0) or 1.0),
            'y': float(scale.get('y', 1.0) or 1.0),
            'z': float(scale.get('z', 1.0) or 1.0),
        }
        enabled = entry.get('enabled', None)
        if enabled is False:
            self.mandela_enabled_mode = 'disabled'
        elif enabled is True:
            self.mandela_enabled_mode = 'enabled'
        else:
            self.mandela_enabled_mode = 'default'

    def _save_selected_mandela(self):
        selected = self._current_selected_mandela()
        if not selected:
            self._set_status("Select a mandela first")
            return

        old_id, entry, _li, layer = selected
        new_id = self.mandela_target_path.strip()
        prefab_path = self.mandela_place_type.strip()
        if '/' not in new_id:
            self._set_status("Mandela target path must look like Root/Child")
            return
        if prefab_path and '/' not in prefab_path:
            self._set_status("Mandela prefab path must look like Root/Child")
            return
        if self._mandela_base_paths and prefab_path and prefab_path not in self._mandela_base_paths:
            self._set_status("Prefab path not found in base-game dumped-mandelas.txt")
            return
        if not prefab_path and self.mandela_enabled_mode == 'default':
            self._set_status("Need a prefab path or an explicit enabled state")
            return
        if new_id != old_id:
            existing = {mid for mid, _, _, _ in self._collect_mandela_entries()}
            if new_id in existing:
                self._set_status(f"Mandela target already exists: {new_id}")
                return

        position = entry.get('localPosition') or {}
        has_position = bool(position)
        enabled, force_enabled = self._mandela_enabled_args()
        if prefab_path and not has_position:
            self._set_status("Selected mandela has no localPosition; duplicate it onto the map instead")
            return

        self._push_undo(f"save mandela {old_id}")
        if new_id != old_id:
            mandela_delete(layer, old_id)

        mandela_set(
            layer,
            new_id,
            prefab_path or None,
            float(position.get('x', 0.0) or 0.0),
            float(position.get('y', 0.0) or 0.0),
            float(position.get('z', 0.0) or 0.0),
            self.mandela_rotation['x'],
            self.mandela_rotation['y'],
            self.mandela_rotation['z'],
            self.mandela_scale['x'],
            self.mandela_scale['y'],
            self.mandela_scale['z'],
            enabled=enabled,
            force_enabled=force_enabled,
        )
        layer.save()
        self.sel_mandela_id = new_id
        self._set_status(f"Saved mandela {new_id}")

    def _duplicate_selected_mandela(self):
        selected = self._current_selected_mandela()
        if not selected:
            self._set_status("Select a mandela first")
            return
        mandela_id, entry, _li, _layer = selected
        self._load_mandela_draft_from_entry(mandela_id, entry, duplicate=True)
        if self.mandela_place_type.strip():
            self.mandela_place_mode = True
            self._set_status(f"Duplicate ready: click the map to place {self.mandela_target_path}")
        else:
            self.mandela_place_mode = False
            self._set_status("Copied selected mandela into the draft; add a prefab path before placing")

    def _collect_mandela_entries(self):
        """Return sorted Mandela entries with their owning layer info."""
        if not self.mod_project:
            return []
        entries = []
        for li, layer in enumerate(self.mod_project.layers):
            for mid, mv in (layer._raw.get('mandelas') or {}).items():
                if mv is not None:
                    entries.append((mid, mv, li, layer))
        entries.sort(key=lambda item: (getattr(item[3], 'label', '').lower(), item[0].lower()))
        return entries

    def _draw_mandela_panel(self, surf, content_top):
        if not self.mandela_panel or not self.mod_project:
            return
        if not self._mandela_base_paths and not self._mandela_base_error:
            self._reload_mandela_base_paths()

        w, h = surf.get_size()
        pw = min(w - 40, 1040)
        ph = min(h - content_top - STATUS_H - 20, 680)
        px = (w - pw) // 2
        py = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 192))
        surf.blit(overlay, (0, content_top))

        header_h = 78
        panel_rect = pygame.Rect(px, py, pw, ph)
        header_rect = pygame.Rect(px, py, pw, header_h)
        pygame.draw.rect(surf, PANEL_ELEVATED_BG, panel_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, panel_rect, 1, border_radius=12)
        pygame.draw.rect(surf, PANEL_HEADER_BG, header_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, header_rect, 1, border_radius=12)
        pygame.draw.rect(surf, (200, 140, 255), (px, py + header_h - 4, pw, 4), border_radius=2)

        self._mandela_rects = []
        xbtn = pygame.Rect(px + pw - 30, py + 8, 22, 22)
        hover_close = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180, 60, 60) if hover_close else (80, 40, 40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220, 80, 80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (xbtn.x + 7, xbtn.y + 3), "x", (236, 216, 216))
        self._mandela_rects.append((xbtn, "close"))

        entries = self._collect_mandela_entries()
        entry_by_id = {mid: (mv, li, layer) for mid, mv, li, layer in entries}
        if self.sel_mandela_id not in entry_by_id:
            self.sel_mandela_id = None
        selected = entry_by_id.get(self.sel_mandela_id)

        def trim_text(font_obj, text, max_w):
            text = str(text)
            if font_obj.get_rect(text).width <= max_w:
                return text
            ellipsis = "..."
            clipped = text
            while clipped and font_obj.get_rect(clipped + ellipsis).width > max_w:
                clipped = clipped[:-1]
            return (clipped or text[:1]) + ellipsis

        def draw_button(x, y, label, action, color, enabled=True, min_w=0):
            bw = max(min_w, self.font_big.get_rect(label).width + 16)
            rect = pygame.Rect(x, y, bw, 24)
            hover = rect.collidepoint(mx0, my0)
            if enabled:
                fill = color if hover else tuple(max(22, v // 2) for v in color)
                border = color
                text_col = (220, 230, 240)
                self._mandela_rects.append((rect, action))
            else:
                fill = (28, 34, 42)
                border = (58, 68, 82)
                text_col = (110, 120, 132)
            pygame.draw.rect(surf, fill, rect, border_radius=4)
            pygame.draw.rect(surf, border, rect, 1, border_radius=4)
            self.font_big.render_to(surf, (rect.x + 8, rect.y + 5), label, text_col)
            return rect

        def draw_card(rect, title, accent, subtitle):
            pygame.draw.rect(surf, PANEL_SECTION_BG, rect, border_radius=10)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, rect, 1, border_radius=10)
            pygame.draw.rect(
                surf,
                PANEL_SECTION_ALT,
                (rect.x, rect.y, rect.width, 34),
                border_top_left_radius=10,
                border_top_right_radius=10,
            )
            pygame.draw.rect(surf, accent, (rect.x, rect.y + 30, rect.width, 3), border_radius=2)
            self.font_big.render_to(surf, (rect.x + 10, rect.y + 8), title, accent)
            title_w = self.font_big.get_rect(title).width
            sub_x = rect.x + 10 + title_w + 6
            sub_max_w = rect.right - 10 - sub_x
            if sub_max_w > 20:
                self.font.render_to(surf, (sub_x, rect.y + 11),
                    trim_text(self.font, subtitle, sub_max_w), TEXT_MUTED)

        def draw_field(x, y, width, label, value, action, active=False, placeholder="", value_color=None):
            self.font.render_to(surf, (x, y + 4), label, TEXT_MUTED)
            rect = pygame.Rect(x + 54, y, width, 22)
            pygame.draw.rect(surf, (30, 20, 50) if active else (18, 12, 30), rect, border_radius=4)
            pygame.draw.rect(surf, (180, 100, 255) if active else (60, 40, 80), rect, 1, border_radius=4)
            text = value or placeholder
            color = value_color if value else TEXT_MUTED
            self.font.render_to(surf, (rect.x + 8, rect.y + 4), trim_text(self.font, text, rect.width - 16), color)
            self._mandela_rects.append((rect, action))
            return rect

        def draw_vec_row(card_x, row_y, label, prefix, values, defaults):
            self.font.render_to(surf, (card_x, row_y + 4), label, TEXT_MUTED)
            box_w = 82
            gap = 8
            start_x = card_x + 54
            for idx, axis in enumerate(('x', 'y', 'z')):
                rect = pygame.Rect(start_x + idx * (box_w + gap), row_y, box_w, 22)
                active = self._mandela_edit == f"{prefix}:{axis}"
                pygame.draw.rect(surf, (30, 20, 50) if active else (18, 12, 30), rect, border_radius=4)
                pygame.draw.rect(surf, (180, 100, 255) if active else (60, 40, 80), rect, 1, border_radius=4)
                raw_value = self._mandela_buf if active else f"{float(values.get(axis, defaults[axis])):.2f}"
                self.font.render_to(surf, (rect.x + 8, rect.y + 4), f"{axis.upper()} {raw_value}", (200, 160, 255))
                self._mandela_rects.append((rect, f"edit:{prefix}:{axis}"))

        def draw_wrapped_buttons(card_rect, start_y, specs):
            bx = card_rect.x + 12
            by = start_y
            max_x = card_rect.right - 12
            for label, action, color, enabled in specs:
                bw = max(90, self.font_big.get_rect(label).width + 16)
                if bx + bw > max_x:
                    bx = card_rect.x + 12
                    by += 30
                rect = draw_button(bx, by, label, action, color, enabled=enabled, min_w=90)
                bx = rect.right + 8
            return by + 24

        cx = px + 16
        cy = py + 14
        self.font_big.render_to(surf, (cx, cy), "Mandela / Prefab Instances", (200, 140, 255))
        cy += 20
        self.font.render_to(surf, (cx, cy), "Base-game prefab picker, placement draft, and selected-entry editor", TEXT_SOFT)

        chip_x = cx
        chip_y = py + 48
        base_count = len(self._mandela_base_paths)
        catalog_path = self._mandela_base_path
        chips = [
            (f"{len(entries)} placed", TEXT_COLOR),
            (f"{base_count} base prefabs", OK_COLOR if base_count else WARN_COLOR),
            ("Target ready" if self.mandela_target_path.strip() else "Need target path", OK_COLOR if self.mandela_target_path.strip() else WARN_COLOR),
            ("Prefab ready" if self.mandela_place_type.strip() else "Need prefab path", OK_COLOR if self.mandela_place_type.strip() else WARN_COLOR),
            ("Place mode ON" if self.mandela_place_mode else "Place mode OFF", ACCENT_COLOR if self.mandela_place_mode else TEXT_MUTED),
        ]
        for chip_text, chip_color in chips:
            chip_w = self.font.get_rect(chip_text).width + 14
            chip_rect = pygame.Rect(chip_x, chip_y, chip_w, 18)
            pygame.draw.rect(surf, PANEL_SECTION_BG, chip_rect, border_radius=9)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, chip_rect, 1, border_radius=9)
            self.font.render_to(surf, (chip_rect.x + 7, chip_rect.y + 3), chip_text, chip_color)
            chip_x += chip_w + 8

        card_top = py + header_h + 14
        gap = 12
        left_w = max(430, int((pw - 44) * 0.58))
        right_w = pw - 32 - left_w - gap
        draft_button_specs = [
            ("Base Pick", "m_pick_prefab", (80, 140, 220), base_count > 0),
            ("Reload Base", "m_reload_base", (100, 180, 255), True),
            ("Load Sel", "m_use_selected", (80, 140, 220), selected is not None),
            ("Save Sel", "m_save_selected", (0, 170, 120), selected is not None),
            ("Duplicate", "m_duplicate_selected", (220, 160, 60), selected is not None),
            ("Place on Map", "place_toggle", (160, 90, 240), bool(self.mandela_target_path.strip() and self.mandela_place_type.strip())),
        ]

        def measure_wrapped_button_height(card_width, specs):
            usable_w = max(120, card_width - 24)
            current_w = 0
            rows = 1
            for label, _action, _color, _enabled in specs:
                bw = max(90, self.font_big.get_rect(label).width + 16)
                next_w = bw if current_w <= 0 else current_w + 8 + bw
                if current_w > 0 and next_w > usable_w:
                    rows += 1
                    current_w = bw
                else:
                    current_w = next_w
            return 24 + max(0, rows - 1) * 30

        draft_action_h = measure_wrapped_button_height(left_w, draft_button_specs)
        min_bottom_h = 180
        desired_top_h = max(260, 212 + draft_action_h)
        max_top_h = max(214, ph - header_h - gap - 26 - min_bottom_h)
        top_h = min(desired_top_h, max_top_h)
        bottom_h = ph - header_h - top_h - gap - 26

        draft_card = pygame.Rect(cx, card_top, left_w, top_h)
        base_card = pygame.Rect(draft_card.right + gap, card_top, right_w, top_h)
        list_card = pygame.Rect(cx, draft_card.bottom + gap, left_w, bottom_h)
        detail_card = pygame.Rect(list_card.right + gap, base_card.bottom + gap, right_w, bottom_h)

        draw_card(draft_card, "DRAFT", (200, 140, 255), "Placement and edit values")
        draw_card(base_card, "BASE LIST", ACCENT_COLOR, "Base-game prefab catalog")
        draw_card(list_card, "PLACED", (200, 140, 255), f"{len(entries)} entries")
        draw_card(detail_card, "SELECTED", ACCENT_COLOR, "Existing entry")

        draft_x = draft_card.x + 12
        draft_y = draft_card.y + 44
        field_w = draft_card.width - 80
        active_target = self._mandela_edit == "target"
        active_prefab = self._mandela_edit == "prefab"
        target_text = (self._mandela_buf + "_") if active_target else self.mandela_target_path
        prefab_text = (self._mandela_buf + "_") if active_prefab else self.mandela_place_type
        draw_field(draft_x, draft_y, field_w, "Target", target_text, "edit:target",
                   active=active_target, placeholder="<root/child path>", value_color=(200, 160, 255))
        draw_field(draft_x, draft_y + 28, field_w, "Prefab", prefab_text, "edit:prefab",
                   active=active_prefab, placeholder="<prefab path>", value_color=(200, 160, 255))
        draw_vec_row(draft_x, draft_y + 56, "Rotate", "rot", self.mandela_rotation, {'x': 0.0, 'y': 0.0, 'z': 0.0})
        draw_vec_row(draft_x, draft_y + 84, "Scale", "scale", self.mandela_scale, {'x': 1.0, 'y': 1.0, 'z': 1.0})

        enabled_rect = pygame.Rect(draft_x + 54, draft_y + 112, 154, 22)
        enabled_hover = enabled_rect.collidepoint(mx0, my0)
        enabled_color = {
            'default': (92, 120, 160),
            'enabled': (70, 165, 110),
            'disabled': (180, 92, 92),
        }.get(self.mandela_enabled_mode, (92, 120, 160))
        pygame.draw.rect(surf, enabled_color if enabled_hover else tuple(max(24, v // 2) for v in enabled_color), enabled_rect, border_radius=4)
        pygame.draw.rect(surf, enabled_color, enabled_rect, 1, border_radius=4)
        self.font.render_to(surf, (draft_x, draft_y + 116), "Enabled", TEXT_MUTED)
        self.font.render_to(surf, (enabled_rect.x + 8, enabled_rect.y + 4), self._mandela_enabled_label(), (220, 230, 240))
        self._mandela_rects.append((enabled_rect, "toggle:enabled"))

        draw_wrapped_buttons(draft_card, enabled_rect.bottom + 12, draft_button_specs)

        catalog_name = catalog_path.name if catalog_path else "dumped-mandelas.txt"
        base_y = base_card.y + 44
        if base_count:
            base_msg = f"Using {catalog_name}  ({base_count} prefabs)"
            base_color = TEXT_SOFT
        else:
            detail = f" ({self._mandela_base_error})" if self._mandela_base_error else ""
            base_msg = f"Base-game prefab list unavailable{detail}"
            base_color = WARN_COLOR
        self.font.render_to(surf, (base_card.x + 12, base_y),
                            trim_text(self.font, base_msg, base_card.width - 24), base_color)

        # Current selection
        sel_text = self.mandela_place_type or "(none selected)"
        sel_color = TEXT_COLOR if self.mandela_place_type else TEXT_MUTED
        self.font.render_to(surf, (base_card.x + 12, base_y + 20),
                            trim_text(self.font, sel_text, base_card.width - 24), sel_color)

        # Browse dropdown button
        browse_r = pygame.Rect(base_card.x + 12, base_y + 42, 120, 22)
        browse_ena = base_count > 0
        browse_col = (80, 140, 220)
        hover_br = browse_r.collidepoint(mx0, my0) and browse_ena
        pygame.draw.rect(surf, browse_col if hover_br else (tuple(max(22, v // 2) for v in browse_col) if browse_ena else (30, 35, 40)), browse_r, border_radius=4)
        pygame.draw.rect(surf, browse_col if browse_ena else (50, 60, 70), browse_r, 1, border_radius=4)
        self.font.render_to(surf, (browse_r.x + 8, browse_r.y + 4), "Browse \u25bc",
                            (220, 230, 240) if browse_ena else (70, 80, 90))
        self._mandela_rects.append((browse_r, "m_base_browse"))

        row_h = 34
        list_top = list_card.y + 40
        max_rows = max(5, (list_card.height - 50) // row_h)
        for idx, (mid, mv, li, layer) in enumerate(entries[:max_rows]):
            ry = list_top + idx * row_h
            row_rect = pygame.Rect(list_card.x + 8, ry, list_card.width - 16, row_h - 2)
            is_sel = mid == self.sel_mandela_id
            if is_sel:
                pygame.draw.rect(surf, ROW_ACTIVE_ALT_BG, row_rect, border_radius=5)
                pygame.draw.rect(surf, (200, 140, 255), row_rect, 1, border_radius=5)
            elif row_rect.collidepoint(mx0, my0):
                pygame.draw.rect(surf, ROW_HOVER_BG, row_rect, border_radius=5)
            else:
                pygame.draw.rect(surf, PANEL_SECTION_ALT if idx % 2 == 0 else ROW_ALT_BG, row_rect, border_radius=5)
            source = mv.get("instantiateFrom") or "<existing scene object>"
            self.font_big.render_to(surf, (row_rect.x + 10, row_rect.y + 4),
                                    trim_text(self.font_big, mid, row_rect.width - 20),
                                    (228, 184, 255) if is_sel else TEXT_COLOR)
            self.font.render_to(surf, (row_rect.x + 10, row_rect.y + 20),
                                trim_text(self.font, source, row_rect.width - 20),
                                TEXT_SOFT if is_sel else TEXT_MUTED)
            self._mandela_rects.append((row_rect, f"sel:{mid}"))

        if not entries:
            self.font.render_to(surf, (list_card.x + 12, list_top), "No mandelas found in loaded layers.", TEXT_MUTED)

        detail_y = detail_card.y + 44
        if selected:
            mv2, li2, layer2 = selected
            pos2 = mv2.get("localPosition") or {}
            rot2 = mv2.get("localRotation") or {}
            scale2 = mv2.get("localScale") or {}
            details = [
                ("Target", self.sel_mandela_id),
                ("Prefab", mv2.get("instantiateFrom", "<existing scene object>")),
                ("Enabled", mv2.get("enabled", "<default>")),
                ("Layer", getattr(layer2, "label", f"Layer {li2}")),
                ("Position", f"x={float(pos2.get('x', 0.0)):.2f}  y={float(pos2.get('y', 0.0)):.2f}  z={float(pos2.get('z', 0.0)):.2f}"),
                ("Rotation", f"x={float(rot2.get('x', 0.0)):.1f}  y={float(rot2.get('y', 0.0)):.1f}  z={float(rot2.get('z', 0.0)):.1f}"),
                ("Scale", f"x={float(scale2.get('x', 1.0)):.2f}  y={float(scale2.get('y', 1.0)):.2f}  z={float(scale2.get('z', 1.0)):.2f}"),
            ]
            base_known = mv2.get("instantiateFrom", "") in self._mandela_base_paths
            details.append(("Base List", "Found in base-game dumped-mandelas.txt" if base_known else "Not in base-game dumped-mandelas.txt"))
            for label, value in details:
                self.font.render_to(surf, (detail_card.x + 12, detail_y), f"{label}:", TEXT_MUTED)
                color = TEXT_SOFT
                if label == "Base List":
                    color = OK_COLOR if base_known else WARN_COLOR
                self.font.render_to(surf, (detail_card.x + 88, detail_y), trim_text(self.font, value, detail_card.width - 100), color)
                detail_y += 18

            detail_button_specs = [
                ("Load Sel", "m_use_selected", (80, 140, 220)),
                ("Save Sel", "m_save_selected", (0, 170, 120)),
                ("Duplicate", "m_duplicate_selected", (220, 160, 60)),
                ("Go To", "m_goto", (60, 40, 120)),
                ("Delete", "m_del", (180, 60, 60)),
            ]
            detail_action_h = measure_wrapped_button_height(detail_card.width, [
                (label, action, color, True) for label, action, color in detail_button_specs
            ])
            draw_wrapped_buttons(
                detail_card,
                max(detail_y + 8, detail_card.bottom - detail_action_h - 12),
                [(label, action, color, True) for label, action, color in detail_button_specs],
            )
        else:
            hints = [
                "Target path is the destination scene path key.",
                "Load Sel copies target, prefab, rotation, scale, and enabled.",
                "Duplicate prepares a new target path and arms place mode.",
                "Save Sel writes the draft back onto the selected entry.",
            ]
            hint_max_w = detail_card.width - 24
            for line in hints:
                words = line.split()
                current = ""
                for word in words:
                    test = (current + " " + word).strip()
                    if self.font.get_rect(test).width > hint_max_w:
                        self.font.render_to(surf, (detail_card.x + 12, detail_y), current, TEXT_MUTED)
                        detail_y += 16
                        current = word
                    else:
                        current = test
                if current:
                    self.font.render_to(surf, (detail_card.x + 12, detail_y), current, TEXT_MUTED)
                    detail_y += 18

    def _handle_mandela_click(self, mx, my, content_top) -> bool:
        w, h = self.screen.get_size()
        pw = min(w - 40, 1040)
        ph = min(h - content_top - STATUS_H - 20, 680)
        px = (w - pw) // 2
        py = content_top + 10
        if not pygame.Rect(px, py, pw, ph).collidepoint(mx, my):
            return False
        for rect, action in self._mandela_rects:
            if not rect.collidepoint(mx, my):
                continue
            if action == "close":
                self.mandela_panel = False
            elif action == "place_toggle":
                self.mandela_place_mode = not self.mandela_place_mode
                if self.mandela_place_mode and not (self.mandela_target_path.strip() and self.mandela_place_type.strip()):
                    self._set_status("Set target path and prefab path first")
                    self.mandela_place_mode = False
            elif action == "edit:target":
                self._mandela_edit = "target"
                self._mandela_buf = self.mandela_target_path
            elif action == "edit:prefab":
                self._mandela_edit = "prefab"
                self._mandela_buf = self.mandela_place_type
            elif action.startswith("edit:rot:"):
                axis = action.split(":")[-1]
                self._mandela_edit = f"rot:{axis}"
                self._mandela_buf = f"{float(self.mandela_rotation.get(axis, 0.0)):.2f}"
            elif action.startswith("edit:scale:"):
                axis = action.split(":")[-1]
                self._mandela_edit = f"scale:{axis}"
                self._mandela_buf = f"{float(self.mandela_scale.get(axis, 1.0)):.2f}"
            elif action == "toggle:enabled":
                cycle = {"default": "enabled", "enabled": "disabled", "disabled": "default"}
                self.mandela_enabled_mode = cycle.get(self.mandela_enabled_mode, "default")
            elif action == "m_pick_prefab":
                self._pick_mandela_base_path()
            elif action == "m_base_browse":
                self._pick_mandela_base_path()
            elif action == "m_reload_base":
                if self._reload_mandela_base_paths():
                    self._set_status(f"Reloaded {len(self._mandela_base_paths)} base-game prefab paths")
                else:
                    detail = f": {self._mandela_base_error}" if self._mandela_base_error else ""
                    self._set_status("Could not reload dumped-mandelas.txt" + detail)
            elif action == "m_use_selected" and self.sel_mandela_id:
                selected_entry = self._current_selected_mandela()
                if selected_entry is not None:
                    self._load_mandela_draft_from_entry(selected_entry[0], selected_entry[1], duplicate=False)
                    self._set_status(f"Loaded {selected_entry[0]} into the draft")
            elif action == "m_save_selected":
                self._save_selected_mandela()
            elif action == "m_duplicate_selected":
                self._duplicate_selected_mandela()
            elif action.startswith("m_base:"):
                self.mandela_place_type = action[7:]
                self._set_status(f"Base-game Mandela prefab selected: {self.mandela_place_type}")
            elif action.startswith("sel:"):
                self.sel_mandela_id = action[4:]
            elif action == "m_del":
                self._delete_selected_mandela()
            elif action == "m_goto":
                self._goto_selected_mandela()
            return True
        return True

    def _handle_mandela_keydown(self, event) -> bool:
        if not self._mandela_edit:
            return False
        if event.key in (pygame.K_RETURN, pygame.K_KP_ENTER):
            try:
                if self._mandela_edit == "target":
                    self.mandela_target_path = self._mandela_buf.strip()
                elif self._mandela_edit == "prefab":
                    self.mandela_place_type = self._mandela_buf.strip()
                elif self._mandela_edit.startswith("rot:"):
                    axis = self._mandela_edit.split(":")[-1]
                    self.mandela_rotation[axis] = float(self._mandela_buf.strip() or "0")
                elif self._mandela_edit.startswith("scale:"):
                    axis = self._mandela_edit.split(":")[-1]
                    self.mandela_scale[axis] = float(self._mandela_buf.strip() or "1")
            except ValueError:
                self._set_status("Mandela fields need numeric values")
                return True
            self._mandela_edit = ""
            self._mandela_buf = ""
            return True
        elif event.key == pygame.K_ESCAPE:
            self._mandela_edit = ""
            self._mandela_buf = ""
            return True
        elif event.key == pygame.K_BACKSPACE:
            self._mandela_buf = self._mandela_buf[:-1]
            return True
        elif event.unicode and event.unicode.isprintable():
            self._mandela_buf += event.unicode
            return True
        return False

    def _place_mandela_at(self, sx, sy):
        if not self.mod_project:
            return
        target_path = self.mandela_target_path.strip()
        prefab_path = self.mandela_place_type.strip()
        if "/" not in target_path:
            self._set_status("Mandela target path must look like Root/Child")
            return
        if "/" not in prefab_path:
            self._set_status("Mandela prefab path must look like Root/Child")
            return
        if self._mandela_base_paths and prefab_path not in self._mandela_base_paths:
            self._set_status("Prefab path not found in base-game dumped-mandelas.txt")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        ux, uz = self.screen_to_unity(sx, sy)
        uy = self._sample_terrain_y(ux, uz) or 0
        existed = target_path in (graph._raw.get("mandelas") or {})
        self._push_undo(f"{'update' if existed else 'place'} mandela {target_path}")
        enabled, force_enabled = self._mandela_enabled_args()
        mandela_set(
            graph,
            target_path,
            prefab_path,
            ux,
            uy,
            uz,
            self.mandela_rotation['x'],
            self.mandela_rotation['y'],
            self.mandela_rotation['z'],
            self.mandela_scale['x'],
            self.mandela_scale['y'],
            self.mandela_scale['z'],
            enabled=enabled,
            force_enabled=force_enabled,
        )
        graph.save()
        self.sel_mandela_id = target_path
        self._set_status(f"{'Updated' if existed else 'Placed'} mandela {target_path}")

    def _delete_selected_mandela(self):
        if not self.sel_mandela_id or not self.mod_project:
            return
        for layer in self.mod_project.layers:
            if self.sel_mandela_id in (layer._raw.get("mandelas") or {}):
                self._push_undo(f"del mandela {self.sel_mandela_id}")
                mandela_delete(layer, self.sel_mandela_id)
                layer.save()
                self._set_status(f"Deleted {self.sel_mandela_id}")
                self.sel_mandela_id = None
                return

    def _goto_selected_mandela(self):
        if not self.sel_mandela_id or not self.mod_project:
            return
        for layer in self.mod_project.layers:
            mv = (layer._raw.get("mandelas") or {}).get(self.sel_mandela_id)
            if mv:
                pos = mv.get("localPosition", {})
                if not pos:
                    self._set_status("Selected mandela has no local position")
                    return
                sx2, sy2 = self.unity_to_screen(pos.get("x", 0), pos.get("z", 0))
                w2, h2 = self.screen.get_size()
                self.pan_x += w2 // 2 - sx2
                self.pan_y += h2 // 2 - sy2
                self._set_status(f"Centered on {self.sel_mandela_id}")
                return

    def _open_progression_editor(self):
        """Load or reload the ProgressionProject from the current mod."""
        if not self.mod_project or not _MOD_AVAILABLE:
            self._set_status("Load a mod first")
            return
        try:
            self.prog_project = ProgressionProject(self.mod_project)
            self.prog_panel   = True
            self.area_panel   = False
            self._set_status(
                f"Progression editor: {len(self.prog_project.sections)} sections  "
                f"{len(self.prog_project.features)} features")
        except Exception as ex:
            self._set_status(f"Progression load failed: {ex}")
            import traceback; traceback.print_exc()

    def _open_area_editor(self):
        """Load or reload areas from the current mod."""
        if not self.mod_project or not _MOD_AVAILABLE:
            self._set_status("Load a mod first")
            return
        if not self.prog_project:
            self.prog_project = ProgressionProject(self.mod_project)
        if self.area_sel_id not in self.prog_project.areas:
            ordered = sorted(
                self.prog_project.areas.values(),
                key=lambda a: (a.order, a.name.lower(), a.id.lower())
            )
            self.area_sel_id = ordered[0].id if ordered else None
            self.area_sel_industry = None
            self.area_sel_component = None
        area = self._selected_area_obj()
        if area and self.area_sel_industry not in area.industries:
            self.area_sel_industry = None
            self.area_sel_component = None
        self.area_panel  = True
        self.prog_panel  = False
        self._set_status(
            f"Area editor: {len(self.prog_project.areas)} areas loaded")

    # ------------------------------------------------------------------
    def _draw_progression_panel(self, surf, content_top):
        """Progression editor panel."""
        if not self.prog_panel or not self.prog_project:
            return
        w, h  = surf.get_size()
        pw    = min(w - 40, 1100)
        ph    = h - content_top - STATUS_H - 20
        px    = (w - pw) // 2
        py    = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 186))
        surf.blit(overlay, (0, content_top))
        self._prog_panel_bounds = pygame.Rect(px, py, pw, ph)
        self._prog_action_rects = []
        self._prog_sec_rects = []
        self._prog_feat_rects = []
        pygame.draw.rect(surf, PANEL_ELEVATED_BG, self._prog_panel_bounds, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, self._prog_panel_bounds, 1, border_radius=12)
        header_rect = pygame.Rect(px, py, pw, 58)
        pygame.draw.rect(surf, PANEL_HEADER_BG, header_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, header_rect, 1, border_radius=12)
        pygame.draw.rect(surf, ACCENT_COLOR, (px, py + 54, pw, 4), border_radius=2)

        # X close
        xbtn = pygame.Rect(px + pw - 30, py + 8, 22, 22)
        self._prog_close_rect = xbtn
        hx   = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180,60,60) if hx else (80,40,40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px+pw-24, py+11), "✕", (220,200,200))

        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236,216,216))
        cx = px + 16; cy = py + 14
        self.font_big.render_to(surf, (cx, cy), "Progression Editor", ACCENT_COLOR)
        cy += 20

        pp = self.prog_project
        dirty_lbl = "  ●" if pp.dirty else ""
        self.font.render_to(surf, (cx, cy),
            f"{len(pp.sections)} sections   {len(pp.features)} features{dirty_lbl}",
            TEXT_SOFT)
        chip_right = xbtn.x - 8
        for text_value, color_value in reversed([
                ("Unlock chain", TEXT_COLOR),
                ("Features", TEXT_SOFT),
                ("Dirty" if pp.dirty else "Saved", WARN_COLOR if pp.dirty else OK_COLOR),
        ]):
            chip_w = self.font.get_rect(text_value).width + 14
            chip = pygame.Rect(chip_right - chip_w, py + 22, chip_w, 18)
            pygame.draw.rect(surf, PANEL_SECTION_BG, chip, border_radius=9)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, chip, 1, border_radius=9)
            self.font.render_to(surf, (chip.x + 7, chip.y + 3), text_value, color_value)
            chip_right = chip.x - 8
        cy = py + 72

        # Action buttons
        bx2 = cx
        for lbl2, act2, col2 in [
                ("+ Section",  "prog_add_section",  (0,140,180)),
                ("+ Feature",  "prog_add_feature",  (0,140,180)),
                ("Save",       "prog_save",          (220,140,0)),
        ]:
            bw2 = self.font_big.get_rect(lbl2).width + 16
            r2  = pygame.Rect(bx2, cy, bw2, 24)
            hv2 = r2.collidepoint(mx0, my0)
            pygame.draw.rect(surf, col2 if hv2 else tuple(v//2 for v in col2),
                             r2, border_radius=4)
            pygame.draw.rect(surf, col2, r2, 1, border_radius=4)
            self.font_big.render_to(surf, (bx2+8, cy+5), lbl2, (220,230,240))
            if not hasattr(self, '_prog_action_rects'):
                self._prog_action_rects = []
            self._prog_action_rects.append((r2, act2))
            bx2 += bw2 + 8
        cy += 32

        def trim_text(font_obj, text, max_w):
            text = str(text)
            if font_obj.get_rect(text).width <= max_w:
                return text
            ellipsis = "..."
            clipped = text
            while clipped and font_obj.get_rect(clipped + ellipsis).width > max_w:
                clipped = clipped[:-1]
            return (clipped or text[:1]) + ellipsis

        # Two columns: sections (left) | features (right)
        col_w   = (pw - 32) // 2
        sec_x   = cx
        feat_x  = cx + col_w + 8
        row_h   = 40

        sec_card = pygame.Rect(sec_x, cy, col_w, ph - (cy - py) - 70)
        feat_card = pygame.Rect(feat_x, cy, col_w, ph - (cy - py) - 70)
        pygame.draw.rect(surf, PANEL_SECTION_BG, sec_card, border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, sec_card, 1, border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_BG, feat_card, border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, feat_card, 1, border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_ALT, (sec_card.x, sec_card.y, sec_card.width, 32), border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_ALT, (feat_card.x, feat_card.y, feat_card.width, 32), border_radius=10)
        pygame.draw.rect(surf, ACCENT_COLOR, (sec_card.x, sec_card.y + 28, sec_card.width, 3), border_radius=2)
        pygame.draw.rect(surf, (0, 188, 170), (feat_card.x, feat_card.y + 28, feat_card.width, 3), border_radius=2)
        sec_title = "Sections"
        feat_title = "Features"
        sec_title_x = sec_x + 10
        feat_title_x = feat_x + 10
        self.font_big.render_to(surf, (sec_title_x, cy + 8), sec_title, ACCENT_COLOR)
        self.font.render_to(
            surf,
            (sec_title_x + self.font_big.get_rect(sec_title).width + 10, cy + 11),
            trim_text(self.font, "Purchase chain / prerequisites", sec_card.width - self.font_big.get_rect(sec_title).width - 28),
            TEXT_MUTED,
        )
        self.font_big.render_to(surf, (feat_title_x, cy + 8), feat_title, (0, 188, 170))
        self.font.render_to(
            surf,
            (feat_title_x + self.font_big.get_rect(feat_title).width + 10, cy + 11),
            trim_text(self.font, "What each unlock enables", feat_card.width - self.font_big.get_rect(feat_title).width - 28),
            TEXT_MUTED,
        )
        cy += 40

        chain    = pp.section_chain()
        features = list(pp.features.values())
        max_rows = max(1, (sec_card.bottom - cy - 14) // row_h)

        for i, sec in enumerate(chain[self.prog_scroll: self.prog_scroll + max_rows]):
            ry     = cy + i * row_h
            is_sel = sec.id == self.prog_sel_section
            r_sec  = pygame.Rect(sec_x + 8, ry, col_w - 16, row_h-2)
            if is_sel:
                pygame.draw.rect(surf, ROW_ACTIVE_BG, r_sec, border_radius=5)
                pygame.draw.rect(surf, ROW_ACTIVE_BORDER, r_sec, 1, border_radius=5)
            elif r_sec.collidepoint(mx0, my0):
                pygame.draw.rect(surf, ROW_HOVER_BG, r_sec, border_radius=5)
            else:
                pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, r_sec, border_radius=5)
            col_s  = ACCENT_COLOR if is_sel else TEXT_COLOR
            prereq = ", ".join(sec.prerequisites) if sec.prerequisites else "—"
            cost   = sec.delivery_phases[0].get('cost',0) if sec.delivery_phases else 0
            inner_w = r_sec.width - 20
            cost_str = f"  ${cost:,}"
            cost_w = self.font.get_rect(cost_str).width
            prereq_str = trim_text(self.font, f"{sec.display_name}  prereq:{prereq}", inner_w - cost_w)
            self.font_big.render_to(surf, (r_sec.x+10, ry+4),
                trim_text(self.font_big, sec.id, inner_w), col_s)
            self.font.render_to(surf, (r_sec.x+10, ry+17),
                prereq_str + cost_str,
                TEXT_SOFT if is_sel else TEXT_MUTED)
            self._prog_sec_rects.append((r_sec, sec.id))

        for i, feat in enumerate(features[self.prog_scroll: self.prog_scroll + max_rows]):
            ry      = cy + i * row_h
            is_sel  = feat.id == self.prog_sel_feature
            r_feat  = pygame.Rect(feat_x + 8, ry, col_w - 16, row_h-2)
            if is_sel:
                pygame.draw.rect(surf, ROW_ACTIVE_ALT_BG, r_feat, border_radius=5)
                pygame.draw.rect(surf, (110, 216, 196), r_feat, 1, border_radius=5)
            elif r_feat.collidepoint(mx0, my0):
                pygame.draw.rect(surf, ROW_HOVER_BG, r_feat, border_radius=5)
            else:
                pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, r_feat, border_radius=5)
            col_f   = (96, 230, 210) if is_sel else TEXT_COLOR
            areas_s = ", ".join(feat.areas_enable[:3])
            if len(feat.areas_enable) > 3:
                areas_s += f"…+{len(feat.areas_enable)-3}"
            inner_w = r_feat.width - 20
            self.font_big.render_to(surf, (r_feat.x+10, ry+4),
                trim_text(self.font_big, feat.id, inner_w), col_f)
            self.font.render_to(surf, (r_feat.x+10, ry+17),
                trim_text(self.font, f"{feat.display_name}  areas:{areas_s or '—'}", inner_w),
                TEXT_SOFT if is_sel else TEXT_MUTED)
            self._prog_feat_rects.append((r_feat, feat.id))

        detail_rect = pygame.Rect(cx, py + ph - 128, pw - 32, 108)
        pygame.draw.rect(surf, PANEL_SECTION_BG, detail_rect, border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, detail_rect, 1, border_radius=10)
        self.font_big.render_to(surf, (detail_rect.x + 12, detail_rect.y + 8), "Selection", TEXT_COLOR)

        # Selected section detail box
        if self.prog_sel_section and self.prog_sel_section in pp.sections:
            sec  = pp.sections[self.prog_sel_section]
            dy   = detail_rect.y + 30
            self.font.render_to(surf, (detail_rect.x + 12, dy),
                f"Selected section: {sec.id}", ACCENT_COLOR)
            dy += 15
            self.font.render_to(surf, (detail_rect.x + 12, dy),
                f"  Name: {sec.display_name}", TEXT_COLOR)
            dy += 14
            self.font.render_to(surf, (detail_rect.x + 12, dy),
                f"  Prereqs: {', '.join(sec.prerequisites) or 'none'}  "
                f"Cost: ${sec.delivery_phases[0].get('cost',0):,}  "
                f"Enables: {', '.join(sec.enable_features) or 'none'}",
                TEXT_SOFT)
            dy += 14
            # Delete button
            del_r = pygame.Rect(detail_rect.right - 96, detail_rect.y + 62, 84, 18)
            hdel  = del_r.collidepoint(mx0, my0)
            pygame.draw.rect(surf, (180,60,60) if hdel else (80,30,30), del_r, border_radius=3)
            pygame.draw.rect(surf, (220,80,80), del_r, 1, border_radius=3)
            self.font.render_to(surf, (del_r.x+10, del_r.y+2), "Delete", (220,200,200))
            self._prog_action_rects.append((del_r, "prog_del_section"))

        elif self.prog_sel_feature and self.prog_sel_feature in pp.features:
            feat = pp.features[self.prog_sel_feature]
            dy   = detail_rect.y + 30
            self.font.render_to(surf, (detail_rect.x + 12, dy),
                f"Selected feature: {feat.id}", (96, 230, 210))
            dy += 15
            self.font.render_to(surf, (detail_rect.x + 12, dy),
                f"  Name: {feat.display_name}", TEXT_COLOR)
            dy += 14
            self.font.render_to(surf, (detail_rect.x + 12, dy),
                f"  Areas unlocked: {', '.join(feat.areas_enable) or 'none'}",
                TEXT_SOFT)
            dy += 14
            del_r = pygame.Rect(detail_rect.right - 96, detail_rect.y + 62, 84, 18)
            hdel  = del_r.collidepoint(mx0, my0)
            pygame.draw.rect(surf, (180,60,60) if hdel else (80,30,30), del_r, border_radius=3)
            pygame.draw.rect(surf, (220,80,80), del_r, 1, border_radius=3)
            self.font.render_to(surf, (del_r.x+10, del_r.y+2), "Delete", (220,200,200))
            self._prog_action_rects.append((del_r, "prog_del_feature"))

    # ------------------------------------------------------------------
    def _draw_area_panel(self, surf, content_top):
        """Area / town editor panel."""
        if not self.area_panel or not self.prog_project:
            return
        w, h  = surf.get_size()
        pw    = min(w - 40, 1100)
        ph    = h - content_top - STATUS_H - 20
        px    = (w - pw) // 2
        py    = content_top + 10
        mx0, my0 = pygame.mouse.get_pos()

        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 186))
        surf.blit(overlay, (0, content_top))
        header_h = 112
        pygame.draw.rect(surf, PANEL_ELEVATED_BG, (px, py, pw, ph), border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, (px, py, pw, ph), 1, border_radius=12)
        pygame.draw.rect(surf, PANEL_HEADER_BG, (px, py, pw, header_h), border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, (px, py, pw, header_h), 1, border_radius=12)
        pygame.draw.rect(surf, ACCENT_COLOR, (px, py + header_h - 4, pw, 4), border_radius=2)
        self._area_panel_bounds = pygame.Rect(px, py, pw, ph)
        self._area_action_rects = []
        self._area_list_rects = []
        self._area_ind_rects = []
        self._area_comp_rects = []

        # X close
        xbtn = pygame.Rect(px + pw - 30, py + 8, 22, 22)
        self._area_close_rect = xbtn
        hx   = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180,60,60) if hx else (80,40,40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236,216,216))
        self.font_big.render_to(surf, (px+pw-24, py+11), "✕", (220,200,200))
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236,216,216))
        cx = px + 16; cy = py + 14
        pp = self.prog_project
        if self.area_sel_id not in pp.areas:
            self.area_sel_id = None
            self.area_sel_industry = None
            self.area_sel_component = None
        area = self._selected_area_obj()
        if area and self.area_sel_industry not in area.industries:
            self.area_sel_industry = None
            self.area_sel_component = None
        ind = self._selected_industry_obj()
        if ind and self.area_sel_component not in ind.components:
            self.area_sel_component = None
        comp = self._selected_component_entry()

        def trim_text(font_obj, text, max_w):
            text = str(text)
            if font_obj.get_rect(text).width <= max_w:
                return text
            ellipsis = "..."
            clipped = text
            while clipped and font_obj.get_rect(clipped + ellipsis).width > max_w:
                clipped = clipped[:-1]
            return (clipped or text[:1]) + ellipsis

        def draw_action_button(x, y, label, action, color, enabled=True, big=False):
            font_obj = self.font_big if big else self.font
            bw = font_obj.get_rect(label).width + (16 if big else 12)
            rect = pygame.Rect(x, y, bw, 24 if big else 18)
            hover = rect.collidepoint(mx0, my0)
            fill = color if (enabled and hover) else tuple(max(20, v // 2) for v in color)
            border = color if enabled else (70, 80, 95)
            text_col = (220, 230, 240) if enabled else (120, 130, 145)
            if not enabled:
                fill = (32, 38, 46) if hover else (24, 28, 34)
            pygame.draw.rect(surf, fill, rect, border_radius=4 if big else 3)
            pygame.draw.rect(surf, border, rect, 1, border_radius=4 if big else 3)
            font_obj.render_to(
                surf,
                (x + (8 if big else 6), y + (5 if big else 2)),
                label,
                text_col
            )
            if enabled:
                self._area_action_rects.append((rect, action))
            return rect

        def draw_chip_flow(x, y, chip_items, max_right):
            chip_h = 18
            row_gap = 6
            cur_x = x
            cur_y = y
            for text_value, color_value in chip_items:
                chip_w = self.font.get_rect(text_value).width + 14
                if cur_x + chip_w > max_right:
                    cur_x = x
                    cur_y += chip_h + row_gap
                chip = pygame.Rect(cur_x, cur_y, chip_w, chip_h)
                pygame.draw.rect(surf, PANEL_SECTION_BG, chip, border_radius=9)
                pygame.draw.rect(surf, PANEL_SECTION_BORDER, chip, 1, border_radius=9)
                self.font.render_to(surf, (chip.x + 7, chip.y + 3), text_value, color_value)
                cur_x = chip.right + 8
            return cur_y + chip_h

        def draw_action_flow(x, y, button_items, max_right):
            cur_x = x
            cur_y = y
            button_h = 24
            for label, action, color, enabled in button_items:
                bw = self.font_big.get_rect(label).width + 16
                if cur_x + bw > max_right:
                    cur_x = x
                    cur_y += button_h + 8
                rect = draw_action_button(cur_x, cur_y, label, action, color, enabled=enabled, big=True)
                cur_x = rect.right + 8
            return cur_y + button_h

        self.font_big.render_to(surf, (cx, cy), "Town Editor", ACCENT_COLOR)
        dirty_count = len(self._area_dirty_layers)
        subtitle = f"{len(pp.areas)} areas loaded"
        if dirty_count:
            subtitle += f"   {dirty_count} unsaved town layer(s)"
        cy += 20
        self.font.render_to(surf, (cx, cy), subtitle, TEXT_SOFT)
        pygame.draw.rect(surf, (180,60,60) if hx else (80,40,40), xbtn, border_radius=4)
        pygame.draw.rect(surf, (220,80,80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236,216,216))

        chip_bottom = draw_chip_flow(cx, cy + 20, [
            (f"{len(pp.areas)} towns", TEXT_COLOR),
            (f"{len(area.industries) if area else 0} industries", (110, 216, 160)),
            (f"{len(ind.components) if ind else 0} components", (232, 190, 92)),
            ("Dirty" if dirty_count else "Saved", WARN_COLOR if dirty_count else OK_COLOR),
        ], px + pw - 44)

        self._area_action_rects = []
        self._area_list_rects = []
        self._area_ind_rects = []
        self._area_comp_rects = []

        actions_bottom = draw_action_flow(cx, chip_bottom + 10, [
            ("+ Area", "area_add", (0,140,180), True),
            ("Edit Area", "area_edit", (0,170,210), area is not None),
            ("+ Industry", "industry_add", (0,150,110), area is not None),
            ("Save", "area_save", (220,140,0), bool(self._area_dirty_layers) or area is not None),
        ], px + pw - 44)
        cy = max(py + header_h + 14, actions_bottom + 14)

        col_gap = 12
        col_w = (pw - 32 - col_gap * 2) // 3
        area_x = cx
        ind_x = area_x + col_w + col_gap
        comp_x = ind_x + col_w + col_gap
        row_h = 34
        detail_h = 158

        areas_sorted = sorted(
            pp.areas.values(),
            key=lambda a: (a.order, a.name.lower(), a.id.lower())
        )
        comp_count = len(ind.components) if ind else 0
        list_bottom = py + ph - detail_h
        area_card = pygame.Rect(area_x, cy, col_w, list_bottom - cy - 10)
        ind_card = pygame.Rect(ind_x, cy, col_w, list_bottom - cy - 10)
        comp_card = pygame.Rect(comp_x, cy, col_w, list_bottom - cy - 10)
        for rect, title, subtitle, accent in [
            (area_card, "Areas", f"{len(areas_sorted)} total", ACCENT_COLOR),
            (ind_card, "Industries", f"{len(area.industries) if area else 0} shown", (110, 216, 160)),
            (comp_card, "Components", f"{comp_count} shown", (232, 190, 92)),
        ]:
            pygame.draw.rect(surf, PANEL_SECTION_BG, rect, border_radius=10)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, rect, 1, border_radius=10)
            pygame.draw.rect(
                surf,
                PANEL_SECTION_ALT,
                (rect.x, rect.y, rect.width, 36),
                border_top_left_radius=10,
                border_top_right_radius=10,
            )
            pygame.draw.rect(surf, accent, (rect.x, rect.y + 32, rect.width, 3), border_radius=2)
            self.font_big.render_to(surf, (rect.x + 10, rect.y + 8), title, accent)
            title_w = self.font_big.get_rect(title).width
            self.font.render_to(surf, (rect.x + 22 + title_w, rect.y + 12), subtitle, TEXT_MUTED)
        cy += 46
        list_top = cy
        max_rows = max(4, (list_bottom - list_top) // row_h)
        max_scroll = max(0, len(areas_sorted) - max_rows)
        self.area_scroll = max(0, min(self.area_scroll, max_scroll))

        def draw_placeholder(x, text):
            self.font.render_to(surf, (x + 12, list_top + 12), text, TEXT_MUTED)

        def draw_more_count(x, y, total, shown, color):
            if total > shown:
                self.font.render_to(surf, (x + 12, y + 4),
                                    f"... {total - shown} more", color)

        shown_areas = areas_sorted[self.area_scroll:self.area_scroll + max_rows]
        for i, area_row in enumerate(shown_areas):
            ry = list_top + i * row_h
            is_sel = area_row.id == self.area_sel_id
            r_area = pygame.Rect(area_x + 8, ry, col_w - 16, row_h - 2)
            li2 = pp.area_layer.get(area_row.id)
            layer = self.mod_project.layers[li2] if li2 is not None else None
            lcol = layer.color if layer else (140,140,140)
            if is_sel:
                pygame.draw.rect(surf, ROW_ACTIVE_BG, r_area, border_radius=5)
                pygame.draw.rect(surf, ROW_ACTIVE_BORDER, r_area, 1, border_radius=5)
            elif r_area.collidepoint(mx0, my0):
                pygame.draw.rect(surf, ROW_HOVER_BG, r_area, border_radius=5)
            else:
                pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, r_area, border_radius=5)
            dot_x = r_area.x + 10
            pygame.draw.circle(surf, lcol, (dot_x, ry + 10), 4)
            inner_w = r_area.width - 34
            name = trim_text(self.font_big, area_row.name, inner_w)
            meta = trim_text(
                self.font,
                f"{area_row.id}  |  ord {area_row.order}  |  {len(area_row.industries)} ind",
                r_area.width - 20
            )
            self.font_big.render_to(surf, (dot_x + 10, ry + 4), name,
                                    ACCENT_COLOR if is_sel else TEXT_COLOR)
            self.font.render_to(surf, (r_area.x + 10, ry + 21), meta,
                                TEXT_SOFT if is_sel else TEXT_MUTED)
            self._area_list_rects.append((r_area, area_row.id))

        if self.area_scroll > 0:
            self.font.render_to(surf, (area_x + col_w - 18, list_top - 14), "^", TEXT_MUTED)
        if self.area_scroll + max_rows < len(areas_sorted):
            self.font.render_to(surf, (area_x + col_w - 18, list_top + max_rows * row_h),
                                "v", TEXT_MUTED)

        if area:
            industry_items = list(area.industries.items())
            shown_industries = industry_items[:max_rows]
            for i, (iid, ind_row) in enumerate(shown_industries):
                ry = list_top + i * row_h
                is_sel = iid == self.area_sel_industry
                r_ind = pygame.Rect(ind_x + 8, ry, col_w - 16, row_h - 2)
                if is_sel:
                    pygame.draw.rect(surf, ROW_ACTIVE_ALT_BG, r_ind, border_radius=5)
                    pygame.draw.rect(surf, (110, 216, 160), r_ind, 1, border_radius=5)
                elif r_ind.collidepoint(mx0, my0):
                    pygame.draw.rect(surf, ROW_HOVER_BG, r_ind, border_radius=5)
                else:
                    pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, r_ind, border_radius=5)
                self.font_big.render_to(
                    surf, (r_ind.x + 10, ry + 4),
                    trim_text(self.font_big, ind_row.name or iid, col_w - 16),
                    (100, 230, 170) if is_sel else TEXT_COLOR
                )
                self.font.render_to(
                    surf, (r_ind.x + 10, ry + 21),
                    trim_text(self.font, f"{iid}  |  {len(ind_row.components)} comp", col_w - 16),
                    TEXT_SOFT if is_sel else TEXT_MUTED
                )
                self._area_ind_rects.append((r_ind, iid))
            draw_more_count(ind_x, list_top + len(shown_industries) * row_h,
                            len(industry_items), len(shown_industries), TEXT_MUTED)
        else:
            draw_placeholder(ind_x, "Select an area to edit industries")

        if ind:
            component_items = list(ind.components.items())
            shown_components = component_items[:max_rows]
            for i, (cid, comp_row) in enumerate(shown_components):
                if not isinstance(comp_row, dict):
                    continue
                ry = list_top + i * row_h
                is_sel = cid == self.area_sel_component
                r_comp = pygame.Rect(comp_x + 8, ry, col_w - 16, row_h - 2)
                if is_sel:
                    pygame.draw.rect(surf, (76, 60, 24), r_comp, border_radius=5)
                    pygame.draw.rect(surf, (232, 190, 92), r_comp, 1, border_radius=5)
                elif r_comp.collidepoint(mx0, my0):
                    pygame.draw.rect(surf, ROW_HOVER_BG, r_comp, border_radius=5)
                else:
                    pygame.draw.rect(surf, PANEL_SECTION_ALT if i % 2 == 0 else ROW_ALT_BG, r_comp, border_radius=5)
                comp_name = comp_row.get("name") or cid or "<unnamed>"
                type_name = comp_row.get("type", "").split(".")[-1] or "Component"
                spans = comp_row.get("trackSpans", [])
                span_count = len(spans) if isinstance(spans, list) else 0
                meta = f"{cid}  |  {type_name}  |  {span_count} span"
                if span_count != 1:
                    meta += "s"
                self.font_big.render_to(
                    surf, (r_comp.x + 10, ry + 4),
                    trim_text(self.font_big, comp_name, col_w - 16),
                    (245, 200, 100) if is_sel else TEXT_COLOR
                )
                self.font.render_to(
                    surf, (r_comp.x + 10, ry + 21),
                    trim_text(self.font, meta, col_w - 16),
                    TEXT_SOFT if is_sel else TEXT_MUTED
                )
                self._area_comp_rects.append((r_comp, cid))
            draw_more_count(comp_x, list_top + len(shown_components) * row_h,
                            len(component_items), len(shown_components), TEXT_MUTED)
        else:
            draw_placeholder(comp_x, "Select an industry to edit components")

        detail_rect = pygame.Rect(cx, py + ph - detail_h + 8, pw - 32, detail_h - 12)
        pygame.draw.rect(surf, PANEL_SECTION_BG, detail_rect, border_radius=10)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, detail_rect, 1, border_radius=10)
        self.font_big.render_to(surf, (detail_rect.x + 12, detail_rect.y + 8), "Inspector", TEXT_COLOR)
        detail_y = detail_rect.y + 30

        if area:
            layer_idx = pp.area_layer.get(area.id)
            layer_name = ""
            if layer_idx is not None and 0 <= layer_idx < len(self.mod_project.layers):
                layer_name = self.mod_project.layers[layer_idx].path.name
            self.font.render_to(
                surf, (detail_rect.x + 12, detail_y),
                trim_text(
                    self.font,
                    f"Area: {area.name} ({area.id})  file={layer_name or '<unknown>'}  "
                    f"pos=({area.x:.0f}, {area.y:.0f}, {area.z:.0f})",
                    detail_rect.width - 24
                ),
                ACCENT_COLOR
            )
            detail_y += 14
            self.font.render_to(
                surf, (detail_rect.x + 12, detail_y),
                f"Radius={area.radius:.0f}  Order={area.order}  Industries={len(area.industries)}  "
                f"tagColor=({area.tag_color[0]:.2f}, {area.tag_color[1]:.2f}, {area.tag_color[2]:.2f})",
                TEXT_SOFT
            )
            detail_y += 16
        else:
            self.font.render_to(
                surf, (detail_rect.x + 12, detail_y),
                "Select an area to edit a town JSON file.",
                TEXT_SOFT
            )
            detail_y += 16

        if ind:
            local = getattr(ind, "local_position", None)
            if local is None:
                local = getattr(ind, "local_pos", None)
            local = local or {"x": 0.0, "y": 0.0, "z": 0.0}
            self.font.render_to(
                surf, (detail_rect.x + 12, detail_y),
                trim_text(
                    self.font,
                    f"Industry: {ind.name} ({ind.id})  usesContract={str(ind.uses_contract).lower()}  "
                    f"local=({float(local.get('x', 0.0)):.1f}, {float(local.get('y', 0.0)):.1f}, {float(local.get('z', 0.0)):.1f})",
                    detail_rect.width - 24
                ),
                (110, 216, 160)
            )
            detail_y += 14
        if comp:
            extra_keys = [k for k in comp.keys() if k not in ("type", "name", "trackSpans")]
            spans = comp.get("trackSpans", [])
            span_count = len(spans) if isinstance(spans, list) else 0
            extra_text = ", ".join(extra_keys[:6]) if extra_keys else "no extra keys"
            self.font.render_to(
                surf, (detail_rect.x + 12, detail_y),
                trim_text(
                    self.font,
                    f"Component: {self.area_sel_component or '<unnamed>'}  type={comp.get('type', 'unknown')}  "
                    f"trackSpans={span_count}  fields={extra_text}",
                    detail_rect.width - 24
                ),
                (232, 190, 92)
            )
            detail_y += 14

        button_y = detail_rect.bottom - 28
        button_x = detail_rect.x + 12
        button_specs = []
        if area:
            button_specs.extend([
                ("Go to Area", "area_goto", (0,140,180), True),
                ("Edit Area", "area_edit", (0,170,210), True),
                ("Del Area", "area_del", (180,60,60), True),
                ("+ Industry", "industry_add", (0,150,110), True),
            ])
        else:
            button_specs.append(("Edit Area", "area_edit", (0,170,210), False))
        if ind:
            button_specs.extend([
                ("Edit Industry", "industry_edit", (0,170,120), True),
                ("Del Industry", "industry_del", (180,60,60), True),
                ("+ Component", "comp_add", (180,130,40), True),
            ])
        else:
            button_specs.extend([
                ("Edit Industry", "industry_edit", (0,170,120), False),
                ("+ Component", "comp_add", (180,130,40), False),
            ])
        if comp:
            button_specs.extend([
                ("Edit Comp", "comp_edit", (220,170,60), True),
                ("Del Comp", "comp_del", (180,60,60), True),
            ])
        else:
            button_specs.append(("Edit Comp", "comp_edit", (220,170,60), False))

        for label, action, color, enabled in button_specs:
            rect = draw_action_button(button_x, button_y, label, action, color,
                                      enabled=enabled, big=False)
            button_x = rect.right + 6


    # ------------------------------------------------------------------
    # Geometry tools panel — curve generator + parallel tracks
    # ------------------------------------------------------------------
    def _draw_geo_panel(self, surf, content_top):
        """Draw the geometry tools panel."""
        if not self.geo_panel or not _MOD_AVAILABLE:
            return
        # Reset rect caches every frame — rebuilt during draw
        self._geo_tab_rects    = []
        self._geo_field_rects  = []
        self._geo_choice_rects = []
        self._geo_btn_rects    = []
        w, h  = surf.get_size()
        pw    = min(480, max(320, w - 20))
        top_limit = PANEL_H + 6
        bottom_limit = h - STATUS_H - 8
        if getattr(self, 'profile_panel', False):
            bottom_limit = min(bottom_limit, self._profile_panel_top() - 8)
        available_ph = max(260, bottom_limit - top_limit)
        row_h_tab = 26
        tabs = [('guide',   'Spliney',  (0, 170, 200)),
                ('pieces',  'Pieces',   (80, 160, 255)),
                ('curve',   'Arc',      (0, 140, 200)),
                ('parallel','Parallel', (0, 180, 120)),
                ('fit_arc', 'Fit Arc',  (0, 200, 255)),
                ('node',    'Add Node', (200, 140, 0)),
                ('grade',   'Grade',    (180, 80, 200)),
                ('turnout', 'Turnout',  (220, 120, 0)),
                ('wye',     'Wye',      (200, 80, 160))]
        geo_text_w = pw - 24

        def wrap_lines_local(font_obj, text, max_w):
            text = str(text)
            words = text.split()
            if not words:
                return [""]
            lines = []
            line = ""
            for word in words:
                test = (line + " " + word).strip()
                if line and font_obj.get_rect(test).width > max_w:
                    lines.append(line)
                    line = word
                else:
                    line = test
            if line:
                lines.append(line)
            return lines or [text]

        def geo_tab_block_height() -> int:
            row_count = 1
            row_x = 12
            for _mode, lbl, _col in tabs:
                bw2 = self.font_big.get_rect(lbl).width + 18
                if row_x + bw2 > pw - 40:
                    row_count += 1
                    row_x = 12
                row_x += bw2 + 5
            return row_count * row_h_tab + max(0, row_count - 1) * 4

        guide_source_preview = None
        guide_deviation_preview = None
        guide_warnings_preview = None
        guide_has_selection = False

        def estimate_guide_panel_height(compact: bool) -> int:
            guide_line_h = 16 if compact else 18
            guide_row_h = 28 if compact else 32
            total = 10 + 22 + geo_tab_block_height() + 8
            source_preview = guide_source_preview
            spline_defaults_preview = self._spliney_style_defaults(self.geo_spline_style)
            target_layer_preview = self._spliney_target_layer(self.geo_spline_style)
            build_width_preview = (
                float(self.geo_spline_width)
                if float(self.geo_spline_width) > 0.0 else
                float(spline_defaults_preview.get('width', 0.0))
            )
            extend_target_preview = self._selected_flowy_extend_target(self.geo_spline_style)
            intro_text = "Build roads/rivers here: trace a guide path, then create a spliney from it."
            source_text = (
                source_preview.get('label', "Source: select a segment or use the Grade chain")
                if source_preview else
                "Source: select a segment or use the Grade chain"
            )
            if extend_target_preview:
                build_target_text = (
                    f"Build target: {extend_target_preview['layer'].label}   "
                    f"extend {self.sel_spliney_id} {extend_target_preview['side']}"
                )
            else:
                target_label = target_layer_preview.label if target_layer_preview else "No writable layer"
                build_target_text = f"Build target: {target_label}"
            profile_text = (
                f"Profile: {spline_defaults_preview.get('profile', '')}   Width: {build_width_preview:.1f} m"
            )
            point_tools_text = "Point tools: zoom in and click a road/river control dot on the map."
            trace_help_text = (
                "Guide trace active - click map to add points, right-click map or Stop Trace to exit"
                if getattr(self, '_geo_guide_place_mode', False) else
                "Click Trace, then click the map to rough in a guide path"
            )
            draft_help_text = "Guide geometry stays draft-only until you fit an arc or build a spliney."
            total += len(wrap_lines_local(self.font, intro_text, geo_text_w)) * guide_line_h
            total += 2 if compact else 4
            total += 18 + 18 + 18 + 4
            total += len(wrap_lines_local(self.font, source_text, geo_text_w)) * guide_line_h
            total += guide_line_h
            total += len(wrap_lines_local(self.font, build_target_text, geo_text_w)) * guide_line_h
            total += len(wrap_lines_local(self.font, profile_text, geo_text_w)) * guide_line_h
            total += len(wrap_lines_local(self.font, point_tools_text, geo_text_w)) * guide_line_h
            if guide_has_selection:
                _sel_layer, sel_spl_preview = self._selected_flowy_entry()
                sel_pts_preview = list(sel_spl_preview.get('points', [])) if sel_spl_preview else []
                sel_count_preview = len(sel_pts_preview)
                sel_index_preview = min(max(self.sel_spliney_pt, 0), max(0, sel_count_preview - 1))
                range_state_preview = self._current_spliney_range_state()
                current_selection_text = (
                    f"Current selection: {self.sel_spliney_id}[{sel_index_preview}]"
                    + (f"   {sel_index_preview + 1}/{sel_count_preview} pts" if sel_count_preview else "")
                )
                if range_state_preview.get('ready'):
                    range_text_preview = (
                        f"Spliney range: {range_state_preview['start']}..{range_state_preview['end']}  "
                        "Set current Width, then Fill Width, or use Grade tools in the Spliney Panel."
                    )
                elif range_state_preview.get('anchor') is not None:
                    range_text_preview = (
                        f"Width start: {range_state_preview['anchor']}  "
                        "shift-click another point or use Prev/Next."
                    )
                else:
                    range_text_preview = (
                        "Spliney range: Mark Start, then shift-click another point. "
                        "Width fill and Spliney Panel grade tools use that span."
                    )
                total += len(wrap_lines_local(self.font, current_selection_text, geo_text_w)) * guide_line_h
                total += len(wrap_lines_local(self.font, range_text_preview, geo_text_w)) * guide_line_h
                total += guide_row_h * 4
            if guide_deviation_preview and guide_deviation_preview.get('rms_distance') is not None:
                deviation_text = (
                    f"Deviation: RMS {guide_deviation_preview['rms_distance']:.2f} m   "
                    f"Max {guide_deviation_preview['max_distance']:.2f} m"
                )
                total += len(wrap_lines_local(self.font, deviation_text, geo_text_w)) * guide_line_h
            if guide_warnings_preview:
                warnings_text = (
                    f"Radius warnings: {len(guide_warnings_preview)} point(s) under "
                    f"{self.alignment_min_radius_m:.0f} m"
                )
                total += len(wrap_lines_local(self.font, warnings_text, geo_text_w)) * guide_line_h
            total += guide_row_h
            total += guide_row_h
            total += len(wrap_lines_local(self.font, trace_help_text, geo_text_w)) * guide_line_h
            if not compact:
                total += len(wrap_lines_local(self.font, draft_help_text, geo_text_w)) * guide_line_h
            return total + 14

        if self.geo_mode == 'guide':
            guide_source_preview = self._alignment_source_chain()
            guide_deviation_preview = self._alignment_current_deviation(guide_source_preview)
            guide_warnings_preview = self._alignment_current_radius_warnings(guide_source_preview)
            _guide_layer, guide_spl_preview = self._selected_flowy_entry()
            guide_has_selection = bool(self.sel_spliney_id and guide_spl_preview)
            full_guide_ph = estimate_guide_panel_height(compact=False)
            compact_guide_ph = estimate_guide_panel_height(compact=True)
            desired_ph = full_guide_ph if full_guide_ph <= available_ph else compact_guide_ph
        elif self.geo_mode == 'grade':
            # Grade panel grows with the node chain — enough room for node list + apply section
            chain_len = len(self.grade_chain)
            visible_chain_len = min(
                chain_len,
                5 if getattr(self, 'profile_panel', False) else 12,
            )
            desired_ph = max(610, 500 + visible_chain_len * 15)
        else:
            desired_ph = 420
        ph = max(260, min(desired_ph, available_ph))
        px    = w - pw - 10
        normal_py = content_top + 10
        py = max(top_limit, min(normal_py, bottom_limit - ph))
        self._geo_panel_rect = pygame.Rect(px, py, pw, ph)
        geo_compact = bool(self.geo_mode == 'guide' and (py < normal_py or ph < desired_ph))
        mx0, my0 = pygame.mouse.get_pos()

        bg = pygame.Surface((pw, ph), pygame.SRCALPHA)
        bg.fill((8, 11, 18, 235))
        surf.blit(bg, (px, py))
        pygame.draw.rect(surf, (40,60,80), (px,py,pw,ph), 1, border_radius=6)

        # X close
        xbtn = pygame.Rect(px+pw-28, py+6, 20, 20)
        pygame.draw.rect(surf, (180,60,60) if xbtn.collidepoint(mx0,my0) else (80,40,40),
                         xbtn, border_radius=3)
        self.font_big.render_to(surf, (px+pw-22, py+9), "✕", (220,200,200))

        cx = px + 12; cy = py + 10
        self.font_big.render_to(surf, (cx, cy), "Geometry Tools", (0,212,255))
        cy += 22

        # Mode tabs — wrap as needed to keep the panel readable.
        tab_row_x = px + 12
        tab_row_y = cy
        for i, (mode, lbl, col) in enumerate(tabs):
            bw2 = self.font_big.get_rect(lbl).width + 18
            # Wrap to second row if we'd exceed panel width
            if tab_row_x + bw2 > px + pw - 40:
                tab_row_x  = px + 12
                tab_row_y += row_h_tab + 4
            r2  = pygame.Rect(tab_row_x, tab_row_y, bw2, row_h_tab - 2)
            act = self.geo_mode == mode
            hov = r2.collidepoint(mx0, my0)
            pygame.draw.rect(surf,
                col if (act or hov) else tuple(v//3 for v in col),
                r2, border_radius=4)
            if act:
                pygame.draw.rect(surf, col, r2, 1, border_radius=4)
            self.font_big.render_to(surf, (tab_row_x+9, tab_row_y+5), lbl,
                                    (220,230,240) if act else (160,180,200))
            self._geo_tab_rects.append((r2, mode))
            tab_row_x += bw2 + 5
        cy = tab_row_y + row_h_tab + 8
        cx = px + 12
        geo_content_top = cy
        geo_content_view = pygame.Rect(
            px + 2,
            geo_content_top,
            pw - 4,
            max(24, py + ph - geo_content_top - 6),
        )
        self._geo_scroll_view_rect = geo_content_view
        geo_scroll = max(
            0,
            int(self._geo_scroll_by_mode.get(self.geo_mode, 0)),
        )
        geo_scroll = min(
            geo_scroll,
            max(
                0,
                int(self._geo_scroll_max_by_mode.get(self.geo_mode, 0)),
            ),
        )
        self._geo_scroll_by_mode[self.geo_mode] = geo_scroll
        previous_geo_clip = surf.get_clip()
        surf.set_clip(geo_content_view)
        cy -= geo_scroll

        def num_field(label, key, value, width=70, nudge_step=None):
            nonlocal cy
            self.font.render_to(surf, (cx, cy), label+":", (100,120,140))
            fx = cx + 110
            active = self._geo_input_focus == key
            fr = pygame.Rect(fx, cy-2, width, 16)
            pygame.draw.rect(surf, (30,50,70) if active else (20,30,45), fr, border_radius=2)
            pygame.draw.rect(surf, (0,200,255) if active else (40,60,80), fr, 1, border_radius=2)
            disp = (self._geo_input_buf if active else str(value)) + ("_" if active else "")
            self.font.render_to(surf, (fx+3, cy), disp, (180,220,180))
            self._geo_field_rects.append((fr, key, value))
            if nudge_step is not None:
                bx = fr.right + 4
                for label, delta in (('-', -float(nudge_step)), ('+', float(nudge_step))):
                    br = pygame.Rect(bx, cy - 2, 16, 16)
                    hov = br.collidepoint(mx0, my0)
                    pygame.draw.rect(surf, BTN_HOVER_C if hov else BTN_INACTIVE, br, border_radius=3)
                    pygame.draw.rect(surf, BTN_BORDER, br, 1, border_radius=3)
                    self.font.render_to(surf, (br.x + 4, cy), label, TEXT_COLOR)
                    self._geo_btn_rects.append((br, f'geo_nudge:{key}:{delta}', True))
                    bx += 20
            cy += 18

        def choice_row(label, key, options, current):
            nonlocal cy, cx
            self.font.render_to(surf, (cx, cy), label+":", (100,120,140))
            bx3 = cx + 110
            for opt in options:
                bw3 = self.font.get_rect(opt).width + 10
                r3  = pygame.Rect(bx3, cy-1, bw3, 16)
                act3 = current == opt
                hov3 = r3.collidepoint(mx0, my0)
                col3 = (0,140,200) if 'lass' in label else (0,160,120)
                pygame.draw.rect(surf,
                    col3 if act3 else ((30,50,60) if hov3 else (15,22,32)), r3, border_radius=2)
                if act3: pygame.draw.rect(surf, col3, r3, 1, border_radius=2)
                self.font.render_to(surf, (bx3+5, cy), opt,
                    (220,230,240) if act3 else (140,160,180))
                self._geo_choice_rects.append((r3, key, opt))
                bx3 += bw3 + 4
            cy += 18

        def gauge_row():
            """Compact canonical gauge picker shared by every track builder."""
            nonlocal cy
            self.font.render_to(surf, (cx, cy), "Gauge:", (100,120,140))
            bx3 = cx + 110
            choices = [
                ('STD', 'Standard'),
                ('3-FT', 'Narrow'),
                ('DUAL', 'DualGauge'),
                ('L', 'DualGauge_L'),
                ('R', 'DualGauge_R'),
                ('DUAL T', 'DualGauge_T'),
            ]
            current = normalize_track_gauge(
                getattr(self, 'geo_gauge', 'Standard')
            )
            for label3, value3 in choices:
                bw3 = self.font.get_rect(label3).width + 8
                r3 = pygame.Rect(bx3, cy - 1, bw3, 16)
                active3 = current == value3
                hover3 = r3.collidepoint(mx0, my0)
                color3 = (
                    (245, 64, 210)
                    if value3 == 'DualGauge_T'
                    else (110, 184, 255)
                    if value3.startswith('DualGauge')
                    else (255, 122, 20)
                    if value3 == 'Narrow'
                    else (180, 160, 70)
                )
                pygame.draw.rect(
                    surf,
                    color3 if active3 else (
                        tuple(v // 2 for v in color3)
                        if hover3 else (15, 22, 32)
                    ),
                    r3,
                    border_radius=2,
                )
                pygame.draw.rect(surf, color3, r3, 1, border_radius=2)
                self.font.render_to(
                    surf,
                    (bx3 + 4, cy),
                    label3,
                    (245, 245, 245) if active3 else (140, 160, 180),
                )
                self._geo_choice_rects.append((r3, 'geo_gauge', value3))
                bx3 += bw3 + 3
            cy += 20
            if current == 'DualGauge_T':
                self.font.render_to(
                    surf,
                    (cx + 110, cy - 2),
                    "One short L-to-R shared-rail transition segment only",
                    (245, 100, 210),
                )
                cy += 16

        def preview_counts():
            total_nodes = sum(len(entry[0]) for entry in self.geo_preview)
            total_segs = sum(len(entry[1]) for entry in self.geo_preview)
            total_updates = sum(len(entry[2]) if len(entry) > 2 else 0 for entry in self.geo_preview)
            return total_nodes, total_segs, total_updates

        def preview_errors():
            return self._geo_preview_errors()

        def preview_warnings():
            return self._geo_preview_warnings()

        def preview_radius_warnings():
            return self._geo_preview_radius_warnings()

        if self.geo_mode in {
                'pieces', 'curve', 'parallel', 'node', 'turnout', 'wye'}:
            gauge_row()

        if self.geo_mode == 'guide':
            guide_line_h = 16 if geo_compact else 18
            guide_row_h = 28 if geo_compact else 32
            def draw_guide_wrapped(text, color):
                nonlocal cy
                for line in wrap_lines_local(self.font, text, geo_text_w):
                    self.font.render_to(surf, (cx, cy), line, color)
                    cy += guide_line_h
            source = guide_source_preview
            deviation = guide_deviation_preview
            warnings = guide_warnings_preview
            guide_len = alignment_polyline_length(self._alignment_guide_points_xz())
            spline_defaults = self._spliney_style_defaults(self.geo_spline_style)
            target_layer = self._spliney_target_layer(self.geo_spline_style)
            build_width = (
                float(self.geo_spline_width)
                if float(self.geo_spline_width) > 0.0 else
                float(spline_defaults.get('width', 0.0))
            )
            draw_guide_wrapped(
                "Build roads/rivers here: trace a guide path, then create a spliney from it.",
                (100, 120, 140),
            )
            cy += 2 if geo_compact else 4
            num_field("Warn radius (m)", 'alignment_min_radius_m', self.alignment_min_radius_m, width=80)
            choice_row("Spline type", 'geo_spline_style', ['Road', 'River'], self.geo_spline_style)
            num_field("Spline width", 'geo_spline_width', self.geo_spline_width, width=80)
            cy += 4
            draw_guide_wrapped(
                source.get('label', "Source: select a segment or use the Grade chain")
                if source else
                "Source: select a segment or use the Grade chain",
                (140, 180, 140) if source else (180, 80, 80),
            )
            draw_guide_wrapped(
                f"Guide path: {len(self.alignment_guide_points)} pts   {guide_len:.1f} m",
                (0, 200, 255) if self.alignment_guide_points else (120, 140, 160),
            )
            extend_target = self._selected_flowy_extend_target(self.geo_spline_style)
            if extend_target:
                build_target_label = (
                    f"{extend_target['layer'].label}   extend {self.sel_spliney_id} {extend_target['side']}"
                )
                build_target_col = (0, 200, 255)
            else:
                build_target_label = target_layer.label if target_layer else "No writable layer"
                build_target_col = (140, 180, 140) if target_layer else (180, 80, 80)
            draw_guide_wrapped(f"Build target: {build_target_label}", build_target_col)
            draw_guide_wrapped(
                f"Profile: {spline_defaults.get('profile', '')}   Width: {build_width:.1f} m",
                (120, 140, 160),
            )
            draw_guide_wrapped(
                "Point tools: zoom in and click a road/river control dot on the map.",
                (120, 140, 160) if not self.sel_spliney_id else (0, 200, 255),
            )
            if self.sel_spliney_id:
                sel_layer, sel_spl = self._selected_flowy_entry()
                sel_pts = list(sel_spl.get('points', [])) if sel_spl else []
                sel_count = len(sel_pts)
                sel_index = min(max(self.sel_spliney_pt, 0), max(0, sel_count - 1))
                range_state = self._current_spliney_range_state()
                draw_guide_wrapped(
                    f"Current selection: {self.sel_spliney_id}[{sel_index}]"
                    + (f"   {sel_index + 1}/{sel_count} pts" if sel_count else ""),
                    (0, 200, 255),
                )
                if range_state.get('ready'):
                    range_text = (
                        f"Spliney range: {range_state['start']}..{range_state['end']}  "
                        "Set current Width, then Fill Width, or use Grade tools in the Spliney Panel."
                    )
                    range_col = (255, 210, 110)
                elif range_state.get('anchor') is not None:
                    range_text = (
                        f"Width start: {range_state['anchor']}  "
                        "shift-click another point or use Prev/Next."
                    )
                    range_col = (255, 210, 110)
                else:
                    range_text = (
                        "Spliney range: Mark Start, then shift-click another point. "
                        "Width fill and Spliney Panel grade tools use that span."
                    )
                    range_col = (120, 140, 160)
                draw_guide_wrapped(range_text, range_col)
                bx_sel = cx
                geo_sel_buttons = [
                    ("Prev", "geo_spl_prev", (60, 120, 180), sel_count > 0 and sel_index > 0),
                    ("Next", "geo_spl_next", (60, 120, 180), sel_count > 0 and sel_index + 1 < sel_count),
                    ("Ins Before", "geo_spl_ins_before", (180, 120, 60), sel_count > 0 and sel_index > 0),
                    ("Ins After", "geo_spl_ins_after", (180, 120, 60), sel_count > 0 and sel_index + 1 < sel_count),
                ]
                for label, action, color, enabled in geo_sel_buttons:
                    bw4 = self.font_big.get_rect(label).width + 16
                    r4 = pygame.Rect(bx_sel, cy, bw4, 24)
                    fill = color if enabled else (30, 35, 40)
                    pygame.draw.rect(surf, fill, r4, border_radius=4)
                    pygame.draw.rect(surf, color if enabled else (50, 60, 70), r4, 1, border_radius=4)
                    self.font_big.render_to(
                        surf,
                        (bx_sel + 8, cy + 5),
                        label,
                        (220, 230, 240) if enabled else (80, 90, 100),
                    )
                    self._geo_btn_rects.append((r4, action, enabled))
                    bx_sel += bw4 + 6
                cy += guide_row_h
                bx_sel = cx
                geo_sel_buttons = [
                    ("Sample Y", "geo_spl_sample_y", (0, 140, 120), sel_count > 0),
                    ("Auto Rot", "geo_spl_auto_rot", (140, 120, 220), sel_count >= 2),
                    ("Spliney Panel", "geo_open_spliney_panel", (40, 110, 180), True),
                ]
                for label, action, color, enabled in geo_sel_buttons:
                    bw4 = self.font_big.get_rect(label).width + 16
                    r4 = pygame.Rect(bx_sel, cy, bw4, 24)
                    fill = color if enabled else (30, 35, 40)
                    pygame.draw.rect(surf, fill, r4, border_radius=4)
                    pygame.draw.rect(surf, color if enabled else (50, 60, 70), r4, 1, border_radius=4)
                    self.font_big.render_to(
                        surf,
                        (bx_sel + 8, cy + 5),
                        label,
                        (220, 230, 240) if enabled else (80, 90, 100),
                    )
                    self._geo_btn_rects.append((r4, action, enabled))
                    bx_sel += bw4 + 6
                cy += guide_row_h
                bx_sel = cx
                geo_sel_buttons = [
                    (
                        "Clear Range" if range_state.get('ready')
                        else ("Clear Start" if range_state.get('anchor') is not None else "Mark Start"),
                        "geo_spl_range_anchor",
                        (220, 150, 60),
                        True,
                    ),
                    ("Fill Width", "geo_spl_fill_width", (0, 150, 110), bool(range_state.get('ready'))),
                ]
                for label, action, color, enabled in geo_sel_buttons:
                    bw4 = self.font_big.get_rect(label).width + 16
                    r4 = pygame.Rect(bx_sel, cy, bw4, 24)
                    fill = color if enabled else (30, 35, 40)
                    pygame.draw.rect(surf, fill, r4, border_radius=4)
                    pygame.draw.rect(surf, color if enabled else (50, 60, 70), r4, 1, border_radius=4)
                    self.font_big.render_to(
                        surf,
                        (bx_sel + 8, cy + 5),
                        label,
                        (220, 230, 240) if enabled else (80, 90, 100),
                    )
                    self._geo_btn_rects.append((r4, action, enabled))
                    bx_sel += bw4 + 6
                cy += guide_row_h
                bx_sel = cx
                geo_sel_buttons = [
                    ("Delete Spliney", "geo_spl_delete", (160, 80, 80), True),
                ]
                for label, action, color, enabled in geo_sel_buttons:
                    bw4 = self.font_big.get_rect(label).width + 16
                    r4 = pygame.Rect(bx_sel, cy, bw4, 24)
                    fill = color if enabled else (30, 35, 40)
                    pygame.draw.rect(surf, fill, r4, border_radius=4)
                    pygame.draw.rect(surf, color if enabled else (50, 60, 70), r4, 1, border_radius=4)
                    self.font_big.render_to(
                        surf,
                        (bx_sel + 8, cy + 5),
                        label,
                        (220, 230, 240) if enabled else (80, 90, 100),
                    )
                    self._geo_btn_rects.append((r4, action, enabled))
                    bx_sel += bw4 + 6
                cy += guide_row_h
            if deviation.get('rms_distance') is not None:
                draw_guide_wrapped(
                    f"Deviation: RMS {deviation['rms_distance']:.2f} m   "
                    f"Max {deviation['max_distance']:.2f} m",
                    (255, 210, 100),
                )
            if warnings:
                draw_guide_wrapped(
                    f"Radius warnings: {len(warnings)} point(s) under {self.alignment_min_radius_m:.0f} m",
                    (255, 110, 90),
                )

            place_mode = getattr(self, '_geo_guide_place_mode', False)
            trace_label = "Stop Trace" if place_mode else "Trace"
            trace_color = (0, 190, 140) if place_mode else (0, 160, 120)
            bx4 = cx
            for label, action, color, enabled in [
                    (trace_label, "guide_place_mode", trace_color, True),
                    ("Use Chain", "guide_use_source", (0, 140, 200), bool(source)),
                    ("Undo Pt", "guide_pop_point", (180, 120, 60), bool(self.alignment_guide_points)),
                    ("Clear", "guide_clear", (140, 80, 80), bool(self.alignment_guide_points)),
            ]:
                bw4 = self.font_big.get_rect(label).width + 16
                r4 = pygame.Rect(bx4, cy, bw4, 24)
                fill = color if enabled else (30, 35, 40)
                pygame.draw.rect(surf, fill, r4, border_radius=4)
                pygame.draw.rect(surf, color if enabled else (50, 60, 70), r4, 1, border_radius=4)
                self.font_big.render_to(
                    surf, (bx4 + 8, cy + 5), label,
                    (220, 230, 240) if enabled else (80, 90, 100),
                )
                self._geo_btn_rects.append((r4, action, enabled))
                bx4 += bw4 + 6
            cy += guide_row_h
            build_enabled = bool((extend_target or target_layer) and len(self.alignment_guide_points) >= 2 and build_width > 0.0)
            build_label = f"{'Extend' if extend_target else 'Build'} {self.geo_spline_style}"
            bw4 = self.font_big.get_rect(build_label).width + 16
            r4 = pygame.Rect(cx, cy, bw4, 24)
            fill = (0, 170, 120) if build_enabled else (30, 35, 40)
            pygame.draw.rect(surf, fill, r4, border_radius=4)
            pygame.draw.rect(surf, (0, 170, 120) if build_enabled else (50, 60, 70), r4, 1, border_radius=4)
            self.font_big.render_to(
                surf, (cx + 8, cy + 5), build_label,
                (220, 230, 240) if build_enabled else (80, 90, 100),
            )
            self._geo_btn_rects.append((r4, 'guide_build_spline', build_enabled))
            cy += guide_row_h
            draw_guide_wrapped(
                "Guide trace active - click map to add points, right-click map or Stop Trace to exit"
                if place_mode else
                "Click Trace, then click the map to rough in a guide path",
                (0, 220, 160) if place_mode else (120, 140, 160),
            )
            if not geo_compact:
                draw_guide_wrapped(
                    "Guide geometry stays draft-only until you fit an arc or build a spliney.",
                    (120, 140, 160),
                )

        elif self.geo_mode == 'pieces':
            piece_line_h = 18

            def draw_piece_wrapped(text, color):
                nonlocal cy
                for line in wrap_lines_local(self.font, text, geo_text_w):
                    self.font.render_to(surf, (cx, cy), line, color)
                    cy += piece_line_h

            anchor_pose = self.geo_piece_start_pose or self._geo_piece_anchor_from_selection()
            end_pose = self.geo_preview_meta.get('end_pose') if self.geo_preview_meta.get('mode') == 'pieces' else None
            piece_count = len(self.geo_piece_chain)
            total_length = float(self.geo_preview_meta.get('total_length_m', 0.0)) if self.geo_preview_meta.get('mode') == 'pieces' else 0.0
            total_nodes, total_segs, _total_updates = preview_counts()

            draw_piece_wrapped(
                "Draft track by snapping straights and arcs end-to-end, then bake the whole chain to the graph.",
                (100, 120, 140),
            )
            cy += 4

            start_label = (
                f"Start: {self.geo_piece_start_node_id}"
                if self.geo_piece_start_node_id else
                ("Start: selected node preview" if anchor_pose else "Start: select a node and press Set Start")
            )
            draw_piece_wrapped(start_label, (0, 200, 255) if anchor_pose else (180, 80, 80))
            if end_pose:
                draw_piece_wrapped(
                    f"Current end: ({float(end_pose.get('x', 0.0)):.1f}, {float(end_pose.get('z', 0.0)):.1f})  rotY {float(end_pose.get('rotY', 0.0)):.1f}°",
                    (120, 170, 220),
                )
            draw_piece_wrapped(
                f"Draft: {piece_count} piece(s)  {total_nodes} new node(s)  {total_segs} new seg(s)  {total_length:.1f} m",
                (120, 170, 140) if piece_count else (120, 140, 160),
            )
            cy += 4

            choice_row("Piece", 'geo_piece_type', ['Straight', 'Arc', 'Turnout'], self.geo_piece_type)
            if self.geo_piece_type == 'Straight':
                num_field("Length (m)", 'geo_piece_length', self.geo_piece_length, width=84)
            elif self.geo_piece_type == 'Arc':
                num_field("Radius (m)", 'geo_radius', self.geo_radius, width=84, nudge_step=10.0)
                num_field("Angle (deg)", 'geo_degrees', self.geo_degrees, width=84, nudge_step=5.0)
                choice_row("Turn", 'geo_direction', ['left', 'right'], self.geo_direction)
                num_field("Arc steps", 'geo_n_segs', self.geo_n_segs, width=70)
            elif self.geo_piece_type == 'Turnout':
                num_field("Leg length (m)",  'turnout_leg_length',    self.turnout_leg_length,    width=84)
                num_field("Diverge angle °", 'turnout_diverge_angle', self.turnout_diverge_angle, width=84)
                num_field("Through curve °", 'turnout_through_curve', self.turnout_through_curve, width=84)
                choice_row("Branch", 'turnout_direction', ['left', 'right'], self.turnout_direction)
                choice_row("Div class", 'turnout_div_class',
                           ['Branch', 'Industrial', 'Mainline'], self.turnout_div_class)
                num_field("Div speed", 'turnout_div_speed', self.turnout_div_speed, width=70)
                # Flip stand toggle — reuse same pattern as Turnout tab
                fsp_r = pygame.Rect(cx, cy - 1, 160, 16)
                hfsp  = fsp_r.collidepoint(mx0, my0)
                fcsp  = (0, 180, 100) if self.turnout_flip else (60, 80, 60)
                pygame.draw.rect(surf, fcsp if (self.turnout_flip or hfsp) else (18, 28, 20),
                                 fsp_r, border_radius=2)
                pygame.draw.rect(surf, fcsp, fsp_r, 1, border_radius=2)
                self.font.render_to(surf, (cx + 5, cy),
                    "Flip stand: " + ("YES" if self.turnout_flip else "NO"),
                    (220, 240, 220) if self.turnout_flip else (120, 140, 120))
                self._geo_btn_rects.append((fsp_r, 'turnout_flip', True))
                cy += 20
            choice_row("Class", 'geo_track_class',
                       ['Mainline', 'Branch', 'Industrial'], self.geo_track_class)
            choice_row("Style", 'geo_style',
                       ['Standard', 'Yard', 'Bridge', 'Tunnel'], self.geo_style)
            num_field("Speed", 'geo_speed', self.geo_speed, width=70)
            cy += 4

            bx4 = cx
            piece_buttons = [
                ("Set Start", "piece_set_start", (0, 140, 200), bool(self.sel_mod_node_id)),
                ("Add Piece", "piece_add", (0, 170, 120), bool(self.mod_project and (self.geo_piece_start_pose or self.sel_mod_node_id))),
                ("Undo Last", "piece_undo", (150, 110, 50), bool(self.geo_piece_chain)),
                ("Clear Draft", "piece_clear", (130, 70, 70), bool(self.geo_piece_chain or self.geo_piece_start_pose)),
            ]
            for lbl4, act4, col4, ena4 in piece_buttons:
                bw4 = self.font.get_rect(lbl4).width + 10
                r4 = pygame.Rect(bx4, cy, bw4, 20)
                hov4 = r4.collidepoint(mx0, my0) and ena4
                pygame.draw.rect(surf, col4 if ena4 else (30, 35, 40), r4, border_radius=3)
                pygame.draw.rect(surf, col4 if ena4 else (50, 60, 70), r4, 1, border_radius=3)
                self.font.render_to(surf, (bx4 + 5, cy + 3), lbl4,
                                    (220, 230, 240) if ena4 else (80, 90, 100))
                self._geo_btn_rects.append((r4, act4, ena4))
                bx4 += bw4 + 5
            cy += 26

            bake_label = "Bake Pieces"
            bake_w = self.font_big.get_rect(bake_label).width + 18
            bake_r = pygame.Rect(cx, cy, bake_w, 24)
            bake_enabled = bool(self.geo_preview)
            pygame.draw.rect(
                surf,
                (0, 160, 120) if bake_enabled else (30, 35, 40),
                bake_r,
                border_radius=4,
            )
            pygame.draw.rect(
                surf,
                (0, 220, 160) if bake_enabled else (50, 60, 70),
                bake_r,
                1,
                border_radius=4,
            )
            self.font_big.render_to(
                surf,
                (cx + 10, cy + 5),
                bake_label,
                (220, 245, 230) if bake_enabled else (80, 90, 100),
            )
            self._geo_btn_rects.append((bake_r, 'geo_commit', bake_enabled))
            cy += 30

            if self.geo_piece_chain:
                self.font.render_to(surf, (cx, cy), "Pieces:", (100, 120, 140))
                cy += 16
                recent = self.geo_piece_chain[-6:]
                start_index = len(self.geo_piece_chain) - len(recent)
                for offset, piece in enumerate(recent, start=1):
                    idx = start_index + offset
                    kind = str(piece.get('kind', 'straight'))
                    if kind == 'straight':
                        line = f"  {idx}. Straight  {float(piece.get('length_m', 0.0)):.1f} m"
                    elif kind == 'turnout':
                        line = (
                            f"  {idx}. Turnout  leg {float(piece.get('leg_length', 0.0)):.1f} m  "
                            f"{float(piece.get('diverge_angle', 0.0)):.1f}°  {piece.get('direction', 'left')}"
                        )
                    else:
                        line = (
                            f"  {idx}. Arc  R {float(piece.get('radius_m', 0.0)):.1f}  "
                            f"{float(piece.get('degrees', 0.0)):.1f}°  {piece.get('direction', 'left')}"
                        )
                    self.font.render_to(surf, (cx, cy), line, (160, 180, 200))
                    cy += 16
            else:
                draw_piece_wrapped(
                    "Workflow: select a node, Set Start once, then keep adding pieces from the last endpoint pose.",
                    (120, 140, 160),
                )

        elif self.geo_mode == 'curve':
            curve_errors = preview_errors()
            curve_warnings = preview_warnings()
            curve_radius_warnings = preview_radius_warnings()
            self.font.render_to(surf, (cx, cy),
                "Generates a constant-radius arc from the selected node", (100,120,140))
            cy += 20
            num_field("Radius (m)",    'geo_radius',   self.geo_radius, nudge_step=10.0)
            num_field("Arc (degrees)", 'geo_degrees',  self.geo_degrees, nudge_step=5.0)
            num_field("Height change", 'geo_height',   self.geo_height)
            num_field("Segments",      'geo_n_segs',   self.geo_n_segs)
            num_field("Speed limit",   'geo_speed',    self.geo_speed)
            num_field("Warn radius",   'alignment_min_radius_m', self.alignment_min_radius_m)
            choice_row("Direction", 'geo_direction', ['left','right'], self.geo_direction)
            choice_row("Class", 'geo_track_class',
                       ['Mainline','Branch','Industrial'],
                       self.geo_track_class)
            node_info = ""
            if self.sel_mod_node_id and self.mod_project:
                n = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
                if n:
                    node_info = (f"From: {self.sel_mod_node_id}  "
                                 f"({n['x']:.0f},{n['y']:.0f},{n['z']:.0f})  "
                                 f"rotY={n.get('rotY',0):.1f}°")
            self.font.render_to(surf, (cx, cy), node_info or "Select a node first",
                                (140,180,140) if node_info else (180,80,80))
            cy += 20

            # Generate / Commit / Cancel buttons
            bx4 = cx
            for lbl4, act4, col4, enabled in [
                    ("Preview",  "geo_preview",  (0,140,200),  bool(self.sel_mod_node_id)),
                    ("Commit",   "geo_commit",   (0,200,100),  self._geo_preview_commit_enabled()),
                    ("Clear",    "geo_clear",    (140,80,80),  bool(self.geo_preview)),
            ]:
                bw4 = self.font_big.get_rect(lbl4).width + 16
                r4  = pygame.Rect(bx4, cy, bw4, 24)
                hov4 = r4.collidepoint(mx0,my0) and enabled
                c4   = col4 if (hov4 or enabled and not hov4) else (40,50,60)
                if not enabled: c4 = (30,35,40)
                pygame.draw.rect(surf, c4, r4, border_radius=4)
                pygame.draw.rect(surf, col4 if enabled else (50,60,70), r4, 1, border_radius=4)
                self.font_big.render_to(surf, (bx4+8, cy+5), lbl4,
                    (220,230,240) if enabled else (80,90,100))
                self._geo_btn_rects.append((r4, act4, enabled))
                bx4 += bw4 + 6

            # Preview stats
            if self.geo_preview:
                total_n, total_s, total_u = preview_counts()
                cy += 30
                self.font.render_to(surf, (cx, cy),
                    f"Preview: {total_n} nodes  {total_s} segments",
                    (0,200,255))
                if total_u:
                    cy += 18
                    self.font.render_to(surf, (cx, cy),
                        f"Updates: {total_u} existing nodes",
                        (160, 200, 255))
                if curve_radius_warnings:
                    cy += 18
                    self.font.render_to(
                        surf,
                        (cx, cy),
                        f"Radius warnings: {len(curve_radius_warnings)} point(s) under "
                        f"{self.alignment_min_radius_m:.0f} m",
                        (255, 110, 90),
                    )
                for error in curve_errors:
                    cy += 18
                    self.font.render_to(surf, (cx, cy), "Blocked: " + error, (255, 110, 90))
                for warning in curve_warnings:
                    cy += 18
                    self.font.render_to(surf, (cx, cy), warning, (255, 210, 100))

        elif self.geo_mode == 'parallel':
            self.font.render_to(surf, (cx, cy),
                "Generates parallel track(s) from selected chain", (100,120,140))
            cy += 20

            # Need a selected segment to define the source chain
            chain_info = ""
            source = self._get_parallel_source()
            if source:
                chain_info = f"Source: {len(source[0])} nodes  {len(source[1])} segs"

            num_field("Separation (m)", 'geo_separation', self.geo_separation)
            num_field("N tracks",       'geo_n_tracks',   self.geo_n_tracks)
            num_field("Speed limit",    'geo_speed',      self.geo_speed)
            choice_row("Side",  'geo_side',
                       ['left','right','both'], self.geo_side)
            choice_row("Class", 'geo_track_class',
                       ['Mainline','Branch','Industrial'],
                       self.geo_track_class)
            choice_row("Style", 'geo_style',
                       ['Standard','Yard','Bridge','Tunnel'], self.geo_style)
            cy += 6
            self.font.render_to(surf, (cx, cy),
                chain_info or "Select a segment to define source track",
                (140,180,140) if chain_info else (180,80,80))
            cy += 20

            bx4 = cx
            for lbl4, act4, col4, enabled in [
                    ("Preview",  "geo_preview",  (0,140,200),  bool(source)),
                    ("Commit",   "geo_commit",   (0,200,100),  bool(self.geo_preview)),
                    ("Clear",    "geo_clear",    (140,80,80),  bool(self.geo_preview)),
            ]:
                bw4 = self.font_big.get_rect(lbl4).width + 16
                r4  = pygame.Rect(bx4, cy, bw4, 24)
                hov4 = r4.collidepoint(mx0,my0) and enabled
                c4   = col4 if enabled else (30,35,40)
                pygame.draw.rect(surf, c4, r4, border_radius=4)
                pygame.draw.rect(surf, col4 if enabled else (50,60,70), r4, 1, border_radius=4)
                self.font_big.render_to(surf, (bx4+8, cy+5), lbl4,
                    (220,230,240) if enabled else (80,90,100))
                self._geo_btn_rects.append((r4, act4, enabled))
                bx4 += bw4 + 6

            if self.geo_preview:
                total_n, total_s, _total_u = preview_counts()
                cy += 30
                self.font.render_to(surf, (cx, cy),
                    f"Preview: {total_n} nodes  {total_s} segments  "
                    f"({self.geo_n_tracks} track(s) offset {self.geo_separation}m)",
                    (0,200,255))

        elif self.geo_mode == 'fit_arc':
            source = self._alignment_source_chain()
            fit_meta = self.geo_preview_meta if self.geo_preview_meta.get('mode') == 'fit_arc' else {}
            fit_stats = fit_meta.get('fit', {})
            fit_warnings = preview_warnings()
            fit_errors = preview_errors()
            fit_radius_warnings = preview_radius_warnings()
            self.font.render_to(
                surf,
                (cx, cy),
                "Convert the Grade chain into a constant-radius arc preview.",
                (100, 120, 140),
            )
            cy += 20
            num_field("Warn radius (m)", 'alignment_min_radius_m', self.alignment_min_radius_m, width=80)
            cy += 4
            if self._alignment_chain_contains_turnout(source):
                self.font.render_to(
                    surf,
                    (cx, cy),
                    "Blocked: the current chain includes turnout/switch nodes.",
                    (255, 110, 90),
                )
                cy += 18
            self.font.render_to(
                surf,
                (cx, cy),
                source.get('label', "Fit Arc uses the Grade chain order")
                if source else
                "Fit Arc uses the Grade chain order",
                (140, 180, 140) if source else (180, 80, 80),
            )
            cy += 18
            self.font.render_to(
                surf,
                (cx, cy),
                "Need 3+ nodes in the Grade chain to solve an arc.",
                (120, 140, 160),
            )
            cy += 20
            bx4 = cx
            preview_enabled = bool(source and len(source.get('nodes', [])) >= 3 and
                                   not self._alignment_chain_contains_turnout(source))
            for label, action, color, enabled in [
                    ("Preview", "geo_preview", (0, 140, 200), preview_enabled),
                    ("Commit", "geo_commit", (0, 200, 100), self._geo_preview_commit_enabled()),
                    ("Clear", "geo_clear", (140, 80, 80), bool(self.geo_preview)),
            ]:
                bw4 = self.font_big.get_rect(label).width + 16
                r4 = pygame.Rect(bx4, cy, bw4, 24)
                fill = color if enabled else (30, 35, 40)
                pygame.draw.rect(surf, fill, r4, border_radius=4)
                pygame.draw.rect(surf, color if enabled else (50, 60, 70), r4, 1, border_radius=4)
                self.font_big.render_to(
                    surf, (bx4 + 8, cy + 5), label,
                    (220, 230, 240) if enabled else (80, 90, 100),
                )
                self._geo_btn_rects.append((r4, action, enabled))
                bx4 += bw4 + 6
            cy += 32
            if fit_stats:
                self.font.render_to(
                    surf,
                    (cx, cy),
                    f"Fit: R {fit_stats.get('radius', 0.0):.1f} m   "
                    f"angle {fit_stats.get('delta_angle_deg', 0.0):.1f} deg",
                    (0, 200, 255),
                )
                cy += 18
                self.font.render_to(
                    surf,
                    (cx, cy),
                    f"Arc {fit_stats.get('arc_length', 0.0):.1f} m   "
                    f"Chord {fit_stats.get('chord_length', 0.0):.1f} m   "
                    f"RMS {fit_stats.get('rms_error', 0.0):.2f} m",
                    (140, 180, 220),
                )
                cy += 18
            if fit_radius_warnings:
                self.font.render_to(
                    surf,
                    (cx, cy),
                    f"Preview warning: {len(fit_radius_warnings)} point(s) fall under "
                    f"{self.alignment_min_radius_m:.0f} m",
                    (255, 110, 90),
                )
                cy += 18
            for error in fit_errors:
                self.font.render_to(surf, (cx, cy), "Blocked: " + error, (255, 110, 90))
                cy += 18
            for warning in fit_warnings:
                self.font.render_to(surf, (cx, cy), warning, (255, 210, 100))
                cy += 18

        elif self.geo_mode == 'node':
            # ---- Add Node tab ----
            self.font.render_to(surf, (cx, cy),
                "Place a new node on the map", (100,120,140))
            cy += 20

            # Track class for new nodes
            choice_row("Class", 'geo_track_class',
                       ['Mainline','Branch','Industrial'],
                       self.geo_track_class)
            choice_row("Style", 'geo_style',
                       ['Standard','Yard','Bridge','Tunnel'], self.geo_style)
            cy += 6

            # --- Placement Y lock ---
            pygame.draw.line(surf, (40, 55, 70), (cx, cy), (cx + pw - 28, cy))
            cy += 6
            self.font.render_to(surf, (cx, cy), "Placement elevation:", (100,120,140))
            cy += 16

            for key3, lbl3, val3 in [
                    ('place_y_lock',    'Lock to fixed Y',         self.place_y_lock),
                    ('place_y_inherit', 'Inherit from last node',  self.place_y_inherit)]:
                r3 = pygame.Rect(cx, cy, 200, 16)
                active3 = val3
                hov3 = r3.collidepoint(mx0, my0)
                box_col = (0,180,120) if active3 else (40,60,50)
                pygame.draw.rect(surf, box_col, pygame.Rect(cx, cy+1, 12, 12), border_radius=2)
                if active3:
                    self.font_big.render_to(surf, (cx+1, cy), "✓", (255,255,255))
                pygame.draw.rect(surf, (80,120,100), pygame.Rect(cx, cy+1, 12, 12), 1, border_radius=2)
                self.font.render_to(surf, (cx+17, cy+1), lbl3,
                                    (200,230,210) if active3 else (120,140,130))
                self._geo_btn_rects.append((r3, key3, True))
                cy += 18

            if self.place_y_lock:
                num_field("Fixed Y (m)", 'place_y_value', self.place_y_value, width=80)
                self.font.render_to(surf, (cx, cy),
                    "  New nodes placed at this elevation", (100,150,120))
                cy += 14
            elif self.place_y_inherit:
                last_y = getattr(self, '_last_placed_y', None)
                if last_y is not None:
                    self.font.render_to(surf, (cx, cy),
                        f"  Last placed Y: {last_y:.1f} m", (120,200,160))
                else:
                    self.font.render_to(surf, (cx, cy),
                        "  Will use terrain until first node placed", (100,130,110))
                cy += 14
            else:
                self.font.render_to(surf, (cx, cy),
                    "  Using terrain height (default)", (100,120,140))
                cy += 14

            cy += 4
            pygame.draw.line(surf, (40, 55, 70), (cx, cy), (cx + pw - 28, cy))
            cy += 6

            # Current cursor position
            mx_cur, my_cur = pygame.mouse.get_pos()
            ux, uz = self.screen_to_unity(mx_cur, my_cur)
            uy = self._sample_terrain_y(ux, uz) or 0
            self.font.render_to(surf, (cx, cy),
                f"Cursor: ({ux:.1f}, {uy:.1f}, {uz:.1f})", (140,180,140))
            cy += 18

            # Show selected node info if one exists
            if self.sel_mod_node_id and self.mod_project:
                n = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
                if n:
                    self.font.render_to(surf, (cx, cy),
                        f"Selected: {self.sel_mod_node_id}  rotY={n.get('rotY',0):.1f}°",
                        (0,200,255))
                    cy += 18

            cy += 6
            # Big obvious place button
            place_r = pygame.Rect(cx, cy, 180, 32)
            hov_p   = place_r.collidepoint(mx0, my0)
            pygame.draw.rect(surf, (200,140,0) if hov_p else (100,70,0),
                             place_r, border_radius=5)
            pygame.draw.rect(surf, (255,180,0), place_r, 1, border_radius=5)
            self.font_big.render_to(surf, (cx+12, cy+8),
                "Click map to place node", (255,240,200))
            self._geo_btn_rects.append((place_r, 'node_place_mode', True))
            cy += 40

            # Status of place mode
            place_active = getattr(self, '_geo_node_place_mode', False)
            status_col   = (0,255,100) if place_active else (140,160,180)
            status_lbl   = "● Placing — click map" if place_active else "○ Click button above, then click map"
            self.font.render_to(surf, (cx, cy), status_lbl, status_col)
            cy += 18
            self.font.render_to(surf, (cx, cy),
                "Also: Ctrl+click anywhere on map creates a node", (100,120,140))
            cy += 18
            self.font.render_to(surf, (cx, cy),
                "Nodes get terrain height automatically", (100,120,140))

        elif self.geo_mode == 'grade':
            # ---- Grade Smoother tab ----
            self.font.render_to(surf, (cx, cy),
                "Click a start node, then click another node to auto-pick the track path between them.", (100,120,140))
            cy += 20

            chain = self.grade_chain
            mp = self.mod_project

            # Fix endpoints checkboxes (drawn as toggle buttons)
            for key3, lbl3, val3 in [
                    ('grade_fix_first', 'Fix first node elevation', self.grade_fix_first),
                    ('grade_fix_last',  'Fix last node elevation',  self.grade_fix_last)]:
                col3 = (0,180,120) if val3 else (60,80,60)
                bw3  = self.font.get_rect(lbl3).width + 12
                r3   = pygame.Rect(cx, cy-1, bw3, 16)
                hov3 = r3.collidepoint(mx0, my0)
                pygame.draw.rect(surf, col3 if (val3 or hov3) else (20,35,25), r3, border_radius=2)
                pygame.draw.rect(surf, col3, r3, 1, border_radius=2)
                self.font.render_to(surf, (cx+6, cy), lbl3, (220,240,220) if val3 else (140,160,140))
                self._geo_btn_rects.append((r3, key3, True))
                cy += 20

            cy += 4
            # Chain status
            if not chain:
                self.font.render_to(surf, (cx, cy),
                    "No chain built — select a node then use buttons below", (180,80,80))
            else:
                if mp:
                    ys = [mp.merged_nodes.get(nid, {}).get('y', 0) for nid in chain]
                    self.font.render_to(surf, (cx, cy),
                        f"Chain: {len(chain)} nodes   "
                        f"Y range: {min(ys):.1f}–{max(ys):.1f} m",
                        (0,200,255))
            cy += 20

            self.font.render_to(surf, (cx, cy),
                "Tip: click another connected node to extend the chain through every node in between.", (100,120,140))
            cy += 18

            # Chain management buttons
            bx4 = cx
            sel_nid = self.sel_mod_node_id
            for lbl4, act4, col4, ena4 in [
                    ("Set Start",   "grade_set_start",  (0,140,200),  bool(sel_nid and mp)),
                    ("Add Path",    "grade_add_node",   (0,120,180),  bool(sel_nid and mp and chain)),
                    ("Remove Last", "grade_remove_last",(120,80,40),  bool(chain)),
                    ("Clear",       "grade_clear",      (120,60,60),  bool(chain)),
            ]:
                bw4 = self.font.get_rect(lbl4).width + 10
                r4  = pygame.Rect(bx4, cy, bw4, 20)
                hov4 = r4.collidepoint(mx0, my0) and ena4
                c4   = col4 if (ena4) else (30,35,40)
                pygame.draw.rect(surf, c4, r4, border_radius=3)
                pygame.draw.rect(surf, col4 if ena4 else (50,60,70), r4, 1, border_radius=3)
                self.font.render_to(surf, (bx4+5, cy+3), lbl4,
                    (220,230,240) if ena4 else (80,90,100))
                self._geo_btn_rects.append((r4, act4, ena4))
                bx4 += bw4 + 5
            cy += 26

            # Node list
            if chain and mp:
                self.font.render_to(surf, (cx, cy), "Chain nodes (top=start, auto-filled path):", (100,120,140))
                cy += 16
                row_h4 = 15
                max_chain_rows = 5 if getattr(self, 'profile_panel', False) else 12
                for i, nid in enumerate(chain[:max_chain_rows]):
                    node4 = mp.merged_nodes.get(nid, {})
                    y4    = node4.get('y', 0)
                    col4  = (0,200,255) if nid == sel_nid else (160,180,200)
                    self.font.render_to(surf, (cx, cy),
                        f"  {i+1}. {nid}   Y={y4:.2f}", col4)
                    cy += row_h4
                if len(chain) > max_chain_rows:
                    self.font.render_to(surf, (cx, cy),
                        f"  … +{len(chain)-max_chain_rows} more", (100,120,140))
                    cy += row_h4

            cy += 6
            # Smooth button
            smooth_r = pygame.Rect(cx, cy, 160, 24)
            hov_sm   = smooth_r.collidepoint(mx0, my0) and len(chain) >= 2
            pygame.draw.rect(surf,
                (180,80,200) if hov_sm else (80,30,100) if len(chain) >= 2 else (30,25,35),
                smooth_r, border_radius=4)
            pygame.draw.rect(surf,
                (220,100,255) if len(chain) >= 2 else (50,40,60), smooth_r, 1, border_radius=4)
            self.font_big.render_to(surf, (cx+10, cy+5), "Smooth Grade",
                (240,220,255) if len(chain) >= 2 else (80,70,90))
            self._geo_btn_rects.append((smooth_r, 'grade_smooth', len(chain) >= 2))

            # Straighten XZ button (inline, same row height)
            cy += 4
            str_r = pygame.Rect(cx + 168, smooth_r.y, 150, 24)
            hov_str = str_r.collidepoint(mx0, my0) and len(chain) >= 3
            pygame.draw.rect(surf,
                (0, 130, 180) if hov_str else (0, 55, 80) if len(chain) >= 3 else (20, 28, 32),
                str_r, border_radius=4)
            pygame.draw.rect(surf,
                (0, 190, 255) if len(chain) >= 3 else (30, 50, 60), str_r, 1, border_radius=4)
            self.font_big.render_to(surf, (str_r.x + 10, str_r.y + 5), "Straighten XZ",
                (200, 240, 255) if len(chain) >= 3 else (60, 75, 85))
            self._geo_btn_rects.append((str_r, 'grade_straighten_xz', len(chain) >= 3))

            # ---- Apply Target Grade % ----
            cy += 30
            pygame.draw.line(surf, (40, 55, 70), (cx, cy), (cx + pw - 28, cy))
            cy += 8
            self.font.render_to(surf, (cx, cy),
                "Apply a constant grade to the chain:", (100, 120, 140))
            cy += 16
            num_field("Target Grade %", 'grade_target_pct', self.grade_target_pct, width=80)

            # Show projected rise/fall for context
            if len(chain) >= 2:
                import math as _math
                chain_nodes = [mp.merged_nodes.get(nid) for nid in chain
                               if mp.merged_nodes.get(nid)]
                if len(chain_nodes) >= 2:
                    total_dist = sum(
                        _math.sqrt(
                            (chain_nodes[i]['x'] - chain_nodes[i-1]['x'])**2 +
                            (chain_nodes[i]['z'] - chain_nodes[i-1]['z'])**2
                        )
                        for i in range(1, len(chain_nodes))
                    )
                    rise = (self.grade_target_pct / 100.0) * total_dist
                    self.font.render_to(surf, (cx, cy),
                        f"  Over {total_dist:.1f} m → {rise:+.2f} m rise/fall",
                        (120, 160, 130))
                    cy += 16

            apply_r = pygame.Rect(cx, cy, 160, 24)
            hov_ap  = apply_r.collidepoint(mx0, my0) and len(chain) >= 2
            pygame.draw.rect(surf,
                (0, 160, 120) if hov_ap else (0, 70, 55) if len(chain) >= 2 else (20, 30, 28),
                apply_r, border_radius=4)
            pygame.draw.rect(surf,
                (0, 220, 160) if len(chain) >= 2 else (30, 50, 45), apply_r, 1, border_radius=4)
            self.font_big.render_to(surf, (cx + 10, cy + 5), "Apply Grade %",
                (200, 255, 230) if len(chain) >= 2 else (60, 80, 70))
            self._geo_btn_rects.append((apply_r, 'grade_apply_pct', len(chain) >= 2))

            # Smooth entry/exit grade transitions.
            cy += 34
            pygame.draw.line(surf, (70, 48, 90), (cx, cy), (cx + pw - 28, cy))
            cy += 8
            self.font_big.render_to(
                surf, (cx, cy), "Smooth Vertical Transition", (220, 150, 255)
            )
            cy += 20
            self.font.render_to(
                surf,
                (cx, cy),
                "Parabolic entry and exit curves keep grade and pitch continuous.",
                (120, 130, 155),
            )
            cy += 18
            num_field("Start grade %", 'grade_start_pct', self.grade_start_pct, width=80)
            num_field("Hold grade %", 'grade_target_pct', self.grade_target_pct, width=80)
            num_field("End grade %", 'grade_end_pct', self.grade_end_pct, width=80)
            num_field(
                "Entry curve m",
                'grade_transition_in_m',
                self.grade_transition_in_m,
                width=80,
                nudge_step=25.0,
            )
            num_field(
                "Exit curve m",
                'grade_transition_out_m',
                self.grade_transition_out_m,
                width=80,
                nudge_step=25.0,
            )

            preview_enabled = len(chain) >= 2
            read_grade_rect = pygame.Rect(cx, cy, 142, 20)
            read_grade_hover = (
                read_grade_rect.collidepoint(mx0, my0) and preview_enabled
            )
            pygame.draw.rect(
                surf,
                (70, 80, 120) if read_grade_hover else (32, 38, 58),
                read_grade_rect,
                border_radius=4,
            )
            pygame.draw.rect(
                surf,
                (110, 140, 210) if preview_enabled else (48, 52, 62),
                read_grade_rect,
                1,
                border_radius=4,
            )
            self.font.render_to(
                surf,
                (read_grade_rect.x + 7, read_grade_rect.y + 3),
                "Read Current Ends",
                TEXT_COLOR if preview_enabled else (80, 86, 96),
            )
            self._geo_btn_rects.append(
                (
                    read_grade_rect,
                    'grade_transition_read_ends',
                    preview_enabled,
                )
            )
            cy += 26

            bx_grade = cx
            for label, action, color, enabled in [
                    ("Preview", "grade_transition_preview", (150, 80, 210), preview_enabled),
                    ("Apply", "grade_transition_apply", (0, 170, 120), preview_enabled),
                    (
                        "Clear Preview",
                        "grade_transition_clear",
                        (110, 70, 90),
                        self.grade_transition_preview_active,
                    )]:
                bw_grade = self.font_big.get_rect(label).width + 16
                rect_grade = pygame.Rect(bx_grade, cy, bw_grade, 24)
                hover_grade = rect_grade.collidepoint(mx0, my0) and enabled
                fill_grade = (
                    color
                    if hover_grade
                    else tuple(max(18, component // 2) for component in color)
                    if enabled
                    else (24, 26, 32)
                )
                pygame.draw.rect(surf, fill_grade, rect_grade, border_radius=4)
                pygame.draw.rect(
                    surf,
                    color if enabled else (48, 52, 62),
                    rect_grade,
                    1,
                    border_radius=4,
                )
                self.font_big.render_to(
                    surf,
                    (rect_grade.x + 8, rect_grade.y + 5),
                    label,
                    TEXT_COLOR if enabled else (80, 86, 96),
                )
                self._geo_btn_rects.append((rect_grade, action, enabled))
                bx_grade = rect_grade.right + 6
            cy += 30

            if self.grade_transition_preview_active:
                profile_data = self._build_profile_data()
                vertical = profile_data.get('vertical_preview') or {}
                if vertical.get('errors'):
                    preview_text = "Blocked: " + str(vertical['errors'][0])
                    preview_color = (255, 110, 100)
                else:
                    preview_text = (
                        f"Preview rise/fall {vertical.get('rise_m', 0.0):+.2f} m; "
                        f"end Y {vertical.get('end_y', 0.0):.2f} m"
                    )
                    preview_color = (205, 160, 255)
                self.font.render_to(
                    surf,
                    (cx, cy),
                    self._fit_text_to_width(self.font, preview_text, pw - 28),
                    preview_color,
                )

        elif self.geo_mode == 'turnout':
            turnout_errors = preview_errors()
            turnout_warnings = preview_warnings()
            self.font.render_to(surf, (cx, cy),
                "Generate a switch from a selected node (the frog point)", (100,120,140))
            cy += 20
            num_field("Diverge angle °", 'turnout_diverge_angle', self.turnout_diverge_angle)
            num_field("Leg length (m)",  'turnout_leg_length',    self.turnout_leg_length)
            num_field("Main speed",      'turnout_speed',         self.turnout_speed)
            num_field("Diverge speed",   'turnout_div_speed',     self.turnout_div_speed)
            choice_row("Direction", 'turnout_direction', ['left','right'], self.turnout_direction)
            choice_row("Main class",  'turnout_track_class',
                       ['Mainline','Branch','Industrial'], self.turnout_track_class)
            choice_row("Div class",   'turnout_div_class',
                       ['Branch','Industrial','Mainline'], self.turnout_div_class)
            fs_r = pygame.Rect(cx, cy-1, 160, 16)
            hfs  = fs_r.collidepoint(mx0, my0)
            fcol = (0,180,100) if self.turnout_flip else (60,80,60)
            pygame.draw.rect(surf, fcol if (self.turnout_flip or hfs) else (18,28,20),
                             fs_r, border_radius=2)
            pygame.draw.rect(surf, fcol, fs_r, 1, border_radius=2)
            self.font.render_to(surf, (cx+5, cy),
                "Flip switch stand: " + ("YES" if self.turnout_flip else "NO"),
                (220,240,220) if self.turnout_flip else (120,140,120))
            self._geo_btn_rects.append((fs_r, 'turnout_flip', True))
            cy += 20
            num_field("Through curve °", 'turnout_through_curve', self.turnout_through_curve)
            cy += 4

            # ── Turnout templates ──────────────────────────────────────
            tpl_names = list(self._turnout_templates.keys())
            active    = self._turnout_active_template

            self.font.render_to(surf, (cx, cy), 'Templates:', (100, 120, 140))
            cy += 18

            # Active template name
            if active and active in self._turnout_templates:
                disp_a = active if len(active) <= 26 else active[:23] + '...'
                self.font.render_to(surf, (cx, cy), disp_a, (0, 200, 130))
            else:
                self.font.render_to(surf, (cx, cy), '(none loaded)', (70, 90, 80))
            cy += 18

            # Load / Save / Delete on one row
            bxt = cx
            has_templates = bool(tpl_names)
            has_active    = bool(active and active in self._turnout_templates)
            for lbl_t, act_t, col_t, ena_t in [
                    ('Load...',  'turnout_tpl_open',   (0, 140, 200),  has_templates),
                    ('Save as...', 'turnout_tpl_save', (0, 110, 160),  True),
                    ('Delete',   'turnout_tpl_delete',  (160, 60, 60), has_active),
            ]:
                bwt = self.font_big.get_rect(lbl_t).width + 14
                rt  = pygame.Rect(bxt, cy, bwt, 22)
                pygame.draw.rect(surf, col_t if ena_t else (30, 35, 40), rt, border_radius=3)
                pygame.draw.rect(surf, col_t if ena_t else (50, 60, 70), rt, 1, border_radius=3)
                self.font_big.render_to(surf, (bxt + 7, cy + 4), lbl_t,
                    (220, 230, 240) if ena_t else (80, 90, 100))
                self._geo_btn_rects.append((rt, act_t, ena_t))
                bxt += bwt + 5
            cy += 26
            if self.sel_mod_node_id and self.mod_project:
                n3 = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
                if n3:
                    self.font.render_to(surf, (cx, cy),
                        f"Frog: {self.sel_mod_node_id}  rotY={n3.get('rotY',0):.1f}°",
                        (140,180,140))
            else:
                self.font.render_to(surf, (cx, cy),
                    "Select a node first (will be the frog/switch point)", (180,80,80))
            cy += 20
            self.font.render_to(
                surf,
                (cx, cy),
                f"Safe checks: leg >= {self.turnout_min_leg_m:.0f} m, angle <= {self.turnout_max_angle_deg:.0f} deg",
                (120,140,160),
            )
            cy += 18
            bx4 = cx
            for lbl4, act4, col4, ena4 in [
                    ("Preview","geo_preview",(0,140,200),bool(self.sel_mod_node_id and self.mod_project)),
                    ("Commit", "geo_commit", (0,200,100),self._geo_preview_commit_enabled()),
                    ("Clear",  "geo_clear",  (140,80,80),bool(self.geo_preview)),
            ]:
                bw4 = self.font_big.get_rect(lbl4).width+16
                r4  = pygame.Rect(bx4, cy, bw4, 24)
                pygame.draw.rect(surf, col4 if ena4 else (30,35,40), r4, border_radius=4)
                pygame.draw.rect(surf, col4 if ena4 else (50,60,70), r4, 1, border_radius=4)
                self.font_big.render_to(surf,(bx4+8,cy+5),lbl4,(220,230,240) if ena4 else (80,90,100))
                self._geo_btn_rects.append((r4, act4, ena4))
                bx4 += bw4+6
            if self.geo_preview:
                cy += 30
                total_n2, total_s2, total_u2 = preview_counts()
                self.font.render_to(surf,(cx,cy),
                    f"Preview: {total_n2} nodes  {total_s2} segs  "
                    f"(diverge {self.turnout_direction} {self.turnout_diverge_angle}°)",
                    (0,200,255))
                if total_u2:
                    cy += 18
                    self.font.render_to(
                        surf,
                        (cx, cy),
                        f"Switch updates: {total_u2} node(s)",
                        (255, 210, 100),
                    )
            meta = self.geo_preview_meta if self.geo_preview_meta.get('mode') == 'turnout' else {}
            radius = meta.get('diverge_radius_m')
            if radius is not None:
                cy += 18
                self.font.render_to(
                    surf,
                    (cx, cy),
                    f"Estimated diverge radius: {radius:.1f} m  "
                    f"(warn below {self.alignment_min_radius_m:.0f} m)",
                    (255, 210, 100) if radius < self.alignment_min_radius_m else (140, 180, 220),
                )
            for error in turnout_errors:
                cy += 18
                self.font.render_to(surf, (cx, cy), "Blocked: " + error, (255, 110, 90))
            for warning in turnout_warnings:
                cy += 18
                self.font.render_to(surf, (cx, cy), warning, (255, 210, 100))

        elif self.geo_mode == 'wye':
            self.font.render_to(surf, (cx, cy),
                "Generate a wye from a selected node (the frog point)", (100, 120, 140))
            cy += 20
            num_field("Left angle °",   'wye_left_angle',  self.wye_left_angle)
            num_field("Right angle °",  'wye_right_angle', self.wye_right_angle)
            num_field("Leg length (m)", 'wye_leg_length',  self.wye_leg_length)
            num_field("Speed",          'wye_speed',       self.wye_speed, width=70)
            choice_row("Class", 'wye_track_class',
                       ['Mainline', 'Branch', 'Industrial'], self.wye_track_class)
            choice_row("Style", 'wye_style',
                       ['Standard', 'Yard', 'Bridge', 'Tunnel'], self.wye_style)
            # Flip stand toggle
            wf_r = pygame.Rect(cx, cy - 1, 160, 16)
            hwf  = wf_r.collidepoint(mx0, my0)
            wcol = (0, 180, 100) if self.wye_flip else (60, 80, 60)
            pygame.draw.rect(surf, wcol if (self.wye_flip or hwf) else (18, 28, 20),
                             wf_r, border_radius=2)
            pygame.draw.rect(surf, wcol, wf_r, 1, border_radius=2)
            self.font.render_to(surf, (cx + 5, cy),
                "Flip switch stand: " + ("YES" if self.wye_flip else "NO"),
                (220, 240, 220) if self.wye_flip else (120, 140, 120))
            self._geo_btn_rects.append((wf_r, 'wye_flip', True))
            cy += 20; cy += 4
            if self.sel_mod_node_id and self.mod_project:
                n_w = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
                if n_w:
                    self.font.render_to(surf, (cx, cy),
                        f"Frog: {self.sel_mod_node_id}  rotY={n_w.get('rotY', 0):.1f}°",
                        (140, 180, 140))
            else:
                self.font.render_to(surf, (cx, cy),
                    "Select a node first (will be the frog point)", (180, 80, 80))
            cy += 20
            cy += 4
            bx_w = cx
            for lbl_w, act_w, col_w, ena_w in [
                    ("Preview", "geo_preview", (0, 140, 200),
                     bool(self.sel_mod_node_id and self.mod_project)),
                    ("Commit",  "geo_commit",  (0, 200, 100),
                     self._geo_preview_commit_enabled()),
                    ("Clear",   "geo_clear",   (140, 80, 80),
                     bool(self.geo_preview)),
            ]:
                bw_w = self.font_big.get_rect(lbl_w).width + 16
                r_w  = pygame.Rect(bx_w, cy, bw_w, 24)
                pygame.draw.rect(surf, col_w if ena_w else (30, 35, 40), r_w, border_radius=4)
                pygame.draw.rect(surf, col_w if ena_w else (50, 60, 70), r_w, 1, border_radius=4)
                self.font_big.render_to(surf, (bx_w + 8, cy + 5), lbl_w,
                    (220, 230, 240) if ena_w else (80, 90, 100))
                self._geo_btn_rects.append((r_w, act_w, ena_w))
                bx_w += bw_w + 6
            if self.geo_preview and self.geo_preview_meta.get('mode') == 'wye':
                cy += 30
                total_nw, total_sw, _ = preview_counts()
                self.font.render_to(surf, (cx, cy),
                    f"Preview: {total_nw} nodes  {total_sw} segs  "
                    f"left={self.wye_left_angle:.1f}°  right={self.wye_right_angle:.1f}°",
                    (0, 200, 255))
            for error in (self.geo_preview_meta.get('errors') or []):
                cy += 18
                self.font.render_to(surf, (cx, cy), "Blocked: " + str(error), (255, 110, 90))

        geo_content_height = max(
            0,
            int(cy + geo_scroll - geo_content_top + 12),
        )
        geo_scroll_max = max(
            0,
            geo_content_height - geo_content_view.height,
        )
        self._geo_scroll_max = geo_scroll_max
        self._geo_scroll_max_by_mode[self.geo_mode] = geo_scroll_max
        if geo_scroll > geo_scroll_max:
            self._geo_scroll_by_mode[self.geo_mode] = geo_scroll_max
        surf.set_clip(previous_geo_clip)

        if geo_scroll_max > 0:
            track_rect = pygame.Rect(
                px + pw - 7,
                geo_content_view.y,
                3,
                geo_content_view.height,
            )
            pygame.draw.rect(surf, (35, 42, 54), track_rect, border_radius=2)
            thumb_h = max(
                24,
                int(
                    geo_content_view.height
                    * geo_content_view.height
                    / max(1, geo_content_height)
                ),
            )
            thumb_travel = max(0, geo_content_view.height - thumb_h)
            thumb_y = geo_content_view.y + int(
                thumb_travel * geo_scroll / max(1, geo_scroll_max)
            )
            pygame.draw.rect(
                surf,
                (120, 90, 155),
                (track_rect.x, thumb_y, track_rect.width, thumb_h),
                border_radius=2,
            )


    def _grade_chain_nodes(self) -> list[dict]:
        if not self.mod_project:
            return []
        return [
            self.mod_project.merged_nodes[node_id]
            for node_id in self.grade_chain
            if node_id in self.mod_project.merged_nodes
        ]

    @staticmethod
    def _grade_pitch_for_node(
            nodes: list[dict], index: int, grade_pct: float) -> float:
        """Convert chain-direction grade to the node's local rotX sign."""
        node = nodes[index]
        if len(nodes) < 2:
            return float(node.get('rotX', 0.0))
        if index <= 0:
            left = node
            right = nodes[1]
        elif index >= len(nodes) - 1:
            left = nodes[index - 1]
            right = node
        else:
            left = nodes[index - 1]
            right = nodes[index + 1]
        dx = float(right.get('x', 0.0)) - float(left.get('x', 0.0))
        dz = float(right.get('z', 0.0)) - float(left.get('z', 0.0))
        length = math.hypot(dx, dz)
        if length < 0.001:
            return float(node.get('rotX', 0.0))
        tx = dx / length
        tz = dz / length
        rot_y = math.radians(float(node.get('rotY', 0.0)))
        forward_x = math.sin(rot_y)
        forward_z = math.cos(rot_y)
        orientation = 1.0 if forward_x * tx + forward_z * tz >= 0.0 else -1.0
        local_grade = float(grade_pct) / 100.0 * orientation
        return -math.degrees(math.atan(local_grade))

    @staticmethod
    def _grades_for_node_elevations(
            nodes: list[dict], y_by_id: dict[str, float]) -> dict[str, float]:
        """Estimate the tangent grade at every node from a proposed profile."""
        grades = {}
        if len(nodes) < 2:
            return grades
        for index, node in enumerate(nodes):
            if index == 0:
                left_index, right_index = 0, 1
            elif index == len(nodes) - 1:
                left_index, right_index = index - 1, index
            else:
                left_index, right_index = index - 1, index + 1
            left = nodes[left_index]
            right = nodes[right_index]
            run = math.hypot(
                float(right.get('x', 0.0)) - float(left.get('x', 0.0)),
                float(right.get('z', 0.0)) - float(left.get('z', 0.0)),
            )
            left_y = float(y_by_id.get(str(left.get('id')), left.get('y', 0.0)))
            right_y = float(y_by_id.get(str(right.get('id')), right.get('y', 0.0)))
            grades[str(node.get('id'))] = (
                (right_y - left_y) / run * 100.0 if run > 0.001 else 0.0
            )
        return grades

    def _write_grade_profile(
            self,
            graph,
            nodes: list[dict],
            y_by_id: dict[str, float],
            grade_by_id: dict[str, float]):
        for index, node in enumerate(nodes):
            node_id = str(node.get('id'))
            if node_id not in y_by_id:
                continue
            grade_pct = float(grade_by_id.get(node_id, 0.0))
            rot_x = self._grade_pitch_for_node(nodes, index, grade_pct)
            graph.set_node(
                node_id,
                float(node.get('x', 0.0)),
                float(y_by_id[node_id]),
                float(node.get('z', 0.0)),
                rot_x,
                float(node.get('rotY', 0.0)),
                float(node.get('rotZ', 0.0)),
                bool(node.get('flipSwitchStand', False)),
            )

    def _set_grade_transition_preview(self, enabled: bool):
        self.grade_transition_preview_active = bool(enabled)
        self._profile_cache_key = None
        self._profile_cache_data = None
        if not enabled:
            return
        if len(self.grade_chain) < 2:
            self.grade_transition_preview_active = False
            self._set_status("Vertical curve preview needs a Grade chain")
            return
        self.profile_panel = True
        data = self._build_profile_data()
        preview = data.get('vertical_preview') or {}
        errors = list(preview.get('errors', []))
        if errors:
            self._set_status(f"Vertical curve blocked: {errors[0]}")
            return
        self._set_status(
            f"Vertical curve preview: {preview.get('total_length_m', 0.0):.1f} m, "
            f"rise/fall {preview.get('rise_m', 0.0):+.2f} m"
        )

    def _seed_grade_transition_from_chain(self):
        was_active = self.grade_transition_preview_active
        self.grade_transition_preview_active = False
        self._profile_cache_key = None
        self._profile_cache_data = None
        data = self._build_profile_data()
        grade_labels = list(data.get('grade_labels', []))
        if not grade_labels:
            self.grade_transition_preview_active = was_active
            self._set_status("Current chain has no measurable end grades")
            return
        self.grade_start_pct = float(grade_labels[0].get('grade_pct', 0.0))
        self.grade_end_pct = float(grade_labels[-1].get('grade_pct', 0.0))
        self.grade_transition_preview_active = was_active
        self._profile_cache_key = None
        self._profile_cache_data = None
        self._set_status(
            f"Read end grades: start {self.grade_start_pct:+.2f}%, "
            f"end {self.grade_end_pct:+.2f}%"
        )

    def _commit_grade_transition(self):
        if len(self.grade_chain) < 2 or not self.mod_project:
            self._set_status("Vertical curve needs a Grade chain")
            return
        if not self.grade_transition_preview_active:
            self._set_grade_transition_preview(True)
        data = self._build_profile_data()
        preview = data.get('vertical_preview') or {}
        errors = list(preview.get('errors', []))
        if errors:
            self._set_status(f"Vertical curve blocked: {errors[0]}")
            return
        graph = self.mod_project.get_graph_layer()
        nodes = self._grade_chain_nodes()
        node_points = list(preview.get('node_points', []))
        if not graph or len(nodes) < 2 or len(node_points) != len(nodes):
            self._set_status("Vertical curve chain changed; preview it again")
            return

        y_by_id = {
            str(point.get('node_id')): float(point.get('y', 0.0))
            for point in node_points
        }
        grade_by_id = {
            str(point.get('node_id')): float(point.get('grade_pct', 0.0))
            for point in node_points
        }
        self._push_undo(f"vertical curve {len(nodes)} nodes")
        self._write_grade_profile(graph, nodes, y_by_id, grade_by_id)
        self._commit_mod_layer_edit(graph, graph_changed=True)
        self.grade_transition_preview_active = False
        self._profile_cache_key = None
        self._profile_cache_data = None
        self._set_status(
            f"Vertical curve applied to {len(nodes)} nodes: "
            f"{self.grade_start_pct:+.2f}% -> {self.grade_target_pct:+.2f}% "
            f"-> {self.grade_end_pct:+.2f}%"
        )

    def _commit_grade_smooth(self):
        """Apply grade smoothing to the built chain."""
        if len(self.grade_chain) < 2 or not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        nodes_ordered = [self.mod_project.merged_nodes.get(nid)
                         for nid in self.grade_chain]
        nodes_ordered = [n for n in nodes_ordered if n]
        if len(nodes_ordered) < 2:
            self._set_status("Not enough valid nodes in chain")
            return
        self._push_undo(f"grade smooth {len(nodes_ordered)} nodes")
        results = smooth_grade(nodes_ordered,
                               fix_first=self.grade_fix_first,
                               fix_last=self.grade_fix_last)
        y_by_id = {str(node_id): float(new_y) for node_id, new_y in results}
        grade_by_id = self._grades_for_node_elevations(nodes_ordered, y_by_id)
        self._write_grade_profile(
            graph, nodes_ordered, y_by_id, grade_by_id
        )
        self._commit_mod_layer_edit(graph, graph_changed=True)
        y_vals = [y for _, y in results]
        self._set_status(
            f"Grade smoothed: {len(results)} nodes  "
            f"Y {min(y_vals):.1f}→{max(y_vals):.1f} m")

    def _commit_apply_grade(self):
        """Set node elevations so the chain runs at exactly grade_target_pct %."""
        if len(self.grade_chain) < 2 or not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        nodes_ordered = [self.mod_project.merged_nodes.get(nid)
                         for nid in self.grade_chain]
        nodes_ordered = [n for n in nodes_ordered if n]
        if len(nodes_ordered) < 2:
            self._set_status("Not enough valid nodes in chain")
            return
        self._push_undo(
            f"apply grade {self.grade_target_pct:+.2f}% to {len(nodes_ordered)} nodes")
        results = apply_grade_from_start(nodes_ordered,
                                         grade_pct=self.grade_target_pct,
                                         fix_first=True)
        y_by_id = {str(node_id): float(new_y) for node_id, new_y in results}
        grade_by_id = {
            str(node.get('id')): float(self.grade_target_pct)
            for node in nodes_ordered
        }
        self._write_grade_profile(
            graph, nodes_ordered, y_by_id, grade_by_id
        )
        self._commit_mod_layer_edit(graph, graph_changed=True)
        y_vals = [y for _, y in results]
        import math as _math
        # Compute actual chain length for the status message
        total_dist = sum(
            _math.sqrt(
                (nodes_ordered[i]['x'] - nodes_ordered[i-1]['x'])**2 +
                (nodes_ordered[i]['z'] - nodes_ordered[i-1]['z'])**2
            )
            for i in range(1, len(nodes_ordered))
        )
        rise = y_vals[-1] - y_vals[0]
        self._set_status(
            f"Grade applied: {self.grade_target_pct:+.2f}% over {total_dist:.1f} m  "
            f"Rise/fall: {rise:+.2f} m  "
            f"Y {y_vals[0]:.1f}→{y_vals[-1]:.1f} m")

    def _commit_straighten_xz(self):
        """Interpolate X/Z positions so the chain is a straight line in plan view."""
        if len(self.grade_chain) < 3 or not self.mod_project:
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            return
        nodes_ordered = [self.mod_project.merged_nodes.get(nid)
                         for nid in self.grade_chain]
        nodes_ordered = [n for n in nodes_ordered if n]
        if len(nodes_ordered) < 3:
            self._set_status("Need at least 3 nodes to straighten")
            return
        self._push_undo(f"straighten XZ {len(nodes_ordered)} nodes")
        results = straighten_chain_xz(nodes_ordered)
        for nid, new_x, new_z in results:
            node = dict(self.mod_project.merged_nodes.get(nid, {}))
            if node:
                graph.set_node(nid, new_x, node['y'], new_z,
                               node.get('rotX', 0), node.get('rotY', 0),
                               node.get('rotZ', 0), node.get('flipSwitchStand', False))

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._set_status(
            f"Straightened {len(results)} nodes between "
            f"({nodes_ordered[0]['x']:.1f}, {nodes_ordered[0]['z']:.1f}) → "
            f"({nodes_ordered[-1]['x']:.1f}, {nodes_ordered[-1]['z']:.1f})")

    def _generate_wye_preview(self):
        """Build wye preview from selected frog node."""
        if not self.sel_mod_node_id or not self.mod_project:
            return
        sw = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
        if not sw:
            return

        sw_id    = self.sel_mod_node_id
        pid      = self.mod_project.definition.get('id', 'T').replace('.', '_')[:6]
        existing = (set(self.mod_project.merged_nodes.keys()) |
                    set(self.mod_project.merged_segments.keys()))

        conn_segs    = self.mod_project.segments_for_node(sw_id)
        entry_segs   = [s for s in conn_segs if s.get('endId')   == sw_id]
        forward_segs = [s for s in conn_segs if s.get('startId') == sw_id]

        approach_rotY = float(sw.get('rotY', 0))
        existing_approach = bool(entry_segs or forward_segs)
        if entry_segs:
            n0 = self.mod_project.merged_nodes.get(entry_segs[0]['startId'])
            if n0:
                approach_rotY = self._bezier_tangent_rotY(n0, sw, t=1.0)
        elif forward_segs:
            n1 = self.mod_project.merged_nodes.get(forward_segs[0]['endId'])
            if n1:
                # A wye has no straight-through route. Treat a sole segment
                # leaving the selected endpoint as the approach behind the
                # frog, so the generated left/right legs replace neither it
                # nor create a fourth connection.
                approach_rotY = (
                    self._bezier_tangent_rotY(sw, n1, t=0.0) + 180.0
                ) % 360.0

        errors = []
        leg = float(self.wye_leg_length)
        la  = float(self.wye_left_angle)
        ra  = float(self.wye_right_angle)
        if len(conn_segs) > 1:
            errors.append("Wye needs an endpoint with zero or one existing route")
        if leg < 10.0:
            errors.append(f"Leg {leg:.1f} m is very short")
        if la < 1.0 or ra < 1.0:
            errors.append("Angles must be at least 1°")
        if la + ra > 60.0:
            errors.append("Combined spread > 60° is unusually wide")
        left_radius = turnout_radius_for_chord(leg, la)
        right_radius = turnout_radius_for_chord(leg, ra)
        if (
            left_radius is None
            or left_radius < float(self.alignment_min_radius_m)
        ):
            errors.append(
                f"Left radius is under {self.alignment_min_radius_m:.0f} m"
            )
        if (
            right_radius is None
            or right_radius < float(self.alignment_min_radius_m)
        ):
            errors.append(
                f"Right radius is under {self.alignment_min_radius_m:.0f} m"
            )

        if forward_segs and not entry_segs:
            n1 = self.mod_project.merged_nodes.get(forward_segs[0]['endId'])
            run = math.hypot(
                float(sw.get('x', 0.0)) - float(n1.get('x', 0.0)),
                float(sw.get('z', 0.0)) - float(n1.get('z', 0.0)),
            ) if n1 else 0.0
            grade_pct = (
                (float(sw.get('y', 0.0)) - float(n1.get('y', 0.0)))
                / run * 100.0
            ) if n1 and run > 0.01 else 0.0
        else:
            grade_pct = self._turnout_approach_grade_pct(
                sw, approach_rotY, entry_segs, [],
            )

        t_nodes, t_segs, sw_id_new, ent_id, left_id, rgt_id = generate_wye(
            float(sw['x']), float(sw['y']), float(sw['z']),
            approach_rotY,
            left_angle=la,
            right_angle=ra,
            leg_length=leg,
            flip_switch_stand=self.wye_flip,
            track_class=self.wye_track_class,
            style=self.wye_style,
            speed_limit=int(self.wye_speed),
            grade_pct=grade_pct,
            id_prefix=f'N{pid}W',
            seg_prefix=f'S{pid}W',
            existing_ids=existing,
        )

        # Separate new nodes from the frog update
        nodes_out = [n for n in t_nodes if n['id'] != sw_id_new and
                     not (n['id'] == ent_id and existing_approach)]
        sw_update = next(n for n in t_nodes if n['id'] == sw_id_new)
        sw_update = dict(sw_update)
        sw_update['id'] = sw_id  # keep original frog id

        # Remap sw_id_new -> sw_id in all segments, drop entry seg if already exists
        segs_out = []
        for s in t_segs:
            if s['startId'] == ent_id and existing_approach:
                continue  # entry leg already exists
            s = dict(s)
            if s['startId'] == sw_id_new:
                s['startId'] = sw_id
            if s['endId'] == sw_id_new:
                s['endId'] = sw_id
            segs_out.append(s)

        self.geo_preview = [(nodes_out, segs_out, [sw_update])]
        self.geo_preview_meta = {
            'mode':   'wye',
            'errors': errors,
            'warnings': [],
            'approach_grade_pct': grade_pct,
            'left_radius_m': left_radius,
            'right_radius_m': right_radius,
        }
        if errors:
            self._set_status("Wye preview: " + errors[0])
        else:
            self._set_status(
                f"Wye: approach={approach_rotY:.1f}°  "
                f"left={approach_rotY - la:.1f}°  right={approach_rotY + ra:.1f}°  "
                f"({len(nodes_out)} new nodes, {len(segs_out)} new segs)")

    def _commit_turnout_preview(self):
        """Generate turnout preview.

        Uses selected node as the switch node.
        switch rotY = approach direction (verified from game data).
        Infers approach from existing entry segment if one exists.
        """
        import math
        if not self.sel_mod_node_id or not self.mod_project:
            return
        sw = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
        if not sw:
            return

        sw_id    = self.sel_mod_node_id
        pid      = self.mod_project.definition.get('id','T').replace('.','_')[:6]
        existing = (set(self.mod_project.merged_nodes.keys()) |
                    set(self.mod_project.merged_segments.keys()))

        # Determine approach_rotY from existing connections
        conn_segs   = self.mod_project.segments_for_node(sw_id)
        entry_segs  = [s for s in conn_segs if s.get('endId')   == sw_id]
        forward_segs= [s for s in conn_segs if s.get('startId') == sw_id]

        approach_rotY = sw.get('rotY', 0)
        auto_through_curve = 0.0

        if entry_segs:
            s  = entry_segs[0]
            n0 = self.mod_project.merged_nodes.get(s['startId'])
            if n0:
                approach_rotY = self._bezier_tangent_rotY(n0, sw, t=1.0)
                # Heading at midpoint vs heading at frog — gives deg change over half segment
                # Scale to leg_length: rate = delta_half / (arc_len/2), result = rate * leg
                h_mid  = self._bezier_tangent_rotY(n0, sw, t=0.5)
                h_end  = approach_rotY
                delta  = ((h_end - h_mid + 180) % 360) - 180  # signed half-segment change
                try:
                    p0, p1, p2, p3 = _bezier_control_points(n0, sw)
                    half_arc = max(0.5, _bezier_length_gauss(p0, p1, p2, p3) / 2.0)
                except Exception:
                    half_arc = max(0.5, math.hypot(
                        float(sw['x']) - float(n0['x']),
                        float(sw['z']) - float(n0['z'])) / 2.0)
                auto_through_curve = (delta / half_arc) * float(self.turnout_leg_length)
        elif forward_segs:
            s  = forward_segs[0]
            n1 = self.mod_project.merged_nodes.get(s['endId'])
            if n1:
                # Forward segment: heading at frog vs heading at midpoint
                h_start = self._bezier_tangent_rotY(sw, n1, t=0.0)
                h_mid   = self._bezier_tangent_rotY(sw, n1, t=0.5)
                approach_rotY = h_start
                delta  = ((h_mid - h_start + 180) % 360) - 180
                try:
                    p0, p1, p2, p3 = _bezier_control_points(sw, n1)
                    half_arc = max(0.5, _bezier_length_gauss(p0, p1, p2, p3) / 2.0)
                except Exception:
                    half_arc = max(0.5, math.hypot(
                        float(n1['x']) - float(sw['x']),
                        float(n1['z']) - float(sw['z'])) / 2.0)
                auto_through_curve = (delta / half_arc) * float(self.turnout_leg_length)


        # Use auto_through_curve when through_curve_angle is 0 (user hasn't overridden)
        use_auto_through_curve = float(self.turnout_through_curve) == 0.0
        effective_through_curve = (
            auto_through_curve
            if use_auto_through_curve else
            float(self.turnout_through_curve)
        )

        sign      = 1.0 if self.turnout_direction == 'right' else -1.0
        leg       = float(self.turnout_leg_length)
        div_deflection = sign * float(self.turnout_diverge_angle)
        thru_deflection = (
            auto_through_curve
            if use_auto_through_curve else
            sign * effective_through_curve
        )
        grade_pct = self._turnout_approach_grade_pct(
            sw, approach_rotY, entry_segs, forward_segs,
        )

        def place(deflection, reverse=False):
            return turnout_leg_pose(
                float(sw['x']), float(sw['y']), float(sw['z']),
                approach_rotY, deflection, leg,
                grade_pct=grade_pct,
                reverse=reverse,
            )

        ctr = [1]
        def next_nid():
            while True:
                nid2 = f"N{pid}T_{ctr[0]:04d}"; ctr[0] += 1
                if nid2 not in existing: existing.add(nid2); return nid2
        def next_sid():
            while True:
                sid2 = f"S{pid}T_{ctr[0]:04d}"; ctr[0] += 1
                if sid2 not in existing: existing.add(sid2); return sid2

        nodes_out = []
        segs_out  = []

        # Entry leg — only if no entry segment already exists
        if not entry_segs:
            ex, ey, ez, erx, entry_rotY = place(0.0, reverse=True)
            eid = next_nid()
            nodes_out.append({'id': eid, 'x': ex, 'y': ey, 'z': ez,
                              'rotX': erx, 'rotY': entry_rotY,
                              'rotZ': 0, 'flipSwitchStand': False})
            segs_out.append({'id': next_sid(), 'startId': eid, 'endId': sw_id,
                             'trackClass': self.turnout_track_class, 'style': 'Standard',
                             'speedLimit': int(self.turnout_speed),
                             'priority': 0, 'groupId': ''})

        # Through leg — only if no forward segs already
        if not forward_segs:
            tx, ty, tz, trx, thru_rotY = place(thru_deflection)
            tid = next_nid()
            nodes_out.append({'id': tid, 'x': tx, 'y': ty, 'z': tz,
                              'rotX': trx, 'rotY': thru_rotY,
                              'rotZ': 0, 'flipSwitchStand': False})
            segs_out.append({'id': next_sid(), 'startId': sw_id, 'endId': tid,
                             'trackClass': self.turnout_track_class, 'style': 'Standard',
                             'speedLimit': int(self.turnout_speed),
                             'priority': 0, 'groupId': ''})

        # Diverge leg — always new
        if forward_segs:
            thru_rotY = (approach_rotY + thru_deflection) % 360.0
            tx = ty = tz = None

        dvx, dvy, dvz, drx, div_rotY = place(div_deflection)
        did = next_nid()
        nodes_out.append({'id': did, 'x': dvx, 'y': dvy, 'z': dvz,
                          'rotX': drx, 'rotY': div_rotY,
                          'rotZ': 0, 'flipSwitchStand': False})
        segs_out.append({'id': next_sid(), 'startId': sw_id, 'endId': did,
                         'trackClass': self.turnout_div_class, 'style': 'Standard',
                         'speedLimit': int(self.turnout_div_speed),
                         'priority': 0, 'groupId': ''})

        # Update switch node rotY to approach direction and set flip.
        # Stored separately so _geo_commit knows not to count it as a new node.
        sw_update = {'id': sw_id,
                     'x': sw['x'], 'y': sw['y'], 'z': sw['z'],
                     'rotX': -math.degrees(math.atan(grade_pct / 100.0)),
                     'rotY': approach_rotY,
                     'rotZ': sw.get('rotZ', 0),
                     'flipSwitchStand': self.turnout_flip}

        diverge_radius = turnout_radius_for_chord(leg, div_deflection)
        through_radius = turnout_radius_for_chord(leg, thru_deflection)
        through_point = (
            (float(tx), float(tz))
            if tx is not None and tz is not None else
            (float(sw['x']), float(sw['z']))
        )
        self.geo_preview = [(nodes_out, segs_out, [sw_update])]
        self.geo_preview_meta = self._build_turnout_preview_meta(
            conn_segs=conn_segs,
            entry_segs=entry_segs,
            forward_segs=forward_segs,
            leg=leg,
            diverge_angle_deg=self.turnout_diverge_angle,
            diverge_radius=diverge_radius,
            diverge_point=(float(dvx), float(dvz)),
            through_angle_deg=thru_deflection,
            through_radius=through_radius,
            through_point=through_point,
            approach_grade_pct=grade_pct,
        )
        self._set_status(
            f"Turnout: approach={approach_rotY:.1f}°  "
            f"through={thru_rotY:.1f}°  diverge={div_rotY:.1f}°  "
            f"curve={'auto {:.2f}°'.format(auto_through_curve) if float(self.turnout_through_curve) == 0.0 and auto_through_curve != 0.0 else '{:.2f}°'.format(effective_through_curve)}  "
            f"({len(nodes_out)-1} new nodes, {len(segs_out)} new segs)")
        if self._geo_preview_errors():
            self._set_status("Turnout preview generated, but commit is blocked by safety checks")

    def _get_parallel_source(self):
        """Get ordered (nodes, segments) for the selected segment's connected chain."""
        if not self.sel_mod_seg_id or not self.mod_project:
            return None
        seg = self.mod_project.merged_segments.get(self.sel_mod_seg_id)
        if not seg:
            return None
        # Build a simple 2-node chain from just this segment
        n0 = self.mod_project.merged_nodes.get(seg.get('startId',''))
        n1 = self.mod_project.merged_nodes.get(seg.get('endId',''))
        if not n0 or not n1:
            return None
        return ([n0, n1], [seg])

    def _geo_generate_preview(self):
        """Run the generator and store results in geo_preview."""
        self._clear_geo_preview()
        if not self.mod_project:
            return
        if self.geo_mode == 'turnout':
            self._commit_turnout_preview()
            return
        if self.geo_mode == 'wye':
            self._generate_wye_preview()
            return
        if self.geo_mode == 'fit_arc':
            self._alignment_fit_arc_preview()
            return
        existing = set(self.mod_project.merged_nodes.keys()) |                    set(self.mod_project.merged_segments.keys())
        pid  = self.mod_project.definition.get('id','geo').replace('.','_')[:8]

        if self.geo_mode == 'curve':
            if not self.sel_mod_node_id:
                return
            n = self.mod_project.merged_nodes.get(self.sel_mod_node_id)
            if not n:
                return
            nodes, segs = generate_curve(
                n['x'], n['y'], n['z'], n.get('rotY', 0),
                radius       = float(self.geo_radius),
                degrees      = float(self.geo_degrees),
                height_change= float(self.geo_height),
                direction    = self.geo_direction,
                n_segments   = int(self.geo_n_segs),
                track_class  = self.geo_track_class,
                style        = self.geo_style,
                speed_limit  = int(self.geo_speed),
                id_prefix    = f"N{pid}c",
                seg_prefix   = f"S{pid}c",
                existing_ids = existing,
                start_rotX   = float(n.get('rotX', 0)),
            )
            # Replace first node with selected node (connect to it)
            if nodes:
                nodes[0]['id'] = self.sel_mod_node_id
                if segs:
                    segs[0]['startId'] = self.sel_mod_node_id
            self.geo_preview = [(nodes[1:], segs)]   # skip the anchor node
            preview_points = [(float(pt.get('x', 0.0)), float(pt.get('z', 0.0))) for pt in nodes]
            self.geo_preview_meta = self._build_curve_preview_meta(preview_points)
            arc_length = abs(math.radians(float(self.geo_degrees)) * float(self.geo_radius))
            status = (
                f"Arc preview: R {float(self.geo_radius):.1f} m  "
                f"angle {float(self.geo_degrees):.1f} deg  "
                f"length {arc_length:.1f} m"
            )
            if self._geo_preview_errors():
                status += " - commit blocked"
            self._set_status(status)

        else:  # parallel
            source = self._get_parallel_source()
            if not source:
                return
            src_nodes, src_segs = source
            results = generate_parallel_tracks(
                source_nodes   = src_nodes,
                source_segments= src_segs,
                separation     = float(self.geo_separation),
                n_tracks       = int(self.geo_n_tracks),
                side           = self.geo_side,
                sample_y_fn    = self._sample_terrain_y,
                track_class    = self.geo_track_class,
                style          = self.geo_style,
                speed_limit    = int(self.geo_speed),
                id_prefix      = f"N{pid}p",
                seg_prefix     = f"S{pid}p",
                existing_ids   = existing,
            )
            self.geo_preview = results

    def _geo_commit(self):
        """Write geo_preview nodes/segments to the graph layer."""
        if not self.geo_preview or not self.mod_project:
            return
        errors = self._geo_preview_errors()
        if errors:
            self._set_status(f"Commit blocked: {errors[0]}")
            return
        graph = self.mod_project.get_graph_layer()
        if not graph:
            self._set_status("No game-graph layer")
            return
        total_n = total_s = total_u = 0
        for entry in self.geo_preview:
            nodes, segs = entry[0], entry[1]
            update_nodes = entry[2] if len(entry) > 2 else []
            # Write updated existing nodes first (e.g. switch node rotY fixup)
            for n in update_nodes:
                graph.set_node(n['id'], n['x'], n['y'], n['z'],
                               n.get('rotX', 0), n['rotY'], n.get('rotZ', 0),
                               n.get('flipSwitchStand', False))
                total_u += 1
            # Write new nodes
            for n in nodes:
                graph.set_node(n['id'], n['x'], n['y'], n['z'],
                               n.get('rotX',0), n['rotY'], n.get('rotZ',0),
                               n.get('flipSwitchStand',False))
                total_n += 1
            for s in segs:
                graph.set_segment(s['id'], s['startId'], s['endId'],
                                  s['trackClass'], s['style'],
                                  s['speedLimit'], s['priority'],
                                  s.get('groupId',''),
                                  s.get(
                                      'gauge',
                                      getattr(self, 'geo_gauge', 'Standard'),
                                  ))
                total_s += 1

        self._commit_mod_layer_edit(graph, graph_changed=True)
        self._clear_geo_preview()
        update_text = f"  {total_u} updated" if total_u else ""
        self._set_status(
            f"Committed {total_n} nodes  {total_s} segments{update_text} -> {graph.label}"
        )

    def _handle_geo_click(self, mx, my):
        """Handle clicks in the geometry panel."""
        panel_r = getattr(self, '_geo_panel_rect', None)
        if panel_r is None:
            w, h = self.screen.get_size()
            content_top = PANEL_H + (TOOLBAR_H if self.edit_mode else 0)
            pw = 480; ph = 420
            px = w - pw - 10; py = content_top + 10
            panel_r = pygame.Rect(px, py, pw, ph)
        if not panel_r.collidepoint(mx, my):
            return False
        px, py, pw, ph = panel_r.x, panel_r.y, panel_r.width, panel_r.height

        # X close
        if pygame.Rect(px+pw-28, py+6, 20, 20).collidepoint(mx, my):
            self.geo_panel = False; return True

        # Tabs
        for r, mode in getattr(self, '_geo_tab_rects', []):
            if r.collidepoint(mx, my):
                self.geo_mode    = mode
                self._clear_geo_preview()
                self._geo_node_place_mode = False
                self._geo_guide_place_mode = False
                return True

        # Numeric fields — activate for keyboard input
        content_view = getattr(self, '_geo_scroll_view_rect', None)
        if content_view is not None and not content_view.collidepoint(mx, my):
            return True

        for r, key, val in getattr(self, '_geo_field_rects', []):
            if r.collidepoint(mx, my):
                self._geo_input_focus = key
                self._geo_input_buf   = str(val)
                return True

        # Choice buttons
        for r, key, opt in getattr(self, '_geo_choice_rects', []):
            if r.collidepoint(mx, my):
                setattr(self, key, opt)
                if not (
                    self.geo_mode == 'pieces'
                    and key in (
                        'geo_piece_type', 'geo_direction',
                        'geo_track_class', 'geo_style', 'geo_gauge',
                    )
                ):
                    self._clear_geo_preview()
                return True

        # Action buttons
        for r, act, enabled in getattr(self, '_geo_btn_rects', []):
            if r.collidepoint(mx, my) and enabled:
                if   act == 'geo_preview':       self._geo_generate_preview()
                elif act == 'geo_commit':        self._geo_commit()
                elif act == 'geo_clear':         self._clear_geo_preview()
                elif act == 'node_place_mode':
                    self._geo_node_place_mode = not getattr(self, '_geo_node_place_mode', False)
                    if self._geo_node_place_mode:
                        self._geo_guide_place_mode = False
                elif act == 'guide_place_mode':
                    self._geo_guide_place_mode = not getattr(self, '_geo_guide_place_mode', False)
                    if self._geo_guide_place_mode:
                        self._geo_node_place_mode = False
                        self._set_status("Guide trace ON - click map to add points; right-click or Stop Trace to exit")
                    else:
                        self._set_status("Guide trace OFF")
                elif act == 'guide_use_source':
                    self._geo_guide_place_mode = False
                    self._alignment_use_source_as_guide()
                elif act == 'guide_pop_point':
                    self._alignment_pop_guide_point()
                elif act == 'guide_clear':
                    self._geo_guide_place_mode = False
                    self.alignment_guide_points = []
                    self._set_status("Guide path cleared")
                elif act == 'guide_build_spline':
                    self._commit_guide_spliney()
                elif act == 'geo_spl_prev':
                    self.sel_spliney_pt = max(0, self.sel_spliney_pt - 1)
                elif act == 'geo_spl_next':
                    layer, spl = self._selected_flowy_entry()
                    if spl:
                        self.sel_spliney_pt = min(len(spl.get('points', [])) - 1, self.sel_spliney_pt + 1)
                elif act == 'geo_spl_ins_before':
                    self._spl_insert_point(after=False)
                elif act == 'geo_spl_ins_after':
                    self._spl_insert_point(after=True)
                elif act == 'geo_spl_sample_y':
                    self._spl_sample_terrain()
                elif act == 'geo_spl_auto_rot':
                    self._spl_auto_rotY()
                elif act == 'geo_spl_range_anchor':
                    self._toggle_spliney_range_anchor()
                elif act == 'geo_spl_fill_width':
                    self._spl_fill_width_range()
                elif act == 'geo_spl_delete':
                    self._delete_selected_flowy_spliney()
                elif act == 'geo_open_spliney_panel':
                    layer, spl = self._selected_flowy_entry()
                    if layer is not None:
                        self.spliney_target_path = str(layer.path)
                    if spl is not None:
                        self.geo_spline_style = 'River' if str(spl.get('style', 'Road')).lower() == 'river' else 'Road'
                    self._geo_guide_place_mode = False
                    self._geo_node_place_mode = False
                    self._toggle_workspace_panel('spliney')
                elif act == 'piece_set_start':
                    self._geo_piece_set_start_from_selection()
                elif act == 'piece_add':
                    self._geo_piece_add_current()
                elif act == 'piece_undo':
                    self._geo_piece_undo_last()
                elif act == 'piece_clear':
                    self._clear_geo_preview()
                    self._set_status("Pieces draft cleared")
                elif act.startswith('geo_nudge:'):
                    _prefix, field_key, delta_txt = act.split(':', 2)
                    self._apply_geo_nudge(field_key, delta_txt)
                elif act == 'place_y_lock':
                    self.place_y_lock = not self.place_y_lock
                    if self.place_y_lock:
                        self.place_y_inherit = False   # mutually exclusive
                        # seed with current cursor terrain Y as a sensible default
                        mx_c, my_c = pygame.mouse.get_pos()
                        ux_c, uz_c = self.screen_to_unity(mx_c, my_c)
                        sampled = self._sample_terrain_y(ux_c, uz_c)
                        if sampled:
                            self.place_y_value = round(sampled, 1)
                elif act == 'place_y_inherit':
                    self.place_y_inherit = not self.place_y_inherit
                    if self.place_y_inherit:
                        self.place_y_lock = False      # mutually exclusive
                # Grade smoother
                elif act == 'grade_set_start':
                    self._set_grade_chain_start(self.sel_mod_node_id)
                elif act == 'grade_add_node':
                    self._extend_grade_chain_to(self.sel_mod_node_id)
                    return True
                    nid = self.sel_mod_node_id
                    if nid and nid not in self.grade_chain:
                        self.grade_chain.append(nid)
                        self._clear_geo_preview()
                        self._set_status(f"Added {nid} — chain now {len(self.grade_chain)} nodes")
                elif act == 'grade_remove_last':
                    if self.grade_chain:
                        removed = self.grade_chain.pop()
                        self._set_grade_transition_preview(False)
                        self._clear_geo_preview()
                        self._set_status(f"Removed {removed}")
                elif act == 'grade_clear':
                    self.grade_chain = []
                    self._set_grade_transition_preview(False)
                    self._clear_geo_preview()
                    self._set_status("Grade chain cleared")
                elif act == 'grade_fix_first':
                    self.grade_fix_first = not self.grade_fix_first
                elif act == 'grade_fix_last':
                    self.grade_fix_last = not self.grade_fix_last
                elif act == 'grade_smooth':
                    self._commit_grade_smooth()
                elif act == 'grade_straighten_xz':
                    self._commit_straighten_xz()
                elif act == 'grade_apply_pct':
                    self._commit_apply_grade()
                elif act == 'grade_transition_preview':
                    self._set_grade_transition_preview(True)
                elif act == 'grade_transition_read_ends':
                    self._seed_grade_transition_from_chain()
                elif act == 'grade_transition_apply':
                    self._commit_grade_transition()
                elif act == 'grade_transition_clear':
                    self._set_grade_transition_preview(False)
                    self._set_status("Vertical curve preview cleared")
                elif act == 'wye_flip':
                    self.wye_flip = not self.wye_flip
                    self._clear_geo_preview()
                elif act == 'turnout_flip':
                    self.turnout_flip = not self.turnout_flip
                    self._clear_geo_preview()
                elif act == 'turnout_tpl_open':
                    tpl_names = list(self._turnout_templates.keys())
                    if tpl_names:
                        chosen = ask_choice_list(self.screen, 'Load Turnout Template', tpl_names,
                                                 prompt='Select a template:',
                                                 initial_filter='')
                        if chosen:
                            self._apply_turnout_template(chosen)
                elif act == 'turnout_tpl_save':
                    name = ask_string(self.screen, 'Save Template',
                                      'Template name:',
                                      self._turnout_active_template or '')
                    if name and name.strip():
                        name = name.strip()
                        self._turnout_templates[name] = self._turnout_template_to_dict()
                        self._turnout_active_template = name
                        self._save_turnout_templates()
                        self._set_status(f'Turnout template saved: {name}')
                elif act == 'turnout_tpl_delete':
                    name = self._turnout_active_template
                    if name and name in self._turnout_templates:
                        del self._turnout_templates[name]
                        self._turnout_active_template = None
                        self._save_turnout_templates()
                        self._set_status(f'Turnout template deleted: {name}')
                elif act.startswith('turnout_tpl_load:'):
                    pass  # handled via turnout_tpl_open dropdown
                return True

        # Click outside active field = deactivate
        self._geo_input_focus = None
        return True

    def _scroll_geo_panel(self, direction: int) -> bool:
        if not self.geo_panel:
            return False
        current = int(self._geo_scroll_by_mode.get(self.geo_mode, 0))
        maximum = int(self._geo_scroll_max_by_mode.get(self.geo_mode, 0))
        updated = max(0, min(maximum, current + int(direction) * 54))
        self._geo_scroll_by_mode[self.geo_mode] = updated
        if updated != current:
            self._geo_input_focus = None
            self._geo_input_buf = ''
        return True

    def _handle_geo_keydown(self, event):
        """Handle keyboard input for geo panel numeric fields."""
        if not self.geo_panel or not self._geo_input_focus:
            return False
        key = self._geo_input_focus
        if event.key == pygame.K_RETURN or event.key == pygame.K_KP_ENTER:
            # Commit the value
            try:
                val = float(self._geo_input_buf)
                if key == 'geo_n_segs':
                    val = max(0, int(val))
                elif key == 'geo_n_tracks':
                    val = max(1, int(val))
                elif key in ('geo_speed', 'turnout_speed', 'turnout_div_speed'):
                    val = max(1, int(val))
                elif key in ('geo_radius', 'alignment_min_radius_m', 'turnout_leg_length', 'geo_spline_width', 'geo_piece_length'):
                    val = max(0.0, float(val))
                elif key in ('grade_transition_in_m', 'grade_transition_out_m'):
                    val = max(0.0, float(val))
                elif key in ('geo_degrees', 'turnout_diverge_angle'):
                    val = abs(float(val))
                setattr(self, key, val)
                if not (self.geo_mode == 'pieces' and key in ('geo_piece_length', 'geo_radius', 'geo_degrees', 'geo_n_segs', 'geo_speed')):
                    self._clear_geo_preview()
                if key.startswith('grade_'):
                    self._profile_cache_key = None
                    self._profile_cache_data = None
            except ValueError:
                pass
            self._geo_input_focus = None
            self._geo_input_buf   = ''
            return True
        elif event.key == pygame.K_ESCAPE:
            self._geo_input_focus = None
            self._geo_input_buf   = ''
            return True
        elif event.key == pygame.K_BACKSPACE:
            self._geo_input_buf = self._geo_input_buf[:-1]
            return True
        elif event.unicode and event.unicode in '0123456789.-':
            self._geo_input_buf += event.unicode
            return True
        return False

    def _draw_mod_panel(self, surf, content_top):
        """Draw the mod project panel — layer list + stats."""
        if not self.mod_panel or not _MOD_AVAILABLE:
            return
        w, h = surf.get_size()
        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 186))
        surf.blit(overlay, (0, content_top))

        pw  = min(w - 40, 980)
        ph  = h - content_top - STATUS_H - 20
        px  = (w - pw) // 2
        py  = content_top + 10
        self._mod_panel_bounds = pygame.Rect(px, py, pw, ph)
        self._mod_panel_action_rects = []
        self._mod_panel_row_rects = []
        self._mod_panel_vis_rects = []
        self._mod_panel_save_rects = []

        pygame.draw.rect(surf, PANEL_ELEVATED_BG, self._mod_panel_bounds, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, self._mod_panel_bounds, 1, border_radius=12)
        header_rect = pygame.Rect(px, py, pw, 54)
        pygame.draw.rect(surf, PANEL_HEADER_BG, header_rect, border_radius=12)
        pygame.draw.rect(surf, PANEL_SECTION_BORDER, header_rect, 1, border_radius=12)
        pygame.draw.rect(surf, ACCENT_COLOR, (px, py + 50, pw, 4), border_radius=2)

        cx = px + 16
        cy = py + 14

        mx0, my0 = pygame.mouse.get_pos()
        xbtn = pygame.Rect(px + pw - 30, py + 8, 22, 22)
        self._mod_panel_close_rect = xbtn
        hov_x = xbtn.collidepoint(mx0, my0)
        pygame.draw.rect(surf, (180, 60, 60) if hov_x else (80, 40, 40),
                         xbtn, border_radius=4)
        pygame.draw.rect(surf, (220, 80, 80), xbtn, 1, border_radius=4)
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236, 216, 216))
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236, 216, 216))
        self.font_big.render_to(surf, (px + pw - 24, py + 11), "✕", (220, 200, 200))
        self.font_big.render_to(surf, (px + pw - 23, py + 10), "x", (236, 216, 216))
        title = self.mod_project.name if self.mod_project else "No mod loaded"
        self.font_big.render_to(surf, (cx, cy), title, (0, 212, 255))
        cy += 18

        if self.mod_project:
            stats = self.mod_project.stats()
            self.font.render_to(surf, (cx, cy), stats, TEXT_SOFT)
            mod_count = sum(1 for src in getattr(self.mod_project, "sources", [])
                            if not src.get("is_base_game"))
            chips = [
                (f"{mod_count or 1} mod(s)", TEXT_COLOR),
                (f"{len(self.mod_project.layers)} layers", TEXT_COLOR),
                (f"{len(self.mod_project.merged_nodes)} nodes", TEXT_SOFT),
                (f"{len(self.mod_project.merged_segments)} segs", TEXT_SOFT),
                ("Dirty" if self.mod_project.dirty else "Clean",
                 WARN_COLOR if self.mod_project.dirty else OK_COLOR),
            ]
            chip_right = xbtn.x - 8
            for text, col in reversed(chips):
                cw = self.font.get_rect(text).width + 14
                chip = pygame.Rect(chip_right - cw, py + 24, cw, 18)
                pygame.draw.rect(surf, PANEL_SECTION_BG, chip, border_radius=9)
                pygame.draw.rect(surf, PANEL_SECTION_BORDER, chip, 1, border_radius=9)
                self.font.render_to(surf, (chip.x + 7, chip.y + 3), text, col)
                chip_right = chip.x - 8
            cy = py + 66

            def draw_action_button(x, y, label, color, action):
                bw2 = self.font_big.get_rect(label).width + 18
                rect = pygame.Rect(x, y, bw2, 26)
                hov = rect.collidepoint(mx0, my0)
                fill = color if hov else tuple(max(24, c // 2) for c in color)
                pygame.draw.rect(surf, fill, rect, border_radius=5)
                pygame.draw.rect(surf, color, rect, 1, border_radius=5)
                self.font_big.render_to(surf, (x + 9, y + 6), label, (224, 232, 240))
                self._mod_panel_action_rects.append((rect, action))
                return rect.right + 8

            bx2 = cx
            for label, color, action in [
                ("Open Mod", (0, 160, 220), self.open_mod_folder_dialog),
                ("Add Mod", (40, 150, 220), self.add_mod_folder_dialog),
                ("Open Base", (0, 145, 210), self.open_base_graph_dialog),
                ("New Mod", (80, 180, 80), self.new_mod_dialog),
                ("Save All", (230, 150, 35), self.save_mod_project),
                ("Save Layer", (200, 120, 35), self._save_active_layer),
                ("Validate", (45, 170, 120), self.validate_mod_project),
                ("Export ZIP", (110, 145, 210), self.export_mod_project),
            ]:
                bx2 = draw_action_button(bx2, cy, label, color, action)
            cy += 38

            header = pygame.Rect(cx, cy, pw - 32, 28)
            pygame.draw.rect(surf, PANEL_SECTION_BG, header, border_radius=6)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, header, 1, border_radius=6)
            self.font.render_to(surf, (cx + 10, cy + 8), "Vis", TEXT_MUTED)
            self.font.render_to(surf, (cx + 38, cy + 8), "Layer", TEXT_MUTED)
            self.font.render_to(surf, (cx + 268, cy + 8), "Type", TEXT_MUTED)
            self.font.render_to(surf, (cx + 358, cy + 8), "Nodes", TEXT_MUTED)
            self.font.render_to(surf, (cx + 450, cy + 8), "Segs", TEXT_MUTED)
            self.font.render_to(surf, (cx + 532, cy + 8), "Spl", TEXT_MUTED)
            self.font.render_to(surf, (cx + 594, cy + 8), "Areas", TEXT_MUTED)
            self.font.render_to(surf, (cx + 666, cy + 8), "State", TEXT_MUTED)
            cy += 34

            row_h = 26
            footer_y = py + ph - 28
            max_rows = max(4, (footer_y - cy - 8) // row_h)
            layers = self.mod_project.layers
            scroll = self.mod_layer_scroll
            active = self.mod_project.active_layer_idx
            type_labels = {
                LAYER_GRAPH: "GRAPH",
                LAYER_TOWN: "TOWN",
                LAYER_BASE: "BASE",
                LAYER_RIVERS: "RIVER",
                LAYER_MIGRATION: "MIG",
                LAYER_OTHER: "OTHER",
            }
            type_colors = {
                LAYER_GRAPH: ACCENT_COLOR,
                LAYER_TOWN: (100, 200, 140),
                LAYER_BASE: (120, 160, 255),
                LAYER_RIVERS: (80, 170, 255),
                LAYER_MIGRATION: (255, 140, 90),
                LAYER_OTHER: TEXT_MUTED,
            }

            for i, layer in enumerate(layers[scroll: scroll + max_rows]):
                li = i + scroll
                row_y = cy + i * row_h
                row_rect = pygame.Rect(cx, row_y, pw - 32, row_h - 2)
                base_bg = PANEL_SECTION_BG if i % 2 == 0 else ROW_ALT_BG
                if li == active:
                    pygame.draw.rect(surf, ROW_ACTIVE_BG, row_rect, border_radius=4)
                    pygame.draw.rect(surf, ROW_ACTIVE_BORDER, row_rect, 1, border_radius=4)
                elif row_rect.collidepoint(mx0, my0):
                    pygame.draw.rect(surf, ROW_HOVER_BG, row_rect, border_radius=4)
                else:
                    pygame.draw.rect(surf, base_bg, row_rect, border_radius=4)

                dot_col = layer.color if layer.visible else (50, 50, 50)
                vis_rect = pygame.Rect(cx + 4, row_y + 4, 14, 14)
                pygame.draw.circle(surf, dot_col, vis_rect.center, 6)
                pygame.draw.circle(surf, (80, 100, 120), vis_rect.center, 6, 1)
                self._mod_panel_vis_rects.append((vis_rect, li))

                lbl_col = (220, 230, 240) if layer.visible else (80, 90, 100)
                self.font.render_to(surf, (cx + 28, row_y + 6), layer.label, lbl_col)

                type_tag = type_labels.get(layer.layer_type, str(layer.layer_type))
                type_col = type_colors.get(layer.layer_type, TEXT_MUTED)
                type_rect = pygame.Rect(cx + 258, row_y + 4, 74, 18)
                pygame.draw.rect(surf, PANEL_SECTION_ALT, type_rect, border_radius=9)
                pygame.draw.rect(surf, type_col, type_rect, 1, border_radius=9)
                self.font.render_to(
                    surf,
                    (type_rect.x + (type_rect.width - self.font.get_rect(type_tag).width) // 2, row_y + 7),
                    type_tag,
                    type_col
                )

                nn = sum(1 for n in layer.nodes.values() if not n['deleted'])
                nd = sum(1 for n in layer.nodes.values() if n['deleted'])
                ns = sum(1 for s in layer.segments.values() if not s['deleted'])
                sd = sum(1 for s in layer.segments.values() if s['deleted'])

                def stat_str(a, d):
                    return f"{a}" + (f" +{d}" if d else "")

                self.font.render_to(surf, (cx + 358, row_y + 6), stat_str(nn, nd), TEXT_SOFT)
                self.font.render_to(surf, (cx + 450, row_y + 6), stat_str(ns, sd), TEXT_SOFT)
                self.font.render_to(surf, (cx + 532, row_y + 6), str(len(layer.splineys)), TEXT_SOFT)
                self.font.render_to(surf, (cx + 594, row_y + 6), str(len(layer.areas)), TEXT_SOFT)

                if layer.dirty:
                    state_rect = pygame.Rect(cx + 654, row_y + 4, 54, 18)
                    pygame.draw.rect(surf, (84, 62, 18), state_rect, border_radius=9)
                    pygame.draw.rect(surf, WARN_COLOR, state_rect, 1, border_radius=9)
                    self.font.render_to(surf, (state_rect.x + 10, row_y + 7), "Dirty", WARN_COLOR)
                    save_btn = pygame.Rect(cx + 720, row_y + 3, 60, 20)
                    hov_save = save_btn.collidepoint(mx0, my0)
                    pygame.draw.rect(surf, (188, 118, 26) if hov_save else (96, 60, 18),
                                     save_btn, border_radius=4)
                    pygame.draw.rect(surf, (230, 160, 50), save_btn, 1, border_radius=4)
                    self.font.render_to(surf, (save_btn.x + 11, save_btn.y + 5), "Save", (244, 232, 204))
                    self._mod_panel_save_rects.append((save_btn, li))
                else:
                    state_rect = pygame.Rect(cx + 654, row_y + 4, 54, 18)
                    pygame.draw.rect(surf, PANEL_SECTION_ALT, state_rect, border_radius=9)
                    pygame.draw.rect(surf, OK_COLOR, state_rect, 1, border_radius=9)
                    self.font.render_to(surf, (state_rect.x + 11, row_y + 7), "Saved", OK_COLOR)

                self._mod_panel_row_rects.append((row_rect, li))

            if len(layers) > max_rows:
                self.font.render_to(surf, (cx, footer_y),
                    f"Scroll ↑↓  ({scroll+1}–{min(scroll+max_rows, len(layers))} of {len(layers)})",
                    TEXT_MUTED)
        else:
            empty = pygame.Rect(cx, py + 84, pw - 32, 160)
            pygame.draw.rect(surf, PANEL_SECTION_BG, empty, border_radius=10)
            pygame.draw.rect(surf, PANEL_SECTION_BORDER, empty, 1, border_radius=10)
            self.font_big.render_to(surf, (empty.x + 18, empty.y + 18), "Open or create a project", ACCENT_COLOR)
            self.font.render_to(
                surf, (empty.x + 18, empty.y + 42),
                "Use a mod folder, a base game graph, or start a new empty mod workspace.",
                TEXT_SOFT
            )

            def draw_empty_button(x, y, label, color, action):
                bw2 = self.font_big.get_rect(label).width + 18
                rect = pygame.Rect(x, y, bw2, 28)
                hov = rect.collidepoint(mx0, my0)
                pygame.draw.rect(surf, color if hov else tuple(max(24, c // 2) for c in color),
                                 rect, border_radius=5)
                pygame.draw.rect(surf, color, rect, 1, border_radius=5)
                self.font_big.render_to(surf, (x + 9, y + 7), label, (224, 232, 240))
                self._mod_panel_action_rects.append((rect, action))
                return rect.right + 10

            bx2 = empty.x + 18
            by2 = empty.y + 88
            bx2 = draw_empty_button(bx2, by2, "Open Mod Folder", (0, 160, 220), self.open_mod_folder_dialog)
            bx2 = draw_empty_button(bx2, by2, "Open Base Graph", (0, 145, 210), self.open_base_graph_dialog)
            draw_empty_button(bx2, by2, "New Mod", (80, 180, 80), self.new_mod_dialog)

    def _draw_generate_panel(self, surf, content_top):
        """Draw the generate panel overlay."""
        w, h = surf.get_size()
        mx0, my0 = pygame.mouse.get_pos()

        # Semi-transparent backdrop
        overlay = pygame.Surface((w, h - content_top - STATUS_H), pygame.SRCALPHA)
        overlay.fill((6, 9, 14, 220))
        surf.blit(overlay, (0, content_top))

        # Panel box
        pw, ph = min(w - 40, 1100), h - content_top - STATUS_H - 20
        px, py = (w - pw) // 2, content_top + 10
        pygame.draw.rect(surf, (14, 20, 30), (px, py, pw, ph), border_radius=8)
        pygame.draw.rect(surf, BTN_BORDER,   (px, py, pw, ph), 1, border_radius=8)

        # Title
        self.font_big.render_to(surf, (px+16, py+14), "Generate Tiles  —  Mapbox Terrain-RGB + NLCD",
                                ACCENT_COLOR)
        pygame.draw.line(surf, BTN_BORDER, (px+12, py+36), (px+pw-12, py+36), 1)

        cy = py + 46  # current y cursor

        # ---- Preset row ----
        lx = px + 16
        self.font.render_to(surf, (lx, cy), "Presets:", DIM_COLOR)
        px2_presets = lx + self.font.get_rect("Presets:").width + 8
        ppx = px2_presets

        if self.gen_presets:
            for name in list(self.gen_presets.keys()):
                nb = self.font.get_rect(name).width + 16
                pr = pygame.Rect(ppx, cy - 2, nb, 22)
                hover_p = pr.collidepoint(mx0, my0)
                pygame.draw.rect(surf, BTN_HOVER_C if hover_p else BTN_INACTIVE, pr, border_radius=4)
                pygame.draw.rect(surf, BTN_BORDER, pr, 1, border_radius=4)
                self.font.render_to(surf, (ppx + 8, cy + 2), name,
                                    ACCENT_COLOR if hover_p else TEXT_COLOR)
                ppx += nb + 4
                # Delete x button
                xr = pygame.Rect(ppx, cy, 16, 16)
                hover_x = xr.collidepoint(mx0, my0)
                pygame.draw.rect(surf, (60, 20, 20) if hover_x else BTN_INACTIVE, xr, border_radius=3)
                self.font.render_to(surf, (ppx + 3, cy + 1), "×", (200, 80, 80) if hover_x else DIM_COLOR)
                ppx += 20
        else:
            self.font.render_to(surf, (ppx, cy + 2), "no presets saved", DIM_COLOR)
            ppx += self.font.get_rect("no presets saved").width + 12

        # Save-as field + button
        ppx += 8
        self.font.render_to(surf, (ppx, cy + 2), "Save as:", DIM_COLOR)
        ppx += self.font.get_rect("Save as:").width + 6
        name_rect = pygame.Rect(ppx, cy - 2, 140, 22)
        active_name = self._gen_input_focus == 'preset_name'
        pygame.draw.rect(surf, (20,28,40) if active_name else (12,18,26), name_rect, border_radius=4)
        pygame.draw.rect(surf, ACCENT_COLOR if active_name else BTN_BORDER, name_rect, 1, border_radius=4)
        self.font.render_to(surf, (ppx + 5, cy + 2),
                            self.gen_preset_name or "click to name…",
                            TEXT_COLOR if self.gen_preset_name else DIM_COLOR)
        ppx += 148
        sv_bw2 = self.font.get_rect("Save").width + 16
        sv_r = pygame.Rect(ppx, cy - 2, sv_bw2, 22)
        hover_sv = sv_r.collidepoint(mx0, my0)
        pygame.draw.rect(surf, BTN_HOVER_C if hover_sv else BTN_INACTIVE, sv_r, border_radius=4)
        pygame.draw.rect(surf, BTN_BORDER, sv_r, 1, border_radius=4)
        self.font.render_to(surf, (ppx + 8, cy + 2), "Save",
                            OK_COLOR if self.gen_preset_name else DIM_COLOR)

        cy += 30
        pygame.draw.line(surf, BTN_BORDER, (px+12, cy), (px+pw-12, cy), 1)
        cy += 10

        # ---- Settings row ----

        # Token field
        self.font.render_to(surf, (lx, cy), "Mapbox token:", DIM_COLOR)
        cy += 18
        tok_rect = pygame.Rect(lx, cy, 420, 26)
        active = self._gen_input_focus == 'token'
        pygame.draw.rect(surf, (20, 28, 40) if active else (12, 18, 26), tok_rect, border_radius=4)
        pygame.draw.rect(surf, ACCENT_COLOR if active else BTN_BORDER, tok_rect, 1, border_radius=4)
        disp_tok = self.gen_token if len(self.gen_token) <= 20 else \
                   self.gen_token[:8] + "…" + self.gen_token[-8:]
        self.font.render_to(surf, (lx+6, cy+6), disp_tok or "click or Ctrl+V to paste token",
                            TEXT_COLOR if self.gen_token else DIM_COLOR)
        cy += 32

        # Output folder
        self.font.render_to(surf, (lx, cy), "Output folder:", DIM_COLOR)
        cy += 18
        dir_rect = pygame.Rect(lx, cy, 420, 26)
        active_dir = self._gen_input_focus == 'outdir'
        pygame.draw.rect(surf, (20, 28, 40) if active_dir else (12, 18, 26), dir_rect, border_radius=4)
        pygame.draw.rect(surf, ACCENT_COLOR if active_dir else BTN_BORDER, dir_rect, 1, border_radius=4)
        disp_dir = str(self.gen_out_dir)[-48:] if self.gen_out_dir else ""
        self.font.render_to(surf, (lx+6, cy+6), disp_dir or "click to choose folder",
                            TEXT_COLOR if self.gen_out_dir else DIM_COLOR)
        cy += 32

        # Options row
        opts_y = cy
        # NLCD toggle
        nlcd_bw = self.font_big.get_rect("NLCD land cover").width + 20
        hover = pygame.Rect(lx, opts_y, nlcd_bw, 26).collidepoint(mx0, my0)
        self._draw_button(surf, (lx, opts_y, nlcd_bw, 26), "NLCD land cover",
                          self.gen_use_nlcd, hover, OK_COLOR)
        ox = lx + nlcd_bw + 10

        # Workers
        self.font.render_to(surf, (ox, opts_y+5), f"Workers: {self.gen_workers}", TEXT_COLOR)
        ox += self.font.get_rect(f"Workers: {self.gen_workers}").width + 4
        for sym, delta in [('−',-1),('+',1)]:
            br = pygame.Rect(ox, opts_y+1, 22, 22)
            hover2 = br.collidepoint(mx0, my0)
            pygame.draw.rect(surf, BTN_HOVER_C if hover2 else BTN_INACTIVE, br, border_radius=3)
            pygame.draw.rect(surf, BTN_BORDER, br, 1, border_radius=3)
            self.font.render_to(surf, (ox+5, opts_y+5), sym, TEXT_COLOR)
            ox += 26
        ox += 14

        # Veg override
        self.font.render_to(surf, (ox, opts_y+5),
            f"Veg override: {'off' if self.gen_veg_override is None else self.gen_veg_override}",
            TEXT_COLOR)
        ox += self.font.get_rect("Veg override: off").width + 4
        for sym2, d2 in [('−',-1),('+',1)]:
            br2 = pygame.Rect(ox, opts_y+1, 22, 22)
            hover3 = br2.collidepoint(mx0, my0)
            pygame.draw.rect(surf, BTN_HOVER_C if hover3 else BTN_INACTIVE, br2, border_radius=3)
            pygame.draw.rect(surf, BTN_BORDER, br2, 1, border_radius=3)
            self.font.render_to(surf, (ox+5, opts_y+5), sym2, TEXT_COLOR)
            ox += 26
        cy = opts_y + 36

        pygame.draw.line(surf, BTN_BORDER, (px+12, cy), (px+pw-12, cy), 1)
        cy += 10

        # ---- Tile grid ----
        grid_label = ("Drag to box-select  ·  right-click to dequeue  ·  "
                      "scroll to zoom  ·  MMB/drag to pan")
        self.font.render_to(surf, (lx, cy), grid_label, DIM_COLOR)
        cy += 18

        # Grid area — everything below the label, above the run button row
        run_row_h = 44
        grid_area = pygame.Rect(lx, cy, pw - 32, h - cy - py - STATUS_H - run_row_h)

        # Clip to grid area
        old_clip = surf.get_clip()
        surf.set_clip(grid_area)

        # Grid coordinate range: existing tiles ± gen_pad, unbounded
        if self.tiles:
            gx_min = self.min_x - self.gen_pad
            gx_max = self.max_x + self.gen_pad
            gy_min = self.min_y - self.gen_pad
            gy_max = self.max_y + self.gen_pad
        else:
            gx_min, gx_max, gy_min, gy_max = -75, -55, -52, -35

        # Also expand to cover queued/done tiles
        all_tracked = (self.gen_queue | self.gen_done | self.gen_failed
                       | set(self.gen_running.keys()))
        if all_tracked:
            all_xs = [t[0] for t in all_tracked]
            all_ys = [t[1] for t in all_tracked]
            gx_min = min(gx_min, min(all_xs) - 2)
            gx_max = max(gx_max, max(all_xs) + 2)
            gy_min = min(gy_min, min(all_ys) - 2)
            gy_max = max(gy_max, max(all_ys) + 2)

        csz = self.gen_cell_sz   # pixels per cell

        # Centre view on existing tiles first time panel opens
        if self.gen_view_x == 0.0 and self.gen_view_y == 0.0 and self.tiles:
            cx_tile = (self.min_x + self.max_x) / 2.0
            cy_tile = (self.min_y + self.max_y) / 2.0
            self.gen_view_x = grid_area.centerx - cx_tile * csz
            self.gen_view_y = grid_area.centery + cy_tile * csz  # Y flipped

        def tile_to_screen(gx, gy):
            sx = grid_area.x + self.gen_view_x + gx * csz
            sy = grid_area.y + self.gen_view_y - gy * csz   # flip Y
            return int(sx), int(sy)

        def screen_to_tile_gen(sx, sy):
            gx = (sx - grid_area.x - self.gen_view_x) / csz
            gy = (grid_area.y + self.gen_view_y - sy) / csz
            return int(math.floor(gx)), int(math.ceil(gy))

        # Store for click handler
        self._gen_grid = dict(
            grid_area=grid_area, csz=csz,
            view_x=self.gen_view_x, view_y=self.gen_view_y,
            px=px, py=py, pw=pw, ph=ph,
            tile_to_screen=tile_to_screen,
            screen_to_tile_gen=screen_to_tile_gen,
        )

        # Draw cells visible in grid area
        vx0, vy0 = screen_to_tile_gen(grid_area.left,  grid_area.bottom)
        vx1, vy1 = screen_to_tile_gen(grid_area.right, grid_area.top)
        vx0 = max(gx_min, vx0 - 1);  vx1 = min(gx_max, vx1 + 1)
        vy0 = max(gy_min, vy0 - 1);  vy1 = min(gy_max, vy1 + 1)

        # Box-select preview rect
        box_rect = None
        if self.gen_box_start and self.gen_box_end:
            bsx, bsy = self.gen_box_start
            bex, bey = self.gen_box_end
            box_rect = (min(bsx,bex), min(bsy,bey), max(bsx,bex), max(bsy,bey))

        for gy in range(vy1, vy0 - 1, -1):
            for gx in range(vx0, vx1 + 1):
                sx2, sy2 = tile_to_screen(gx, gy)
                key = f'{gx},{gy}'
                exists  = key in self.tiles
                queued  = (gx, gy) in self.gen_queue
                running = (gx, gy) in self.gen_running
                done    = (gx, gy) in self.gen_done
                failed  = (gx, gy) in self.gen_failed
                in_box  = (box_rect and
                           box_rect[0] <= gx <= box_rect[2] and
                           box_rect[1] <= gy <= box_rect[3])

                if running:   col = (180, 140,   0)
                elif done:    col = (  0, 140, 100)
                elif failed:  col = (180,  40,  40)
                elif queued:  col = ( 60, 100, 180)
                elif exists:  col = ( 20,  80,  70)
                else:         col = ( 18,  24,  34)

                cr = pygame.Rect(sx2, sy2, csz - 1, csz - 1)
                pygame.draw.rect(surf, col, cr, border_radius=max(1, csz//8))
                if in_box:
                    pygame.draw.rect(surf, (120, 180, 255), cr, 2, border_radius=max(1, csz//8))
                elif cr.collidepoint(mx0, my0):
                    pygame.draw.rect(surf, (200, 220, 255), cr, 1, border_radius=max(1, csz//8))

                if csz >= 14 and running:
                    msg = self.gen_running.get((gx,gy),'')[:max(1, csz//8)]
                    self.font.render_to(surf, (sx2+2, sy2+2), msg, (255,220,80))
                if csz >= 20 and (exists or queued or done):
                    lbl = f"{gx},{gy}"
                    self.font.render_to(surf, (sx2+2, sy2+2), lbl, DIM_COLOR)

        # Hover tooltip
        hgx, hgy = screen_to_tile_gen(mx0, my0)
        if grid_area.collidepoint(mx0, my0):
            hkey = f'{hgx},{hgy}'
            hstatus = ("running" if (hgx,hgy) in self.gen_running else
                       "queued" if (hgx,hgy) in self.gen_queue else
                       "done" if (hgx,hgy) in self.gen_done else
                       "failed" if (hgx,hgy) in self.gen_failed else
                       "exists" if hkey in self.tiles else "empty")
            tip = f"({hgx}, {hgy})  {hstatus}"
            if self.gen_box_start and box_rect:
                nx = box_rect[2]-box_rect[0]+1; ny = box_rect[3]-box_rect[1]+1
                tip += f"  |  selecting {nx}×{ny} = {nx*ny} tiles"
            self.font.render_to(surf, (mx0+14, my0-20), tip, TEXT_COLOR)

        surf.set_clip(old_clip)

        # Grid border
        pygame.draw.rect(surf, BTN_BORDER, grid_area, 1)

        # Legend + Run button row
        leg_y = grid_area.bottom + 8
        llx = lx
        for col2, label2 in [
            ((20,80,70),"exists"), ((60,100,180),"queued"),
            ((180,140,0),"running"), ((0,140,100),"done"),
            ((180,40,40),"failed"), ((18,24,34),"empty"),
        ]:
            pygame.draw.rect(surf, col2, (llx, leg_y, 12, 12), border_radius=2)
            self.font.render_to(surf, (llx+16, leg_y), label2, DIM_COLOR)
            llx += self.font.get_rect(label2).width + 28

        # ---- Run / Stop button ----
        btn_w = 140; btn_h = 32
        btn_x = px + pw - btn_w - 16; btn_y = py + ph - btn_h - 12
        if self.gen_active:
            label3 = f"Running ({len(self.gen_running)})…"
            self._draw_button(surf, (btn_x, btn_y, btn_w, btn_h), label3, True, False, WARN_COLOR)
        else:
            q = len(self.gen_queue)
            label3 = f"Generate {q} tile{'s' if q!=1 else ''}" if q else "Nothing queued"
            hover4 = pygame.Rect(btn_x, btn_y, btn_w, btn_h).collidepoint(mx0, my0)
            self._draw_button(surf, (btn_x, btn_y, btn_w, btn_h), label3,
                              q > 0, hover4, OK_COLOR)

        # Queue count summary
        self.font.render_to(surf, (px+16, btn_y+8),
            f"Queued: {len(self.gen_queue)}  Running: {len(self.gen_running)}  "
            f"Done: {len(self.gen_done)}  Failed: {len(self.gen_failed)}",
            DIM_COLOR)

    def _draw_profile_panel(self, w, h, content_top):
        if not getattr(self, 'profile_panel', False):
            self._profile_panel_rect = None
            self._profile_plot_rect = None
            self._profile_button_rects = []
            self._profile_node_rects = []
            self._profile_last_data = None
            self.profile_hover_world = None
            self.profile_hover_station_m = None
            self.profile_hover_node_id = None
            return

        panel_h = self._profile_dock_height()
        py = h - STATUS_H - panel_h
        panel_rect = pygame.Rect(0, py, w, panel_h)
        self._profile_panel_rect = panel_rect
        self._profile_button_rects = []
        self._profile_node_rects = []

        panel = pygame.Surface((w, panel_h), pygame.SRCALPHA)
        panel.fill((8, 12, 20, 236))
        self.screen.blit(panel, (0, py))
        pygame.draw.line(self.screen, BORDER_COLOR, (0, py), (w, py), 1)
        pygame.draw.line(self.screen, BORDER_COLOR, (0, h - STATUS_H), (w, h - STATUS_H), 1)

        header_y = py + 8
        self.font_big.render_to(self.screen, (14, header_y), "Profile", ACCENT_COLOR)
        data = self._build_profile_data()
        self._profile_last_data = data
        source = data.get('source')

        button_specs = [
            ("Bench", "profile_bench_mark", (120, 200, 255), bool(source and self._profile_anchor_node_id())),
            ("Clear Bench", "profile_bench_clear", WARN_COLOR, bool(self.profile_benchmarks)),
        ]
        bx = w - 14
        for label, action, color, enabled in reversed(button_specs):
            bw = self.font_big.get_rect(label).width + 16
            rect = pygame.Rect(bx - bw, py + 6, bw, 24)
            hover = rect.collidepoint(pygame.mouse.get_pos())
            fill = color if hover and enabled else ((22, 28, 40) if enabled else (18, 22, 30))
            border = color if enabled else (50, 58, 70)
            txt = TEXT_COLOR if enabled else (92, 102, 114)
            pygame.draw.rect(self.screen, fill, rect, border_radius=5)
            pygame.draw.rect(self.screen, border, rect, 1, border_radius=5)
            self.font_big.render_to(self.screen, (rect.x + 8, rect.y + 5), label, txt)
            if enabled:
                self._profile_button_rects.append((rect, action))
            bx = rect.x - 8

        if not source:
            self.font.render_to(
                self.screen,
                (14, py + 34),
                "Build a Grade chain, set a Measure pair, or select a segment to open the vertical profile.",
                TEXT_SOFT,
            )
            self.font.render_to(
                self.screen,
                (14, py + 52),
                "Phase 3 uses the current chain automatically and keeps the map visible above the dock.",
                DIM_COLOR,
            )
            return

        summary = (
            f"{source.get('label', 'Chain')}  |  {len(data.get('node_marks', []))} pts  |  "
            f"{float(data.get('station_end_m', 0.0)):.1f} m  |  max cut/fill {float(data.get('max_cut_fill_m', 0.0)):.1f} m"
        )
        summary_x = 88
        summary_max_w = max(180, bx - summary_x - 10)
        self.font.render_to(
            self.screen,
            (summary_x, header_y + 2),
            self._fit_text_to_width(self.font, summary, summary_max_w),
            TEXT_SOFT,
        )

        warnings = list(data.get('warnings', []))
        warning_entries = []
        for warning in warnings[:3]:
            warn_col = WARN_COLOR if warning.get('severity') != 'error' else (255, 110, 110)
            warning_entries.append((
                f"{warning.get('text', 'Warning')} at {self._format_station_value(warning.get('station_m', 0.0))}",
                warn_col,
            ))
        if len(warnings) > 3:
            warning_entries.append((f"+{len(warnings) - 3} more warning(s)", WARN_COLOR))

        warning_box_h = 0
        warning_box_bottom = py + 34
        if warning_entries:
            warning_rect = pygame.Rect(12, py + 34, max(220, w - 24), 10)
            wrapped_warning_entries = []
            for text, color in warning_entries:
                for line in self._wrap_text_lines(self.font, text, warning_rect.width - 20):
                    wrapped_warning_entries.append((line, color))
            warning_box_h = 14 + 16 + len(wrapped_warning_entries) * 15
            warning_rect.height = warning_box_h
            pygame.draw.rect(self.screen, PANEL_SECTION_BG, warning_rect, border_radius=8)
            pygame.draw.rect(self.screen, WARN_COLOR, warning_rect, 1, border_radius=8)
            self.font.render_to(
                self.screen,
                (warning_rect.x + 8, warning_rect.y + 5),
                f"Warnings ({len(warnings)})",
                WARN_COLOR,
            )
            line_y = warning_rect.y + 21
            for line, color in wrapped_warning_entries:
                self.font.render_to(self.screen, (warning_rect.x + 10, line_y), line, color)
                line_y += 15
            warning_box_bottom = warning_rect.bottom

        vertical_preview = data.get('vertical_preview') or {}
        if vertical_preview and not vertical_preview.get('errors'):
            footer_text = (
                "Purple = proposed vertical curve; cyan = current track. "
                "Apply from Geometry > Grade when the profile is correct."
            )
        else:
            footer_text = (
                "Drag a node vertically here to edit Y only. "
                "Bench pins hold target elevations on the current chain."
            )
        footer_lines = self._wrap_text_lines(self.font, footer_text, max(220, w - 28))
        footer_h = 8 + len(footer_lines) * 15
        station_label_h = 18
        plot_top = (warning_box_bottom + 8) if warning_box_h else (py + 36)
        plot_bottom = py + panel_h - footer_h - station_label_h - 8
        plot_h = max(96, plot_bottom - plot_top)
        plot_rect = pygame.Rect(68, plot_top, max(240, w - 82), plot_h)
        self._profile_plot_rect = plot_rect
        self._profile_last_data['plot_rect'] = plot_rect
        self._profile_last_data['panel_rect'] = panel_rect

        pygame.draw.rect(self.screen, (10, 16, 26), plot_rect, border_radius=8)
        pygame.draw.rect(self.screen, PANEL_SECTION_BORDER, plot_rect, 1, border_radius=8)

        y_min = float(data.get('y_min', 0.0))
        y_max = float(data.get('y_max', 1.0))
        station_end = max(1.0, float(data.get('station_end_m', 1.0)))

        def station_to_x(station_m: float) -> int:
            frac = max(0.0, min(1.0, float(station_m) / station_end))
            return int(plot_rect.x + frac * plot_rect.width)

        def elev_to_y(elev_m: float) -> int:
            frac = 0.5 if abs(y_max - y_min) < 1e-6 else ((float(elev_m) - y_min) / (y_max - y_min))
            return int(plot_rect.bottom - frac * plot_rect.height)

        self._profile_last_data['station_to_x'] = station_to_x
        self._profile_last_data['elev_to_y'] = elev_to_y
        self._profile_last_data['y_min'] = y_min
        self._profile_last_data['y_max'] = y_max

        for tick in range(5):
            frac = tick / 4.0
            gy = int(plot_rect.bottom - frac * plot_rect.height)
            elev = y_min + (y_max - y_min) * frac
            pygame.draw.line(self.screen, (24, 34, 50), (plot_rect.x, gy), (plot_rect.right, gy), 1)
            self.font.render_to(self.screen, (12, gy - 7), f"{elev:.0f}m", DIM_COLOR)

        for tick in range(6):
            frac = tick / 5.0
            gx = int(plot_rect.x + frac * plot_rect.width)
            sta = station_end * frac
            pygame.draw.line(self.screen, (24, 34, 50), (gx, plot_rect.y), (gx, plot_rect.bottom), 1)
            sta_text = self._format_station_value(sta)
            sta_rect = self.font.get_rect(sta_text)
            sta_x = max(plot_rect.x, min(plot_rect.right - sta_rect.width, gx - (sta_rect.width // 2)))
            self.font.render_to(self.screen, (sta_x, plot_rect.bottom + 4), sta_text, DIM_COLOR)

        samples = data.get('samples', [])
        if len(samples) >= 2:
            cutfill_overlay = pygame.Surface((w, h), pygame.SRCALPHA)
            for left, right in zip(samples, samples[1:]):
                lx = station_to_x(left.get('station_m', 0.0))
                rx = station_to_x(right.get('station_m', 0.0))
                lty = elev_to_y(left.get('track_y', 0.0))
                rty = elev_to_y(right.get('track_y', 0.0))
                lgy = elev_to_y(left.get('terrain_y', 0.0))
                rgy = elev_to_y(right.get('terrain_y', 0.0))
                shade = (45, 70, 38, 90) if ((left.get('track_y', 0.0) + right.get('track_y', 0.0)) * 0.5 <= (left.get('terrain_y', 0.0) + right.get('terrain_y', 0.0)) * 0.5) else (90, 62, 32, 90)
                poly = [(lx, lty), (rx, rty), (rx, rgy), (lx, lgy)]
                pygame.draw.polygon(cutfill_overlay, shade, poly)
            self.screen.blit(cutfill_overlay, (0, 0))

            terrain_pts = [(station_to_x(sample.get('station_m', 0.0)), elev_to_y(sample.get('terrain_y', 0.0))) for sample in samples]
            track_pts = [(station_to_x(sample.get('station_m', 0.0)), elev_to_y(sample.get('track_y', 0.0))) for sample in samples]
            if len(terrain_pts) >= 2:
                pygame.draw.lines(self.screen, (120, 150, 95), False, terrain_pts, 2)
            if len(track_pts) >= 2:
                pygame.draw.lines(self.screen, (0, 220, 255), False, track_pts, 3)

        if vertical_preview and not vertical_preview.get('errors'):
            preview_pts = [
                (
                    station_to_x(point.get('relative_station_m', 0.0)),
                    elev_to_y(point.get('y', 0.0)),
                )
                for point in vertical_preview.get('dense_points', [])
            ]
            if len(preview_pts) >= 2:
                pygame.draw.lines(
                    self.screen, (210, 120, 255), False, preview_pts, 3
                )

            boundaries = [
                (
                    vertical_preview.get('transition_in_end_m'),
                    "entry end",
                ),
                (
                    vertical_preview.get('transition_out_start_m'),
                    "exit start",
                ),
            ]
            for station_m, label in boundaries:
                if station_m is None:
                    continue
                boundary_x = station_to_x(float(station_m))
                for dash_y in range(plot_rect.y, plot_rect.bottom, 8):
                    pygame.draw.line(
                        self.screen,
                        (150, 90, 190),
                        (boundary_x, dash_y),
                        (boundary_x, min(plot_rect.bottom, dash_y + 4)),
                        1,
                    )
                self.font.render_to(
                    self.screen,
                    (boundary_x + 4, plot_rect.bottom - 16),
                    label,
                    (180, 125, 215),
                )

            for point in vertical_preview.get('node_points', []):
                point_x = station_to_x(
                    point.get('relative_station_m', 0.0)
                )
                point_y = elev_to_y(point.get('y', 0.0))
                pygame.draw.circle(
                    self.screen, (210, 120, 255), (point_x, point_y), 5, 1
                )

        last_label_x = -9999
        for grade in data.get('grade_labels', []):
            gx = station_to_x(grade.get('station_m', 0.0))
            if gx - last_label_x < 52:
                continue
            gy = elev_to_y(next((mark.get('track_y', 0.0) for mark in data.get('node_marks', []) if mark.get('id') == grade.get('start_id')), data.get('node_marks', [{}])[0].get('track_y', 0.0)))
            grade_text = f"{abs(float(grade.get('grade_pct', 0.0))):.2f}%"
            grade_col = OK_COLOR if abs(float(grade.get('grade_pct', 0.0))) < self.profile_grade_warn_pct else WARN_COLOR
            self.font.render_to(self.screen, (gx - 14, max(plot_rect.y + 2, gy - 18)), grade_text, grade_col)
            last_label_x = gx

        selected_ids = {self.sel_mod_node_id, self.profile_selected_node_id}
        for bench in data.get('benchmarks', []):
            bx2 = station_to_x(bench.get('station_m', 0.0))
            by2 = elev_to_y(bench.get('track_y', 0.0))
            for dy in range(plot_rect.y, plot_rect.bottom, 6):
                pygame.draw.line(self.screen, (90, 180, 255), (bx2, dy), (bx2, min(plot_rect.bottom, dy + 3)), 1)
            pygame.draw.circle(self.screen, (90, 180, 255), (bx2, by2), 5, 1)
            self.font.render_to(self.screen, (bx2 + 8, by2 - 10), str(bench.get('label', 'Bench')), (90, 180, 255))

        hover_node_id = None
        mx0, my0 = pygame.mouse.get_pos()
        if plot_rect.collidepoint(mx0, my0) and samples:
            target_station = station_end * ((mx0 - plot_rect.x) / max(1, plot_rect.width))
            hover_sample = min(samples, key=lambda sample: abs(float(sample.get('station_m', 0.0)) - target_station))
            self.profile_hover_station_m = float(hover_sample.get('station_m', 0.0))
            self.profile_hover_world = {
                'x': float(hover_sample.get('x', 0.0)),
                'z': float(hover_sample.get('z', 0.0)),
                'track_y': float(hover_sample.get('track_y', 0.0)),
                'terrain_y': float(hover_sample.get('terrain_y', 0.0)),
                'station_m': float(hover_sample.get('station_m', 0.0)),
            }
            hx = station_to_x(hover_sample.get('station_m', 0.0))
            pygame.draw.line(self.screen, (160, 200, 255), (hx, plot_rect.y), (hx, plot_rect.bottom), 1)
            tip = (
                f"{self._format_station_value(hover_sample.get('station_m', 0.0))}  "
                f"trk {float(hover_sample.get('track_y', 0.0)):.1f} m  "
                f"ter {float(hover_sample.get('terrain_y', 0.0)):.1f} m"
            )
            preview_samples = list(vertical_preview.get('dense_points', []))
            if preview_samples and not vertical_preview.get('errors'):
                preview_hover = min(
                    preview_samples,
                    key=lambda point: abs(
                        float(point.get('relative_station_m', 0.0))
                        - target_station
                    ),
                )
                tip += (
                    f"  proposed {float(preview_hover.get('y', 0.0)):.1f} m"
                    f" @ {float(preview_hover.get('grade_pct', 0.0)):+.2f}%"
                )
            self.font.render_to(
                self.screen,
                (plot_rect.x + 10, plot_rect.y + 8),
                self._fit_text_to_width(self.font, tip, plot_rect.width - 20),
                TEXT_COLOR,
            )
            hsx, hsy = self.unity_to_screen(hover_sample.get('x', 0.0), hover_sample.get('z', 0.0))
            if content_top < hsy < py:
                pygame.draw.circle(self.screen, (255, 255, 255), (hsx, hsy), 7, 1)
                pygame.draw.circle(self.screen, (0, 220, 255), (hsx, hsy), 5, 1)
        else:
            self.profile_hover_station_m = None
            self.profile_hover_world = None

        for warning in data.get('warnings', []):
            wx = station_to_x(warning.get('station_m', 0.0))
            wy = plot_rect.y + 8
            wcol = WARN_COLOR if warning.get('severity') != 'error' else (255, 110, 110)
            pygame.draw.polygon(self.screen, wcol, [(wx, wy), (wx - 5, wy + 10), (wx + 5, wy + 10)])

        for mark in data.get('node_marks', []):
            sx = station_to_x(mark.get('station_m', 0.0))
            sy = elev_to_y(mark.get('track_y', 0.0))
            node_id = str(mark.get('id'))
            is_selected = node_id in {str(item) for item in selected_ids if item}
            radius = 6 if is_selected else 5
            fill = (255, 210, 80) if is_selected else (0, 220, 255)
            border = (255, 255, 255) if is_selected else (10, 16, 26)
            rect = pygame.Rect(sx - 8, sy - 8, 16, 16)
            self._profile_node_rects.append((rect, node_id, float(mark.get('track_y', 0.0)), float(mark.get('station_m', 0.0))))
            if rect.collidepoint(mx0, my0):
                hover_node_id = node_id
            pygame.draw.circle(self.screen, fill, (sx, sy), radius)
            pygame.draw.circle(self.screen, border, (sx, sy), radius, 1)
            if is_selected or rect.collidepoint(mx0, my0):
                self.font.render_to(self.screen, (sx + 8, sy - 10), node_id, TEXT_COLOR)

        self.profile_hover_node_id = hover_node_id
        footer_y = py + panel_h - footer_h
        for line in footer_lines:
            self.font.render_to(self.screen, (14, footer_y), line, DIM_COLOR)
            footer_y += 15

    # ------------------------------------------------------------------
    # Drawing
    # ------------------------------------------------------------------


    def draw(self):
        """Main draw: delegates to sub-methods from DrawMixin."""
        w, h = self.screen.get_size()
        self.screen.fill(BG_COLOR)
        content_top = PANEL_H + (TOOLBAR_H if self.edit_mode else 0)
        content_bottom = self._profile_panel_top() if self.profile_panel else (h - STATUS_H)

        mx0, my0 = pygame.mouse.get_pos()
        if self.edit_mode and content_top < my0 < content_bottom:
            self._update_cursor_readout(mx0, my0)
        else:
            self.cursor_height_m = None

        ts = self.tile_size * self.zoom

        # Stable bounds during generation
        if self.gen_active:
            pre_tiles = [t for t in list(self.tiles.values())
                         if (t.x, t.y) not in self.gen_done]
            if pre_tiles:
                draw_min_x = min(t.x for t in pre_tiles)
                draw_max_x = max(t.x for t in pre_tiles)
                draw_min_y = min(t.y for t in pre_tiles)
                draw_max_y = max(t.y for t in pre_tiles)
            else:
                draw_min_x, draw_max_x = self.min_x, self.max_x
                draw_min_y, draw_max_y = self.min_y, self.max_y
        else:
            draw_min_x, draw_max_x = self.min_x, self.max_x
            draw_min_y, draw_max_y = self.min_y, self.max_y

        # ── Layer 1: terrain ──────────────────────────────────────────
        self._draw_welcome(w, h, content_top)
        self._draw_terrain(w, h, content_top, ts,
                           draw_min_x, draw_max_x, draw_min_y, draw_max_y)

        # ── Layer 2: track overlay ────────────────────────────────────
        self._draw_track_overlay(w, h, content_top)

        # ── Layer 3: active panel ─────────────────────────────────────
        if _MOD_AVAILABLE and self.mod_project:
            if self.mod_panel:
                self._draw_mod_panel(self.screen, content_top)
            elif self.prog_panel:
                self._draw_progression_panel(self.screen, content_top)
            elif self.area_panel:
                self._draw_area_panel(self.screen, content_top)
            elif self.span_panel:
                self._draw_spans_panel(self.screen, content_top)
            elif self.scenery_panel:
                self._draw_scenery_panel(self.screen, content_top)
            elif self.spliney_panel:
                self._draw_spliney_panel(self.screen, content_top)
            elif self.group_panel:
                self._draw_group_rubber_band()
                self._draw_group_panel(self.screen, content_top)
            elif self.calc_panel:
                self._draw_calc_panel(self.screen, content_top)
            elif self.mandela_panel:
                self._draw_mandela_panel(self.screen, content_top)
            elif self.geo_panel:
                self._draw_geo_panel(self.screen, content_top)
            elif self.sel_spliney_id:
                self._draw_spliney_props(self.screen, content_top)
        elif self.calc_panel:
            self._draw_calc_panel(self.screen, content_top)

        # ── Layer 4: selection, geo preview, cursors, hover ───────────
        self._draw_selection_highlight(w, h, content_top)
        self._draw_geo_preview(w, h, content_top)
        self._draw_cursors(w, h, content_top, mx0, my0)
        self._draw_hover_info(w, h, content_top, mx0, my0)
        self._draw_profile_panel(w, h, content_top)

        # ── Layer 5: UI chrome ────────────────────────────────────────
        self._draw_properties_panel(self.screen, content_top)
        self._draw_navbar(w, h, content_top, mx0, my0)
        self._draw_toolbar(w, h, content_top, mx0, my0)
        self._draw_tile_cleanup_panel(w, h, content_top, mx0, my0)
        self._draw_status_and_overlays(w, h, content_top, mx0, my0)

        pygame.display.flip()

    def handle_event(self, event):
        """Route pygame events to handler sub-methods from EventsMixin."""
        w, h = self.screen.get_size()
        content_top = PANEL_H + (TOOLBAR_H if self.edit_mode else 0)
        mx0, my0 = pygame.mouse.get_pos()
        resize_events = {
            event_id for event_id in (
                pygame.VIDEORESIZE,
                getattr(pygame, 'WINDOWRESIZED', None),
                getattr(pygame, 'WINDOWSIZECHANGED', None),
            )
            if event_id is not None
        }
        release_events = {
            event_id for event_id in (
                getattr(pygame, 'WINDOWFOCUSLOST', None),
                getattr(pygame, 'WINDOWLEAVE', None),
                getattr(pygame, 'WINDOWMINIMIZED', None),
            )
            if event_id is not None
        }

        if event.type == pygame.QUIT:
            return False
        elif event.type == pygame.DROPFILE:
            self.load_folder(event.file)
            return True
        elif event.type == pygame.KEYDOWN:
            return self._handle_keydown(event, mx0, my0, content_top)
        elif event.type == pygame.MOUSEBUTTONDOWN:
            return self._handle_mousedown(event, mx0, my0, content_top, w, h)
        elif event.type == pygame.MOUSEBUTTONUP:
            return self._handle_mouseup(event, mx0, my0, content_top)
        elif event.type == pygame.MOUSEMOTION:
            return self._handle_mousemotion(event, mx0, my0, content_top)
        elif event.type == pygame.MOUSEWHEEL:
            return self._handle_mousewheel(event, mx0, my0, content_top)
        elif event.type in release_events:
            self._cancel_pointer_interactions()
            self._suspend_canvas_drag = True
            return True
        elif event.type in resize_events:
            self._cancel_pointer_interactions()
            self._suspend_canvas_drag = True
            surface = pygame.display.get_surface()
            if surface is not None:
                self.screen = surface
            else:
                size = getattr(event, 'size', None)
                if size is None:
                    width = getattr(event, 'x', w)
                    height = getattr(event, 'y', h)
                    size = (width, height)
                self.screen = pygame.display.set_mode(size, pygame.RESIZABLE)
            return True
        return True


    def _zoom_at(self, pos, factor):
        mx, my = pos
        new_zoom = max(0.05, min(50, self.zoom * factor))
        self.pan_x = mx - (mx - self.pan_x) * (new_zoom / self.zoom)
        self.pan_y = my - (my - self.pan_y) * (new_zoom / self.zoom)
        self.zoom  = new_zoom
        # OSM surf cache is zoom-keyed — no invalidation needed, old entries
        # auto-expire since new disp_size differs; but clear to save memory.
        if self.osm.enabled:
            self.osm._surf_cache.clear()

    # ------------------------------------------------------------------
    # Main loop
    # ------------------------------------------------------------------
    def run(self):
        clock   = pygame.time.Clock()
        running = True
        frame = 0
        while running:
            for event in pygame.event.get():
                result = self.handle_event(event)
                if not result:
                    print(f"[run] handle_event returned False on event: {event}", flush=True)
                    running = False
            frame += 1
            if frame == 1:
                print(f"[run] first frame drawn OK, screen={self.screen.get_size()}", flush=True)
            self.draw()
            dt = clock.tick(60) / 1000.0   # seconds this frame

            # Bridge state poll (thread-safe handoff)
            self._poll_bridge()

            # Autosave
            if any(t.dirty for t in list(self.tiles.values())):
                self._autosave_timer += dt
                if self._autosave_timer >= self._autosave_interval:
                    self._autosave_timer = 0.0
                    threading.Thread(target=self._autosave, daemon=True).start()
            else:
                self._autosave_timer = 0.0

        # Bridge cleanup
        if self.bridge is not None:
            self.bridge.stop()

        # Prompt save on exit if dirty tiles exist
        dirty = [t for t in list(self.tiles.values()) if t.dirty]
        if dirty:
            print(f"\n{len(dirty)} tile(s) have unsaved changes.")
            try:
                ans = input("Save before exiting? [y/N] ").strip().lower()
                if ans == 'y':
                    self.save_all()
            except Exception:
                pass

        pygame.quit()


# =========================
# Entry point
# =========================
def main():
    parser = argparse.ArgumentParser(description="Terrain tile editor")
    parser.add_argument("folders", nargs='*', default=None)
    args = parser.parse_args()
    log_path = Path(__file__).resolve().parent.parent / "crash.log"
    print("[main] starting...", flush=True)
    try:
        print("[main] creating TileEditor...", flush=True)
        # Start immediately — use "Load Tiles" button to load folders at runtime
        editor = TileEditor(args.folders or [])
        print("[main] calling run()...", flush=True)
        editor.run()
        print("[main] run() returned normally", flush=True)
    except Exception:
        err = traceback.format_exc()
        try:
            sys.stderr.write(err + "\n")
            log_path.write_text(err, encoding="utf-8")
            sys.stderr.write("\nCrash log: " + str(log_path) + "\n")
        except Exception:
            pass
        input("\nPress Enter to close...")
        sys.exit(1)


if __name__ == '__main__':
    main()
