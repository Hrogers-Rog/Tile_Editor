"""mod_project.layer
Layer class — one JSON file inside a mod (or the base game file).
"""

import copy as _copy
import json
import re
import shutil
from pathlib import Path
from typing import Dict, List, Tuple, Optional

from .constants import (
    LAYER_BASE, LAYER_GRAPH, LAYER_TOWN, LAYER_RIVERS, LAYER_MIGRATION, LAYER_OTHER,
    LAYER_COLORS, TOWN_PALETTE,
    TRACK_CLASS_NAMES, TRACK_CLASS_JSON, TRACK_STYLES,
    _GAUSS_T, _GAUSS_C,
)
from .geometry import _bezier_for_nodes


def _load_json(path: Path) -> dict:
    """Load JSON tolerantly: strips C-style comments and trailing commas."""
    text = path.read_text(encoding='utf-8-sig')

    # Remove // comments that are NOT inside strings
    result = []
    i = 0
    in_str = False
    while i < len(text):
        c = text[i]
        if in_str:
            result.append(c)
            if c == '\\' and i + 1 < len(text):
                result.append(text[i + 1])
                i += 2
                continue
            if c == '"':
                in_str = False
        else:
            if c == '"':
                in_str = True
                result.append(c)
            elif c == '/' and i + 1 < len(text) and text[i + 1] == '/':
                while i < len(text) and text[i] != '\n':
                    i += 1
                continue
            else:
                result.append(c)
        i += 1
    text = ''.join(result)

    # Strip trailing commas before } or ]
    text = re.sub(r',(\s*[}\]])', r'\1', text)

    # D7: detect duplicate JSON keys -- game rejects them with an error
    _seen_keys: list = []

    def _detect_duplicates(pairs):
        d = {}
        for k, v in pairs:
            if k in d:
                print(f"[load_json] WARNING: duplicate key '{k}' in {path.name} -- "
                      f"Railloader would reject this file (DuplicatePropertyNameHandling.Error)")
            d[k] = v
        return d

    return json.loads(text, object_pairs_hook=_detect_duplicates)




def _save_json(path: Path, data: dict, indent: int = 2):
    """Save JSON with consistent formatting."""
    path.write_text(json.dumps(data, indent=indent, ensure_ascii=False), encoding='utf-8')


