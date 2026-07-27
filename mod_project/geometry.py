"""mod_project.geometry
Bezier math, curve generation, and track geometry helpers.
Matches game source: BezierCurve.cs, BezierMath.cs, TrackMath.cs
"""

import math as _math
from .constants import (
    _rand_chars, _used_ids,
    _GAUSS_T, _GAUSS_C,
    TRACK_CLASS_NAMES, TRACK_CLASS_JSON, TRACK_CLASS_DEFAULT_SPEED,
    TRACK_STYLES, TRACK_MARKER_TYPES,
)

# ---------------------------------------------------------------------------
# Low-level bezier primitives (F1-F10)
# ---------------------------------------------------------------------------
def _cubic_point(p0, p1, p2, p3, t):
    """Evaluate cubic bezier at t. Points are (x,y,z) tuples."""
    u = 1.0 - t
    return (
        u*u*u*p0[0] + 3*u*u*t*p1[0] + 3*u*t*t*p2[0] + t*t*t*p3[0],
        u*u*u*p0[1] + 3*u*u*t*p1[1] + 3*u*t*t*p2[1] + t*t*t*p3[1],
        u*u*u*p0[2] + 3*u*u*t*p1[2] + 3*u*t*t*p2[2] + t*t*t*p3[2],
    )


def _cubic_deriv(p0, p1, p2, p3, t):
    """First derivative of cubic bezier at t."""
    u = 1.0 - t
    return (
        3*(u*u*(p1[0]-p0[0]) + 2*u*t*(p2[0]-p1[0]) + t*t*(p3[0]-p2[0])),
        3*(u*u*(p1[1]-p0[1]) + 2*u*t*(p2[1]-p1[1]) + t*t*(p3[1]-p2[1])),
        3*(u*u*(p1[2]-p0[2]) + 2*u*t*(p2[2]-p1[2]) + t*t*(p3[2]-p2[2])),
    )


def _cubic_split(p0, p1, p2, p3, t):
    """De Casteljau split -- returns (left_pts, right_pts) each as 4-tuple."""
    # Level 1
    q0 = tuple(p0[i] + t*(p1[i]-p0[i]) for i in range(3))
    q1 = tuple(p1[i] + t*(p2[i]-p1[i]) for i in range(3))
    q2 = tuple(p2[i] + t*(p3[i]-p2[i]) for i in range(3))
    # Level 2
    r0 = tuple(q0[i] + t*(q1[i]-q0[i]) for i in range(3))
    r1 = tuple(q1[i] + t*(q2[i]-q1[i]) for i in range(3))
    # Level 3
    s  = tuple(r0[i] + t*(r1[i]-r0[i]) for i in range(3))
    return (p0, q0, r0, s), (s, r1, q2, p3)


def _bezier_length_gauss(p0, p1, p2, p3):
    """F6: 24-point Gaussian quadrature arc length -- matches BezierMath.CalculateLength exactly.
    Offset curve to origin first for numerical stability (matches GetPositionRotation$BurstManaged).
    """
    ox, oy, oz = p0
    # Offset all points by -p0 for numerical stability
    q0 = (0.0, 0.0, 0.0)
    q1 = (p1[0]-ox, p1[1]-oy, p1[2]-oz)
    q2 = (p2[0]-ox, p2[1]-oy, p2[2]-oz)
    q3 = (p3[0]-ox, p3[1]-oy, p3[2]-oz)
    total = 0.0
    for ti, ci in zip(_GAUSS_T, _GAUSS_C):
        t = 0.5 * ti + 0.5
        dx, dy, dz = _cubic_deriv(q0, q1, q2, q3, t)
        arc = _math.sqrt(dx*dx + dy*dy + dz*dz)
        total += ci * arc
    return 0.5 * total


def _bezier_tangent_factor(rx0, ry0, rx1, ry1):
    """F2: Compute tangent factor from node rotations.
    Matches TrackSegment.BezierTangentFactorForTangents(forward_a, forward_b).
    factor = Lerp(0.35, 0.41, InverseLerp(45, 90, angle))
    """
    # Forward vectors from euler angles (rotX=pitch, rotY=yaw)
    cos0 = _math.cos(_math.radians(rx0))
    cos1 = _math.cos(_math.radians(rx1))
    f0 = (_math.sin(_math.radians(ry0))*cos0,
          -_math.sin(_math.radians(rx0)),
          _math.cos(_math.radians(ry0))*cos0)
    f1 = (_math.sin(_math.radians(ry1))*cos1,
          -_math.sin(_math.radians(rx1)),
          _math.cos(_math.radians(ry1))*cos1)
    # angle between forward vectors
    dot = max(-1.0, min(1.0, f0[0]*f1[0] + f0[1]*f1[1] + f0[2]*f1[2]))
    angle = _math.degrees(_math.acos(dot))
    if angle > 90.0:
        angle = 180.0 - angle
    # Lerp(0.35, 0.41, InverseLerp(45, 90, angle))
    t = max(0.0, min(1.0, (angle - 45.0) / 45.0))
    return 0.35 + t * (0.41 - 0.35)


def _node_forward_vector(rx_deg, ry_deg):
    cos_rx = _math.cos(_math.radians(rx_deg))
    return (
        _math.sin(_math.radians(ry_deg)) * cos_rx,
        -_math.sin(_math.radians(rx_deg)),
        _math.cos(_math.radians(ry_deg)) * cos_rx,
    )


