"""mod_project.helpers
All typed set_*/add_*/delete_* functions that operate on Layer objects.
"""

import copy as _copy
import math as _math
from pathlib import Path
from typing import Optional

from .layer import Layer
from .geometry import (
    _rand_chars, _bezier_length_gauss,
    _bezier_tangent_factor, _bezier_control_points,
    _cubic_point, _cubic_deriv, _cubic_split,
    segment_length, segments_for_node,
    _heading_to_vec,
)
from .constants import TRACK_CLASS_NAMES, TRACK_CLASS_JSON, TRACK_STYLES, SIMPLE_GRAPH_TAGS


# ---------------------------------------------------------------------------
# Area / Industry helpers
# ---------------------------------------------------------------------------
def area_set(layer: 'Layer', area_id: str, name: str,
             x: float, y: float, z: float,
             radius: float = 500.0, order: int = 0,
             tag_color: list = None):
    """Add or update an area (town) header in a layer.

    This sets only the area's metadata (name/position/radius/order/tagColor).
    Industries within the area are managed separately via industry_set().
    tag_color -- [r, g, b] floats 0-1, e.g. [0.6, 0.7, 0.5]
    """
    if 'areas' not in layer._raw:
        layer._raw['areas'] = {}
    existing = (layer._raw['areas'].get(area_id) or {})
    entry = dict(existing)
    entry.update({
        'name':     name,
        'position': {'x': x, 'y': y, 'z': z},
        'radius':   radius,
        'order':    order,
    })
    if tag_color is not None:
        entry['tagColor'] = tag_color
    if 'industries' not in entry:
        entry['industries'] = {}
    layer._raw['areas'][area_id] = entry
    layer.areas[area_id] = _copy.deepcopy(entry)
    layer.dirty = True


def area_delete(layer: 'Layer', area_id: str):
    """Delete an area from a layer (null patch = delete in merged view)."""
    layer.areas.pop(area_id, None)
    if 'areas' not in layer._raw:
        layer._raw['areas'] = {}
    layer._raw['areas'][area_id] = None
    layer.dirty = True


def industry_set(layer: 'Layer', area_id: str, industry_id: str,
                  name: str, components: dict,
                  local_pos: dict = None, uses_contract: bool = False):
    """Add or update an industry inside an area.

    components -- dict of component_id -> component dict (type, trackSpans, etc.)
                 Each component is written verbatim. Note: SerializedComponent.SharedStorage
                 defaults to true in C# (B11) -- omit 'sharedStorage' from component dicts
                 to accept the default, or set it explicitly only when you intend false.
    local_pos  -- {'x':0,'y':0,'z':0} offset from area position (optional)
    """
    if 'areas' not in layer._raw:
        layer._raw['areas'] = {}
    if area_id not in layer._raw['areas'] or layer._raw['areas'][area_id] is None:
        layer._raw['areas'][area_id] = {'industries': {}}
    area = layer._raw['areas'][area_id]
    if 'industries' not in area:
        area['industries'] = {}
    ind = {
        'name':          name,
        'localPosition': local_pos or {'x': 0, 'y': 0, 'z': 0},
        'usesContract':  uses_contract,
        'components':    components,
    }
    area['industries'][industry_id] = ind
    # Sync in-memory (deep copy so _raw and in-memory don't share refs)
    if area_id not in layer.areas or layer.areas[area_id] is None:
        layer.areas[area_id] = {'industries': {}}
    layer.areas[area_id].setdefault('industries', {})[industry_id] = _copy.deepcopy(ind)
    layer.dirty = True


def industry_delete(layer: 'Layer', area_id: str, industry_id: str):
    """Delete an industry from an area."""
    area_raw = (layer._raw.get('areas') or {}).get(area_id) or {}
    (area_raw.get('industries') or {}).pop(industry_id, None)
    area_mem = (layer.areas.get(area_id)) or {}
    (area_mem.get('industries') or {}).pop(industry_id, None)
    layer.dirty = True



# ---------------------------------------------------------------------------
# Span helpers (A2/B2)
# ---------------------------------------------------------------------------
def span_set(layer: 'Layer', span_id: str,
             lower_seg: str, lower_dist: float, lower_end: str,
             upper_seg: str, upper_dist: float, upper_end: str,
             normalize: bool = False):
    """Write a span entry to a layer.

    Argument order matches PatchEditor.AddOrUpdateSpan:
      (spanId, lowerId, lowerDist, lowerEnd, upperId, upperDist, upperEnd)
    i.e. lower comes first.

    normalize -- when True, game calls NormalizeUpperLower() after applying the span,
                which swaps upper/lower so lower.distance < upper.distance on the
                same segment.  Corresponds to SerializedSpan.Normalize.
                Only written to JSON when True (DefaultValueHandling=Ignore).
    """
    if 'tracks' not in layer._raw:
        layer._raw['tracks'] = {}
    if 'spans' not in layer._raw['tracks']:
        layer._raw['tracks']['spans'] = {}
    d = {
        'upper': {'segmentId': upper_seg, 'distance': upper_dist, 'end': upper_end},
        'lower': {'segmentId': lower_seg, 'distance': lower_dist, 'end': lower_end},
    }
    if normalize:
        d['normalize'] = True
    layer._raw['tracks']['spans'][span_id] = d
    layer.spans[span_id] = d
    layer.dirty = True


