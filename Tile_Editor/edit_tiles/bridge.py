"""edit_tiles.bridge — RailroaderBridge integration mixin.

BridgeMixin provides _init_bridge, _on_bridge_state, _apply_bridge_state,
and _poll_bridge. TileEditor inherits from this mixin.
"""

import math
import threading
from pathlib import Path

try:
    from railroader_bridge import RailroaderBridge
    _BRIDGE_AVAILABLE = True
except ImportError:
    _BRIDGE_AVAILABLE = False


class BridgeMixin:
    """Mixin for TileEditor providing bridge connection and state polling."""

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
        self.bridge.on_state_update = self._on_bridge_state
        self._bridge_reload_tracks_direct = self.bridge.reload_tracks
        self.bridge.reload_tracks = self._bridge_reload_tracks_proxy
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

    def _poll_bridge(self):
        """Call once per frame from the main loop to apply any pending bridge state."""
        with self._bridge_lock:
            state = self._bridge_pending_state
            self._bridge_pending_state = None
            if self.bridge is not None:
                self.bridge_connected = self.bridge.connected
        if state is not None:
            # Only rebuild the geometry if something actually changed
            fingerprint = (len(state.nodes), len(state.segments))
            if fingerprint != self._bridge_last_fingerprint:
                self._bridge_last_fingerprint = fingerprint
                self._apply_bridge_state(state)
            else:
                # Just update cars — cheap
                self.bridge_cars = list(state.cars)