def _tangent_point_toward_other(node, other, distance):
    """Pick the tangent-side point that lies closer to the opposite node.

    This keeps segments from hooking backward when one endpoint's local axis
    points away from the other endpoint.
    """
    x0, y0, z0 = node['x'], node['y'], node['z']
    ox, oy, oz = other['x'], other['y'], other['z']
    fwd = _node_forward_vector(node.get('rotX', 0.0), node.get('rotY', 0.0))
    forward_pt = (
        x0 + fwd[0] * distance,
        y0 + fwd[1] * distance,
        z0 + fwd[2] * distance,
    )
    backward_pt = (
        x0 - fwd[0] * distance,
        y0 - fwd[1] * distance,
        z0 - fwd[2] * distance,
    )

    def dist3(pt):
        return _math.sqrt((pt[0] - ox) ** 2 + (pt[1] - oy) ** 2 + (pt[2] - oz) ** 2)

    if dist3(backward_pt) < dist3(forward_pt):
        return backward_pt
    return forward_pt


def _bezier_control_points(n0, n1):
    """Return cubic bezier control points for a segment between two nodes.

    The tangent control point at each node is chosen from the node's forward/back
    axis so the selected side is the one closer to the opposite endpoint.
    """
    x0, y0, z0 = n0['x'], n0['y'], n0['z']
    x1, y1, z1 = n1['x'], n1['y'], n1['z']
    ry0 = n0.get('rotY', 0.0);  rx0 = n0.get('rotX', 0.0)
    ry1 = n1.get('rotY', 0.0);  rx1 = n1.get('rotX', 0.0)

    ep0 = (x0, y0, z0)
    ep1 = (x1, y1, z1)
    dist3d = _math.sqrt((x1-x0)**2 + (y1-y0)**2 + (z1-z0)**2)
    if dist3d < 0.1:
        return ep0, ep0, ep1, ep1

    factor = _bezier_tangent_factor(rx0, ry0, rx1, ry1)
    d = dist3d * factor
    p1 = _tangent_point_toward_other(n0, n1, d)
    p2 = _tangent_point_toward_other(n1, n0, d)
    return ep0, p1, p2, ep1


def _bezier_for_nodes(n0, n1):
    """F1/F2/F3: Compute cubic bezier for two node dicts {x,y,z,rotY,rotX}.

    Uses the same tangent-factor math as TrackSegment.CreateBezier(), while
    choosing the tangent side at each node from that node's forward/back axis
    based on which side is closer to the opposite endpoint.

    Previous code simplified one endpoint and could make segments hook backward
    when connected nodes faced different directions.

    Approximation model:
      d = |ep0 - ep1| * BezierTangentFactorForTangents(forward_a, forward_b)
      P1/P2 = the closer tangent-side point at each node along its local axis

    Returns list of (x, z) points for 2D rendering (edit_tiles.py compatible),
    using adaptive subdivision matching BezierCurve.Approximate() defaults.
    """
    ep0, p1, p2, ep1 = _bezier_control_points(n0, n1)
    return _cubic_approximate_xz(ep0, p1, p2, ep1)


def _cubic_approximate_xz(p0, p1, p2, p3,
                           flatness=1.000005, min_len=0.5, max_len=40.0, depth=16):
    """F4: Adaptive subdivision matching BezierCurve.Approximate().
    Returns list of (x, z) points for 2D rendering.
    Stop condition: (P0P1+P1P2+P2P3) < flatness * |P0P3|  OR  chord/2 < min_len
    """
    pts = [(p0[0], p0[2])]
    _approx_recurse(p0, p1, p2, p3, pts, flatness, min_len, max_len, depth)
    pts.append((p3[0], p3[2]))
    return pts


def _approx_recurse(p0, p1, p2, p3, pts, flatness, min_len, max_len, depth):
    chord = _math.sqrt((p3[0]-p0[0])**2 + (p3[2]-p0[2])**2)
    if depth <= 0:
        return
    # Perimeter of control polygon (XZ only for 2D test)
    perim = (_math.sqrt((p1[0]-p0[0])**2+(p1[2]-p0[2])**2) +
             _math.sqrt((p2[0]-p1[0])**2+(p2[2]-p1[2])**2) +
             _math.sqrt((p3[0]-p2[0])**2+(p3[2]-p2[2])**2))
    flat   = perim < flatness * chord
    short  = chord / 2.0 < min_len
    long_  = chord > max_len
    if not long_ and (flat or short):
        return
    left, right = _cubic_split(p0, p1, p2, p3, 0.5)
    mid = left[3]
    _approx_recurse(left[0],  left[1],  left[2],  left[3],  pts, flatness, min_len, max_len, depth-1)
    pts.append((mid[0], mid[2]))
    _approx_recurse(right[0], right[1], right[2], right[3], pts, flatness, min_len, max_len, depth-1)