def span_delete(layer: 'Layer', span_id: str):
    """Delete a span from a layer (null = delete in merged view)."""
    layer.spans.pop(span_id, None)
    if 'tracks' in layer._raw and 'spans' in layer._raw['tracks']:
        layer._raw['tracks']['spans'].pop(span_id, None)
    layer.dirty = True



# ---------------------------------------------------------------------------
# Node topology helpers
# ---------------------------------------------------------------------------
def merge_nodes(graph_layer: 'Layer', merged_nodes: dict,
                merged_segments: dict, middle_node_id: str) -> str:
    """
    Remove a middle node that connects exactly two segments,
    replacing them with a single new segment.
    Returns the new segment ID or '' on failure.
    """
    segs = [s for s in merged_segments.values()
            if s.get('startId') == middle_node_id or
               s.get('endId')   == middle_node_id]
    if len(segs) != 2:
        return ''

    s0, s1 = segs
    # Find the outer endpoints
    a = s0['endId']   if s0['startId'] == middle_node_id else s0['startId']
    b = s1['endId']   if s1['startId'] == middle_node_id else s1['startId']

    # New segment inherits properties from s0
    new_sid = f"Smerge_{_rand_chars()}"
    graph_layer.set_segment(new_sid, a, b,
                            s0.get('trackClass', 'Mainline'),
                            s0.get('style', 'Standard'),
                            s0.get('speedLimit', 0),   # 0 = use class default, not 45
                            s0.get('priority', 0),
                            s0.get('groupId', ''),
                            s0.get('gauge', 'Standard'))
    graph_layer.delete_segment(s0['id'])
    graph_layer.delete_segment(s1['id'])
    graph_layer.delete_node(middle_node_id)
    return new_sid


def split_node(graph_layer: 'Layer', merged_nodes: dict,
               node_id: str, seg_id_to_disconnect: str) -> str:
    """
    Duplicate a node, reconnecting seg_id_to_disconnect to the new copy.
    Returns new node ID.
    """
    node = merged_nodes.get(node_id)
    if not node:
        return ''
    new_nid = f"Nsplit_{_rand_chars()}"
    # Place slightly offset so it's visible
    graph_layer.set_node(new_nid,
                         node['x'] + 0.5, node['y'], node['z'] + 0.5,
                         node.get('rotX', 0), node.get('rotY', 0),
                         node.get('rotZ', 0), node.get('flipSwitchStand', False))
    # Rewire the segment
    seg = graph_layer.segments.get(seg_id_to_disconnect, {})
    if not seg:
        # Look in the raw data of any segments referencing this node
        return new_nid
    s = dict(seg)
    if s.get('startId') == node_id:
        s['startId'] = new_nid
    elif s.get('endId') == node_id:
        s['endId'] = new_nid
    graph_layer.set_segment(s['id'], s['startId'], s['endId'],
                            s.get('trackClass', 'Mainline'),
                            s.get('style', 'Standard'),
                            s.get('speedLimit', 0),   # 0 = use class default, not 45
                            s.get('priority', 0),
                            s.get('groupId', ''),
                            s.get('gauge', 'Standard'))
    return new_nid



# ---------------------------------------------------------------------------
# Grade smoothing
# ---------------------------------------------------------------------------
def smooth_grade(nodes: list, fix_first: bool = False,
                 fix_last: bool = False) -> list:
    """
    Distribute elevation linearly across a chain of nodes.
    nodes: ordered list of node dicts {id, x, y, z, ...}
    Returns list of (node_id, new_y) tuples.
    """
    import math as _math
    if len(nodes) < 2:
        return []

    # Compute cumulative distance along chain
    dists = [0.0]
    for i in range(1, len(nodes)):
        dx = nodes[i]['x'] - nodes[i-1]['x']
        dz = nodes[i]['z'] - nodes[i-1]['z']
        dists.append(dists[-1] + _math.sqrt(dx*dx + dz*dz))

    total = dists[-1]
    if total < 0.001:
        return []

    y_start = nodes[0]['y']
    y_end   = nodes[-1]['y']

    results = []
    for i, node in enumerate(nodes):
        if i == 0 and fix_first:
            new_y = node['y']
        elif i == len(nodes)-1 and fix_last:
            new_y = node['y']
        else:
            t     = dists[i] / total
            new_y = y_start + t * (y_end - y_start)
        results.append((node['id'], new_y))

    return results


def straighten_chain_xz(nodes: list) -> list:
    """
    Interpolate X and Z positions linearly between the first and last node,
    so the chain becomes a straight line in plan view.  Y is untouched.

    nodes: ordered list of node dicts {id, x, y, z, ...}
    Returns list of (node_id, new_x, new_z) tuples.
    """
    import math as _math
    if len(nodes) < 3:
        # Nothing to straighten with only 2 nodes
        return [(n['id'], n['x'], n['z']) for n in nodes]

    x0, z0 = nodes[0]['x'],  nodes[0]['z']
    x1, z1 = nodes[-1]['x'], nodes[-1]['z']

    # Cumulative distances along the *current* chain (used to distribute evenly)
    dists = [0.0]
    for i in range(1, len(nodes)):
        dx = nodes[i]['x'] - nodes[i-1]['x']
        dz = nodes[i]['z'] - nodes[i-1]['z']
        dists.append(dists[-1] + _math.sqrt(dx*dx + dz*dz))
    total = dists[-1]

    results = []
    for i, node in enumerate(nodes):
        t = dists[i] / total if total > 0.001 else i / (len(nodes) - 1)
        new_x = x0 + t * (x1 - x0)
        new_z = z0 + t * (z1 - z0)
        results.append((node['id'], new_x, new_z))
    return results


