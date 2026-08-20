"""mod_project.layer
Layer class — one JSON file inside a mod (or the base game file).
"""

import copy as _copy
import base64
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


TRACK_GAUGES = (
    'Standard',
    'Narrow',
    'DualGauge',
    'DualGauge_L',
    'DualGauge_R',
    'DualGauge_T',
)


def normalize_track_gauge(value) -> str:
    """Return the canonical NarrowGauge/FUSE metadata value.

    Unknown non-empty values are retained verbatim so a newer companion mod
    can round-trip through an older Tile Editor without losing metadata.
    """
    raw = str(value or '').strip()
    if not raw or raw.lower() in ('standard', 'std'):
        return 'Standard'
    aliases = {
        '3ft': 'Narrow',
        '3 ft': 'Narrow',
        'threefoot': 'Narrow',
        'three foot': 'Narrow',
        'dual': 'DualGauge',
        'mixed': 'DualGauge',
        'mixedgauge': 'DualGauge',
    }
    alias = aliases.get(raw.lower())
    if alias:
        return alias
    for gauge in TRACK_GAUGES:
        if raw.lower() == gauge.lower():
            return gauge
    return raw


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
        self.track_schema = 'legacy'
        self._original_track_node_ids = set()
        self._original_track_segment_ids = set()
        self._created_track_node_ids = set()
        self._created_track_segment_ids = set()

        # Parsed node/segment data for rendering
        # Each node: {id, x, y, z, rotY, flipSwitchStand, deleted}
        # deleted=True means this layer has null for this id (deletion patch)
        self.nodes:    Dict[str, dict] = {}
        # Each segment: {id, startId, endId, trackClass, style, speedLimit,
        #                priority, groupId, gauge, deleted}
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
        self.track_schema = self._detect_track_schema()
        tracks = self._raw.get('tracks') or {}
        self._original_track_node_ids = set(
            (tracks.get('nodes') or {}).keys()
        )
        self._original_track_segment_ids = set(
            (tracks.get('segments') or {}).keys()
        )
        self._created_track_node_ids.clear()
        self._created_track_segment_ids.clear()
        self._check_patch_operators()
        self._parse()

    def _detect_track_schema(self) -> str:
        tracks = self._raw.get('tracks') or {}
        segments = tracks.get('segments') or {}
        for segment in segments.values():
            if isinstance(segment, dict) and (
                    'startNodeId' in segment or 'endNodeId' in segment):
                return 'fuse'
        if (
            self.path.name.lower().endswith('.fuse.json')
            and ('schemaVersion' in self._raw or 'tracks' in self._raw)
        ):
            return 'fuse'
        return 'legacy'

    @property
    def is_fuse_native(self) -> bool:
        """Whether this layer is a native FUSE schema document."""
        return self.track_schema == 'fuse'

    def raw_collection(self, name: str, create: bool = False) -> dict:
        """Return the format-correct raw dictionary for an editor concept.

        The desktop editor keeps one format-neutral in-memory view, but native
        FUSE does not use RailLoader's root-level scenery/spliney/area blocks.
        All mutations must pass through this adapter so the format switch is a
        real serializer choice rather than a different file extension.
        """
        if not self.is_fuse_native:
            value = self._raw.get(name)
            if isinstance(value, dict):
                return value
            if not create:
                return {}
            value = {}
            self._raw[name] = value
            return value

        if name == 'areas':
            parent = self._raw.get('tracks')
            if not isinstance(parent, dict):
                if not create:
                    return {}
                parent = {}
                self._raw['tracks'] = parent
            value = parent.get('areas')
        elif name == 'loads':
            parent = self._raw.get('operations')
            if not isinstance(parent, dict):
                if not create:
                    return {}
                parent = {}
                self._raw['operations'] = parent
            value = parent.get('loads')
        elif name == 'industries':
            parent = self._raw.get('operations')
            if not isinstance(parent, dict):
                if not create:
                    return {}
                parent = {}
                self._raw['operations'] = parent
            value = parent.get('industries')
        else:
            native_names = {
                'scenery': 'scenery',
                'splineys': 'splineys',
                'mandelas': 'sceneClones',
                'texts': 'mapLabels',
                'simpleGraphs': None,
            }
            native_name = native_names.get(name, name)
            if native_name is None:
                extensions = self._raw.get('extensions')
                if not isinstance(extensions, dict):
                    if not create:
                        return {}
                    extensions = {}
                    self._raw['extensions'] = extensions
                value = extensions.get('simpleGraphs')
                parent = extensions
                native_name = 'simpleGraphs'
            else:
                parent = self._raw.get('world')
                if not isinstance(parent, dict):
                    if not create:
                        return {}
                    parent = {}
                    self._raw['world'] = parent
                value = parent.get(native_name)

        if isinstance(value, dict):
            return value
        if not create:
            return {}
        value = {}
        parent[native_name if name not in ('areas', 'loads', 'industries')
               else name] = value
        return value

    @staticmethod
    def scene_clone_id(target_path: str) -> str:
        encoded = base64.urlsafe_b64encode(
            str(target_path or '').strip().encode('utf-8')
        ).decode('ascii').rstrip('=')
        return f'scene-{encoded}'

    def find_scene_clone_key(self, target_path: str) -> Optional[str]:
        target_path = str(target_path or '').strip()
        if not self.is_fuse_native:
            return target_path if target_path in self.raw_collection('mandelas') else None
        for key, value in self.raw_collection('mandelas').items():
            if isinstance(value, dict) and str(value.get('targetPath', '')).strip() == target_path:
                return key
        return None

    def add_world_removal(self, kind: str, object_id: str):
        if not self.is_fuse_native or not object_id:
            return
        world = self._raw.setdefault('world', {})
        removals = world.setdefault('removals', {})
        values = removals.setdefault(kind, [])
        if object_id not in values:
            values.append(object_id)

    def remove_world_removal(self, kind: str, object_id: str):
        if not self.is_fuse_native or not object_id:
            return
        values = (((self._raw.get('world') or {}).get('removals') or {})
                  .get(kind))
        if isinstance(values, list):
            values[:] = [value for value in values if value != object_id]

    @staticmethod
    def spliney_for_editor(entry: dict) -> dict:
        result = _copy.deepcopy(entry or {})
        native_type = str(result.get('type', '')).strip().lower()
        if native_type == 'trestle':
            result.setdefault('handler', 'StrangeCustoms.AutoTrestleBuilder')
            if 'headStyle' in result:
                result.setdefault('headstyle', result['headStyle'])
            if 'tailStyle' in result:
                result.setdefault('tailstyle', result['tailStyle'])
        elif native_type:
            result.setdefault('handler', 'StrangeCustoms.FlowyThingBuilder')
            result.setdefault('style', 'River' if native_type in ('river', 'waterfall') else 'Road')
        return result

    @staticmethod
    def spliney_for_native(entry: dict) -> dict:
        source = _copy.deepcopy(entry or {})
        handler = str(source.pop('handler', source.pop('Handler', '')))
        native_type = str(source.get('type', '')).strip()
        if not native_type:
            if 'AutoTrestle' in handler:
                native_type = 'trestle'
            else:
                style = str(source.get('style', 'Road')).strip().lower()
                native_type = 'river' if style == 'river' else 'road'
        source['type'] = native_type
        if 'headstyle' in source:
            source['headStyle'] = source.pop('headstyle')
        if 'tailstyle' in source:
            source['tailStyle'] = source.pop('tailstyle')
        allowed = {
            'type', 'profile', 'style', 'offsetY', 'headStyle',
            'tailStyle', 'points',
        }
        return {key: value for key, value in source.items() if key in allowed}

    @staticmethod
    def scenery_for_editor(entry: dict) -> dict:
        result = _copy.deepcopy(entry or {})
        identifier = result.get('assetIdentifier', result.get('model', ''))
        if isinstance(identifier, str) and identifier.startswith('scenery://'):
            identifier = identifier[len('scenery://'):]
        result['modelIdentifier'] = identifier
        return result

    @staticmethod
    def scenery_for_native(entry: dict) -> dict:
        source = _copy.deepcopy(entry or {})
        identifier = str(source.pop(
            'modelIdentifier', source.pop('model', source.get('assetIdentifier', ''))
        ) or '').strip()
        if '://' not in identifier:
            identifier = 'scenery://' + identifier
        source['assetIdentifier'] = identifier
        allowed = {
            'assetIdentifier', 'position', 'rotation', 'scale',
            'anchorSpanIds',
        }
        return {key: value for key, value in source.items() if key in allowed}

    @staticmethod
    def mandela_for_editor(target_path: str, entry: dict) -> dict:
        result = _copy.deepcopy(entry or {})
        result.pop('targetPath', None)
        source = str(result.pop('source', '') or '')
        prefix = 'path://scene/'
        if source.lower().startswith(prefix):
            source = source[len(prefix):]
        if source:
            result['instantiateFrom'] = source
        return result

    @staticmethod
    def mandela_for_native(target_path: str, entry: dict) -> dict:
        source = _copy.deepcopy(entry or {})
        instantiate_from = str(source.pop('instantiateFrom', '') or '').strip()
        result = {'targetPath': str(target_path or '').strip()}
        if instantiate_from:
            result['source'] = (
                instantiate_from if '://' in instantiate_from
                else 'path://scene/' + instantiate_from
            )
        for key in ('enabled', 'localPosition', 'localRotation', 'localScale'):
            if key in source:
                result[key] = source[key]
        return result

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
        self.spans = _copy.deepcopy(dict(raw_spans))
        if self.is_fuse_native:
            self.splineys = {
                key: self.spliney_for_editor(value)
                for key, value in self.raw_collection('splineys').items()
                if isinstance(value, dict)
            }
            self.areas = _copy.deepcopy(self.raw_collection('areas'))
            self.scenery = {
                key: self.scenery_for_editor(value)
                for key, value in self.raw_collection('scenery').items()
                if isinstance(value, dict)
            }
            self.mandelas = {}
            for key, value in self.raw_collection('mandelas').items():
                if not isinstance(value, dict):
                    continue
                target_path = str(value.get('targetPath', '') or '').strip()
                if target_path:
                    self.mandelas[target_path] = self.mandela_for_editor(
                        target_path, value
                    )
            self.texts = _copy.deepcopy(self.raw_collection('texts'))
            self.simpleGraphs = _copy.deepcopy(
                self.raw_collection('simpleGraphs')
            )
            self.loads = _copy.deepcopy(self.raw_collection('loads'))
            self._merge_native_industries_into_areas()
        else:
            self.splineys = _copy.deepcopy(d.get('splineys', {}) or {})
            self.areas = _copy.deepcopy(d.get('areas', {}) or {})
            self.scenery = _copy.deepcopy(d.get('scenery', {}) or {})
            self.mandelas = _copy.deepcopy(d.get('mandelas', {}) or {})
            self.texts = _copy.deepcopy(d.get('texts', {}) or {})
            self.simpleGraphs = _copy.deepcopy(d.get('simpleGraphs', {}) or {})
            self.loads = _copy.deepcopy(d.get('loads', {}) or {})

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
                    'isDiamond': bool(v.get('isDiamond', v.get('IsDiamond', False))),
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
                    'startId':    v.get(
                        'startId',
                        v.get('StartId', v.get('startNodeId', ''))
                    ),
                    'endId':      v.get(
                        'endId',
                        v.get('EndId', v.get('endNodeId', ''))
                    ),
                    'trackClass': TRACK_CLASS_NAMES.get(tc_raw, 'Mainline'),
                    'style':      v.get('Style',      v.get('style',      'Standard')),
                    'speedLimit': int(v.get('speedLimit', v.get('SpeedLimit', 0))),
                    'priority':   int(v.get('priority',   v.get('Priority',   0))),
                    # groupId: real mods write "" for no group; null/absent also valid.
                    # Normalise to '' so set_segment round-trips cleanly.
                    'groupId':    v.get('groupId', v.get('GroupId')) or '',
                    'gauge':      normalize_track_gauge(
                        v.get('gauge', v.get('Gauge', 'Standard'))
                    ),
                    'bridgeSupportsSteel': bool(v.get('bridgeSupportsSteel', False)),
                    'yard': bool(v.get('yard', False)),
                }

    def _merge_native_industries_into_areas(self):
        """Expose native operations industries through the legacy-neutral UI."""
        for industry_id, industry in self.raw_collection('industries').items():
            if not isinstance(industry, dict):
                continue
            area_id = str(industry.get('areaId', '') or '').strip()
            if not area_id:
                continue
            area = self.areas.setdefault(area_id, {
                'name': area_id,
                'position': {'x': 0.0, 'y': 0.0, 'z': 0.0},
                'radius': 500.0,
                'order': 0,
                'tagColor': [0.5, 0.5, 0.5],
            })
            area_position = area.get('position') or {}
            position = industry.get('position') or {}
            local_position = {
                axis: float(position.get(axis, 0.0))
                      - float(area_position.get(axis, 0.0))
                for axis in ('x', 'y', 'z')
            }
            components = {}
            for component_id, component in (
                    industry.get('components') or {}).items():
                if not isinstance(component, dict):
                    continue
                editor_component = _copy.deepcopy(component)
                if 'trackSpanIds' in editor_component:
                    editor_component['trackSpans'] = editor_component.pop(
                        'trackSpanIds'
                    )
                components[component_id] = editor_component
            area.setdefault('industries', {})[industry_id] = {
                'name': industry.get('name', industry_id),
                'localPosition': local_position,
                'usesContract': bool(industry.get('usesContract', False)),
                'components': components,
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
        GAUGE_COLORS = {
            'Narrow':      (255, 122,  20),
            'DualGauge':   (110, 184, 255),
            'DualGauge_L': (110, 184, 255),
            'DualGauge_R': (110, 184, 255),
            'DualGauge_T': (245,  64, 210),
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
            gauge_col = GAUGE_COLORS.get(
                normalize_track_gauge(seg.get('gauge', 'Standard'))
            )
            if gauge_col is not None:
                color = gauge_col
            elif style_col is not None:
                color = style_col
            else:
                color = base_color
            self.curves.append((pts, color, seg['id']))

    # ------------------------------------------------------------------
    def set_node(self, nid: str, x: float, y: float, z: float,
                 rotX: float, rotY: float, rotZ: float,
                 flip: bool = False, is_diamond: Optional[bool] = None):
        """Add or update a node in this layer."""
        if self.read_only:
            return
        if (
            self.track_schema == 'fuse'
            and nid not in self._original_track_node_ids
            and nid not in ((self._raw.get('tracks') or {}).get('nodes') or {})
        ):
            self._created_track_node_ids.add(nid)
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
        existing_node = self.nodes.get(nid, {})
        existing_raw = (
            ((self._raw.get('tracks') or {}).get('nodes') or {}).get(nid)
        )
        if is_diamond is None:
            is_diamond = bool(existing_node.get('isDiamond', False))
            if isinstance(existing_raw, dict):
                is_diamond = bool(existing_raw.get(
                    'isDiamond', existing_raw.get('IsDiamond', is_diamond)
                ))
        self.nodes[nid] = {
            'id': nid, 'deleted': False,
            'x': x, 'y': y, 'z': z,
            'rotX': rotX, 'rotY': rotY, 'rotZ': rotZ,
            'flipSwitchStand': flip,
            'isDiamond': bool(is_diamond),
        }
        # Update raw
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'nodes' not in self._raw['tracks']: self._raw['tracks']['nodes'] = {}
        raw_node = (
            _copy.deepcopy(existing_raw)
            if isinstance(existing_raw, dict) else {}
        )
        raw_node.update({
            'position': {'x': x, 'y': y, 'z': z},
            'rotation': {'x': rotX, 'y': rotY, 'z': rotZ},
            'flipSwitchStand': flip,
            'isDiamond': bool(is_diamond),
        })
        raw_node.pop('IsDiamond', None)
        self._raw['tracks']['nodes'][nid] = raw_node
        self._remove_fuse_track_removal('nodes', nid)
        self.dirty = True

    def delete_node(self, nid: str):
        """Mark a node as deleted (writes null)."""
        if self.read_only:
            return
        self.nodes[nid] = {'id': nid, 'deleted': True, 'x': 0, 'y': 0, 'z': 0, 'rotY': 0}
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'nodes' not in self._raw['tracks']: self._raw['tracks']['nodes'] = {}
        if self.track_schema == 'fuse':
            self._raw['tracks']['nodes'].pop(nid, None)
            if nid in self._created_track_node_ids:
                self._created_track_node_ids.discard(nid)
            elif nid not in self._original_track_node_ids:
                self._add_fuse_track_removal('nodes', nid)
        else:
            self._raw['tracks']['nodes'][nid] = None
        self.dirty = True

    def set_segment(self, sid: str, start_id: str, end_id: str,
                    track_class: str = 'Mainline', style: str = 'Standard',
                    speed_limit: Optional[int] = 0, priority: int = 0,
                    group_id: str = '', gauge: Optional[str] = None,
                    bridge_supports_steel: Optional[bool] = None,
                    yard: Optional[bool] = None):
        if self.read_only:
            return
        if (
            self.track_schema == 'fuse'
            and sid not in self._original_track_segment_ids
            and sid not in (
                ((self._raw.get('tracks') or {}).get('segments') or {})
            )
        ):
            self._created_track_segment_ids.add(sid)
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
        # An omitted gauge means "keep what this segment already has". This
        # makes every pre-gauge editor action safe without forcing all callers
        # to understand companion-mod metadata.
        existing_segment = self.segments.get(sid, {})
        existing_raw = (
            ((self._raw.get('tracks') or {}).get('segments') or {}).get(sid)
        )
        if gauge is None:
            gauge = existing_segment.get('gauge')
            if gauge is None and isinstance(existing_raw, dict):
                gauge = existing_raw.get('gauge', existing_raw.get('Gauge'))
        gauge = normalize_track_gauge(gauge)
        if bridge_supports_steel is None:
            bridge_supports_steel = bool(existing_segment.get(
                'bridgeSupportsSteel', False
            ))
            if isinstance(existing_raw, dict):
                bridge_supports_steel = bool(existing_raw.get(
                    'bridgeSupportsSteel', bridge_supports_steel
                ))
        if yard is None:
            yard = bool(existing_segment.get('yard', False))
            if isinstance(existing_raw, dict):
                yard = bool(existing_raw.get('yard', yard))
        self.segments[sid] = {
            'id': sid, 'deleted': False,
            'startId': start_id, 'endId': end_id,
            'trackClass': track_class, 'style': style_norm,
            'speedLimit': speed_limit, 'priority': priority,
            'groupId': group_id, 'gauge': gauge,
            'bridgeSupportsSteel': bool(bridge_supports_steel),
            'yard': bool(yard),
        }
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'segments' not in self._raw['tracks']: self._raw['tracks']['segments'] = {}
        # Update a copy instead of replacing the object. Gauge, tags, and
        # future FUSE/companion fields must survive routine property edits.
        raw_segment = (
            _copy.deepcopy(existing_raw)
            if isinstance(existing_raw, dict) else {}
        )
        if self.track_schema == 'fuse':
            fuse_track_classes = {
                'Mainline': 'main',
                'Branch': 'branch',
                'Industrial': 'industrial',
            }
            raw_segment.update({
                'startNodeId': start_id,
                'endNodeId': end_id,
                'trackClass': fuse_track_classes.get(
                    track_class, str(track_class).lower()
                ),
                'style': style_norm.lower(),
                'speedLimit': speed_limit,
                'priority': priority,
                'groupId': group_id,
                'gauge': gauge,
                'bridgeSupportsSteel': bool(bridge_supports_steel),
                'yard': bool(yard),
            })
            for legacy_key in (
                    'startId', 'endId', 'StartId', 'EndId', 'Style'):
                raw_segment.pop(legacy_key, None)
        else:
            raw_segment.update({
                'startId':    start_id,
                'endId':      end_id,
                'trackClass': TRACK_CLASS_JSON.get(track_class, track_class),
                'Style':      style_norm,
                'speedLimit': speed_limit,
                'priority':   priority,
                'groupId':    group_id,
                'gauge':      gauge,
            })
        raw_segment.pop('Gauge', None)
        self._raw['tracks']['segments'][sid] = raw_segment
        self._remove_fuse_track_removal('segments', sid)
        self.dirty = True

    def delete_segment(self, sid: str):
        if self.read_only:
            return
        self.segments[sid] = {'id': sid, 'deleted': True, 'startId': '', 'endId': ''}
        if 'tracks' not in self._raw: self._raw['tracks'] = {}
        if 'segments' not in self._raw['tracks']: self._raw['tracks']['segments'] = {}
        if self.track_schema == 'fuse':
            self._raw['tracks']['segments'].pop(sid, None)
            if sid in self._created_track_segment_ids:
                self._created_track_segment_ids.discard(sid)
            elif sid not in self._original_track_segment_ids:
                self._add_fuse_track_removal('segments', sid)
        else:
            self._raw['tracks']['segments'][sid] = None
        self.dirty = True

    def _add_fuse_track_removal(self, kind: str, object_id: str):
        if self.track_schema != 'fuse' or not object_id:
            return
        tracks = self._raw.setdefault('tracks', {})
        removals = tracks.setdefault('removals', {})
        values = removals.setdefault(kind, [])
        if object_id not in values:
            values.append(object_id)

    def _remove_fuse_track_removal(self, kind: str, object_id: str):
        if self.track_schema != 'fuse' or not object_id:
            return
        removals = (self._raw.get('tracks') or {}).get('removals') or {}
        values = removals.get(kind)
        if isinstance(values, list):
            removals[kind] = [
                value for value in values if value != object_id
            ]

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
            if self.track_schema == 'fuse':
                if not str(seg.get('groupId') or '').strip():
                    seg.pop('groupId', None)
                if 'gauge' in seg:
                    seg['gauge'] = normalize_track_gauge(seg['gauge'])
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