def _bezier_split(p0, p1, p2, p3, up0, up3, t=0.5):
    """F9: Split cubic bezier at t with up vector interpolation.
    Returns (left_curve, right_curve) each as (p0,p1,p2,p3,up0,up3) tuple.
    Matches HullCalculation.Hull() -- up vector at split point from rotation.
    """
    left_pts, right_pts = _cubic_split(p0, p1, p2, p3, t)
    # Up at split: lerp between up0 and up3 (simplified -- game uses GetRotation)
    up_mid = tuple(up0[i] + t*(up3[i]-up0[i]) for i in range(3))
    return (left_pts[0],  left_pts[1],  left_pts[2],  left_pts[3],  up0,    up_mid), \
           (right_pts[0], right_pts[1], right_pts[2], right_pts[3], up_mid, up3)


def _bezier_parameter_for_distance(p0, p1, p2, p3, up0, up3, d, accuracy=0.1):
    """F5: Binary-search parameter for distance along curve.
    Matches BezierCurve.ParameterForDistance_() -- recursive Split(0.5).
    Applies floating-origin offset if |p0| > 1000 (F10).
    """
    # F10: offset for numerical stability
    if _math.sqrt(p0[0]**2+p0[1]**2+p0[2]**2) > 1000.0:
        ox,oy,oz = p0
        p0b = (0,0,0)
        p1b = (p1[0]-ox,p1[1]-oy,p1[2]-oz)
        p2b = (p2[0]-ox,p2[1]-oy,p2[2]-oz)
        p3b = (p3[0]-ox,p3[1]-oy,p3[2]-oz)
        return _bezier_parameter_for_distance(p0b,p1b,p2b,p3b, up0,up3, d, accuracy)
    return _pfd_recurse(p0,p1,p2,p3,up0,up3, d, accuracy, 32)


def _pfd_recurse(p0,p1,p2,p3,up0,up3, d, accuracy, iters):
    length = _bezier_length_gauss(p0,p1,p2,p3)
    if d - length > accuracy:
        return 1.0
    if abs(d) < accuracy:
        return 0.0
    if abs(d - length) < accuracy:
        return 1.0
    if iters == 0:
        return 0.5
    left, right = _bezier_split(p0,p1,p2,p3,up0,up3, 0.5)
    llen = _bezier_length_gauss(left[0],left[1],left[2],left[3])
    if abs(llen - d) < accuracy:
        return 0.5
    if d < llen:
        return _pfd_recurse(*left, d, accuracy, iters-1) * 0.5
    return 0.5 + _pfd_recurse(*right, d - llen, accuracy, iters-1) * 0.5


def _bezier_parameter_closest_to(p0,p1,p2,p3,up0,up3, point, depth=10):
    """F8: Snap cursor to track -- recursive split, find closer endpoint.
    Matches BezierCurve.ParameterClosestTo_() exactly.
    """
    if depth == 0:
        return 0.0
    px,py,pz = point
    d0 = _math.sqrt((p0[0]-px)**2+(p0[1]-py)**2+(p0[2]-pz)**2)
    d3 = _math.sqrt((p3[0]-px)**2+(p3[1]-py)**2+(p3[2]-pz)**2)
    left, right = _bezier_split(p0,p1,p2,p3,up0,up3, 0.5)
    if d0 < d3:
        return _bezier_parameter_closest_to(*left, point, depth-1) * 0.5
    return 0.5 + _bezier_parameter_closest_to(*right, point, depth-1) * 0.5


# ---------------------------------------------------------------------------
# Legacy quad bezier (kept for edit_tiles.py compatibility)
# ---------------------------------------------------------------------------
def quad_bezier(p0, cp, p1, steps=20):
    """Legacy quadratic bezier for edit_tiles.py compatibility.
    For new code use _bezier_for_nodes (cubic, matches game source).
    """
    pts = []
    for i in range(steps + 1):
        t  = i / steps
        x  = (1-t)**2*p0[0] + 2*(1-t)*t*cp[0] + t**2*p1[0]
        z  = (1-t)**2*p0[1] + 2*(1-t)*t*cp[1] + t**2*p1[1]
        pts.append((x, z))
    return pts



# ---------------------------------------------------------------------------
# F12/F14/F15/F16/F19/F20/F21 track segment helpers
# ---------------------------------------------------------------------------
_CLASS_SPEED = {'Mainline': 35, 'Branch': 25, 'Industrial': 15}

def effective_speed_limit(speed_limit: int, track_class: str = 'Mainline') -> int:
    """F12: Return the effective speed limit for a segment.
    speed_limit == 0 means use class default (Mainline=35, Branch=25, Industrial=15).
    Confirmed from TrackSegment.GetExpectedSpeedLimit().
    """
    if speed_limit != 0:
        return speed_limit
    return _CLASS_SPEED.get(track_class, 35)


# ---------------------------------------------------------------------------
# F14: Location end flip helper
# Confirmed from TrackSegmentExtensions.Flipped() / TrackSpan.Location
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


# ---------------------------------------------------------------------------
# F19: segment_length -- arc length using 24-point Gaussian quadrature
# F20: segment_grade  -- percent grade A->B
# F21: segment_curve_degrees -- curvature in degrees per 100 feet
# ---------------------------------------------------------------------------