def apply_grade_from_start(nodes: list, grade_pct: float,
                            fix_first: bool = True) -> list:
    """
    Set node elevations so the chain runs at a constant grade_pct (%).

    grade_pct: percent grade, positive = uphill A->B  (e.g. -2.1 for -2.1%)
    fix_first: when True the first node's Y is kept as the origin elevation;
               when False the midpoint elevation is preserved instead.

    Formula: new_y = y_origin + (grade_pct / 100) * cumulative_distance

    nodes: ordered list of node dicts {id, x, y, z, ...}
    Returns list of (node_id, new_y) tuples.
    """
    import math as _math
    if len(nodes) < 2:
        return []

    # Cumulative XZ distances along the chain
    dists = [0.0]
    for i in range(1, len(nodes)):
        dx = nodes[i]['x'] - nodes[i-1]['x']
        dz = nodes[i]['z'] - nodes[i-1]['z']
        dists.append(dists[-1] + _math.sqrt(dx*dx + dz*dz))

    total = dists[-1]
    if total < 0.001:
        return []

    if fix_first:
        y_origin = nodes[0]['y']
        origin_dist = 0.0
    else:
        # Anchor to the midpoint so the chain pivots around its centre
        mid_idx = len(nodes) // 2
        y_origin = nodes[mid_idx]['y']
        origin_dist = dists[mid_idx]

    results = []
    for i, node in enumerate(nodes):
        rise = (grade_pct / 100.0) * (dists[i] - origin_dist)
        new_y = y_origin + rise
        results.append((node['id'], new_y))

    return results


# ---------------------------------------------------------------------------
# Spliney / road / river helpers
# ---------------------------------------------------------------------------
def spliney_set_point(layer: 'Layer', spliney_id: str, point_idx: int,
                       x: float, y: float, z: float,
                       rotX: float = 0.0, rotY: float = 0.0, rotZ: float = 0.0,
                       width: float = None):
    """Update a single control point of a spliney in place."""
    spl = layer.splineys.get(spliney_id)
    if not spl or 'points' not in spl:
        return False
    pts = spl['points']
    if point_idx < 0 or point_idx >= len(pts):
        return False
    pt = dict(pts[point_idx])
    pt['position'] = {'x': x, 'y': y, 'z': z}
    pt['rotation']  = {'x': rotX, 'y': rotY, 'z': rotZ}
    if width is not None:
        pt['width'] = width
    pts[point_idx] = pt
    spl['points'] = pts
    # Sync back to raw -- create the key path if it doesn't exist yet
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = spl
    layer.dirty = True
    return True



# ---------------------------------------------------------------------------
# Scenery helpers (A3/B3)
# ---------------------------------------------------------------------------
def scenery_set(layer: 'Layer', scenery_id: str,
                model_id: str, x: float, y: float, z: float,
                rotX: float = 0.0, rotY: float = 0.0, rotZ: float = 0.0,
                scale_x: float = 1.0, scale_y: float = 1.0, scale_z: float = 1.0):
    """Add or update a scenery object in a layer.

    All three rotation axes and per-axis scale are written explicitly.
    SerializedScenery.ExtraData ([JsonExtensionData]) is preserved from the
    existing entry so unknown fields survive a round-trip (B3).
    """
    # Start from existing entry to preserve ExtraData / unknown fields
    existing = (layer._raw.get('scenery') or {}).get(scenery_id) or {}
    entry = dict(existing)
    entry.update({
        'modelIdentifier': model_id,
        'position': {'x': x, 'y': y, 'z': z},
        'rotation': {'x': rotX, 'y': rotY, 'z': rotZ},
        'scale':    {'x': scale_x, 'y': scale_y, 'z': scale_z},
    })
    if 'scenery' not in layer._raw:
        layer._raw['scenery'] = {}
    layer._raw['scenery'][scenery_id] = entry
    layer.scenery[scenery_id] = _copy.deepcopy(entry)
    layer.dirty = True


def scenery_delete(layer: 'Layer', scenery_id: str):
    """Write a mixinto deletion marker for a scenery object."""
    if layer.read_only:
        return False
    layer.scenery[scenery_id] = None
    if 'scenery' not in layer._raw:
        layer._raw['scenery'] = {}
    layer._raw['scenery'][scenery_id] = None
    layer.dirty = True
    return True


# ---------------------------------------------------------------------------
# Load helpers (A18/B10)
# ---------------------------------------------------------------------------
def load_set(layer: 'Layer', load_id: str,
             description: str,
             units: str,
             density: float,
             unit_weight_in_pounds: float,
             importable: bool = False,
             pay_per_quantity: float = 0.0,
             cost_per_unit: float = 0.0):
    """Add or update a load definition in a layer.

    Matches SerializedLoad (StrangeCustoms/Tracks/SerializedLoad.cs) exactly:
      description          -- display name
      units                -- LoadUnits enum string, e.g. 'Ton', 'CubicFoot', 'Each'
      density              -- mass per unit volume
      unit_weight_in_pounds
      importable           -- whether the load can be imported
      pay_per_quantity     -- revenue per unit delivered
      cost_per_unit        -- purchase cost per unit
    """
    entry = {
        'description':       description,
        'units':             units,
        'density':           float(density),
        'unitWeightInPounds': float(unit_weight_in_pounds),
        'importable':        bool(importable),
        'payPerQuantity':    float(pay_per_quantity),
        'costPerUnit':       float(cost_per_unit),
    }
    if 'loads' not in layer._raw:
        layer._raw['loads'] = {}
    layer._raw['loads'][load_id] = entry
    layer.loads[load_id] = _copy.deepcopy(entry)
    layer.dirty = True


