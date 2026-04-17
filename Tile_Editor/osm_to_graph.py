"""
osm_to_graph.py  —  OSM railway → Railroader game-graph.json converter
========================================================================
Queries OpenStreetMap (via Overpass API) for the railway between
Andrews NC, Marble NC, and Murphy NC, then converts the lat/lon
geometry into Railroader Unity world coordinates and writes nodes +
segments into your mod's game-graph.json.

USAGE
-----
    # Preview — print how many nodes/segments would be generated:
    python osm_to_graph.py --dry-run

    # Merge into your mod's game-graph.json:
    python osm_to_graph.py --merge "C:/path/to/your/mod/game-graph.json"

    # Also decimate nodes (remove redundant ones on straight sections):
    python osm_to_graph.py --merge game-graph.json --simplify 2.0

REQUIREMENTS
------------
    pip install requests

COORDINATE SYSTEM (from the tile editor source)
-------------------------------------------------
  Origin lat/lon : 35.382614, -83.49541   (GEN_ORIGIN_LAT/LON)
  Tile size      : 500 m                   (UNITY_TILE)
  Tile stride    : 512 px                  (TILE_STRIDE, not needed here)
  x  = East  (positive = East of origin)
  z  = North (positive = North of origin — note: editor uses max_y formula)
  y  = terrain height (we set 0.0 and let the game figure it out,
       or you can set a flat value per region)
  rotY = heading in degrees (0 = North, 90 = East)
"""

import json
import math
import uuid
import sys
import argparse
from pathlib import Path
from typing import Optional

# ---------------------------------------------------------------------------
# Railroader world coordinate origin  (from edit_tiles/constants.py)
# ---------------------------------------------------------------------------
ORIGIN_LAT = 35.382614
ORIGIN_LON = -83.49541

# Approx metres per degree at this latitude
METRES_PER_DEG_LAT = 111_320.0
METRES_PER_DEG_LON = 111_320.0 * math.cos(math.radians(ORIGIN_LAT))  # ≈ 91_130 m/deg

# ---------------------------------------------------------------------------
# Overpass query — Andrews → Marble → Murphy rail corridor
# Bounding box: SW (35.0, -84.2)  NE (35.6, -83.7)
# Grabs rail, disused, and abandoned railway ways in the corridor
# ---------------------------------------------------------------------------
OVERPASS_URL = "https://overpass-api.de/api/interpreter"
OVERPASS_QUERY = """
[out:json][timeout:90];
(
  way[railway~"^(rail|disused|abandoned)$"](35.0,-84.2,35.6,-83.7);
);
out body;
>;
out skel qt;
"""

# ---------------------------------------------------------------------------
# Coordinate conversion
# ---------------------------------------------------------------------------

def latlon_to_unity(lat: float, lon: float, offset_x: float = 0.0, offset_z: float = 0.0) -> tuple[float, float]:
    """Convert WGS-84 lat/lon to Railroader Unity (x, z) in metres."""
    x = (lon - ORIGIN_LON) * METRES_PER_DEG_LON + offset_x
    z = (lat - ORIGIN_LAT) * METRES_PER_DEG_LAT + offset_z
    return x, z


def heading_from_vec(dx: float, dz: float) -> float:
    """Direction vector → rotY degrees. 0=North (+Z), 90=East (+X)."""
    return math.degrees(math.atan2(dx, dz)) % 360


# ---------------------------------------------------------------------------
# OSM fetch
# ---------------------------------------------------------------------------

def fetch_osm() -> dict:
    """Query Overpass and return parsed JSON."""
    try:
        import requests
    except ImportError:
        sys.exit("ERROR: 'requests' not installed. Run:  pip install requests")

    print("Querying Overpass API for Andrews–Murphy railway...")
    resp = requests.post(OVERPASS_URL, data={"data": OVERPASS_QUERY}, timeout=120)
    resp.raise_for_status()
    data = resp.json()
    print(f"  Got {len(data['elements'])} OSM elements")
    return data


# ---------------------------------------------------------------------------
# Simplification (Ramer–Douglas–Peucker)
# ---------------------------------------------------------------------------

def _rdp_recursive(points: list, epsilon: float, start: int, end: int, keep: list):
    if end <= start + 1:
        return
    # Find point with max perpendicular distance from start→end line
    x0, z0 = points[start]
    x1, z1 = points[end]
    dx, dz = x1 - x0, z1 - z0
    line_len = math.sqrt(dx * dx + dz * dz)
    max_dist = 0.0
    max_idx = start + 1
    for i in range(start + 1, end):
        xi, zi = points[i]
        if line_len < 0.001:
            d = math.sqrt((xi - x0) ** 2 + (zi - z0) ** 2)
        else:
            d = abs(dx * (z0 - zi) - (x0 - xi) * dz) / line_len
        if d > max_dist:
            max_dist = d
            max_idx = i
    if max_dist > epsilon:
        _rdp_recursive(points, epsilon, start, max_idx, keep)
        keep.append(max_idx)
        _rdp_recursive(points, epsilon, max_idx, end, keep)