def segment_length(seg: dict, merged_nodes: dict) -> float:
    """F19: Compute segment arc length using 24-point Gaussian quadrature.
    Matches TrackSegment.GetLength() which uses BezierMath.CalculateLength().
    Returns metres, or 0.0 if nodes not found.
    """
    n0 = merged_nodes.get(seg.get('startId', ''))
    n1 = merged_nodes.get(seg.get('endId', ''))
    if not n0 or not n1:
        return 0.0
    p0, p1, p2, p3 = _bezier_control_points(n0, n1)
    return _bezier_length_gauss(p0, p1, p2, p3)


def segment_grade(seg: dict, merged_nodes: dict) -> float:
    """F20: Compute percent grade for a segment (positive = uphill A->B).
    grade = (endY - startY) / length * 100
    Returns 0.0 if nodes not found or segment has zero length.
    """
    n0 = merged_nodes.get(seg.get('startId', ''))
    n1 = merged_nodes.get(seg.get('endId', ''))
    if not n0 or not n1:
        return 0.0
    length = segment_length(seg, merged_nodes)
    if length < 0.001:
        return 0.0
    return (n1['y'] - n0['y']) / length * 100.0


def segment_curve_degrees(seg: dict, merged_nodes: dict) -> float:
    """F21: Compute curvature in degrees per 100 feet.
    Matches TrackMath.CalculateCurveDegrees() from Assembly-CSharp/Track/TrackMath.cs:
      - Extend perpendiculars from both nodes in XZ plane
      - Find intersection (radius centre)
      - radius_feet = distance_to_centre * 3.28084
      - curvature = 2 * arcsin(100 / (2 * radius_feet)) * (180/pi)
    Returns 0.0 for straight track (perpendiculars don't intersect).
    """
    n0 = merged_nodes.get(seg.get('startId', ''))
    n1 = merged_nodes.get(seg.get('endId', ''))
    if not n0 or not n1:
        return 0.0

    # Positions (XZ only -- game zeroes Y for this calculation)
    ax, az = n0['x'], n0['z']
    bx, bz = n1['x'], n1['z']
    ay_deg = n0.get('rotY', 0.0)
    by_deg = n1.get('rotY', 0.0)

    # Delta angle, fold to determine which side
    delta = ay_deg - by_deg
    # Normalise to [-180, 180]
    while delta > 180:  delta -= 360
    while delta < -180: delta += 360

    ry0 = _math.radians(ay_deg)
    ry1 = _math.radians(by_deg)

    # If negative delta, flip both perpendiculars (matches TrackMath)
    if delta < 0:
        ry0 += _math.pi
        ry1 += _math.pi

    # Perpendicular right of each heading
    rx0 = _math.cos(ry0); rz0 = -_math.sin(ry0)
    rx1 = _math.cos(ry1); rz1 = -_math.sin(ry1)

    # Line intersection: (ax,az) + t*(rx0,rz0) = (bx,bz) + s*(rx1,rz1)
    # Solve 2x2 system
    denom = rx0*rz1 - rz0*rx1
    if abs(denom) < 1e-6:
        return 0.0  # parallel -- straight track

    dx = bx - ax; dz = bz - az
    t = (dx*rz1 - dz*rx1) / denom

    cx = ax + t*rx0
    cz = az + t*rz0

    ra = _math.sqrt((ax-cx)**2 + (az-cz)**2)
    rb = _math.sqrt((bx-cx)**2 + (bz-cz)**2)
    radius_m = (ra + rb) * 0.5
    radius_ft = radius_m * 3.28084

    if radius_ft < 1.0:
        return 0.0

    arg = max(-1.0, min(1.0, 100.0 / (2.0 * radius_ft)))
    return 2.0 * _math.degrees(_math.asin(arg))


# ---------------------------------------------------------------------------
# F25: segments_for_node, node_valency
# ---------------------------------------------------------------------------
def segments_for_node(node_id: str, merged_segments: dict) -> list:
    """F25: Return all segment dicts connected to a given node.
    Matches Graph.SegmentsConnectedTo(node) which iterates all segments
    checking if node is segment.a or segment.b.
    Used for switch detection and node valency checking.
    """
    result = []
    for seg in merged_segments.values():
        if seg.get('startId') == node_id or seg.get('endId') == node_id:
            result.append(seg)
    return result


def node_valency(node_id: str, merged_segments: dict) -> int:
    """Return the number of segments connected to a node.
    valency=1 -> dead end, valency=2 -> through track, valency=3 -> switch
    """
    return len(segments_for_node(node_id, merged_segments))


# ---------------------------------------------------------------------------
# Geometry utilities: heading, perpendicular
# ---------------------------------------------------------------------------
def _heading_to_vec(rotY_deg: float) -> tuple:
    """Convert RotY heading to a unit direction vector (x, z)."""
    r = _math.radians(rotY_deg)
    return _math.sin(r), _math.cos(r)


def _perpendicular(rotY_deg: float) -> tuple:
    """Return unit vector perpendicular (right) to heading."""
    r = _math.radians(rotY_deg)
    return _math.cos(r), -_math.sin(r)