def load_delete(layer: 'Layer', load_id: str):
    """Delete a load definition from a layer."""
    layer.loads.pop(load_id, None)
    if 'loads' in layer._raw:
        layer._raw['loads'].pop(load_id, None)
    layer.dirty = True



# ---------------------------------------------------------------------------
# Text helpers (A1)
# ---------------------------------------------------------------------------
def text_set(layer: 'Layer', text_id: str, text: str):
    """Add or update a text entry in a layer.

    TrackState.Texts is Dictionary<string,string> -- the value must be a plain
    string, not a nested dict.  e.g. {"SIGN1": "Waynesville"}
    """
    if 'texts' not in layer._raw:
        layer._raw['texts'] = {}
    layer._raw['texts'][text_id] = text
    layer.texts[text_id] = text
    layer.dirty = True


def text_delete(layer: 'Layer', text_id: str):
    """Delete a text entry from a layer."""
    layer.texts.pop(text_id, None)
    if 'texts' in layer._raw:
        layer._raw['texts'].pop(text_id, None)
    layer.dirty = True


# ---------------------------------------------------------------------------
# Trestle / spliney generators
# ---------------------------------------------------------------------------
def _trestle_points_for_segment(seg: dict, merged_nodes: dict,
                                 deck_offset_y: float = -0.3) -> list:
    """Sample the exact 3D rail Bezier for an AutoTrestleBuilder spliney."""
    n0 = merged_nodes.get(seg.get('startId', ''))
    n1 = merged_nodes.get(seg.get('endId', ''))
    if not n0 or not n1:
        return []

    start_rot_x = float(n0.get('rotX', 0.0) or 0.0)
    start_rot_y = float(n0.get('rotY', 0.0) or 0.0)
    start_rot_z = float(n0.get('rotZ', 0.0) or 0.0)
    end_rot_x = float(n1.get('rotX', 0.0) or 0.0)
    end_rot_y = float(n1.get('rotY', 0.0) or 0.0)
    end_rot_z = float(n1.get('rotZ', 0.0) or 0.0)

    curve_node0 = {
        'x': float(n0['x']),
        'y': float(n0['y']),
        'z': float(n0['z']),
        'rotX': start_rot_x,
        'rotY': start_rot_y,
    }
    curve_node1 = {
        'x': float(n1['x']),
        'y': float(n1['y']),
        'z': float(n1['z']),
        'rotX': end_rot_x,
        'rotY': end_rot_y,
    }
    p0, p1, p2, p3 = _bezier_control_points(curve_node0, curve_node1)
    curve_length = _bezier_length_gauss(p0, p1, p2, p3)

    # AutoTrestle joins its control points with its own spline. Sampling the
    # actual 3D rail cubic every ~8 m keeps the bridge deck on the rail curve
    # through both horizontal curves and vertical crests/sags. The previous
    # endpoint/linear-height approximation could cut visibly across a curve.
    sample_count = max(2, min(65, int(_math.ceil(curve_length / 8.0)) + 1))

    points = []
    for sample_idx in range(sample_count):
        t = sample_idx / max(1, sample_count - 1)
        px, py, pz = _cubic_point(p0, p1, p2, p3, t)
        dx, dy, dz = _cubic_deriv(p0, p1, p2, p3, t)
        horizontal = _math.hypot(dx, dz)
        if horizontal < 1e-7 and abs(dy) < 1e-7:
            heading = start_rot_y if sample_idx == 0 else end_rot_y
            rot_x = start_rot_x if sample_idx == 0 else end_rot_x
        else:
            heading = (_math.degrees(_math.atan2(dx, dz))) % 360.0
            rot_x = _math.degrees(_math.atan2(-dy, max(horizontal, 1e-7)))

        # Interpolate roll over the shortest angular path. Roll does not alter
        # the centerline, but retaining it keeps the bridge deck orientation
        # consistent with superelevated track.
        roll_delta = ((end_rot_z - start_rot_z + 180.0) % 360.0) - 180.0
        rot_z = start_rot_z + roll_delta * t
        points.append({
            'position': {
                'x': px,
                'y': py + float(deck_offset_y),
                'z': pz,
            },
            'rotation': {'x': rot_x, 'y': heading, 'z': rot_z},
        })
    return points


def fit_trestle_to_segment(layer: 'Layer', spliney_id: str, seg: dict,
                            merged_nodes: dict,
                            deck_offset_y: float = -0.3) -> bool:
    """Refit an existing AutoTrestle spliney to a track segment."""
    entry = layer.splineys.get(spliney_id)
    if not entry or 'AutoTrestle' not in str(entry.get('handler', '')):
        return False
    points = _trestle_points_for_segment(
        seg, merged_nodes, deck_offset_y=deck_offset_y
    )
    if len(points) < 2:
        return False
    updated = _copy.deepcopy(entry)
    updated['points'] = points
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = updated
    layer.splineys[spliney_id] = _copy.deepcopy(updated)
    layer.dirty = True
    return True