def rdp_simplify(points: list, epsilon: float) -> list:
    """Ramer–Douglas–Peucker polyline simplification. epsilon in metres."""
    if len(points) <= 2:
        return points
    keep = [0]
    _rdp_recursive(points, epsilon, 0, len(points) - 1, keep)
    keep.append(len(points) - 1)
    keep_set = sorted(set(keep))
    return [points[i] for i in keep_set]


# ---------------------------------------------------------------------------
# ID generation
# ---------------------------------------------------------------------------

def _new_id(prefix: str, used: set) -> str:
    for _ in range(10_000):
        c = prefix + uuid.uuid4().hex[:6].upper()
        if c not in used:
            used.add(c)
            return c
    raise RuntimeError("ID space exhausted")


# ---------------------------------------------------------------------------
# Main conversion
# ---------------------------------------------------------------------------

def osm_to_graph(
    simplify_epsilon: float = 3.0,
    default_y: float = 0.0,
    track_class: str = "Branch",
    style: str = "Standard",
    speed_limit: int = 0,
    id_prefix: str = "AM",
    offset_x: float = 0.0,
    offset_z: float = 0.0,
) -> dict:
    """
    Fetch OSM data and convert to game-graph node/segment dicts.

    Args:
        simplify_epsilon: RDP tolerance in metres. Higher = fewer nodes.
                          0 = keep every OSM node (can be thousands).
                          2-5 m is a good balance for rail geometry.
        default_y:        Y (height) to assign all nodes. The game
                          will snap to terrain if close enough.
        track_class:      "Mainline", "Branch", or "Industrial"
        style:            "Standard", "Bridge", "Tunnel", or "Yard"
        speed_limit:      0 = use class default
        id_prefix:        Short prefix for generated IDs ("AM" = Andrews-Murphy)

    Returns:
        dict with "nodes" and "segments" keys.
    """
    raw = fetch_osm()

    osm_nodes = {
        e["id"]: e
        for e in raw["elements"]
        if e["type"] == "node"
    }
    osm_ways = [e for e in raw["elements"] if e["type"] == "way"]

    print(f"  Ways: {len(osm_ways)}, OSM nodes: {len(osm_nodes)}")

    # Filter to ways that look like the main corridor
    # (exclude sidings, spurs tagged as industrial if needed — adjust as desired)
    def _keep_way(way: dict) -> bool:
        tags = way.get("tags", {})
        rwy = tags.get("railway", "")
        # Keep rail, disused, abandoned
        if rwy not in ("rail", "disused", "abandoned"):
            return False
        # Skip service tracks (yard leads, sidings) unless you want them
        # Comment this out if you want ALL tracks including yard leads:
        svc = tags.get("service", "")
        if svc in ("siding", "yard", "crossover"):
            return False
        return True

    kept_ways = [w for w in osm_ways if _keep_way(w)]
    skipped = len(osm_ways) - len(kept_ways)
    print(f"  Kept {len(kept_ways)} ways (skipped {skipped} service/other)")

    used_ids: set = set()
    out_nodes: dict = {}
    out_segments: dict = {}

    for way in kept_ways:
        node_refs = way.get("nodes", [])
        if len(node_refs) < 2:
            continue

        # Build list of Unity (x, z) positions for this way
        positions = []
        for nref in node_refs:
            osm_n = osm_nodes.get(nref)
            if osm_n is None:
                continue
            ux, uz = latlon_to_unity(osm_n["lat"], osm_n["lon"], offset_x, offset_z)
            positions.append((ux, uz))

        if len(positions) < 2:
            continue

        # Simplify if requested
        if simplify_epsilon > 0:
            before = len(positions)
            positions = rdp_simplify(positions, simplify_epsilon)
            after = len(positions)
        else:
            before = after = len(positions)

        # Generate node IDs and compute rotY for each node
        way_node_ids = []
        for i, (ux, uz) in enumerate(positions):
            nid = _new_id(id_prefix + "N", used_ids)
            way_node_ids.append(nid)

            # Heading: average of incoming and outgoing vectors
            if i == 0:
                dx = positions[1][0] - positions[0][0]
                dz = positions[1][1] - positions[0][1]
            elif i == len(positions) - 1:
                dx = positions[-1][0] - positions[-2][0]
                dz = positions[-1][1] - positions[-2][1]
            else:
                dx = positions[i + 1][0] - positions[i - 1][0]
                dz = positions[i + 1][1] - positions[i - 1][1]

            rotY = heading_from_vec(dx, dz)

            out_nodes[nid] = {
                "position": {
                    "x": round(ux, 2),
                    "y": round(default_y, 2),
                    "z": round(uz, 2),
                },
                "rotation": {"x": 0.0, "y": round(rotY, 2), "z": 0.0},
                "flipSwitchStand": False,
            }

        # Generate segments between consecutive nodes
        seen_pairs: set = set()
        for i in range(len(way_node_ids) - 1):
            s_id = way_node_ids[i]
            e_id = way_node_ids[i + 1]
            if s_id == e_id:
                continue  # skip self-loops
            pair = tuple(sorted([s_id, e_id]))
            if pair in seen_pairs:
                continue  # skip duplicates
            seen_pairs.add(pair)
            sid = _new_id(id_prefix + "S", used_ids)
            out_segments[sid] = {
                "startId": s_id,
                "endId": e_id,
                "trackClass": track_class,
                "style": style,
                "speedLimit": speed_limit,
                "priority": 0,
                "groupId": "",
            }

    print(f"\nResult: {len(out_nodes)} nodes, {len(out_segments)} segments")
    return {"nodes": out_nodes, "segments": out_segments}