# ---------------------------------------------------------------------------
# Track generators: generate_curve, generate_straight, generate_parallel_tracks
# ---------------------------------------------------------------------------
def generate_curve(
        start_x: float, start_y: float, start_z: float, start_rotY: float,
        radius: float, degrees: float, height_change: float,
        direction: str = 'left',   # 'left' or 'right'
        n_segments: int = 0,       # 0 = auto
        track_class: str = 'Mainline',
        style: str = 'Standard',
        speed_limit: int = 0,
        id_prefix: str = 'N',
        seg_prefix: str = 'S',
        existing_ids: set = None,
        start_rotX: float = 0.0,   # pitch of start node; used to continue an existing grade
) -> tuple:
    """
    Generate a curved chain of nodes and segments.

    Returns (nodes_list, segments_list) where each item is a dict
    ready to pass to Layer.set_node / Layer.set_segment.

    degrees  -- total arc angle (e.g. 90 for a quarter circle)
    radius   -- curve radius in metres
    direction -- 'left' curves left, 'right' curves right
    height_change -- total elevation change across the curve (0 = continue at start_rotX grade)
    start_rotX -- pitch of the start node (negative = uphill in Unity convention)
    n_segments -- number of intermediate nodes (0 = auto-calculate for smooth arc)
    """
    if existing_ids is None:
        existing_ids = set()

    # Auto segment count: one node per ~5 degrees for smooth rendering
    if n_segments <= 0:
        n_segments = max(2, int(abs(degrees) / 5))

    arc_len    = _math.radians(abs(degrees)) * abs(radius)
    sign       = -1 if direction == 'right' else 1   # left = positive rotation
    d_angle    = degrees / n_segments                 # degrees per step

    # If no explicit height change is requested, continue the existing grade from start_rotX.
    # rotX convention: negative = uphill (Y component of forward vector = -sin(rotX)).
    if height_change == 0.0 and abs(float(start_rotX)) > 0.001 and arc_len > 0.0:
        height_change = arc_len * _math.tan(_math.radians(-float(start_rotX)))

    d_height   = height_change / n_segments if n_segments > 0 else 0.0

    # Compute the pitch angle for nodes on this curve.
    step_dist  = arc_len / n_segments if n_segments > 0 else arc_len
    if step_dist > 0.001:
        node_rotX = -_math.degrees(_math.atan2(d_height, step_dist))
    else:
        node_rotX = float(start_rotX)

    nodes    = []
    segments = []

    cur_x, cur_y, cur_z = start_x, start_y, start_z
    cur_rotY = start_rotY
    prev_nid = None

    # ID generator
    def next_nid():
        while True:
            nid = f"{id_prefix}_{_rand_chars()}"
            if nid not in existing_ids:
                existing_ids.add(nid)
                return nid
    def next_sid():
        while True:
            sid = f"{seg_prefix}_{_rand_chars()}"
            if sid not in existing_ids:
                existing_ids.add(sid)
                return sid

    for i in range(n_segments + 1):
        nid = next_nid()
        nodes.append({
            'id':    nid,
            'x':     cur_x,
            'y':     cur_y,
            'z':     cur_z,
            'rotX':  node_rotX,
            'rotY':  cur_rotY,
            'rotZ':  0.0,
            'flipSwitchStand': False,
        })

        if prev_nid is not None:
            segments.append({
                'id':         next_sid(),
                'startId':    prev_nid,
                'endId':      nid,
                'trackClass': track_class,
                'style':      style,
                'speedLimit': speed_limit,
                'priority':   0,
                'groupId':    None,
            })

        prev_nid = nid

        if i < n_segments:
            # Rotate heading FIRST, then step forward along the new heading.
            # (Previously: advance -> rotate, which placed the terminal node one
            # rotation step ahead of its actual arc position.)
            cur_rotY  = (cur_rotY + sign * d_angle) % 360
            fx, fz    = _heading_to_vec(cur_rotY)
            cur_x    += fx * step_dist
            cur_z    += fz * step_dist
            cur_y    += d_height

    return nodes, segments


def generate_straight(
        start_x: float, start_y: float, start_z: float, start_rotY: float,
        length: float, height_change: float = 0.0,
        n_segments: int = 1,
        track_class: str = 'Mainline',
        style: str = 'Standard',
        speed_limit: int = 0,
        id_prefix: str = 'N',
        seg_prefix: str = 'S',
        existing_ids: set = None,
) -> tuple:
    """Generate a straight chain of nodes and segments along start_rotY heading."""
    import math as _m
    if existing_ids is None:
        existing_ids = set()
    n_segments = max(1, n_segments)

    step_dist  = length / n_segments
    d_height   = height_change / n_segments

    r = _m.radians(start_rotY)
    fx, fz = _m.sin(r), _m.cos(r)

    nodes    = []
    segments = []

    def next_nid():
        while True:
            nid = f"{id_prefix}_{_rand_chars()}"
            if nid not in existing_ids: existing_ids.add(nid); return nid
    def next_sid():
        while True:
            sid = f"{seg_prefix}_{_rand_chars()}"
            if sid not in existing_ids: existing_ids.add(sid); return sid

    cur_x, cur_y, cur_z = start_x, start_y, start_z
    prev_nid = None

    for i in range(n_segments + 1):
        nid = next_nid()
        nodes.append({
            'id':    nid,
            'x':     cur_x,
            'y':     cur_y,
            'z':     cur_z,
            'rotX':  0.0,
            'rotY':  start_rotY,
            'rotZ':  0.0,
            'flipSwitchStand': False,
        })
        if prev_nid is not None:
            segments.append({
                'id':         next_sid(),
                'startId':    prev_nid,
                'endId':      nid,
                'trackClass': track_class,
                'style':      style,
                'speedLimit': speed_limit,
                'priority':   0,
                'groupId':    None,
            })
        prev_nid = nid
        if i < n_segments:
            cur_x += fx * step_dist
            cur_z += fz * step_dist
            cur_y += d_height

    return nodes, segments


