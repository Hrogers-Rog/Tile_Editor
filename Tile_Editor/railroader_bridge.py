"""
railroader_bridge.py
====================
Python side of the TrackBridge mod connection.

Watches  Mods/TrackBridge/bridge_state.json  for changes and exposes the
live game state to edit_tiles.py.  Also provides helpers to write
mixinto-format JSON that Strange Customs will hot-reload into the game.

Usage in edit_tiles.py
----------------------
    from railroader_bridge import RailroaderBridge

    bridge = RailroaderBridge()            # auto-finds Railroader install
    bridge.on_state_update = my_callback   # called whenever state changes
    bridge.start()                         # start background watcher

    # Push a new track layout into the game:
    bridge.write_mixinto("Mods/MyMod/patches/my_track.json", nodes, segments)

    # When done:
    bridge.stop()
"""

import json
import math
import os
import platform
import threading
import time
from pathlib import Path
from typing import Callable, List, Dict, Optional


# ---------------------------------------------------------------------------
# Data classes (plain Python dicts, matches BridgeState JSON fields)
# ---------------------------------------------------------------------------
class BridgeNode:
    __slots__ = ('id','x','y','z','rotX','rotY','rotZ',
                 'flipSwitchStand','segmentCount')
    def __init__(self, d: dict):
        self.id            = d.get('id','')
        self.x             = float(d.get('x', 0))
        self.y             = float(d.get('y', 0))
        self.z             = float(d.get('z', 0))
        self.rotX          = float(d.get('rotX', 0))
        self.rotY          = float(d.get('rotY', 0))
        self.rotZ          = float(d.get('rotZ', 0))
        self.flipSwitchStand = bool(d.get('flipSwitchStand', False))
        self.segmentCount  = int(d.get('segmentCount', 0))

    def to_dict(self):
        return {k: getattr(self, k) for k in self.__slots__}


class BridgeSegment:
    __slots__ = ('id','startId','endId','trackClass','style',
                 'priority','speedLimit','groupId')
    def __init__(self, d: dict):
        self.id         = d.get('id','')
        self.startId    = d.get('startId','')
        self.endId      = d.get('endId','')
        self.trackClass = int(d.get('trackClass', 0))
        self.style      = int(d.get('style', 0))
        self.priority   = int(d.get('priority', 0))
        self.speedLimit = int(d.get('speedLimit', 45))
        self.groupId    = d.get('groupId', None)


class BridgeCar:
    __slots__ = ('id','type','roadNumber','x','y','z',
                 'heading','velocity','isLocomotive')
    def __init__(self, d: dict):
        self.id           = d.get('id','')
        self.type         = d.get('type','')
        self.roadNumber   = d.get('roadNumber','')
        self.x            = float(d.get('x', 0))
        self.y            = float(d.get('y', 0))
        self.z            = float(d.get('z', 0))
        self.heading      = float(d.get('heading', 0))
        self.velocity     = float(d.get('velocity', 0))
        self.isLocomotive = bool(d.get('isLocomotive', False))


class BridgeState:
    def __init__(self, d: dict):
        self.timestamp = int(d.get('timestamp', 0))
        self.saveName  = d.get('saveName', None)
        self.nodes     = [BridgeNode(n)    for n in d.get('nodes', [])]
        self.segments  = [BridgeSegment(s) for s in d.get('segments', [])]
        self.cars      = [BridgeCar(c)     for c in d.get('cars', [])]
        # Quick-access dicts
        self.nodes_by_id    = {n.id: n for n in self.nodes}
        self.segments_by_id = {s.id: s for s in self.segments}

    @property
    def is_map_loaded(self):
        return bool(self.nodes) or bool(self.segments)


# ---------------------------------------------------------------------------
# Mixinto writer  (Strange Customs game-graph format)
# ---------------------------------------------------------------------------
# TrackClass values from game enum
TRACK_CLASS = {
    'mainline': 0,
    'branch':   1,
    'industry': 2,
    'siding':   3,
    'yard':     4,
    'logging':  5,
}

# Map internal trackClass display names → JSON strings used by real mods
_BRIDGE_TRACK_CLASS_JSON = {
    'Mainline': 'Mainline', 'Branch': 'Branch',
    'Industry': 'Industrial', 'Siding': 'Siding',
    'Yard': 'Yard', 'Logging': 'Logging',
}


