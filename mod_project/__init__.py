"""mod_project — Railroader mod toolset.

This package replaces the monolithic mod_project.py (was 4,148 lines).
All public symbols are re-exported here so existing code that does:

    from mod_project import ModProject, generate_curve, ...

continues to work with zero changes (K1).

Module layout:
    constants.py   — enums, lookup tables, magic numbers
    geometry.py    — bezier math, generate_curve, generate_turnout, etc.
    layer.py       — Layer class, _load_json, _save_json
    project.py     — ModProject, open_mod_folder, new_mod
    progression.py — ProgressionProject, ProgressionSection, MapFeature, Area
    helpers.py     — all set_*/delete_*/add_* functions
    validation.py  — validate_mod, export_clean_zip
    codegen.py     — generate_csharp_template, generate_harmony_patch, etc.
"""

# ── Constants ─────────────────────────────────────────────────────────────
from .constants import (
    # Layer type constants
    LAYER_BASE, LAYER_GRAPH, LAYER_TOWN, LAYER_RIVERS, LAYER_MIGRATION, LAYER_OTHER,
    LAYER_COLORS, TOWN_PALETTE,
    # Track class constants
    TRACK_CLASS_NAMES, TRACK_CLASS_JSON, TRACK_CLASS_DEFAULT_SPEED, TRACK_STYLES,
    # Track group / marker / simple graph
    TRACK_GROUP_NOTES, TRACK_MARKER_TYPES, SIMPLE_GRAPH_TAGS,
    # CTC / signal constants
    SIGNAL_ASPECTS, SIGNAL_HEAD_CONFIGS, SIGNAL_DIRECTIONS, CTC_DIRECTIONS,
    SWITCH_FILTERS, AI_HEURISTIC_COSTS, CTC_KEY_FORMATS,
    # ID helpers
    _rand_chars, _used_ids, _ID_CHARS,
    # Gauss constants (used by geometry internals)
    _GAUSS_T, _GAUSS_C,
)

# ── Layer ──────────────────────────────────────────────────────────────────
from .layer import (
    Layer, TRACK_GAUGES, normalize_track_gauge, _load_json, _save_json,
)

# ── Project ────────────────────────────────────────────────────────────────
from .project import ModProject

# ── Progression ────────────────────────────────────────────────────────────
from .progression import (
    ProgressionProject, ProgressionSection, MapFeature, AreaIndustry, Area,
)

# ── Geometry ───────────────────────────────────────────────────────────────
from .geometry import (
    # Bezier primitives
    _cubic_point, _cubic_deriv, _cubic_split,
    _bezier_length_gauss, _bezier_tangent_factor, _bezier_control_points, _bezier_for_nodes,
    _cubic_approximate_xz, _bezier_split,
    _bezier_parameter_for_distance, _bezier_parameter_closest_to,
    quad_bezier,
    # Track geometry helpers
    effective_speed_limit, flip_end, normalize_span,
    segment_length, segment_grade, segment_curve_degrees,
    segments_for_node, node_valency, next_marker_id,
    # Track generators
    generate_curve, generate_straight, generate_parallel_tracks, generate_turnout,
    turnout_leg_pose, turnout_radius_for_chord,
    generate_wye,
    # Node / segment utilities
    node_flatten, node_reverse, node_set_rotY, segment_set_props,
    # Geometry utilities
    _heading_to_vec, _perpendicular,
)