def generate_parallel_tracks(
        source_nodes: list,      # ordered list of node dicts {x,y,z,rotY,...}
        source_segments: list,   # ordered list of segment dicts
        separation: float,       # metres between track centres
        n_tracks: int = 1,       # additional tracks to generate (1 = one parallel)
        side: str = 'right',     # 'right', 'left', or 'both'
        sample_y_fn=None,        # optional fn(x,z)->float for terrain height
        track_class: str = 'Mainline',
        style: str = 'Standard',
        speed_limit: int = 0,
        id_prefix: str = 'N',
        seg_prefix: str = 'S',
        existing_ids: set = None,
) -> list:
    """
    Generate parallel track(s) offset from a source chain.

    Returns list of (nodes_list, segments_list) tuples, one per generated track.
    side='both' generates tracks on each side.
    """
    if existing_ids is None:
        existing_ids = set()

    # Build offset directions to generate
    if side == 'both':
        offsets = [i * separation for i in range(-n_tracks, n_tracks + 1) if i != 0]
    elif side == 'right':
        offsets = [i * separation for i in range(1, n_tracks + 1)]
    else:  # left
        offsets = [-i * separation for i in range(1, n_tracks + 1)]

    results = []

    def next_nid():
        while True:
            nid = f"{id_prefix}_{_rand_chars()}"
            if nid not in existing_ids:
                existing_ids.add(nid); return nid

    def next_sid():
        while True:
            sid = f"{seg_prefix}_{_rand_chars()}"
            if sid not in existing_ids:
                existing_ids.add(sid); return sid

    for offset in offsets:
        new_nodes = []
        new_segs  = []
        id_map    = {}   # source_nid -> new_nid

        for node in source_nodes:
            px, pz = _perpendicular(node['rotY'])
            nx = node['x'] + px * offset
            nz = node['z'] + pz * offset
            ny = node['y']
            if sample_y_fn:
                sampled = sample_y_fn(nx, nz)
                if sampled and sampled > 0:
                    ny = sampled
            nid = next_nid()
            id_map[node['id']] = nid
            new_nodes.append({
                'id':    nid,
                'x':     nx,
                'y':     ny,
                'z':     nz,
                'rotX':  node.get('rotX', 0),
                'rotY':  node['rotY'],
                'rotZ':  node.get('rotZ', 0),
                'flipSwitchStand': node.get('flipSwitchStand', False),
            })

        for seg in source_segments:
            new_start = id_map.get(seg.get('startId', ''))
            new_end   = id_map.get(seg.get('endId', ''))
            if not new_start or not new_end:
                continue
            new_segs.append({
                'id':         next_sid(),
                'startId':    new_start,
                'endId':      new_end,
                'trackClass': track_class or seg.get('trackClass', 'Mainline'),
                'style':      style or seg.get('style', 'Standard'),
                'speedLimit': speed_limit or seg.get('speedLimit', 0),
                'priority':   seg.get('priority', 0),
                'groupId':    seg.get('groupId', ''),
            })

        results.append((new_nodes, new_segs))

    return results



# ---------------------------------------------------------------------------
# Node / segment utility functions
# ---------------------------------------------------------------------------
def node_flatten(node: dict) -> dict:
    """Return a copy of node with rotX and rotZ zeroed (level the node)."""
    n = dict(node)
    n['rotX'] = 0.0
    n['rotZ'] = 0.0
    return n


def node_reverse(node: dict) -> dict:
    """Return a copy of node with rotY flipped 180 degrees."""
    n = dict(node)
    n['rotY'] = (node.get('rotY', 0) + 180.0) % 360.0
    return n


def node_set_rotY(node: dict, rotY: float) -> dict:
    """Return a copy of node with rotY set to given value."""
    n = dict(node)
    n['rotY'] = rotY % 360.0
    return n


def segment_set_props(seg: dict, track_class: str = None, style: str = None,
                      speed_limit: int = None, priority: int = None,
                      group_id: str = None) -> dict:
    """Return a copy of segment with updated properties."""
    s = dict(seg)
    if track_class  is not None: s['trackClass']  = track_class
    if style        is not None: s['style']        = style
    if speed_limit  is not None: s['speedLimit']   = speed_limit
    if priority     is not None: s['priority']     = priority
    if group_id     is not None: s['groupId']      = group_id
    return s



# ---------------------------------------------------------------------------
# F24: next_marker_id
# ---------------------------------------------------------------------------
def next_marker_id(existing_ids: set = None) -> str:
    """F24: Generate a unique TrackMarker ID matching game's IdGenerator("M", 3) format."""
    if existing_ids is None:
        existing_ids = set()
    while True:
        mid = 'M' + _rand_chars(3)
        if mid not in existing_ids:
            existing_ids.add(mid)
            return mid