# ---------------------------------------------------------------------------
# Merge into game-graph.json
# ---------------------------------------------------------------------------

def merge_into_file(graph: dict, path: str):
    p = Path(path)
    if p.exists():
        with open(p, encoding="utf-8") as f:
            data = json.load(f)
    else:
        data = {
            "tracks": {"nodes": {}, "segments": {}, "spans": {}},
            "areas": {}, "texts": {}, "scenery": {},
            "splineys": {}, "simpleGraphs": {}, "mandelas": {},
        }

    if "tracks" not in data:
        data["tracks"] = {}
    data["tracks"].setdefault("nodes", {})
    data["tracks"].setdefault("segments", {})

    data["tracks"]["nodes"].update(graph["nodes"])
    data["tracks"]["segments"].update(graph["segments"])

    # Safety: remove self-loops and duplicates
    seen_pairs: set = set()
    for sid in list(data["tracks"]["segments"].keys()):
        seg = data["tracks"]["segments"][sid]
        if not isinstance(seg, dict):
            continue
        s, e = seg.get("startId", ""), seg.get("endId", "")
        if s == e:
            del data["tracks"]["segments"][sid]
            continue
        pair = tuple(sorted([s, e]))
        if pair in seen_pairs:
            del data["tracks"]["segments"][sid]
        else:
            seen_pairs.add(pair)

    with open(p, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)

    print(f"Merged into: {p}")


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Convert OSM railway data (Andrews–Murphy NC) to Railroader game-graph.json"
    )
    parser.add_argument(
        "--merge", metavar="PATH",
        help="Path to game-graph.json to merge results into"
    )
    parser.add_argument(
        "--dry-run", action="store_true",
        help="Fetch and convert but don't write anything — just show stats"
    )
    parser.add_argument(
        "--simplify", type=float, default=3.0, metavar="METRES",
        help="RDP simplification tolerance in metres (default 3.0). "
             "0 = keep every OSM node. Higher = fewer nodes."
    )
    parser.add_argument(
        "--track-class", default="Branch",
        choices=["Mainline", "Branch", "Industrial"],
        help="Track class for generated segments (default: Branch)"
    )
    parser.add_argument(
        "--style", default="Standard",
        choices=["Standard", "Bridge", "Tunnel", "Yard"],
        help="Track style (default: Standard)"
    )
    parser.add_argument(
        "--speed-limit", type=int, default=0, metavar="MPH",
        help="Speed limit (default 0 = class default)"
    )
    parser.add_argument(
        "--y", type=float, default=0.0, metavar="HEIGHT",
        help="Y (terrain height) to assign all nodes (default 0.0)"
    )
    parser.add_argument(
        "--offset-x", type=float, default=0.0, metavar="METRES",
        help="Shift all nodes East/West in metres (positive=East, negative=West)"
    )
    parser.add_argument(
        "--offset-z", type=float, default=0.0, metavar="METRES",
        help="Shift all nodes North/South in metres (positive=North, negative=South)"
    )
    args = parser.parse_args()

    graph = osm_to_graph(
        simplify_epsilon=args.simplify,
        default_y=args.y,
        track_class=args.track_class,
        style=args.style,
        speed_limit=args.speed_limit,
        offset_x=args.offset_x,
        offset_z=args.offset_z,
    )

    if args.dry_run:
        print("\n-- DRY RUN -- nothing written.")
        print(f"Would generate {len(graph['nodes'])} nodes and {len(graph['segments'])} segments.")
    elif args.merge:
        merge_into_file(graph, args.merge)
    else:
        print("\nNo --merge path given. Printing JSON to stdout:\n")
        print(json.dumps({"tracks": graph}, indent=2))
