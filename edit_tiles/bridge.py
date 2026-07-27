"""edit_tiles.bridge — RailroaderBridge integration mixin.

BridgeMixin provides _init_bridge, _on_bridge_state, _apply_bridge_state,
and _poll_bridge. TileEditor inherits from this mixin.
"""

import json
import math
import threading
import time
from pathlib import Path

try:
    from railroader_bridge import RailroaderBridge
    _BRIDGE_AVAILABLE = True
except ImportError:
    _BRIDGE_AVAILABLE = False


class BridgeMixin:
    """Mixin for TileEditor providing bridge connection and state polling."""

    def _configure_bridge(self, bridge):
        """Attach a bridge and route reloads through the editor's save mode."""
        self.bridge = bridge
        self._bridge_reload_tracks_direct = bridge.reload_tracks
        bridge.reload_tracks = self._bridge_reload_tracks_proxy
        bridge.on_editor_command = self._on_editor_bridge_command
        return bridge

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

    def _bridge_reload_tracks_proxy(self, mixinto_path: str):
        path = str(mixinto_path or '')
        if not path:
            return
        if not getattr(self, 'live_mod_apply', True):
            pending = getattr(self, '_pending_bridge_reload_paths', None)
            if isinstance(pending, set):
                pending.add(path)
            return
        direct = getattr(self, '_bridge_reload_tracks_direct', None)
        if direct is not None:
            direct(path)

    def _flush_pending_bridge_reload_paths(self) -> int:
        pending = getattr(self, '_pending_bridge_reload_paths', None)
        if not isinstance(pending, set) or not pending:
            return 0
        paths = sorted(pending)
        pending.clear()
        direct = getattr(self, '_bridge_reload_tracks_direct', None)
        if direct is None:
            return 0
        for path in paths:
            direct(path)
        return len(paths)

    def _on_bridge_state(self, state):
        """Called from the bridge watcher thread — just stash the state."""
        with self._bridge_lock:
            self._bridge_pending_state = state
            self.bridge_connected = True

    def _on_editor_bridge_command(self, command):
        """Watcher-thread callback; UI actions run later on the main thread."""
        with self._bridge_lock:
            self._bridge_pending_editor_commands.append(dict(command or {}))

    def _editor_bridge_state(self):
        """Return the compact status document consumed by the UMM panel."""
        project = getattr(self, 'mod_project', None)
        layer = None
        if project and 0 <= project.active_layer_idx < len(project.layers):
            layer = project.layers[project.active_layer_idx]

        selection_kind = ''
        selection_id = ''
        for kind, attr in (
            ('Node', 'sel_mod_node_id'),
            ('Segment', 'sel_mod_seg_id'),
            ('Scenery', 'sel_scenery_id'),
            ('Spliney', 'sel_spliney_id'),
        ):
            value = getattr(self, attr, None)
            if value:
                selection_kind, selection_id = kind, str(value)
                break

        graph = project.get_graph_layer() if project else None
        terrain_dirty_count = sum(
            1
            for tile in getattr(self, 'tiles', {}).values()
            if getattr(tile, 'dirty', False)
        )
        graph_dirty = bool(
            project and project.dirty
        ) or bool(getattr(self, '_area_dirty_layers', set()))
        return {
            'gameConnected': bool(getattr(self, 'bridge_connected', False)),
            'projectLoaded': bool(project),
            'projectName': str(project.name) if project else '',
            'layerName': str(layer.label) if layer else '',
            'layerPath': str(layer.path) if layer else '',
            'geoPanelOpen': bool(getattr(self, 'geo_panel', False)),
            'geoMode': str(getattr(self, 'geo_mode', '')),
            'selectionKind': selection_kind,
            'selectionId': selection_id,
            'liveApply': bool(getattr(self, 'live_mod_apply', True)),
            'dirty': bool(layer and layer.dirty),
            'graphDirty': graph_dirty,
            'terrainDirty': terrain_dirty_count > 0,
            'terrainDirtyCount': terrain_dirty_count,
            'canUndo': bool(getattr(self, '_mod_undo_stack', [])),
            'pendingChanges': int(self._pending_mod_apply_count()) if project else 0,
            'nodeCount': len(project.merged_nodes) if project else 0,
            'segmentCount': len(project.merged_segments) if project else 0,
            'sceneryCount': len(project.merged_scenery) if project else 0,
            'status': str(getattr(self, 'status_msg', '') or ''),
        }

    def _focus_editor_window(self):
        """Bring the pygame window forward when requested by the game panel."""
        try:
            import platform
            import pygame
            if platform.system() != 'Windows':
                pygame.display.set_caption(pygame.display.get_caption()[0])
                return
            import ctypes
            hwnd = int(pygame.display.get_wm_info().get('window', 0) or 0)
            if hwnd:
                ctypes.windll.user32.ShowWindow(hwnd, 9)  # SW_RESTORE
                ctypes.windll.user32.SetForegroundWindow(hwnd)
        except Exception:
            pass

    def _open_editor_panel_from_bridge(self, panel_name: str):
        panel_name = str(panel_name or '').strip().lower()
        if panel_name == 'geo':
            if not getattr(self, 'mod_project', None):
                self._set_status("Bridge: load a mod project before opening Geo")
                return
            self._close_workspace_panels()
            self.geo_panel = True
            self._geo_tab_rects = []
            self._geo_field_rects = []
            self._geo_choice_rects = []
            self._geo_btn_rects = []
            self._set_status("Bridge: Geo panel ready")
        elif panel_name == 'scenery':
            if not getattr(self, 'mod_project', None):
                self._set_status("Bridge: load a mod project before opening Scenery")
                return
            self._close_workspace_panels()
            self.scenery_panel = True
            self._set_status("Bridge: Scenery panel ready")

    def _handle_editor_bridge_command(self, command):
        action = str(command.get('action', '') or '').strip().lower()
        payload = str(command.get('payload', '') or '').strip()
        if action == 'open_panel':
            self._open_editor_panel_from_bridge(payload)
        elif action == 'set_geo_mode':
            valid_modes = {
                'guide', 'pieces', 'curve', 'parallel', 'fit_arc',
                'node', 'grade', 'turnout', 'wye',
            }
            mode = payload.lower()
            if mode in valid_modes:
                self._open_editor_panel_from_bridge('geo')
                if getattr(self, 'mod_project', None):
                    self.geo_mode = mode
                    self._set_status(f"Bridge: Geo {mode.title()} ready")
        elif action == 'focus_editor':
            self._focus_editor_window()
            self._set_status("Bridge: editor brought forward")
        elif action == 'undo':
            self._pop_undo()
        elif action == 'save_reload':
            if getattr(self, 'mod_project', None):
                self._save_active_layer()
            else:
                self._set_status("Bridge: no mod project loaded")
        elif action == 'files_saved_in_game':
            self._reload_files_saved_in_game(payload)

    @staticmethod
    def _sync_payload(payload: str) -> tuple[str, list[Path]]:
        rows = [
            row.strip()
            for row in str(payload or '').splitlines()
            if row.strip()
        ]
        if not rows:
            return '', []
        kind = rows[0].lower()
        paths = []
        seen = set()
        for row in rows[1:]:
            path = Path(row)
            try:
                key = str(path.resolve()).lower()
            except OSError:
                key = str(path).lower()
            if key in seen:
                continue
            seen.add(key)
            paths.append(path)
        return kind, paths

    @staticmethod
    def _sync_conflict_path(path: Path, owner: str) -> Path:
        stamp = time.strftime('%Y%m%d-%H%M%S')
        suffix = path.suffix
        return Path(
            str(path)
            + f'.{owner}-conflict-{stamp}'
            + suffix)

    def _preserve_desktop_layer_conflict(self, layer) -> Path | None:
        if not getattr(layer, 'dirty', False):
            return None
        conflict = self._sync_conflict_path(
            Path(layer.path),
            'desktop')
        conflict.write_text(
            json.dumps(
                getattr(layer, '_raw', {}) or {},
                indent=2,
                ensure_ascii=False),
            encoding='utf-8')
        return conflict

    def _reload_json_files_saved_in_game(
            self, paths: list[Path]) -> tuple[int, list[Path]]:
        if not paths:
            return 0, []
        resolved = {}
        for path in paths:
            try:
                resolved[str(path.resolve()).lower()] = path
            except OSError:
                resolved[str(path).lower()] = path
        reloaded = 0
        conflicts = []
        project = getattr(self, 'mod_project', None)
        if project:
            for layer in project.layers:
                try:
                    key = str(Path(layer.path).resolve()).lower()
                except OSError:
                    key = str(layer.path).lower()
                if key not in resolved:
                    continue
                conflict = self._preserve_desktop_layer_conflict(layer)
                if conflict is not None:
                    conflicts.append(conflict)
                layer.load()
                reloaded += 1
            if reloaded:
                project._rebuild_merge()
                self._mod_undo_stack.clear()
                self._mark_measure_cache_dirty()

        track_graph_path = getattr(self, 'track_graph_path', None)
        if track_graph_path:
            try:
                track_key = str(Path(track_graph_path).resolve()).lower()
            except OSError:
                track_key = str(track_graph_path).lower()
            if track_key in resolved:
                self.load_track_graph(track_graph_path)
                reloaded += 1
        return reloaded, conflicts

    def _reload_terrain_files_saved_in_game(
            self, paths: list[Path]) -> tuple[int, list[Path]]:
        from .terrain import load_tile

        reloaded = 0
        conflicts = []
        changed_keys = set()
        for path in paths:
            if not path.is_file():
                continue
            incoming = load_tile(path)
            if incoming is None:
                continue
            key = f'{incoming.x},{incoming.y}'
            current = self.tiles.get(key)
            if current is not None and current.dirty:
                conflict = self._sync_conflict_path(
                    Path(current.path or path),
                    'desktop')
                current.write_copy(conflict)
                conflicts.append(conflict)
            self.tiles[key] = incoming
            changed_keys.add(key)
            parent = str(path.parent)
            if parent not in self.folders:
                self.folders.append(parent)
            reloaded += 1

        if changed_keys:
            self.undo_stack[:] = [
                record
                for record in self.undo_stack
                if record.tile_key not in changed_keys
            ]
            self._configure_map_georeference(
                self.folders,
                preserve_if_missing=True)
            self._update_bounds()
            self.invalidate_all()
        return reloaded, conflicts

    def _reload_files_saved_in_game(self, payload: str):
        rows = [
            row
            for row in str(payload or '').splitlines()
            if row.strip()
        ]
        if rows and rows[0].strip().lower() == 'batch':
            grouped = {}
            processed = getattr(
                self,
                '_processed_game_sync_entries',
                None)
            if not isinstance(processed, set):
                processed = set()
                self._processed_game_sync_entries = processed
            for row in rows[1:]:
                parts = row.split('\t', 3)
                if len(parts) != 4:
                    continue
                event_id, event_time, category, raw_path = parts
                event_id = event_id.strip()
                if not event_id or event_id in processed:
                    continue
                processed.add(event_id)
                try:
                    event_time = int(event_time)
                except (TypeError, ValueError):
                    continue
                if event_time < int(
                        getattr(
                            self,
                            '_game_sync_session_started_at',
                            0) or 0):
                    continue
                category = category.strip().lower()
                raw_path = raw_path.strip()
                if category and raw_path:
                    grouped.setdefault(category, []).append(
                        Path(raw_path))
            reloaded = 0
            conflicts = []
            terrain_paths = grouped.pop('terrain', [])
            if terrain_paths:
                count, found = (
                    self._reload_terrain_files_saved_in_game(
                        terrain_paths)
                )
                reloaded += count
                conflicts.extend(found)
            json_paths = [
                path
                for paths_for_kind in grouped.values()
                for path in paths_for_kind
            ]
            if json_paths:
                count, found = (
                    self._reload_json_files_saved_in_game(
                        json_paths)
                )
                reloaded += count
                conflicts.extend(found)
            message = (
                f"In-game sync: reloaded {reloaded} file(s)"
            )
            if conflicts:
                message += (
                    f"; preserved {len(conflicts)} desktop conflict "
                    + ("copies" if len(conflicts) != 1 else "copy")
                )
            self._set_status(message)
            return

        kind, paths = self._sync_payload(payload)
        if not paths:
            self._set_status(
                "In-game sync contained no saved files")
            return
        if kind == 'terrain':
            reloaded, conflicts = (
                self._reload_terrain_files_saved_in_game(paths)
            )
        else:
            reloaded, conflicts = (
                self._reload_json_files_saved_in_game(paths)
            )
        message = (
            f"In-game sync: reloaded {reloaded} "
            f"{kind or 'content'} file(s)"
        )
        if conflicts:
            message += (
                f"; preserved {len(conflicts)} desktop conflict copy"
                + ('ies' if len(conflicts) != 1 else '')
            )
        self._set_status(message)

    @staticmethod
    def _bridge_track_fingerprint(state):
        """Return a geometry fingerprint that ignores car-only state changes."""
        nodes = tuple(
            (
                node.id,
                node.x,
                node.y,
                node.z,
                node.rotX,
                node.rotY,
                node.rotZ,
                node.flipSwitchStand,
            )
            for node in state.nodes
        )
        segments = tuple(
            (
                segment.id,
                segment.startId,
                segment.endId,
                segment.trackClass,
                segment.style,
                segment.priority,
                segment.speedLimit,
                segment.groupId,
                getattr(segment, 'gauge', 'Standard'),
            )
            for segment in state.segments
        )
        return nodes, segments

    def _apply_bridge_state(self, state):
        """Called from the main thread to update track graph and cars from bridge state."""
        import math
        # Rebuild track_nodes dict
        self.track_nodes = {
            n.id: (n.x, n.z, n.rotY)
            for n in state.nodes
        }
        self.track_node_list = [(n.x, n.z, n.id) for n in state.nodes]
        # Keep full objects for property display
        self._bridge_nodes_raw    = {n.id: n for n in state.nodes}
        self._bridge_segments_raw = {s.id: s for s in state.segments}

        # Rebuild segments with bezier curves (same logic as load_track_graph)
        def quad_bezier(p0, cp, p1, steps=20):
            pts = []
            for i in range(steps + 1):
                t = i / steps
                x = (1-t)**2*p0[0] + 2*(1-t)*t*cp[0] + t**2*p1[0]
                z = (1-t)**2*p0[1] + 2*(1-t)*t*cp[1] + t**2*p1[1]
                pts.append((x, z))
            return pts

        # trackClass int -> colour key
        CLASS_NAMES = {0: 'Mainline', 1: 'Branch', 2: 'Industrial'}

        self.track_segments = []
        for seg in state.segments:
            if seg.startId not in self.track_nodes or seg.endId not in self.track_nodes:
                continue
            x0, z0, ry0 = self.track_nodes[seg.startId]
            x1, z1, ry1 = self.track_nodes[seg.endId]
            dist = math.sqrt((x1-x0)**2 + (z1-z0)**2)
            if dist < 0.1:
                continue
            mx2, mz2 = (x0+x1)/2, (z0+z1)/2
            dx, dz   = (x1-x0)/dist, (z1-z0)/dist
            perp_x, perp_z = -dz, dx
            a0 = math.radians(ry0); a1 = math.radians(ry1)
            d0x, d0z = math.sin(a0), math.cos(a0)
            d1x, d1z = math.sin(a1), math.cos(a1)
            if d0x*dx + d0z*dz < 0: d0x, d0z = -d0x, -d0z
            if d1x*(-dx) + d1z*(-dz) < 0: d1x, d1z = -d1x, -d1z
            cross = d0x*d1z - d0z*d1x
            bend  = max(-dist*0.2, min(dist*0.2, cross * dist * 0.25))
            cp = (mx2 + perp_x * bend, mz2 + perp_z * bend)
            tc = CLASS_NAMES.get(seg.trackClass, 'Mainline')
            self.track_segments.append((quad_bezier((x0,z0), cp, (x1,z1)), tc))

        # Cars
        self.bridge_cars = list(state.cars)
        self.show_tracks = True

    def _update_game_sync_locks(self):
        bridge = getattr(self, 'bridge', None)
        panel = (
            bridge.game_panel_state
            if bridge is not None and bridge.connected
            else {}
        )
        graph_locked = bool(
            panel.get('graphDirty')
            or panel.get('splineyDirty')
            or panel.get('telegraphPoleDirty')
        )
        terrain_locked = bool(panel.get('terrainDirty'))
        changed = (
            graph_locked
            != bool(
                getattr(
                    self,
                    '_game_graph_sync_locked',
                    False))
            or terrain_locked
            != bool(
                getattr(
                    self,
                    '_game_terrain_sync_locked',
                    False))
        )
        self._game_graph_sync_locked = graph_locked
        self._game_terrain_sync_locked = terrain_locked
        if hasattr(self, '_sync_mod_project_save_mode'):
            self._sync_mod_project_save_mode()
        if changed and (graph_locked or terrain_locked):
            locked = []
            if graph_locked:
                locked.append("map content")
            if terrain_locked:
                locked.append("terrain")
            self._set_status(
                "In-game editor owns unsaved "
                + " and ".join(locked)
                + "; desktop writes are paused")

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