def create_trestle_from_segment(layer: 'Layer', seg: dict,
                                 merged_nodes: dict,
                                 id_prefix: str = 'TRS',
                                 head_style: str = 'bent',
                                 tail_style: str = 'bent') -> str:
    """
    Wrap a segment in an AutoTrestleBuilder spliney.
    Returns the new spliney ID.

    head_style / tail_style -- AutoTrestle.EndStyle enum values.
      Valid values (from AutoTrestle source): 'bent', 'straight'
      'bent'     -- angled abutment at each end (default, most common)
      'straight' -- vertical cut at each end
    """
    points = _trestle_points_for_segment(seg, merged_nodes)
    if len(points) < 2:
        return ''

    # ID
    trs_id = f"{id_prefix}_{_rand_chars()}"

    entry = {
        'handler':   'StrangeCustoms.AutoTrestleBuilder',
        'points': points,
        'headstyle': head_style,
        'tailstyle': tail_style,
    }

    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][trs_id] = entry
    layer.splineys[trs_id] = _copy.deepcopy(entry)
    layer.dirty = True
    return trs_id


def spliney_add_road(layer: 'Layer', spliney_id: str, profile: str,
                     points: list, style: str = 'Road'):
    """Add or replace a road/river FlowyThingBuilder spliney.

    profile -- SplineProfile name, e.g. 'RAM Road profile'
    style   -- 'Road' or 'River'
    points  -- list of dicts: {position:{x,y,z}, rotation:{x,y,z}, width:float}
    """
    entry = {
        'handler': 'StrangeCustoms.FlowyThingBuilder',
        'profile': profile,
        'style':   style,
        'points':  points,
    }
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = entry
    layer.splineys[spliney_id] = _copy.deepcopy(entry)
    layer.dirty = True


def next_spliney_id(layer: 'Layer', prefix: str = 'SP') -> str:
    """Generate a unique spliney ID for the given layer."""
    existing = set(layer.splineys.keys())
    safe_prefix = ''.join(
        ch if ch.isalnum() or ch in ('_', '-') else '_'
        for ch in str(prefix or 'SP')
    ).strip('_') or 'SP'
    while True:
        sid = f"{safe_prefix}_{_rand_chars()}"
        if sid not in existing:
            return sid


def spliney_insert_point(layer: 'Layer', spliney_id: str, point_idx: int,
                         point: dict, after: bool = True) -> int:
    """Insert a control point before or after the given point index.

    Returns the inserted index, or -1 on failure.
    """
    spl = layer.splineys.get(spliney_id)
    if not spl or 'points' not in spl:
        return -1
    pts = list(spl.get('points', []))
    if point_idx < 0 or point_idx >= len(pts):
        return -1
    insert_at = point_idx + 1 if after else point_idx
    pts.insert(insert_at, _copy.deepcopy(point))
    updated = dict(spl)
    updated['points'] = pts
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = updated
    layer.splineys[spliney_id] = _copy.deepcopy(updated)
    layer.dirty = True
    return insert_at


def spliney_delete_point(layer: 'Layer', spliney_id: str, point_idx: int) -> bool:
    """Delete a spliney control point.

    Keeps a minimum of two points, since FlowyThing splineys need at least one segment.
    """
    spl = layer.splineys.get(spliney_id)
    if not spl or 'points' not in spl:
        return False
    pts = list(spl.get('points', []))
    if len(pts) <= 2 or point_idx < 0 or point_idx >= len(pts):
        return False
    pts.pop(point_idx)
    updated = dict(spl)
    updated['points'] = pts
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = updated
    layer.splineys[spliney_id] = _copy.deepcopy(updated)
    layer.dirty = True
    return True


# ---------------------------------------------------------------------------
# AlinasMapMod spliney handler names (confirmed from AlinasMapModPlugin.cs)
# ---------------------------------------------------------------------------
# CURRENT handler strings (write these in new files):
# 'AlinasMapMod.Map.MapLabelBuilder'                  -> map text labels
# 'AlinasMapMod.Loaders.LoaderBuilder'                -> fuel/water loaders
# 'AlinasMapMod.Stations.StationAgentBuilder'         -> passenger stations
# 'AlinasMapMod.TelegraphPoles.TelegraphPoleBuilder'  -> telegraph poles
# 'AlinasMapMod.TelegraphPoles.TelegraphPoleMover'    -> telegraph pole mover
# 'AlinasMapMod.Turntable.TurntableBuilder'           -> turntable
# 'AlinasMapMod.MapMaskBuilder'                       -> terrain map mask
#
# OLD names (auto-migrated, but avoid in new files):
# 'AlinasMapMod.MapLabelBuilder'      -> now Map.MapLabelBuilder
# 'AlinasMapMod.LoaderBuilder'        -> now Loaders.LoaderBuilder
# 'AlinasMapMod.StationAgentBuilder'  -> now Stations.StationAgentBuilder
# 'AlinasMapMod.TelegraphPoleBuilder' -> now TelegraphPoles.TelegraphPoleBuilder
# 'AlinasMapMod.TelegraphPoleMover'   -> now TelegraphPoles.TelegraphPoleMover
#
# Vanilla prefab names (from AlinasMapMod/VanillaPrefabs.cs):
#   Loaders:  coalConveyor, coalTower, dieselFuelingStand, waterTower, waterColumn
#   Stations: flagStopStation, brysonDepot, dillsboroDepot, southernCombinationDepot
#   Roundhouse: roundhouseStall, roundhouseStart, roundhouseEnd
# ---------------------------------------------------------------------------