def build_mixinto(
    nodes:        List[dict],
    segments:     List[dict],
    splineys:     Optional[dict] = None,
    scenery:      Optional[dict] = None,
    mandelas:     Optional[dict] = None,
    areas:        Optional[dict] = None,
    texts:        Optional[dict] = None,
    simple_graphs: Optional[dict] = None,
) -> dict:
    """
    Build a game-graph mixinto dict ready to be written as JSON.

    nodes    — list of dicts with keys:
               {id, x, y, z, rotX, rotY, rotZ, flipSwitchStand}
               OR {id, position:{x,y,z}, rotation:{x,y,z}, flipSwitchStand}
    segments — list of dicts:
               {id, startId, endId, trackClass:str, style:str,
                priority:int, speedLimit:int, groupId:str}
               trackClass: 'Mainline'|'Branch'|'Industry'|'Siding'|'Yard'|'Logging'
               style: 'Standard'|'Yard'|'Bridge'
    splineys  — dict of spliney id → spliney object (trestles, rivers, roads)
    scenery   — dict of scenery id → scenery object
    mandelas  — dict of mandela id → mandela object (instantiateFrom, localPosition…)
    areas     — dict of area id → area object (industries, position, radius…)
    texts     — dict of text id → text object
    simple_graphs — dict of simpleGraph id → graph object
    """
    nodes_obj    = {}
    segments_obj = {}

    for n in nodes:
        nodes_obj[n['id']] = {
            'position': n.get('position') or {
                'x': n.get('x', 0), 'y': n.get('y', 0), 'z': n.get('z', 0)},
            'rotation': n.get('rotation') or {
                'x': n.get('rotX', 0), 'y': n.get('rotY', 0), 'z': n.get('rotZ', 0)},
            'flipSwitchStand': n.get('flipSwitchStand', False),
        }

    for s in segments:
        tc_internal = s.get('trackClass', 'Mainline')
        segments_obj[s['id']] = {
            'startId':   s['startId'],
            'endId':     s['endId'],
            'Style':     s.get('style', 'Standard'),
            'trackClass': _BRIDGE_TRACK_CLASS_JSON.get(tc_internal, tc_internal),
            'priority':  s.get('priority', 0),
            'speedLimit': s.get('speedLimit', 0),
            'groupId':   s.get('groupId', ''),
        }

    return {
        'tracks': {
            'nodes':    nodes_obj,
            'segments': segments_obj,
            'spans':    {},
        },
        'splineys':     splineys     or {},
        'scenery':      scenery      or {},
        'mandelas':     mandelas     or {},
        'areas':        areas        or {},
        'texts':        texts        or {},
        'simpleGraphs': simple_graphs or {},
    }


def write_mixinto(path: str,
                  nodes: List[dict],
                  segments: List[dict],
                  splineys:     Optional[dict] = None,
                  scenery:      Optional[dict] = None,
                  mandelas:     Optional[dict] = None,
                  areas:        Optional[dict] = None,
                  texts:        Optional[dict] = None,
                  simple_graphs: Optional[dict] = None):
    """
    Write a Strange Customs mixinto JSON to disk.
    Strange Customs FileSystemWatcher will pick it up within ~500ms.
    """
    data = build_mixinto(nodes, segments, splineys, scenery,
                         mandelas, areas, texts, simple_graphs)
    tmp  = path + '.tmp'
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(tmp, 'w') as f:
        json.dump(data, f, indent=2)
    os.replace(tmp, path)


# ---------------------------------------------------------------------------
# Bridge: watches bridge_state.json and surfaces callbacks
# ---------------------------------------------------------------------------
def _railroader_path_candidates() -> List[Path]:
    """Return likely Railroader install locations for the current platform."""
    if platform.system() == 'Linux':
        return [Path('~/.steam/debian-installation/steamapps/common/Railroader').expanduser()]

    if platform.system() == 'Windows':
        candidates = []
        steam_roots = [
            Path(os.environ.get('PROGRAMFILES(X86)', 'C:/Program Files (x86)')) / 'Steam',
            Path(os.environ.get('PROGRAMFILES', 'C:/Program Files')) / 'Steam',
            Path('C:/Steam'),
        ]
        for root in steam_roots:
            candidates.append(root / 'steamapps' / 'common' / 'Railroader')
        for drive in 'DEFGH':
            candidates.append(Path(f'{drive}:/SteamLibrary/steamapps/common/Railroader'))
        return candidates

    return [Path.home()]


def preferred_railroader_path() -> Path:
    """Return the best default Railroader path for the current platform."""
    candidates = _railroader_path_candidates()
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return candidates[0]


def _default_railroader_path() -> Optional[Path]:
    """Try to find the Railroader game directory automatically."""
    for candidate in _railroader_path_candidates():
        if candidate.exists():
            return candidate
    return None