# ── Helpers ────────────────────────────────────────────────────────────────
from .helpers import (
    # Area / industry
    area_set, area_delete, industry_set, industry_delete,
    # Spans
    span_set, span_delete,
    # Node topology
    merge_nodes, split_node, smooth_grade, apply_grade_from_start, straighten_chain_xz,
    # Splineys
    spliney_set_point, spliney_add_road, spliney_add_maplabel, spliney_delete,
    next_spliney_id, spliney_insert_point, spliney_delete_point,
    turntable_set,
    # Scenery
    scenery_set, scenery_delete, next_scenery_id,
    # Loads
    load_set, load_delete,
    # Texts
    text_set, text_delete,
    # Trestle
    create_trestle_from_segment, fit_trestle_to_segment,
    # Group move
    move_group,
    # Mandela
    mandela_set, mandela_delete, next_mandela_id,
    # SimpleGraph
    simple_graph_node_set, simple_graph_node_delete, simple_graph_delete,
    # Segment flip / migration
    flip_segment, migration_set,
)

# ── Validation ─────────────────────────────────────────────────────────────
from .validation import validate_mod, export_clean_zip
from .vertical import (
    build_vertical_alignment,
    dense_vertical_alignment_stations,
)

# ── Code generation ────────────────────────────────────────────────────────
from .codegen import (
    bulletin_manifest_json,
    generate_csharp_template,
    generate_harmony_patch,
    generate_umm_entry,
)

__all__ = [
    # Constants
    'LAYER_BASE', 'LAYER_GRAPH', 'LAYER_TOWN', 'LAYER_RIVERS',
    'LAYER_MIGRATION', 'LAYER_OTHER', 'LAYER_COLORS', 'TOWN_PALETTE',
    'TRACK_CLASS_NAMES', 'TRACK_CLASS_JSON', 'TRACK_CLASS_DEFAULT_SPEED',
    'TRACK_STYLES', 'TRACK_GAUGES', 'normalize_track_gauge',
    'TRACK_GROUP_NOTES', 'TRACK_MARKER_TYPES', 'SIMPLE_GRAPH_TAGS',
    'SIGNAL_ASPECTS', 'SIGNAL_HEAD_CONFIGS', 'SIGNAL_DIRECTIONS', 'CTC_DIRECTIONS',
    'SWITCH_FILTERS', 'AI_HEURISTIC_COSTS', 'CTC_KEY_FORMATS',
    # Core classes
    'Layer', 'ModProject',
    # Progression
    'ProgressionProject', 'ProgressionSection', 'MapFeature', 'AreaIndustry', 'Area',
    # Geometry
    'effective_speed_limit', 'flip_end', 'normalize_span',
    'segment_length', 'segment_grade', 'segment_curve_degrees',
    'segments_for_node', 'node_valency', 'next_marker_id',
    'generate_curve', 'generate_straight', 'generate_parallel_tracks', 'generate_turnout',
    'turnout_leg_pose', 'turnout_radius_for_chord',
    'generate_wye',
    'node_flatten', 'node_reverse', 'node_set_rotY', 'segment_set_props',
    'quad_bezier',
    # Helpers
    'area_set', 'area_delete', 'industry_set', 'industry_delete',
    'span_set', 'span_delete',
    'merge_nodes', 'split_node', 'smooth_grade', 'apply_grade_from_start', 'straighten_chain_xz',
    'spliney_set_point', 'spliney_add_road', 'spliney_add_maplabel', 'spliney_delete',
    'next_spliney_id', 'spliney_insert_point', 'spliney_delete_point',
    'turntable_set', 'scenery_set', 'scenery_delete', 'next_scenery_id',
    'load_set', 'load_delete', 'text_set', 'text_delete',
    'create_trestle_from_segment', 'fit_trestle_to_segment', 'move_group',
    'mandela_set', 'mandela_delete', 'next_mandela_id',
    'simple_graph_node_set', 'simple_graph_node_delete', 'simple_graph_delete',
    'flip_segment', 'migration_set',
    # Validation
    'validate_mod', 'export_clean_zip',
    'build_vertical_alignment', 'dense_vertical_alignment_stations',
    # Code generation
    'bulletin_manifest_json', 'generate_csharp_template',
    'generate_harmony_patch', 'generate_umm_entry',
    # JSON helpers (used by edit_tiles indirectly)
    '_load_json', '_save_json',
]