def turntable_set(layer: 'Layer', spliney_id: str,
                  x: float, y: float, z: float,
                  rot_x: float = 0.0, rot_y: float = 0.0, rot_z: float = 0.0,
                  radius: int = 15,
                  subdivisions: int = 32,
                  roundhouse_stalls: int = 0,
                  roundhouse_track_length: int = 46,
                  stall_prefab: str = 'vanilla://roundhouseStall',
                  start_prefab: str = 'vanilla://roundhouseStart',
                  end_prefab: str = 'vanilla://roundhouseEnd',
                  handler: str = 'AlinasMapMod.Turntable.TurntableBuilder'):
    """Add or update a turntable spliney.

    handler -- ISplineyBuilder handler string. Default is AlinasMapMod's builder
               (requires AlinasMapMod installed). Pass your own handler string
               if you implement a turntable builder yourself.

    Schema confirmed from AlinasMapMod/Definitions/SerializedTurntable.cs.

    radius               -- turntable radius in metres, must be 5-50 (default 15)
    subdivisions         -- number of track positions on the turntable, 1-50 (default 32)
    roundhouse_stalls    -- number of roundhouse stall bays (0 = no roundhouse),
                           must not exceed subdivisions
    roundhouse_track_length -- length of each stall track in metres (default 46)
    stall/start/end_prefab  -- vanilla:// prefab URIs for roundhouse geometry.
      Valid values: 'vanilla://roundhouseStall', 'vanilla://roundhouseStart',
                    'vanilla://roundhouseEnd', or '' for no geometry
    """
    if not (5 <= radius <= 50):
        print(f"[turntable_set] WARNING: radius {radius} out of valid range 5-50")
    if not (1 <= subdivisions <= 50):
        print(f"[turntable_set] WARNING: subdivisions {subdivisions} out of valid range 1-50")
    if roundhouse_stalls > subdivisions:
        print(f"[turntable_set] WARNING: roundhouseStalls ({roundhouse_stalls}) "
              f"exceeds subdivisions ({subdivisions})")

    entry = {
        'handler':              handler,
        'Position':             {'x': float(x), 'y': float(y), 'z': float(z)},
        'Rotation':             {'x': float(rot_x), 'y': float(rot_y), 'z': float(rot_z)},
        'Radius':               radius,
        'Subdivisions':         subdivisions,
        'RoundhouseStalls':     roundhouse_stalls,
        'RoundhouseTrackLength': roundhouse_track_length,
        'StallPrefab':          stall_prefab,
        'StartPrefab':          start_prefab,
        'EndPrefab':            end_prefab,
    }
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = entry
    layer.splineys[spliney_id] = _copy.deepcopy(entry)
    layer.dirty = True


def spliney_add_maplabel(layer: 'Layer', spliney_id: str,
                          text: str, x: float, y: float, z: float,
                          alignment: str = 'TopLeft',
                          handler: str = 'AlinasMapMod.Map.MapLabelBuilder'):
    """Add or replace a map label spliney.

    handler -- the ISplineyBuilder handler string that will render this label.
      Default is 'AlinasMapMod.Map.MapLabelBuilder' (requires AlinasMapMod installed).
      If you implement your own map label builder, pass your own handler string
      instead, e.g. 'YourMod.MapLabelBuilder', to remove the AlinasMapMod dependency.

    Schema (Position + Text) confirmed from AlinasMapMod/Definitions/SerializedMapLabel.cs.
    """
    entry = {
        'handler':   handler,
        'position':  {'x': x, 'y': y, 'z': z},
        'text':      text,
        'alignment': alignment,
    }
    if 'splineys' not in layer._raw:
        layer._raw['splineys'] = {}
    layer._raw['splineys'][spliney_id] = entry
    layer.splineys[spliney_id] = _copy.deepcopy(entry)
    layer.dirty = True


def spliney_delete(layer: 'Layer', spliney_id: str):
    """Delete a spliney from a layer."""
    layer.splineys.pop(spliney_id, None)
    if 'splineys' in layer._raw:
        layer._raw['splineys'].pop(spliney_id, None)
    layer.dirty = True




# ---------------------------------------------------------------------------
# Group move helper (A19)
# ---------------------------------------------------------------------------
def move_group(layer: 'Layer', node_ids: list,
               dx: float = 0, dy: float = 0, dz: float = 0,
               rot_delta: float = 0, pivot_x: float = None, pivot_z: float = None,
               proj=None):
    """
    Translate and/or rotate a group of nodes in place.

    dx/dy/dz  -- world-space offset to add to every node position.
    rot_delta  -- rotation in degrees around Y axis (pivot = group centroid if not given).
    pivot_x/z  -- optional pivot point for rotation; defaults to group centroid.
    proj       -- ModProject instance; if provided, _rebuild_merge() is called so
                 the merged view (and all spatial queries) reflect the new positions.

    Returns list of node ids that were updated.
    """
    import math as _m

    nodes = {nid: layer.nodes.get(nid) for nid in node_ids
             if layer.nodes.get(nid) and not layer.nodes[nid].get('deleted')}
    if not nodes:
        return []

    # Compute centroid as default pivot
    if pivot_x is None:
        pivot_x = sum(n['x'] for n in nodes.values()) / len(nodes)
    if pivot_z is None:
        pivot_z = sum(n['z'] for n in nodes.values()) / len(nodes)

    r = _m.radians(rot_delta)
    cos_r, sin_r = _m.cos(r), _m.sin(r)

    updated = []
    for nid, node in nodes.items():
        # Rotate around pivot
        ox = node['x'] - pivot_x
        oz = node['z'] - pivot_z
        new_x = pivot_x + ox * cos_r - oz * sin_r + dx
        new_z = pivot_z + ox * sin_r + oz * cos_r + dz
        new_y = node['y'] + dy
        new_rotY = (node.get('rotY', 0) + rot_delta) % 360

        layer.set_node(nid, new_x, new_y, new_z,
                       node.get('rotX', 0), new_rotY, node.get('rotZ', 0),
                       node.get('flipSwitchStand', False))
        updated.append(nid)

    layer.dirty = True
    # Rebuild merged view so subsequent spatial queries and rebuild_curves
    # use the updated positions rather than stale pre-move coordinates.
    if proj is not None:
        proj._rebuild_merge()
    return updated