# ---------------------------------------------------------------------------
# Layer class
# ---------------------------------------------------------------------------
class Layer:
    def __init__(self, path: Path, layer_type: str, color: tuple,
                 label: str, visible: bool = True, read_only: bool = False):
        self.path        = path
        self.layer_type  = layer_type
        self.color       = color
        self.label       = label
        self.visible     = visible
        self.read_only   = read_only   # if True: never write edits or save
        self.dirty       = False
        self.owner_project = None

        # Raw JSON data (what gets saved)
        self._raw: dict  = {}

        # Parsed node/segment data for rendering
        # Each node: {id, x, y, z, rotY, flipSwitchStand, deleted}
        # deleted=True means this layer has null for this id (deletion patch)
        self.nodes:    Dict[str, dict] = {}
        # Each segment: {id, startId, endId, trackClass, style, speedLimit,
        #                priority, groupId, deleted}
        self.segments: Dict[str, dict] = {}
        # Splineys (rivers, roads, trestles, labels, turntables)
        self.splineys: Dict[str, dict] = {}
        # Areas (towns with industries)
        self.areas:    Dict[str, dict] = {}
        # Spans
        self.spans:    Dict[str, dict] = {}
        # Scenery
        self.scenery:  Dict[str, dict] = {}
        # Mandelas (prefab instances)
        self.mandelas: Dict[str, dict] = {}
        # Texts (map labels stored in game-graph format) -- values are plain strings
        self.texts:         Dict[str, str] = {}
        # SimpleGraphs (AI path graphs)
        self.simpleGraphs:  Dict[str, dict] = {}
        # Load definitions (custom cargo types) — A18
        self.loads:         Dict[str, dict] = {}

        # Pre-computed bezier curves for rendering
        # list of (pts, color, segment_id)
        self.curves: List[Tuple[list, tuple, str]] = []

    # ------------------------------------------------------------------
    def load(self):
        try:
            self._raw = _load_json(self.path)
        except Exception as e:
            print(f"[layer] failed to load {self.path.name}: {e}")
            self._raw = {}
        self._check_patch_operators()
        self._parse()

    def _check_patch_operators(self):
        """Warn if this file uses StrangeCustoms patch operators ($find, $replace, etc.).

        B8: Migration files use operators like $find/$replace/$add that our _parse does
        not interpret -- we load the raw JSON verbatim, which is correct for read-only
        display, but any write-back would strip operator intent.  Emit a warning so the
        caller knows this layer should not be written back via save().
        """
        import json as _json
        text = _json.dumps(self._raw)
        OPERATORS = ('$find', '$replace', '$remove', '$add', '$append',
                     '$clone', '$optional', '$moveTo')
        found = [op for op in OPERATORS if op in text]
        if found:
            print(f"[layer] WARNING: {self.path.name} uses patch operators "
                  f"{found} -- this layer is read-only; do not write back.")

    def _parse(self):
        d = self._raw
        tracks   = d.get('tracks',   {}) or {}
        raw_nodes = tracks.get('nodes', {}) or {}
        raw_segs  = tracks.get('segments', {}) or {}
        raw_spans = tracks.get('spans', {}) or {}

        self.nodes    = {}
        self.segments = {}
        self.spans    = _copy.deepcopy(dict(raw_spans))
        self.splineys = _copy.deepcopy(d.get('splineys', {}) or {})
        self.areas    = _copy.deepcopy(d.get('areas', {}) or {})
        self.scenery  = _copy.deepcopy(d.get('scenery', {}) or {})
        self.mandelas     = _copy.deepcopy(d.get('mandelas', {}) or {})
        self.texts        = _copy.deepcopy(d.get('texts', {}) or {})
        self.simpleGraphs = _copy.deepcopy(d.get('simpleGraphs', {}) or {})
        self.loads        = _copy.deepcopy(d.get('loads', {}) or {})

        for nid, v in raw_nodes.items():
            if v is None:
                self.nodes[nid] = {'id': nid, 'deleted': True,
                                   'x': 0, 'y': 0, 'z': 0, 'rotY': 0}
            else:
                pos = v.get('position', {})
                rot = v.get('rotation', {})
                self.nodes[nid] = {
                    'id':            nid,
                    'deleted':       False,
                    'x':             float(pos.get('x', 0)),
                    'y':             float(pos.get('y', 0)),
                    'z':             float(pos.get('z', 0)),
                    'rotX':          float(rot.get('x', 0)),
                    'rotY':          float(rot.get('y', 0)),
                    'rotZ':          float(rot.get('z', 0)),
                    'flipSwitchStand': bool(v.get('flipSwitchStand', False)),
                }

        for sid, v in raw_segs.items():
            if v is None:
                self.segments[sid] = {'id': sid, 'deleted': True,
                                      'startId': '', 'endId': ''}
            else:
                tc_raw = v.get('trackClass', v.get('TrackClass', 'Mainline'))
                self.segments[sid] = {
                    'id':         sid,
                    'deleted':    False,
                    'startId':    v.get('startId', v.get('StartId', '')),
                    'endId':      v.get('endId',   v.get('EndId',   '')),
                    'trackClass': TRACK_CLASS_NAMES.get(tc_raw, 'Mainline'),
                    'style':      v.get('Style',      v.get('style',      'Standard')),
                    'speedLimit': int(v.get('speedLimit', v.get('SpeedLimit', 0))),
                    'priority':   int(v.get('priority',   v.get('Priority',   0))),
                    # groupId: real mods write "" for no group; null/absent also valid.
                    # Normalise to '' so set_segment round-trips cleanly.
                    'groupId':    v.get('groupId', v.get('GroupId')) or '',
                }

    def rebuild_curves(self, all_nodes: dict):
        """Rebuild bezier curves using the merged node positions."""
        self.curves = []
        TRACK_COLORS = {
            'Mainline':   (255, 230,  50),
            'Branch':     (255, 120,   0),
            'Industrial': (200,  80, 255),
        }
        # Style overrides — each style gets its own distinct color
        STYLE_COLORS = {
            'Standard': None,                # use track class color as-is
            'Yard':     (160,  90,  30),     # brown
            'Bridge':   ( 80, 160, 255),     # blue
            'Tunnel':   (220,  50,  50),     # red
        }
        for seg in self.segments.values():
            if seg['deleted']:
                continue
            n0 = all_nodes.get(seg['startId'])
            n1 = all_nodes.get(seg['endId'])
            if not n0 or not n1:
                continue
            if n0.get('deleted') or n1.get('deleted'):
                continue
            pts        = _bezier_for_nodes(n0, n1)
            base_color = TRACK_COLORS.get(seg['trackClass'], (220, 220, 100))
            style_key  = (seg.get('style') or 'Standard').capitalize()
            style_col  = STYLE_COLORS.get(style_key)
            if style_col is not None:
                color = style_col
            else:
                color = base_color
            self.curves.append((pts, color, seg['id']))

    # ------------------------------------------------------------------
    def set_node(self, nid: str, x: float, y: float, z: float,
                 rotX: float, rotY: float, rotZ: float,
                 flip: bool = False):
        """Add or update a node in this layer."""
        if self.read_only:
            return
        import math as _m
        # Guard against NaN/Inf coordinates which crash the game physics
        def _safe(v, default=0.0):
            try:
                f = float(v)
                return default if (_m.isnan(f) or _m.isinf(f)) else f
            except (TypeError, ValueError):
                return default
        x = _safe(x); y = _safe(y, 0.0); z = _safe(z)
        rotX = _safe(rotX); rotY = _safe(rotY) % 360; rotZ = _safe(rotZ)
        # Clamp Y to reasonable terrain range (prevents nodes at sea level or underground)
        if y == 0.0:
            y = 0.0  # allow 0 as valid (sea level maps exist)
        self.nodes[nid] = {
            'id': nid, 'deleted': False,
            'x': x, 'y': y, 'z': z,
            'rotX': rotX, 'rotY': rotY, 'rotZ': rotZ,
            'flipSwitchStand': flip,
        }
        # Update raw
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'nodes' not in self._raw['tracks']: self._raw['tracks']['nodes'] = {}
        self._raw['tracks']['nodes'][nid] = {
            'position': {'x': x, 'y': y, 'z': z},
            'rotation': {'x': rotX, 'y': rotY, 'z': rotZ},
            'flipSwitchStand': flip,
        }
        self.dirty = True

    def delete_node(self, nid: str):
        """Mark a node as deleted (writes null)."""
        if self.read_only:
            return
        self.nodes[nid] = {'id': nid, 'deleted': True, 'x': 0, 'y': 0, 'z': 0, 'rotY': 0}
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'nodes' not in self._raw['tracks']: self._raw['tracks']['nodes'] = {}
        self._raw['tracks']['nodes'][nid] = None
        self.dirty = True

    def set_segment(self, sid: str, start_id: str, end_id: str,
                    track_class: str = 'Mainline', style: str = 'Standard',
                    speed_limit: Optional[int] = 0, priority: int = 0, group_id: str = ''):
        if self.read_only:
            return
        # Guard: never create a self-loop
        if start_id == end_id:
            print(f"[layer] WARNING: refused self-loop segment {sid} ({start_id} -> {end_id})")
            return
        # Guard: never create a duplicate connection between the same pair of nodes
        new_pair = tuple(sorted([start_id, end_id]))
        for existing_sid, existing_seg in self.segments.items():
            if existing_seg.get('deleted'):
                continue
            if existing_sid == sid:
                continue  # updating an existing segment is fine
            ep = tuple(sorted([existing_seg.get('startId',''), existing_seg.get('endId','')]))
            if ep == new_pair:
                print(f"[layer] WARNING: refused duplicate segment {sid} ({start_id} -> {end_id}), already have {existing_sid}")
                return
        # Normalize trackClass -- game only has Mainline/Branch/Industrial
        track_class = TRACK_CLASS_NAMES.get(track_class, 'Mainline')
        # Normalize style: accept any case -> 'Standard'/'Yard'/'Bridge'/'Tunnel'
        style_norm = style.capitalize() if style else 'Standard'
        if style_norm not in TRACK_STYLES:
            style_norm = 'Standard'
        # Normalise groupId: None -> '' for consistency; real mods always write "groupId": ""
        group_id = group_id or ''
        # speed_limit=None means preserve the existing value (B1 -- don't overwrite a
        # hard-coded speed when patching only trackClass/style on an existing segment)
        if speed_limit is None:
            existing = self.segments.get(sid, {})
            speed_limit = existing.get('speedLimit', 0)
        self.segments[sid] = {
            'id': sid, 'deleted': False,
            'startId': start_id, 'endId': end_id,
            'trackClass': track_class, 'style': style_norm,
            'speedLimit': speed_limit, 'priority': priority,
            'groupId': group_id,
        }
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'segments' not in self._raw['tracks']: self._raw['tracks']['segments'] = {}
        self._raw['tracks']['segments'][sid] = {
            'startId':    start_id,
            'endId':      end_id,
            'trackClass': TRACK_CLASS_JSON.get(track_class, track_class),
            'Style':      style_norm,
            'speedLimit': speed_limit,
            'priority':   priority,
            'groupId':    group_id,
        }
        self.dirty = True

    def delete_segment(self, sid: str):
        if self.read_only:
            return
        self.segments[sid] = {'id': sid, 'deleted': True, 'startId': '', 'endId': ''}
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'segments' not in self._raw['tracks']: self._raw['tracks']['segments'] = {}
        self._raw['tracks']['segments'][sid] = None
        self.dirty = True

    def _normalize_raw(self):
        """Ensure _raw has correct JSON field names before writing.

        Normalizes trackClass values that may have been injected directly
        into _raw without going through set_segment (e.g. from external tools
        or patched data with non-standard casing).

        Also canonicalizes PascalCase segment field variants (StartId, EndId,
        SpeedLimit, Priority, GroupId) to the camelCase form the game expects,
        so a file written by an external editor round-trips correctly.
        """
        segs = (self._raw.get('tracks') or {}).get('segments') or {}
        for seg in segs.values():
            if not isinstance(seg, dict):
                continue
            # PascalCase -> camelCase field renames (A20)
            for pascal, camel in (('StartId', 'startId'), ('EndId', 'endId'),
                                  ('SpeedLimit', 'speedLimit'), ('Priority', 'priority'),
                                  ('GroupId', 'groupId')):
                if pascal in seg and camel not in seg:
                    seg[camel] = seg.pop(pascal)
                elif pascal in seg:
                    seg.pop(pascal)  # drop duplicate PascalCase key
            # TrackClass value normalisation
            tc = seg.get('trackClass')
            if tc in TRACK_CLASS_JSON:
                seg['trackClass'] = TRACK_CLASS_JSON[tc]
            # Style casing
            style = seg.get('Style', seg.get('style'))
            if style:
                style_norm = str(style).capitalize()
                if style_norm not in TRACK_STYLES:
                    style_norm = 'Standard'
                seg.pop('style', None)   # remove lowercase key if present
                seg['Style'] = style_norm
            # groupId: normalise null/absent -> '' to match real mod convention (A7)
            if 'groupId' not in seg:
                seg['groupId'] = ''
            elif seg['groupId'] is None:
                seg['groupId'] = ''

    def save(self, force: bool = False):
        if self.read_only or not self.dirty:
            return False
        owner = getattr(self, 'owner_project', None)
        if owner is not None and getattr(owner, 'defer_writes', False) and not force:
            pending = getattr(owner, '_pending_save_paths', None)
            if isinstance(pending, set):
                pending.add(str(self.path))
            return False
        self._normalize_raw()
        # Strip empty top-level collection keys before writing.
        # An empty {"texts": {}} in a mixinto shadows (clears) the base-game
        # texts for that key on hot-reload -- only write keys that have content.
        _OMIT_IF_EMPTY = ('texts', 'areas', 'mandelas', 'scenery', 'splineys',
                          'simpleGraphs', 'loads')
        raw_to_write = dict(self._raw)
        for key in _OMIT_IF_EMPTY:
            if key in raw_to_write and not raw_to_write[key]:
                del raw_to_write[key]
        # Same for nested tracks sub-keys
        if 'tracks' in raw_to_write:
            tracks = dict(raw_to_write['tracks'])
            for sub in ('spans',):
                if sub in tracks and not tracks[sub]:
                    del tracks[sub]
            raw_to_write['tracks'] = tracks
        # Rotate backup: keep one .bak that always reflects the last good save.
        # Previously only the very first save created a backup, so after the
        # second save the only recovery point was overwritten.
        bak = self.path.with_suffix('.json.bak')
        if self.path.exists():
            shutil.copy2(self.path, bak)
        _save_json(self.path, raw_to_write)
        self.dirty = False
        if owner is not None:
            pending = getattr(owner, '_pending_save_paths', None)
            if isinstance(pending, set):
                pending.discard(str(self.path))
        print(f"[layer] saved {self.path.name}")
        return True