# ---------------------------------------------------------------------------
# Turnout geometry
# ---------------------------------------------------------------------------
def turnout_radius_for_chord(chord_length: float, deflection_deg: float):
    """Return the exact circular radius for a chord and tangent deflection.

    A circular arc's chord points halfway between its entry and exit tangents.
    Straight legs have no finite radius and return ``None``.
    """
    chord = abs(float(chord_length))
    angle = abs(float(deflection_deg))
    if chord <= 0.0 or angle <= 1e-9:
        return None
    sine = _math.sin(_math.radians(angle) * 0.5)
    if abs(sine) <= 1e-12:
        return None
    return chord / (2.0 * sine)


def turnout_leg_pose(
        sw_x: float,
        sw_y: float,
        sw_z: float,
        approach_rotY: float,
        deflection_deg: float,
        leg_length: float,
        grade_pct: float = 0.0,
        reverse: bool = False,
) -> tuple:
    """Solve one turnout endpoint using circular-chord geometry.

    ``deflection_deg`` is signed from the approach tangent.  For an outgoing
    leg, the chord bearing lies halfway between the entry and exit tangents.
    ``reverse`` creates the entry leg behind the switch.  Grade is expressed
    in the approach direction, so a positive grade raises outgoing endpoints
    and lowers the entry endpoint.

    Returns ``(x, y, z, rotX, rotY)``.
    """
    length = max(0.0, float(leg_length))
    grade_ratio = float(grade_pct) / 100.0
    if reverse:
        chord_heading = float(approach_rotY) + 180.0
        end_rot_y = chord_heading % 360.0
        local_grade = -grade_ratio
    else:
        chord_heading = float(approach_rotY) + float(deflection_deg) * 0.5
        end_rot_y = (float(approach_rotY) + float(deflection_deg)) % 360.0
        local_grade = grade_ratio

    radians = _math.radians(chord_heading)
    end_x = float(sw_x) + length * _math.sin(radians)
    end_z = float(sw_z) + length * _math.cos(radians)
    end_y = float(sw_y) + length * local_grade
    end_rot_x = -_math.degrees(_math.atan(local_grade))
    return end_x, end_y, end_z, end_rot_x, end_rot_y


# generate_turnout (A12)
# ---------------------------------------------------------------------------
def generate_turnout(
        sw_x: float, sw_y: float, sw_z: float,
        approach_rotY: float,
        diverge_angle: float = 10.0,
        leg_length: float = 30.0,
        direction: str = 'left',
        flip_switch_stand: bool = False,
        track_class: str = 'Mainline',
        diverge_class: str = 'Branch',
        style: str = 'Standard',
        speed_limit: int = 0,
        diverge_speed: int = 0,
        through_curve_angle: float = 0.0,
        grade_pct: float = 0.0,
        id_prefix: str = 'N',
        seg_prefix: str = 'S',
        existing_ids: set = None,
) -> tuple:
    """
    Generate a 3-node switch (turnout).

    switch_node.rotY = approach_rotY; through/diverge node rotY carry the exit angles
    1 segment enters:  entry_node -> switch_node  (switch is end)
    2 segments leave:  switch_node -> through_node
                       switch_node -> diverge_node
    through_curve_angle: 0 = straight through; non-zero = curved turnout
      (positive = same side as branch, creating a curved turnout)
    diverge_angle: total angle of branch from approach heading

    Returns (nodes, segments, sw_id, entry_id, through_id, diverge_id)
    """
    if existing_ids is None:
        existing_ids = set()

    def next_nid():
        while True:
            nid = f"{id_prefix}_{_rand_chars()}"
            if nid not in existing_ids: existing_ids.add(nid); return nid
    def next_sid():
        while True:
            sid = f"{seg_prefix}_{_rand_chars()}"
            if sid not in existing_ids: existing_ids.add(sid); return sid

    sign       = 1.0 if direction == 'right' else -1.0
    div_deflection = sign * float(diverge_angle)
    thru_deflection = sign * float(through_curve_angle)
    ex, ey, ez, erx, entry_rotY = turnout_leg_pose(
        sw_x, sw_y, sw_z, approach_rotY, 0.0, leg_length,
        grade_pct=grade_pct, reverse=True,
    )
    tx, ty, tz, trx, thru_rotY = turnout_leg_pose(
        sw_x, sw_y, sw_z, approach_rotY, thru_deflection, leg_length,
        grade_pct=grade_pct,
    )
    dx, dy, dz, drx, div_rotY = turnout_leg_pose(
        sw_x, sw_y, sw_z, approach_rotY, div_deflection, leg_length,
        grade_pct=grade_pct,
    )
    sw_rot_x = -_math.degrees(_math.atan(float(grade_pct) / 100.0))

    sw_id   = next_nid()
    ent_id  = next_nid()
    thru_id = next_nid()
    div_id  = next_nid()

    nodes = [
        {'id': sw_id,   'x': sw_x,  'y': sw_y,  'z': sw_z,
         'rotX': sw_rot_x, 'rotY': approach_rotY, 'rotZ': 0,
         'flipSwitchStand': flip_switch_stand},
        {'id': ent_id,  'x': ex,    'y': ey,  'z': ez,
         'rotX': erx, 'rotY': entry_rotY, 'rotZ': 0, 'flipSwitchStand': False},
        {'id': thru_id, 'x': tx,    'y': ty,  'z': tz,
         'rotX': trx, 'rotY': thru_rotY, 'rotZ': 0, 'flipSwitchStand': False},
        {'id': div_id,  'x': dx,    'y': dy,  'z': dz,
         'rotX': drx, 'rotY': div_rotY, 'rotZ': 0, 'flipSwitchStand': False},
    ]

    _style_norm = (style or 'Standard').capitalize()
    if _style_norm not in {'Standard', 'Yard', 'Bridge', 'Tunnel'}:
        _style_norm = 'Standard'

    segments = [
        {'id': next_sid(), 'startId': ent_id,  'endId': sw_id,
         'trackClass': track_class,   'style': _style_norm,
         'speedLimit': speed_limit,   'priority': 0, 'groupId': None},
        {'id': next_sid(), 'startId': sw_id,   'endId': thru_id,
         'trackClass': track_class,   'style': _style_norm,
         'speedLimit': speed_limit,   'priority': 0, 'groupId': None},
        {'id': next_sid(), 'startId': sw_id,   'endId': div_id,
         'trackClass': diverge_class, 'style': _style_norm,
         'speedLimit': diverge_speed, 'priority': 0, 'groupId': None},
    ]

    return nodes, segments, sw_id, ent_id, thru_id, div_id