# ---------------------------------------------------------------------------
# Mandela helpers (A4/A5)
# ---------------------------------------------------------------------------
def mandela_set(layer: 'Layer', mandela_id: str,
                instantiate_from: Optional[str] = None,
                x: float = 0.0, y: float = 0.0, z: float = 0.0,
                rot_x: float = 0.0, rot_y: float = 0.0, rot_z: float = 0.0,
                scale_x: float = 1.0, scale_y: float = 1.0, scale_z: float = 1.0,
                enabled: bool = True,
                force_enabled: bool = False):
    """Add or update a mandela (prefab instance) in a layer.

    instantiate_from -- Unity scene path of the source mesh to clone.
                       Optional: omit (None) to write a disable-only patch for
                       a base-game scene object, e.g. {"enabled": false}.

    Mandela.Enabled is bool? (nullable):
      - Absent/null  -> game default (object is active)
      - False        -> object is hidden
      - True         -> explicitly re-enabled (use force_enabled=True for this)

    Real mods do not write 'enabled' at all for normal (enabled) mandelas.
    Pass enabled=False to hide an object.
    Pass enabled=True, force_enabled=True to explicitly re-enable a previously
    disabled mandela (writes "enabled": true to the JSON).
    """
    entry: dict = {}
    if instantiate_from is not None:
        entry['instantiateFrom'] = instantiate_from
        entry['localPosition']   = {'x': x, 'y': y, 'z': z}
        entry['localRotation']   = {'x': rot_x, 'y': rot_y, 'z': rot_z}
        entry['localScale']      = {'x': scale_x, 'y': scale_y, 'z': scale_z}
    # Write 'enabled' only when disabling, or when explicitly forcing re-enable.
    if not enabled:
        entry['enabled'] = False
    elif force_enabled:
        entry['enabled'] = True
    if 'mandelas' not in layer._raw:
        layer._raw['mandelas'] = {}
    layer._raw['mandelas'][mandela_id] = entry
    layer.mandelas[mandela_id] = _copy.deepcopy(entry)
    layer.dirty = True


def mandela_delete(layer: 'Layer', mandela_id: str):
    """Delete a mandela from a layer."""
    layer.mandelas.pop(mandela_id, None)
    if 'mandelas' in layer._raw:
        layer._raw['mandelas'].pop(mandela_id, None)
    layer.dirty = True


def next_mandela_id(layer: 'Layer') -> str:
    """Generate a unique mandela ID."""
    existing = set(layer.mandelas.keys())
    while True:
        mid = f"M_{_rand_chars()}"
        if mid not in existing:
            return mid



# ---------------------------------------------------------------------------
# SimpleGraph helpers (C2/C3)
# ---------------------------------------------------------------------------
def simple_graph_node_set(layer: 'Layer', graph_id: str, node_id: str,
                           x: float, y: float, z: float,
                           rot_x: float = 0.0, rot_y: float = 0.0, rot_z: float = 0.0,
                           tag: str = None):
    """Add or update a node in a SimpleGraph (AI crew pathfinding graph).

    graph_id -- the simpleGraph key, e.g. 'MyMod_Platform_Graph'
    node_id  -- node key within the graph, conventionally 'N' + 3 alphanumeric
    x/y/z    -- position in game-space coordinates
    rot_x/y/z -- euler rotation angles
    tag      -- optional routing tag; see SIMPLE_GRAPH_TAGS for known values.
               None = absent from JSON (nullable field).

    SerializedSimpleGraph.Nodes is Dict<string, SerializedSimpleNode>.
    SerializedSimpleNode has Position (Vector3), Rotation (Vector3), Tag (string?).
    """
    node_entry: dict = {
        'position': {'x': float(x), 'y': float(y), 'z': float(z)},
        'rotation': {'x': float(rot_x), 'y': float(rot_y), 'z': float(rot_z)},
    }
    if tag is not None:
        node_entry['tag'] = tag

    if 'simpleGraphs' not in layer._raw:
        layer._raw['simpleGraphs'] = {}
    if graph_id not in layer._raw['simpleGraphs'] or layer._raw['simpleGraphs'][graph_id] is None:
        layer._raw['simpleGraphs'][graph_id] = {'nodes': {}}
    graph = layer._raw['simpleGraphs'][graph_id]
    if 'nodes' not in graph:
        graph['nodes'] = {}
    graph['nodes'][node_id] = node_entry

    # Sync in-memory
    if graph_id not in layer.simpleGraphs or layer.simpleGraphs[graph_id] is None:
        layer.simpleGraphs[graph_id] = {'nodes': {}}
    layer.simpleGraphs[graph_id].setdefault('nodes', {})[node_id] = _copy.deepcopy(node_entry)
    layer.dirty = True


def simple_graph_node_delete(layer: 'Layer', graph_id: str, node_id: str):
    """Delete a node from a SimpleGraph."""
    graph_raw = (layer._raw.get('simpleGraphs') or {}).get(graph_id) or {}
    (graph_raw.get('nodes') or {}).pop(node_id, None)
    graph_mem = (layer.simpleGraphs.get(graph_id)) or {}
    (graph_mem.get('nodes') or {}).pop(node_id, None)
    layer.dirty = True