class RailroaderBridge:
    """
    Watches Mods/TrackBridge/bridge_state.json and fires callbacks.

    Quick start:
        bridge = RailroaderBridge()
        bridge.on_state_update = lambda state: print(len(state.nodes), "nodes")
        bridge.start()
        ...
        bridge.stop()
    """

    def __init__(self, game_dir: Optional[str] = None, poll_interval: float = 1.5):
        if game_dir:
            self._game_dir = Path(game_dir)
        else:
            found = _default_railroader_path()
            self._game_dir = found or Path('.')

        self._state_file   = self._game_dir / 'Mods' / 'TrackBridge' / 'bridge_state.json'
        self._command_file = self._game_dir / 'Mods' / 'TrackBridge' / 'bridge_commands.json'
        self._poll_interval = poll_interval

        self._thread: Optional[threading.Thread] = None
        self._stop_event = threading.Event()
        self._last_mtime: float = 0.0
        self._last_state: Optional[BridgeState] = None
        self._lock = threading.Lock()

        # Callbacks — set these before calling start()
        self.on_state_update:  Optional[Callable[[BridgeState], None]] = None
        self.on_connect:       Optional[Callable[[], None]] = None
        self.on_disconnect:    Optional[Callable[[], None]] = None

        self._connected = False

    # ------------------------------------------------------------------
    @property
    def state(self) -> Optional[BridgeState]:
        with self._lock:
            return self._last_state

    @property
    def connected(self) -> bool:
        return self._connected

    @property
    def game_dir(self) -> Path:
        return self._game_dir

    @game_dir.setter
    def game_dir(self, p):
        self._game_dir = Path(p)
        self._state_file   = self._game_dir / 'Mods' / 'TrackBridge' / 'bridge_state.json'
        self._command_file = self._game_dir / 'Mods' / 'TrackBridge' / 'bridge_commands.json'

    # ------------------------------------------------------------------
    def start(self):
        """Start the background watcher thread."""
        self._stop_event.clear()
        self._thread = threading.Thread(target=self._watch_loop,
                                        daemon=True, name='RailroaderBridge')
        self._thread.start()

    def stop(self):
        """Stop the background watcher thread."""
        self._stop_event.set()
        if self._thread:
            self._thread.join(timeout=3.0)

    # ------------------------------------------------------------------
    def send_command(self, action: str, payload: str = ''):
        """Write a command for the in-game mod to pick up."""
        cmd = {
            'action':  action,
            'payload': payload,
            'sentAt':  int(time.time() * 1000),
        }
        tmp = str(self._command_file) + '.tmp'
        os.makedirs(self._command_file.parent, exist_ok=True)
        with open(tmp, 'w') as f:
            json.dump(cmd, f)
        os.replace(tmp, str(self._command_file))

    def reload_tracks(self, mixinto_path: str):
        """Tell the game to hot-reload a mixinto file immediately."""
        self.send_command('reload_tracks', mixinto_path)

    def ping(self):
        """Check if the bridge is alive (appears in game log)."""
        self.send_command('ping')

    # ------------------------------------------------------------------
    def write_mixinto(self, path: str,
                      nodes: List[dict], segments: List[dict],
                      splineys=None, scenery=None,
                      mandelas=None, areas=None,
                      texts=None, simple_graphs=None):
        """Convenience: write mixinto then trigger hot-reload."""
        write_mixinto(path, nodes, segments, splineys, scenery,
                      mandelas, areas, texts, simple_graphs)
        self.reload_tracks(path)

    # ------------------------------------------------------------------
    def _watch_loop(self):
        # How old the file can be before we consider the game disconnected.
        # The mod writes every 1s; we allow 8s to handle Windows file caching
        # and File.Move() not always updating mtime immediately.
        STALE_SECS = 8.0
        _last_content_hash = None

        while not self._stop_event.is_set():
            try:
                if self._state_file.exists():
                    stat  = self._state_file.stat()
                    mtime = stat.st_mtime
                    size  = stat.st_size
                    age   = time.time() - mtime

                    # Use (mtime, size) as change key — File.Move on Windows
                    # sometimes preserves mtime, so size change catches it too
                    change_key = (mtime, size)
                    if change_key != (self._last_mtime, getattr(self, '_last_size', -1)):
                        self._last_mtime = mtime
                        self._last_size  = size
                        self._try_load_state()

                    # Connected if file is fresh OR if we just got new content
                    now_connected = age < STALE_SECS
                    if now_connected and not self._connected:
                        self._connected = True
                        if self.on_connect:
                            try: self.on_connect()
                            except Exception: pass
                    elif not now_connected and self._connected:
                        self._connected = False
                        if self.on_disconnect:
                            try: self.on_disconnect()
                            except Exception: pass
                else:
                    if self._connected:
                        self._connected = False
                        if self.on_disconnect:
                            try: self.on_disconnect()
                            except Exception: pass

            except Exception as _e:
                # Log unexpected errors so they're visible during development.
                # Expected transient errors (file locked, half-written) are
                # handled inside _try_load_state; anything reaching here is
                # worth surfacing.
                import sys
                print(f"[RailroaderBridge] watch_loop error: {_e}", file=sys.stderr)

            self._stop_event.wait(self._poll_interval)

    def _try_load_state(self):
        try:
            text = self._state_file.read_text(encoding='utf-8')
            data = json.loads(text)
            state = BridgeState(data)
            with self._lock:
                self._last_state = state
            if self.on_state_update:
                try: self.on_state_update(state)
                except Exception: pass
        except (json.JSONDecodeError, UnicodeDecodeError):
            # Expected: game wrote a partial file mid-frame.  Wait for next poll.
            pass
        except Exception as _e:
            import sys
            print(f"[RailroaderBridge] _try_load_state error: {_e}", file=sys.stderr)


