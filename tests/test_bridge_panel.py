import json
import tempfile
import threading
import time
import unittest
from pathlib import Path

import numpy as np

from edit_tiles.bridge import BridgeMixin
from edit_tiles.terrain import Tile, load_tile
from railroader_bridge import RailroaderBridge


class BridgePanelProtocolTests(unittest.TestCase):
    def test_editor_state_is_published_beside_existing_bridge_files(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            bridge = RailroaderBridge(game_dir=temp_dir)
            bridge.publish_editor_state({
                "projectLoaded": True,
                "projectName": "Test Project",
                "geoMode": "grade",
            })

            path = (
                Path(temp_dir)
                / "Mods"
                / "TrackBridge"
                / "editor_state.json"
            )
            state = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual(state["protocolVersion"], 1)
            self.assertGreater(state["timestamp"], 0)
            self.assertTrue(state["projectLoaded"])
            self.assertEqual(state["geoMode"], "grade")

    def test_editor_command_is_delivered_once(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            bridge = RailroaderBridge(game_dir=temp_dir)
            received = []
            bridge.on_editor_command = received.append
            command_path = (
                Path(temp_dir)
                / "Mods"
                / "TrackBridge"
                / "editor_commands.json"
            )
            command_path.parent.mkdir(parents=True)
            command_path.write_text(json.dumps({
                "requestId": "request-1",
                "action": "set_geo_mode",
                "payload": "turnout",
                "sentAt": int(time.time() * 1000),
            }), encoding="utf-8")

            bridge._try_load_editor_command()
            bridge._try_load_editor_command()

            self.assertEqual(len(received), 1)
            self.assertEqual(received[0]["action"], "set_geo_mode")
            self.assertEqual(received[0]["payload"], "turnout")

    def test_stale_editor_command_is_not_replayed(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            bridge = RailroaderBridge(game_dir=temp_dir)
            received = []
            bridge.on_editor_command = received.append
            command_path = (
                Path(temp_dir)
                / "Mods"
                / "TrackBridge"
                / "editor_commands.json"
            )
            command_path.parent.mkdir(parents=True)
            command_path.write_text(json.dumps({
                "requestId": "old-request",
                "action": "undo",
                "payload": "",
                "sentAt": int(time.time() * 1000) - 121_000,
            }), encoding="utf-8")

            bridge._try_load_editor_command()

            self.assertEqual(received, [])

    def test_umm_panel_heartbeat_marks_game_connected_without_graph_bridge(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            bridge = RailroaderBridge(
                game_dir=temp_dir,
                poll_interval=0.01,
            )
            connected = threading.Event()
            bridge.on_connect = connected.set
            heartbeat_path = (
                Path(temp_dir)
                / "Mods"
                / "TrackBridge"
                / "game_panel_state.json"
            )
            heartbeat_path.parent.mkdir(parents=True)
            heartbeat_path.write_text(json.dumps({
                "protocolVersion": 1,
                "timestamp": int(time.time() * 1000),
                "loaded": True,
                "panelVersion": "test",
            }), encoding="utf-8")

            bridge.start()
            try:
                self.assertTrue(connected.wait(0.5))
                self.assertTrue(bridge.connected)
                self.assertIsNone(bridge.state)
            finally:
                bridge.stop()

    def test_desktop_terrain_save_requests_in_game_reload(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            bridge = RailroaderBridge(game_dir=temp_dir)
            tile_a = str(Path(temp_dir) / "tile_001_002.data")
            tile_b = str(Path(temp_dir) / "tile_003_004.data")

            bridge.reload_terrain_tiles([tile_a, tile_b])

            command_path = (
                Path(temp_dir)
                / "Mods"
                / "TrackBridge"
                / "bridge_commands.json"
            )
            command = json.loads(
                command_path.read_text(encoding="utf-8")
            )
            self.assertEqual(
                command["action"],
                "reload_terrain_tiles",
            )
            self.assertEqual(
                command["payload"].splitlines(),
                [tile_a, tile_b],
            )

    def test_game_terrain_save_reloads_desktop_and_preserves_conflict(self):
        class Harness(BridgeMixin):
            def __init__(self):
                self.tiles = {}
                self.folders = []
                self.undo_stack = []
                self.status = ""

            def _set_status(self, value):
                self.status = value

            def _configure_map_georeference(self, *args, **kwargs):
                return False

            def _update_bounds(self):
                return None

            def invalidate_all(self):
                return None

        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            path = root / "tile_001_002.data"
            zeros = np.zeros(16, dtype=np.uint8)
            incoming = Tile(
                1,
                2,
                np.full(16, 12, dtype=np.uint8),
                np.full(16, 34, dtype=np.uint8),
                np.full(16, 0x50, dtype=np.uint8),
                4,
                path=path,
            )
            incoming.write_copy(path)
            current = Tile(
                1,
                2,
                np.full(16, 99, dtype=np.uint8),
                zeros,
                zeros,
                4,
                path=path,
            )
            current.dirty = True
            harness = Harness()
            harness.tiles["1,2"] = current

            harness._reload_files_saved_in_game(
                "terrain\n" + str(path)
            )

            self.assertEqual(
                int(harness.tiles["1,2"].r[0]),
                12,
            )
            conflicts = list(
                root.glob("tile_001_002.data.desktop-conflict-*")
            )
            self.assertEqual(len(conflicts), 1)
            self.assertIn("preserved 1", harness.status)

    def test_desktop_tile_save_is_atomic_and_backed_up(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            path = root / "tile_005_006.data"
            tile = Tile(
                5,
                6,
                np.full(16, 10, dtype=np.uint8),
                np.full(16, 20, dtype=np.uint8),
                np.zeros(16, dtype=np.uint8),
                4,
                path=path,
            )
            tile.write_copy(path)
            tile.r[:] = 30
            tile.dirty = True

            self.assertTrue(tile.save())

            backups = list(
                root.glob(
                    "tile_005_006.data.tile-editor-backup-*")
            )
            self.assertEqual(len(backups), 1)
            self.assertFalse(
                Path(str(path) + ".tile-editor.tmp").exists()
            )
            reloaded = load_tile(path)
            self.assertIsNotNone(reloaded)
            self.assertEqual(int(reloaded.r[0]), 30)

    def test_game_panel_state_exposes_unsaved_edit_ownership(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            bridge = RailroaderBridge(game_dir=temp_dir)
            path = (
                Path(temp_dir)
                / "Mods"
                / "TrackBridge"
                / "game_panel_state.json"
            )
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps({
                "graphDirty": True,
                "terrainDirty": True,
                "graphPath": "game-graph.json",
            }), encoding="utf-8")

            bridge._try_load_game_panel_state()

            self.assertTrue(bridge.game_panel_state["graphDirty"])
            self.assertTrue(bridge.game_panel_state["terrainDirty"])


if __name__ == "__main__":
    unittest.main()