def simple_graph_delete(layer: 'Layer', graph_id: str):
    """Delete an entire SimpleGraph from a layer."""
    layer.simpleGraphs.pop(graph_id, None)
    if 'simpleGraphs' in layer._raw:
        layer._raw['simpleGraphs'].pop(graph_id, None)
    layer.dirty = True



# ---------------------------------------------------------------------------
# flip_segment (C6) / migration helpers (C4)
# ---------------------------------------------------------------------------
def flip_segment(layer: 'Layer', seg_id: str):
    """Swap the startId and endId of a segment, reversing its direction.

    Equivalent to ChangeTrackSegment.Flip() in Mapeditor.
    This reverses which end is the switch stub -- changes switch throw behavior.
    The segment must exist in this layer (not just in the merged view).
    """
    seg = layer.segments.get(seg_id)
    if not seg or seg.get('deleted'):
        return False
    layer.set_segment(
        seg_id,
        seg['endId'],    # swap: old end becomes new start
        seg['startId'],  # swap: old start becomes new end
        seg.get('trackClass', 'Mainline'),
        seg.get('style', 'Standard'),
        seg.get('speedLimit', 0),
        seg.get('priority', 0),
        seg.get('groupId', ''),
        seg.get('gauge', 'Standard'),
    )
    return True


# ---------------------------------------------------------------------------
# C4 -- game-migrations mixinto support
# ---------------------------------------------------------------------------

def migration_set(layer: 'Layer',
                  car_types: dict = None,
                  waybill_destinations: dict = None,
                  properties: dict = None):
    """Write a game-migrations mixinto file.

    Targets the 'game-migrations' mixinto key -- processed by MigrationPatches.cs
    on WorldStore.Migrate() (i.e. when a save file is loaded after a mod update).

    MigrationHolder schema (all fields optional):
      car_types            -- {old_prototype_id: new_prototype_id}
                             Renames car types in the save file.
      waybill_destinations -- {old_industry_id: new_industry_id}
                             Remaps waybill originId/destId in car properties.
      properties           -- {old_property_key: new_property_key}
                             Renames top-level car property keys in the snapshot.

    The layer path should point to the migration JSON file. Wire it up in
    Definition.json as:
      "mixintos": {"game-migrations": ["file(my-migration.json)"]}

    NOTE: Migration files often also use StrangeCustoms patch operators
    ($find, $replace, etc.) for surgical edits. _check_patch_operators() will
    warn if such operators are detected on load (C5/B8).
    """
    entry: dict = {}
    if car_types:
        entry['carTypes'] = dict(car_types)
    if waybill_destinations:
        entry['waybillDestinations'] = dict(waybill_destinations)
    if properties:
        entry['properties'] = dict(properties)
    layer._raw.update(entry)
    layer.dirty = True


# ===========================================================================
# SECTION G -- CTC Signals System
# From Assembly-CSharp/Track/Signals/ (35 CS files)
# Confirmed from full source read March 2026.
#
# IMPORTANT: Zamu has publicly stated the CTC system requires "a lot of work"
# to add modded signals. This section documents WHY and what the C# rewrite
# needs to handle.
# ===========================================================================


# ---------------------------------------------------------------------------
# flip_end, normalize_span (F14/F15/F16)
# ---------------------------------------------------------------------------
def flip_end(end: str) -> str:
    """F14/F15: Flip a span end string -- 'Start' <-> 'End'.
    Matches TrackSegmentExtensions.Flipped(): End.A <-> End.B.
    In JSON these are serialized as 'Start' (End.A) and 'End' (End.B).
    """
    return 'End' if end == 'Start' else 'Start'


# ---------------------------------------------------------------------------
# F16: NormalizeUpperLower -- ensure upper is upstream (closer to main line)
# Confirmed from TrackSpan.NormalizeUpperLower() in Assembly-CSharp.
# In Python we can't check the live graph topology, but we can validate the
# JSON shape and warn if upper/lower appear reversed based on distance.
# ---------------------------------------------------------------------------
def normalize_span(span: dict) -> dict:
    """F16: Return a copy of a span dict with upper/lower normalized.
    'Upper' = closer to main line (lower distance from start of segment).
    'Lower' = closer to car stop.
    If upper.distance > lower.distance on the same segment, swap them.
    This is a heuristic -- the authoritative check requires live graph topology.
    Confirmed: TrackSpan.NormalizeUpperLower() is called on every span update.
    Pass normalize=True to span_set() to apply this automatically.
    """
    import copy as _copy2
    span = _copy2.deepcopy(span)
    upper = span.get('upper', {})
    lower = span.get('lower', {})
    # If same segment and upper is downstream of lower, swap
    if (isinstance(upper, dict) and isinstance(lower, dict) and
            upper.get('segmentId') == lower.get('segmentId') and
            upper.get('end') == lower.get('end')):
        ud = upper.get('distance', 0.0)
        ld = lower.get('distance', 0.0)
        if ud > ld:
            span['upper'], span['lower'] = lower, upper
    return span



def next_scenery_id(layer: 'Layer', existing_ids: set = None) -> str:
    """Generate a unique scenery object ID."""
    existing = existing_ids if existing_ids is not None else set(layer.scenery.keys())
    while True:
        sid = f"Z_{_rand_chars()}"
        if sid not in existing:
            return sid