# ---------------------------------------------------------------------------
# Coordinate helpers
# ---------------------------------------------------------------------------
def unity_to_lat_lon(ux: float, uz: float,
                     origin_lat: float, origin_lon: float,
                     tile_dim_m: float = 500.0,
                     origin_e_bias: float = 8.0,
                     origin_n_bias: float = -8.0) -> tuple:
    """
    Convert Unity world coordinates (x=east, z=north) back to lat/lon.
    Uses the same origin constants as the terrain generator.
    """
    east_m  = ux - origin_e_bias
    north_m = uz - origin_n_bias
    lat = origin_lat + north_m / 111111.0
    lon = origin_lon + east_m  / (111111.0 * math.cos(math.radians(origin_lat)))
    return lat, lon


def bridge_node_to_track_dict(node: BridgeNode) -> dict:
    """Convert a BridgeNode to the dict format expected by build_mixinto."""
    return {
        'id': node.id,
        'position': {'x': node.x, 'y': node.y, 'z': node.z},
        'rotation': {'x': node.rotX, 'y': node.rotY, 'z': node.rotZ},
        'flipSwitchStand': node.flipSwitchStand,
    }


def bridge_segment_to_track_dict(seg: BridgeSegment) -> dict:
    """Convert a BridgeSegment to the dict format expected by build_mixinto."""
    return {
        'id':         seg.id,
        'startId':    seg.startId,
        'endId':      seg.endId,
        'trackClass': seg.trackClass,
        'style':      seg.style,
        'priority':   seg.priority,
        'speedLimit': seg.speedLimit,
        'groupId':    seg.groupId,
    }


# ---------------------------------------------------------------------------
# Quick self-test (run standalone to check a live state file)
# ---------------------------------------------------------------------------
if __name__ == '__main__':
    import sys

    print("RailroaderBridge self-test")
    bridge = RailroaderBridge(game_dir=sys.argv[1] if len(sys.argv) > 1 else None)
    print(f"  Game dir:   {bridge.game_dir}")
    print(f"  State file: {bridge._state_file}")
    print(f"  Exists:     {bridge._state_file.exists()}")

    if bridge._state_file.exists():
        bridge._try_load_state()
        s = bridge.state
        if s:
            print(f"  Nodes:    {len(s.nodes)}")
            print(f"  Segments: {len(s.segments)}")
            print(f"  Cars:     {len(s.cars)}")
            print(f"  Map loaded: {s.is_map_loaded}")
            if s.nodes:
                n = s.nodes[0]
                print(f"  First node: {n.id}  pos=({n.x:.1f},{n.y:.1f},{n.z:.1f})")
    else:
        print("  State file not found — is Railroader running with TrackBridge installed?")

    # Test write_mixinto format
    test_nodes = [
        {'id': 'N_TEST_0001', 'position': {'x':100,'y':600,'z':200},
         'rotation': {'x':0,'y':45,'z':0}, 'flipSwitchStand': False},
        {'id': 'N_TEST_0002', 'position': {'x':200,'y':602,'z':300},
         'rotation': {'x':0,'y':45,'z':0}, 'flipSwitchStand': False},
    ]
    test_segs = [
        {'id': 'S_TEST_0001', 'startId': 'N_TEST_0001', 'endId': 'N_TEST_0002',
         'trackClass': 0, 'style': 0, 'priority': 0, 'speedLimit': 45},
    ]
    import tempfile, os
    with tempfile.NamedTemporaryFile(suffix='.json', delete=False, mode='w') as f:
        tmp = f.name
    write_mixinto(tmp, test_nodes, test_segs)
    data = json.loads(open(tmp).read())
    assert 'tracks' in data
    assert 'N_TEST_0001' in data['tracks']['nodes']
    assert 'S_TEST_0001' in data['tracks']['segments']
    os.unlink(tmp)
    print("  write_mixinto format: OK ✓")
    print()
    print("All self-tests passed.")
