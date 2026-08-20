"""mod_project.project
ModProject — loads, merges, and saves a full mod folder or base game file.
"""

import copy as _copy
import json
import math
import re
import shutil
from pathlib import Path
from typing import Dict, List, Optional

from .constants import (
    LAYER_BASE, LAYER_GRAPH, LAYER_TOWN, LAYER_RIVERS, LAYER_MIGRATION, LAYER_OTHER,
    LAYER_COLORS, TOWN_PALETTE, _rand_chars,
)
from .layer import Layer, _load_json, _save_json


class ModProject:
    def __init__(self):
        self.name:        str          = "Untitled"
        self.folder:      Optional[Path] = None
        self.is_base_game: bool        = False
        self.layers:      List[Layer]  = []
        self.definition:  dict         = {}
        self.sources:     List[dict]   = []
        self.defer_writes: bool        = False
        self._pending_save_paths: set[str] = set()

        # Merged view -- result of applying all visible layers in order
        # These are what edit_tiles.py renders
        self.merged_nodes:        Dict[str, dict] = {}
        self.merged_segments:     Dict[str, dict] = {}
        self.merged_splineys:     Dict[str, dict] = {}
        self.merged_scenery:      Dict[str, dict] = {}
        self.merged_mandelas:     Dict[str, dict] = {}
        self.merged_areas:        Dict[str, dict] = {}
        self.merged_texts:        Dict[str, dict] = {}
        self.merged_simplegraphs: Dict[str, dict] = {}
        self.merged_loads:        Dict[str, dict] = {}

        # Active layer index for editing
        self.active_layer_idx: int = 0

        # Selected node/segment ids
        self.selected_node_id:    Optional[str] = None
        self.selected_segment_id: Optional[str] = None

    @property
    def active_layer(self) -> Optional[Layer]:
        if 0 <= self.active_layer_idx < len(self.layers):
            return self.layers[self.active_layer_idx]
        return None

    @property
    def dirty(self) -> bool:
        return any(l.dirty for l in self.layers)

    def _add_source(self, folder: Optional[Path], name: str,
                    definition: Optional[dict] = None,
                    definition_path: Optional[Path] = None,
                    is_base_game: bool = False) -> int:
        source = {
            'folder': Path(folder) if folder else None,
            'name': name or "Untitled",
            'definition': definition if definition is not None else {},
            'definition_path': Path(definition_path) if definition_path else None,
            'is_base_game': bool(is_base_game),
        }
        self.sources.append(source)
        return len(self.sources) - 1

    def _source_for_layer(self, layer: Optional[Layer]) -> Optional[dict]:
        if layer is None:
            return None
        src_idx = getattr(layer, 'source_idx', None)
        if src_idx is None or not (0 <= src_idx < len(self.sources)):
            return None
        return self.sources[src_idx]

    def _default_edit_source(self) -> Optional[dict]:
        for source in self.sources:
            if not source.get('is_base_game'):
                return source
        return self.sources[0] if self.sources else None

    @property
    def active_source(self) -> Optional[dict]:
        active = self.active_layer
        source = self._source_for_layer(active)
        if source and not source.get('is_base_game'):
            return source
        return self._default_edit_source() or source

    def _tag_layer_source(self, layer: Layer, source_idx: int):
        layer.owner_project = self
        layer.source_idx = source_idx
        layer.base_label = getattr(layer, 'base_label', layer.label)
        source = self.sources[source_idx] if 0 <= source_idx < len(self.sources) else None
        layer.source_name = source.get('name') if source else ''
        layer.source_folder = source.get('folder') if source else None
        layer.source_is_base_game = bool(source.get('is_base_game')) if source else False

    def _refresh_layer_labels(self):
        multi_source = len(self.sources) > 1
        for layer in self.layers:
            base_label = getattr(layer, 'base_label', layer.label)
            source = self._source_for_layer(layer)
            source_name = source.get('name') if source else ''
            if multi_source and source_name:
                layer.label = f"{source_name}: {base_label}"
            else:
                layer.label = base_label

    def _refresh_workspace_name(self):
        if not self.sources:
            return
        mod_sources = [src for src in self.sources if not src.get('is_base_game')]
        if mod_sources:
            primary = mod_sources[0].get('name') or "Untitled"
            extra = len(mod_sources) - 1
            self.name = primary if extra <= 0 else f"{primary} + {extra} mod(s)"
        else:
            self.name = self.sources[0].get('name') or "Untitled"

    def _sync_active_source(self):
        source = self.active_source
        if source is None:
            return
        self.folder = source.get('folder')
        self.definition = source.get('definition') or {}
        self.is_base_game = bool(source.get('is_base_game'))

    def set_active_layer(self, idx: int):
        if 0 <= idx < len(self.layers):
            self.active_layer_idx = idx
            self._sync_active_source()

    @classmethod
    def open_mod_folders(cls, folders) -> 'ModProject':
        folder_list = []
        seen = set()
        for folder in folders or []:
            path = Path(folder)
            key = str(path.resolve()) if path.exists() else str(path)
            if key in seen:
                continue
            seen.add(key)
            folder_list.append(path)
        if not folder_list:
            raise ValueError("No mod folders selected")
        proj = cls.open_mod_folder(folder_list[0])
        for folder in folder_list[1:]:
            proj.append_mod_folder(folder, make_active=False)
        proj._refresh_workspace_name()
        proj._refresh_layer_labels()
        proj._sync_active_source()
        return proj

    def append_mod_folder(self, folder, make_active: bool = True):
        folder = Path(folder)
        for source in self.sources:
            src_folder = source.get('folder')
            if src_folder and src_folder.resolve() == folder.resolve():
                raise ValueError(f"Mod already loaded: {folder}")

        other = ModProject.open_mod_folder(folder)
        source_offset = len(self.sources)
        for source in other.sources:
            copied_source = {
                'folder': source.get('folder'),
                'name': source.get('name'),
                'definition': _copy.deepcopy(source.get('definition') or {}),
                'definition_path': source.get('definition_path'),
                'is_base_game': bool(source.get('is_base_game')),
            }
            self.sources.append(copied_source)

        start_idx = len(self.layers)
        for layer in other.layers:
            layer_source_idx = getattr(layer, 'source_idx', 0) + source_offset
            self._tag_layer_source(layer, layer_source_idx)
            self.layers.append(layer)

        self._refresh_workspace_name()
        self._refresh_layer_labels()
        self._rebuild_merge()
        if make_active and start_idx < len(self.layers):
            preferred = next((i for i in range(start_idx, len(self.layers))
                              if self.layers[i].layer_type == LAYER_GRAPH), start_idx)
            self.set_active_layer(preferred)
        else:
            self._sync_active_source()
        return len(other.layers)

    # ------------------------------------------------------------------
    @classmethod
    def open_base_game(cls, path: Path) -> 'ModProject':
        """Load the base game graph-data.json as a single-layer project."""
        proj = cls()
        source_name = f"Base Game -- {path.name}"
        source_idx = proj._add_source(path.parent, source_name, {}, None, is_base_game=True)
        proj.name = source_name
        layer = Layer(path, LAYER_BASE, LAYER_COLORS[LAYER_BASE],
                      path.name, visible=True)
        layer.load()
        proj._tag_layer_source(layer, source_idx)
        proj.layers.append(layer)
        proj._rebuild_merge()
        proj._refresh_layer_labels()
        proj._sync_active_source()
        return proj

    def add_base_graph(self, path: Path):
        """Add a read-only base game graph as an underlay to the current mod project.
        The base graph is always inserted at index 0 (bottom of the layer stack)
        so it renders beneath mod layers and can never be edited or saved."""
        # Remove any existing base graph layers first to avoid duplicates
        self.layers = [l for l in self.layers if l.layer_type != LAYER_BASE]
        self.sources = [s for s in self.sources if not s.get('is_base_game')]
        source_idx = self._add_source(path.parent, f"Base Game -- {path.name}", {}, None, is_base_game=True)
        label = f"[READ-ONLY] {path.name}"
        layer = Layer(path, LAYER_BASE, LAYER_COLORS[LAYER_BASE],
                      label, visible=True, read_only=True)
        layer.load()
        self._tag_layer_source(layer, source_idx)
        # Insert at bottom of stack so mod layers always override it
        self.layers.insert(0, layer)
        # Keep active layer pointing at the mod graph layer
        for i, l in enumerate(self.layers):
            if l.layer_type == LAYER_GRAPH:
                self.set_active_layer(i)
                break
        self._rebuild_merge()
        self._refresh_workspace_name()
        self._refresh_layer_labels()
        self._sync_active_source()

    @classmethod
    def open_mod_folder(cls, folder) -> "ModProject":
        """Load a mod folder by parsing Definition.json and all referenced files."""
        from pathlib import Path
        folder = Path(folder)
        proj = cls()

        def_path  = folder / 'Definition.json'
        info_path = folder / 'Info.json'

        # Prefer Info.json (UMM) over Definition.json (Railloader)
        if not def_path.exists() and not info_path.exists():
            candidates = (list(folder.glob('definition.json')) +
                          list(folder.glob('Definition.json')) +
                          list(folder.glob('Info.json')) +
                          list(folder.glob('info.json')))
            if candidates:
                p = candidates[0]
                if p.name.lower() == 'info.json':
                    info_path = p
                else:
                    def_path = p

        active_def = info_path if info_path.exists() else def_path
        if active_def.exists():
            try:
                proj.definition = _load_json(active_def)
            except Exception as e:
                print(f"[mod] failed to parse {active_def.name}: {e}")
                proj.definition = {}

        # UMM uses DisplayName/Id; Railloader uses name/id
        source_name = (proj.definition.get('DisplayName') or
                       proj.definition.get('name') or
                       proj.definition.get('Id') or
                       proj.definition.get('id') or
                       folder.name)
        proj.name = source_name
        source_idx = proj._add_source(folder, source_name, proj.definition, active_def if active_def.exists() else None)

        # D9: Railloader's StringsAreArraysTooConverter accepts a bare string
        # where string[] is expected. Normalise Railloader fields to list.
        # UMM uses 'Requirements' (string[]) -- normalise that too.
        for _field in ('requires', 'loadBefore', 'conflictsWith'):
            v = proj.definition.get(_field)
            if isinstance(v, str):
                proj.definition[_field] = [{'id': v}]
            elif isinstance(v, list):
                # D8: parse compact ModReference form "1.0<ModId<2.0"
                normalised = []
                for ref in v:
                    if isinstance(ref, str):
                        parts = ref.split('<')
                        if len(parts) == 1:
                            normalised.append({'id': parts[0].strip()})
                        elif len(parts) == 2:
                            normalised.append({'id': parts[1].strip(),
                                               'notBefore': parts[0].strip()})
                        elif len(parts) == 3:
                            normalised.append({'id': parts[1].strip(),
                                               'notBefore': parts[0].strip(),
                                               'notAfter':  parts[2].strip()})
                        else:
                            normalised.append({'id': ref})
                    elif isinstance(ref, dict):
                        normalised.append(ref)
                proj.definition[_field] = normalised
        # UMM Requirements is a plain string list -- leave as-is, it's not
        # a ModReference array so no version-range parsing needed.

        # Collect all files referenced in mixintos
        # ordered_files: list of (filename, target, conflicts_with) triples
        # conflicts_with is a list of mod ID strings or None
        ordered_files = []
        mixintos = proj.definition.get('mixintos', {})
        for target, entries in mixintos.items():
            if isinstance(entries, str):
                entries = [entries]
            if not isinstance(entries, list):
                entries = [entries]
            for entry in entries:
                fname = None
                # D10: capture per-entry conflictsWith
                entry_conflicts = None
                if isinstance(entry, str):
                    m = re.match(r'file\((.+?)\)', entry)
                    if m:
                        fname = m.group(1)
                    else:
                        # C15: dir() mixinto -- expand directory to all .json files within
                        m = re.match(r'dir\((.+?)\)', entry)
                        if m:
                            dir_rel = m.group(1)
                            dir_path = folder / dir_rel
                            if dir_path.is_dir():
                                for jp in sorted(dir_path.glob('*.json')):
                                    if not jp.name.endswith('.json.bak'):
                                        ordered_files.append((str(jp.relative_to(folder)), target, None))
                            else:
                                print(f"[mod] dir() mixinto path not found: {dir_rel}")
                            continue
                elif isinstance(entry, dict):
                    ref = entry.get('mixinto', '')
                    # D10: preserve conflictsWith from the mixinto entry dict
                    raw_conflicts = entry.get('conflictsWith')
                    if isinstance(raw_conflicts, str):
                        entry_conflicts = [raw_conflicts]
                    elif isinstance(raw_conflicts, list):
                        entry_conflicts = raw_conflicts
                    m = re.match(r'file\((.+?)\)', ref)
                    if m:
                        fname = m.group(1)
                    else:
                        m = re.match(r'dir\((.+?)\)', ref)
                        if m:
                            dir_rel = m.group(1)
                            dir_path = folder / dir_rel
                            if dir_path.is_dir():
                                for jp in sorted(dir_path.glob('*.json')):
                                    if not jp.name.endswith('.json.bak'):
                                        ordered_files.append((str(jp.relative_to(folder)), target, entry_conflicts))
                            else:
                                print(f"[mod] dir() mixinto path not found: {dir_rel}")
                            continue
                if fname:
                    ordered_files.append((fname, target, entry_conflicts))

        # Native FUSE packages list schema fragments directly in Info.json.
        # Load them in manifest order; track-bearing fragments are promoted to
        # editable graph layers below.
        fuse_data_files = proj.definition.get('FuseDataFiles', [])
        if isinstance(fuse_data_files, str):
            fuse_data_files = [fuse_data_files]
        for fname in fuse_data_files:
            if isinstance(fname, str) and fname.strip():
                ordered_files.append((fname.strip(), 'fuse-data', None))

        # Collect all .json files in folder, excluding backups and Definition
        all_jsons = set(p.name for p in folder.glob('*.json')
                        if p.name not in (
                            'Definition.json', 'definition.json',
                            'Info.json', 'info.json',
                        )
                        and not p.name.endswith('.json.bak'))
        referenced = set(f for f, _, _c in ordered_files)

        town_color_idx = 0
        seen = set()

        def add_layer(fname, target='game-graph'):
            nonlocal town_color_idx
            if fname in seen:
                return
            seen.add(fname)
            fpath = folder / fname
            if not fpath.exists():
                print(f"[mod] WARNING: referenced mixinto not found: {fname} "
                      f"(target={target}) -- this layer will be skipped")
                return

            # Determine layer type and color
            fn = fname.lower()
            if target == 'game-migrations' or 'migration' in fn or 'mig' in fn:
                ltype = LAYER_MIGRATION
                color = LAYER_COLORS[LAYER_MIGRATION]
                label = fname
            elif 'game-graph' in fn or 'graph-data' in fn:
                ltype = LAYER_GRAPH
                color = LAYER_COLORS[LAYER_GRAPH]
                label = fname
            elif fn.startswith('town_'):
                ltype = LAYER_TOWN
                color = TOWN_PALETTE[town_color_idx % len(TOWN_PALETTE)]
                town_color_idx += 1
                label = fname.replace('town_', '').replace('.json', '').title()
            elif 'river' in fn or 'road' in fn:
                ltype = LAYER_RIVERS
                color = LAYER_COLORS[LAYER_RIVERS]
                label = fname
            elif 'patch' in fn:
                ltype = LAYER_MIGRATION
                color = LAYER_COLORS[LAYER_MIGRATION]
                label = fname
            else:
                ltype = LAYER_OTHER
                color = LAYER_COLORS[LAYER_OTHER]
                label = fname

            layer = Layer(fpath, ltype, color, label)
            layer.load()
            if (
                fpath.name.lower().endswith('.fuse.json')
                and isinstance(layer._raw.get('tracks'), dict)
            ):
                layer.layer_type = LAYER_GRAPH
                layer.color = LAYER_COLORS[LAYER_GRAPH]
            proj._tag_layer_source(layer, source_idx)
            proj.layers.append(layer)

        # Add in definition order first
        for fname, target, entry_conflicts in ordered_files:
            add_layer(fname, target)
            # D10: attach per-mixinto conflictsWith to the layer for callers to inspect
            if entry_conflicts and proj.layers:
                proj.layers[-1].mixinto_conflicts_with = entry_conflicts

        # Add any unreferenced json files (excluding progression files handled separately)
        for fname in sorted(all_jsons - seen):
            if fname.lower() not in ('progressions.json', 'progressions-new.json'):
                add_layer(fname)

        # Always load progressions.json as a layer (excluded above from auto-scan
        # so it gets a dedicated slot -- ProgressionProject._find_layer searches
        # proj.layers by filename, so it must be present here).
        for prog_name in ('progressions.json', 'progressions-new.json'):
            prog_path = folder / prog_name
            if prog_path.exists() and prog_name not in seen:
                seen.add(prog_name)
                prog_layer = Layer(prog_path, LAYER_OTHER,
                                   LAYER_COLORS[LAYER_OTHER], prog_name)
                prog_layer.load()
                proj._tag_layer_source(prog_layer, source_idx)
                proj.layers.append(prog_layer)

        proj._rebuild_merge()
        proj._refresh_layer_labels()
        proj._sync_active_source()
        return proj

    @classmethod
    def new_mod(cls, folder: Path, mod_id: str, mod_name: str,
                version: str = '0.1.0',
                author: str = 'Author',
                loader: str = 'compatible',
                assemblies: list = None,
                conflicts_with: list = None,
                load_before: list = None,
                priority: int = 0,
                update_url: str = None,
                requirements: list = None,
                complete_map: bool = False,
                map_origin_lat: float = None,
                map_origin_lon: float = None,
                map_tile_dimension: float = 500.0) -> 'ModProject':
        """Create a new empty mod project.

        mod_id    -- must match ^[A-Za-z0-9_.]+$ (hyphens are invalid in both
                     Railloader and UMM mod IDs)
        loader    -- 'compatible' (default), 'fuse', or 'umm'.
                     'compatible' / 'railloader' -- writes one legacy graph
                       package that RailLoader loads directly and FUSE imports.
                     'fuse' -- writes a native FUSE Info.json and data fragment.
                     'umm' -- writes an Info.json for a C# utility mod.
        author    -- mod author name
        requirements -- list of required mod IDs (UMM only)

        Railloader-only fields (ignored when loader='umm'):
          assemblies, conflicts_with, load_before, priority, update_url
        """
        import re as _re
        folder = Path(folder)
        loader_kind = str(loader or 'compatible').strip().lower()
        if loader_kind == 'railloader':
            loader_kind = 'compatible'
        if loader_kind not in ('compatible', 'fuse', 'umm'):
            raise ValueError(
                "loader must be 'compatible', 'fuse', or 'umm'"
            )

        # Use the common RailLoader/UMM ID subset so either package type can
        # be selected without silently creating an invalid folder.
        # Confirmed: Railloader ValidIdRegex = ^[A-Za-z0-9_.]+$
        # UMM ModInfo.Id has same constraint in practice
        _VALID_ID = _re.compile(r'^[A-Za-z0-9_.]+$')
        mod_id = str(mod_id or '').strip()
        mod_name = str(mod_name or '').strip()
        author = str(author or '').strip()
        if not mod_id or not _VALID_ID.fullmatch(mod_id):
            raise ValueError(
                "Mod ID must use only letters, numbers, underscores, and dots"
            )
        if mod_id.lower() in ('railloader', 'railroader', 'fuse'):
            raise ValueError(f"Mod ID '{mod_id}' is reserved")
        if not mod_name:
            raise ValueError("Mod display name cannot be empty")
        if complete_map and loader_kind != 'fuse':
            raise ValueError("Complete standalone maps require loader='fuse'")
        if complete_map:
            map_origin_lat = float(map_origin_lat)
            map_origin_lon = float(map_origin_lon)
            map_tile_dimension = float(map_tile_dimension)
            if not all(math.isfinite(value) for value in (
                    map_origin_lat, map_origin_lon, map_tile_dimension)):
                raise ValueError("Map origin and tile dimension must be finite")
            if not -90.0 <= map_origin_lat <= 90.0:
                raise ValueError("Map origin latitude must be between -90 and 90")
            if not -180.0 <= map_origin_lon <= 180.0:
                raise ValueError("Map origin longitude must be between -180 and 180")
            if map_tile_dimension <= 0.0:
                raise ValueError("Map tile dimension must be greater than zero")
        if folder.exists() and any(folder.iterdir()):
            raise FileExistsError(
                f"Refusing to overwrite non-empty mod folder: {folder}"
            )

        folder.mkdir(parents=True, exist_ok=True)
        proj = cls()
        proj.name   = mod_name

        if loader_kind == 'fuse':
            # A native FUSE data package. The schema URI points to the copy
            # supplied by the installed FUSE runtime; it is guidance for JSON
            # editors and is not required for loading.
            graph_path = folder / 'map.fuse.json'
            empty = {
                '$schema': '../FUSE/schemas/fuse-mod.schema.json',
                'schemaVersion': '1.0',
                'id': mod_id,
                'name': mod_name,
                'author': author,
                'modVersion': version,
                'coordinateSpace': 'world',
                'tracks': {
                    'nodes': {},
                    'segments': {},
                    'spans': {},
                    'removals': {
                        'nodes': [],
                        'segments': [],
                        'spans': [],
                    },
                },
            }
            if complete_map:
                empty['map'] = {
                    'displayName': mod_name,
                    'mapFolder': 'Map',
                    'suppressBaseWorld': True,
                }
                map_folder = folder / 'Map'
                map_folder.mkdir(parents=True, exist_ok=True)
                _save_json(map_folder / 'Map.json', {
                    'origin': {
                        'latitude': map_origin_lat,
                        'longitude': map_origin_lon,
                    },
                    'tileDimension': map_tile_dimension,
                    'tiles': [],
                })
            _save_json(graph_path, empty)
            proj.definition = {
                'Id': mod_id,
                'DisplayName': mod_name,
                'Author': author,
                'Version': version,
                'ManagerVersion': '0.27.10',
                'GameVersion': '2025.1',
                'Requirements': [
                    {'Id': 'FUSE', 'NotBefore': '1.0.0'},
                ],
                'LoadAfter': ['FUSE'],
                'FuseLoadPriority': 100,
                'FuseLoadAfter': [],
                'FuseLoadBefore': [],
                'FuseDataFiles': ['map.fuse.json'],
            }
            _save_json(folder / 'Info.json', proj.definition)
            definition_path = folder / 'Info.json'

        elif loader_kind == 'umm':
            graph_path = folder / 'game-graph.json'
            empty = {'tracks': {'nodes': {}, 'segments': {}, 'spans': {}},
                     'areas': {}, 'texts': {},
                     'scenery': {}, 'splineys': {}, 'simpleGraphs': {}, 'mandelas': {}}
            _save_json(graph_path, empty)
            # UMM Info.json format
            # Confirmed from UnityModManager/UnityModManager.cs ModInfo class
            proj.definition = {
                'Id':             mod_id,
                'DisplayName':    mod_name,
                'Author':         author,
                'Version':        version,
                'ManagerVersion': '0.27.0',
                'AssemblyName':   f'{mod_id}.dll',
                'EntryMethod':    f'{mod_id.replace(".", "_")}.UMM.Mod.Load',
            }
            if requirements:
                proj.definition['Requirements'] = list(requirements)
            _save_json(folder / 'Info.json', proj.definition)
            definition_path = folder / 'Info.json'

        else:
            graph_path = folder / 'game-graph.json'
            empty = {'tracks': {'nodes': {}, 'segments': {}, 'spans': {}},
                     'areas': {}, 'texts': {},
                     'scenery': {}, 'splineys': {}, 'simpleGraphs': {}, 'mandelas': {}}
            _save_json(graph_path, empty)
            # One-source compatible package: RailLoader loads this manifest,
            # while FUSE imports the supported legacy graph schema. Do not add
            # Strange Customs or Alina unless a project actually uses them.
            # D4: manifestVersion=8 = ModDefinition.CurrentManifestVersion
            proj.definition = {
                'manifestVersion': 8,
                'id':      mod_id,
                'name':    mod_name,
                'version': version,
                'requires': [
                    {'id': 'railloader', 'notBefore': '1.8.2.1'},
                ],
                'mixintos': {
                    'game-graph': ['file(game-graph.json)']
                }
            }
            if assemblies:
                proj.definition['assemblies'] = list(assemblies)
            if conflicts_with:
                proj.definition['conflictsWith'] = [{'id': m} for m in conflicts_with]
            if load_before:
                proj.definition['loadBefore'] = list(load_before)
            if priority:
                proj.definition['priority'] = priority
            if update_url:
                proj.definition['updateUrl'] = update_url
            _save_json(folder / 'Definition.json', proj.definition)
            definition_path = folder / 'Definition.json'

        source_idx = proj._add_source(folder, mod_name, proj.definition, definition_path)

        layer = Layer(graph_path, LAYER_GRAPH, LAYER_COLORS[LAYER_GRAPH],
                      graph_path.name)
        layer.load()
        proj._tag_layer_source(layer, source_idx)
        proj.layers.append(layer)
        proj._rebuild_merge()
        proj._refresh_layer_labels()
        proj._sync_active_source()
        return proj

    # ------------------------------------------------------------------
    def _rebuild_merge(self):
        """Merge all visible layers to produce the final view of all data.

        Tracks (nodes/segments): later layers override earlier; null = delete.
        Everything else (splineys, scenery, mandelas, areas, texts, simpleGraphs):
        later layers win at the key level (null = delete for those keys too).
        """
        self.merged_nodes       = {}
        self.merged_segments    = {}
        self.merged_splineys    = {}
        self.merged_scenery     = {}
        self.merged_mandelas    = {}
        self.merged_areas       = {}
        self.merged_texts       = {}
        self.merged_simplegraphs = {}
        self.merged_loads       = {}

        for layer in self.layers:
            if not layer.visible:
                continue
            for nid, node in layer.nodes.items():
                if node['deleted']:
                    self.merged_nodes.pop(nid, None)
                else:
                    self.merged_nodes[nid] = node
            for sid, seg in layer.segments.items():
                if seg['deleted']:
                    self.merged_segments.pop(sid, None)
                else:
                    self.merged_segments[sid] = seg
            for kid, val in layer.splineys.items():
                if val is None:
                    self.merged_splineys.pop(kid, None)
                else:
                    self.merged_splineys[kid] = val
            for kid, val in layer.scenery.items():
                if val is None:
                    self.merged_scenery.pop(kid, None)
                else:
                    self.merged_scenery[kid] = val
            for kid, val in layer.mandelas.items():
                if val is None:
                    self.merged_mandelas.pop(kid, None)
                else:
                    self.merged_mandelas[kid] = val
            for kid, val in layer.areas.items():
                if val is None:
                    self.merged_areas.pop(kid, None)
                else:
                    self.merged_areas[kid] = val
            for kid, val in layer.texts.items():
                if val is None:
                    self.merged_texts.pop(kid, None)
                else:
                    self.merged_texts[kid] = val
            for kid, val in layer.simpleGraphs.items():
                if val is None:
                    self.merged_simplegraphs.pop(kid, None)
                else:
                    self.merged_simplegraphs[kid] = val
            for kid, val in layer.loads.items():
                if val is None:
                    self.merged_loads.pop(kid, None)
                else:
                    self.merged_loads[kid] = val

        # Rebuild bezier curves using merged node positions
        for layer in self.layers:
            if layer.visible:
                layer.rebuild_curves(self.merged_nodes)

    def toggle_layer(self, idx: int):
        if 0 <= idx < len(self.layers):
            self.layers[idx].visible = not self.layers[idx].visible
            self._rebuild_merge()

    def save_all(self, force: bool = False):
        saved_layers = []
        for layer in self.layers:
            if layer.save(force=force):
                saved_layers.append(layer)
        saved_defs = set()
        for source in self.sources:
            def_path = source.get('definition_path')
            if not def_path or source.get('is_base_game'):
                continue
            key = str(def_path)
            if key in saved_defs:
                continue
            _save_json(def_path, source.get('definition') or {})
            saved_defs.add(key)
        return saved_layers

    def save_layer(self, idx: int, force: bool = False):
        if 0 <= idx < len(self.layers):
            layer = self.layers[idx]
            if layer.save(force=force):
                return layer
        return None

    # ------------------------------------------------------------------
    def get_graph_layer(self) -> 'Optional[Layer]':
        """Return the first LAYER_GRAPH layer (mod game-graph.json).
        This is always where node/segment edits go."""
        active = self.active_layer
        if active and active.layer_type == LAYER_GRAPH and not active.read_only:
            return active
        active_source = self.active_source
        if active_source is not None:
            active_source_idx = self.sources.index(active_source)
            for layer in self.layers:
                if (getattr(layer, 'source_idx', None) == active_source_idx
                        and layer.layer_type == LAYER_GRAPH and not layer.read_only):
                    return layer
        for layer in self.layers:
            if layer.layer_type == LAYER_GRAPH and not layer.read_only:
                return layer
        return None

    def writable_layers(self, source: Optional[dict] = None) -> list[tuple[int, Layer]]:
        """Return writable layers for the given source (defaults to active source)."""
        source = source or self.active_source or self._default_edit_source()
        if source is None:
            return []
        source_idx = self.sources.index(source) if source in self.sources else None
        layers: list[tuple[int, Layer]] = []
        for li, layer in enumerate(self.layers):
            if layer.read_only:
                continue
            if source_idx is not None and getattr(layer, 'source_idx', None) != source_idx:
                continue
            layers.append((li, layer))
        return layers

    def _infer_layer_meta(self, rel_path: str, target: str = 'game-graph') -> tuple[str, tuple, str]:
        rel_name = Path(rel_path).name
        fn = rel_name.lower()
        if target == 'game-migrations' or 'migration' in fn or 'mig' in fn:
            return LAYER_MIGRATION, LAYER_COLORS[LAYER_MIGRATION], rel_name
        if 'game-graph' in fn or 'graph-data' in fn:
            return LAYER_GRAPH, LAYER_COLORS[LAYER_GRAPH], rel_name
        if fn.startswith('town_'):
            town_idx = sum(1 for layer in self.layers if layer.layer_type == LAYER_TOWN)
            label = rel_name.replace('town_', '').replace('.json', '').title()
            return LAYER_TOWN, TOWN_PALETTE[town_idx % len(TOWN_PALETTE)], label
        if 'river' in fn or 'road' in fn:
            return LAYER_RIVERS, LAYER_COLORS[LAYER_RIVERS], rel_name
        if 'patch' in fn:
            return LAYER_MIGRATION, LAYER_COLORS[LAYER_MIGRATION], rel_name
        return LAYER_OTHER, LAYER_COLORS[LAYER_OTHER], rel_name

    def ensure_json_layer(self, rel_path: str, target: str = 'game-graph',
                          template: Optional[dict] = None,
                          source: Optional[dict] = None,
                          make_active: bool = True) -> Optional[Layer]:
        """Ensure a JSON file exists, is loaded as a layer, and is referenced in mixintos."""
        source = source or self.active_source or self._default_edit_source()
        if source is None or source.get('is_base_game'):
            return None
        folder = source.get('folder')
        if folder is None:
            return None
        folder = Path(folder)

        rel = Path(str(rel_path).strip())
        if not str(rel):
            return None
        if rel.suffix.lower() != '.json':
            rel = rel.with_suffix('.json')
        if rel.is_absolute():
            try:
                rel = rel.resolve().relative_to(folder.resolve())
            except Exception:
                return None
        abs_path = (folder / rel).resolve()
        try:
            abs_path.relative_to(folder.resolve())
        except Exception:
            return None

        rel_posix = rel.as_posix()
        existing = next(
            (layer for layer in self.layers
             if str(layer.path.resolve()) == str(abs_path)),
            None,
        )

        if not abs_path.exists():
            abs_path.parent.mkdir(parents=True, exist_ok=True)
            _save_json(abs_path, template if template is not None else {'splineys': {}})

        definition = source.setdefault('definition', {})
        mixintos = definition.setdefault('mixintos', {})
        raw_entries = mixintos.get(target, [])
        if isinstance(raw_entries, list):
            entries = list(raw_entries)
        elif raw_entries in (None, ''):
            entries = []
        else:
            entries = [raw_entries]

        wanted = f"file({rel_posix})"
        found = False
        for entry in entries:
            ref = ''
            if isinstance(entry, str):
                ref = entry
            elif isinstance(entry, dict):
                ref = str(entry.get('mixinto', ''))
            match = re.match(r'file\((.+?)\)', ref)
            if match and match.group(1).replace('\\', '/') == rel_posix:
                found = True
                break
        if not found:
            entries.append(wanted)
            mixintos[target] = entries
            def_path = source.get('definition_path')
            if def_path and not source.get('is_base_game'):
                _save_json(def_path, definition)

        if existing is None:
            layer_type, color, label = self._infer_layer_meta(rel_posix, target)
            existing = Layer(abs_path, layer_type, color, label)
            existing.load()
            source_idx = self.sources.index(source) if source in self.sources else 0
            self._tag_layer_source(existing, source_idx)
            self.layers.append(existing)
            self._refresh_layer_labels()
            self._rebuild_merge()

        if make_active and existing in self.layers:
            self.set_active_layer(self.layers.index(existing))
        else:
            self._sync_active_source()
        return existing

    def _id_prefix_definition(self) -> dict:
        active_source = self.active_source
        if active_source and isinstance(active_source.get('definition'), dict):
            return active_source['definition']
        return self.definition or {}

    def next_node_id(self, exclude: set = None) -> str:
        """Generate a unique node ID matching the game's alphanumeric format.

        Format: N_{modId}_{4 random [a-z0-9] chars}, e.g. N_MyMod_ab3f
        Checked against all known node IDs to guarantee no collision.
        """
        definition = self._id_prefix_definition()
        prefix = (definition.get('id') or definition.get('Id') or '').replace('.', '_')[:8]
        existing = set(self.merged_nodes.keys())
        for layer in self.layers:
            existing.update(layer.nodes.keys())
        if exclude:
            existing.update(exclude)
        pfx = f"N_{prefix}" if prefix else "N"
        while True:
            nid = f"{pfx}_{_rand_chars()}"
            if nid not in existing:
                return nid

    def next_seg_id(self, exclude: set = None) -> str:
        """Generate a unique segment ID matching the game's alphanumeric format.

        Format: S_{modId}_{4 random [a-z0-9] chars}, e.g. S_MyMod_ab3f
        """
        definition = self._id_prefix_definition()
        prefix = (definition.get('id') or definition.get('Id') or '').replace('.', '_')[:8]
        existing = set(self.merged_segments.keys())
        for layer in self.layers:
            existing.update(layer.segments.keys())
        if exclude:
            existing.update(exclude)
        pfx = f"S_{prefix}" if prefix else "S"
        while True:
            sid = f"{pfx}_{_rand_chars()}"
            if sid not in existing:
                return sid

    def segments_for_node(self, node_id: str) -> list:
        """Return list of segment dicts connected to node_id from merged view."""
        return [s for s in self.merged_segments.values()
                if s.get('startId') == node_id or s.get('endId') == node_id]

    # Stats for display
    def stats(self) -> str:
        nn = len(self.merged_nodes)
        ns = len(self.merged_segments)
        nd = sum(1 for l in self.layers
                 for n in l.nodes.values() if n['deleted'])
        sd = sum(1 for l in self.layers
                 for s in l.segments.values() if s['deleted'])
        return (f"{nn} nodes  {ns} segments"
                + (f"  ({nd} deleted nodes  {sd} deleted segs)" if nd or sd else ""))