# generate_wye
# ---------------------------------------------------------------------------
def generate_wye(
        sw_x: float, sw_y: float, sw_z: float,
        approach_rotY: float,          # heading trains ARRIVE from
        left_angle: float = 10.0,      # degrees the left leg diverges from approach
        right_angle: float = 10.0,     # degrees the right leg diverges from approach
        leg_length: float = 30.0,
        flip_switch_stand: bool = False,
        track_class: str = 'Mainline',
        style: str = 'Standard',
        speed_limit: int = 0,
        grade_pct: float = 0.0,
        id_prefix: str = 'N',
        seg_prefix: str = 'S',
        existing_ids: set = None,
) -> tuple:
    """
    Generate a wye switch.

    One entry, two diverging legs — no through route.
    left_angle and right_angle are measured from approach_rotY.
    Frog rotY follows the common entry tangent. Endpoint positions use the
    circular chord bearing halfway between entry and exit tangents.

    Returns (nodes, segments, sw_id, entry_id, left_id, right_id)
    """
    if existing_ids is None:
        existing_ids = set()

    def next_nid():
        while True:
            nid = f"{id_prefix}_{_rand_chars()}"
            if nid not in existing_ids: existing_ids.add(nid); return nid
    def next_sid():
        while True:
            sid = f"{seg_prefix}_{_rand_chars()}"
            if sid not in existing_ids: existing_ids.add(sid); return sid

    ex, ey, ez, erx, entry_rotY = turnout_leg_pose(
        sw_x, sw_y, sw_z, approach_rotY, 0.0, leg_length,
        grade_pct=grade_pct, reverse=True,
    )
    lx, ly, lz, lrx, left_rotY = turnout_leg_pose(
        sw_x, sw_y, sw_z, approach_rotY, -abs(left_angle), leg_length,
        grade_pct=grade_pct,
    )
    rx, ry, rz, rrx, right_rotY = turnout_leg_pose(
        sw_x, sw_y, sw_z, approach_rotY, abs(right_angle), leg_length,
        grade_pct=grade_pct,
    )
    switch_rot_x = -_math.degrees(
        _math.atan(float(grade_pct) / 100.0)
    )

    sw_id   = next_nid()
    ent_id  = next_nid()
    left_id = next_nid()
    rgt_id  = next_nid()

    _style_norm = (style or 'Standard').capitalize()
    if _style_norm not in {'Standard', 'Yard', 'Bridge', 'Tunnel'}:
        _style_norm = 'Standard'

    nodes = [
        {'id': sw_id,   'x': sw_x, 'y': sw_y, 'z': sw_z,
         'rotX': switch_rot_x, 'rotY': approach_rotY, 'rotZ': 0,
         'flipSwitchStand': flip_switch_stand},
        {'id': ent_id,  'x': ex,   'y': ey, 'z': ez,
         'rotX': erx, 'rotY': entry_rotY, 'rotZ': 0, 'flipSwitchStand': False},
        {'id': left_id, 'x': lx,   'y': ly, 'z': lz,
         'rotX': lrx, 'rotY': left_rotY, 'rotZ': 0, 'flipSwitchStand': False},
        {'id': rgt_id,  'x': rx,   'y': ry, 'z': rz,
         'rotX': rrx, 'rotY': right_rotY, 'rotZ': 0, 'flipSwitchStand': False},
    ]

    segments = [
        {'id': next_sid(), 'startId': ent_id,  'endId': sw_id,
         'trackClass': track_class, 'style': _style_norm,
         'speedLimit': speed_limit, 'priority': 0, 'groupId': None},
        {'id': next_sid(), 'startId': sw_id,   'endId': left_id,
         'trackClass': track_class, 'style': _style_norm,
         'speedLimit': speed_limit, 'priority': 0, 'groupId': None},
        {'id': next_sid(), 'startId': sw_id,   'endId': rgt_id,
         'trackClass': track_class, 'style': _style_norm,
         'speedLimit': speed_limit, 'priority': 0, 'groupId': None},
    ]

    return nodes, segments, sw_id, ent_id, left_id, rgt_id
