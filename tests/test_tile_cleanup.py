import collections
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np

from edit_tiles.app import TileEditor
from edit_tiles.terrain import Tile, TileDeleteRecord, load_tile


class TileCleanupTests(unittest.TestCase):
    def _make_editor(self):
        editor = TileEditor.__new__(TileEditor)
        editor.tiles = {}
        editor.tile_delete_selection = set()
        editor.tile_delete_dragging = False
        editor.tile_delete_drag_start = None
        editor.tile_delete_drag_end = None
        editor.tile_delete_drag_operation = 'replace'
        editor.tile_delete_confirm = False
        editor.undo_stack = collections.deque(maxlen=20)
        editor._game_terrain_sync_locked = False
        editor.min_x = editor.max_x = editor.min_y = editor.max_y = 0
        editor.status_messages = []
        editor._set_status = editor.status_messages.append
        return editor

    def _write_tile(self, folder, x, y, value=0):
        pixels = np.full(4, value, dtype=np.uint8)
        alpha = np.full(4, 16, dtype=np.uint8)
        path = folder / f'tile_{x}_{y}.data'
        tile = Tile(x, y, pixels, pixels, alpha, 2, path)
        tile.write_copy(path)
        return tile

    def test_box_add_subtract_and_invert_support_row_cleanup(self):
        editor = self._make_editor()
        for y in range(3):
            for x in range(4):
                editor.tiles[f'{x},{y}'] = object()

        editor.tile_delete_drag_start = (1, 0)
        editor.tile_delete_drag_end = (2, 2)
        editor.tile_delete_drag_operation = 'replace'
        editor.tile_delete_dragging = True
        editor._commit_tile_cleanup_box()
        self.assertEqual(len(editor.tile_delete_selection), 6)

        editor.tile_delete_drag_start = (2, 1)
        editor.tile_delete_drag_end = (2, 2)
        editor.tile_delete_drag_operation = 'subtract'
        editor.tile_delete_dragging = True
        editor._commit_tile_cleanup_box()
        self.assertNotIn('2,1', editor.tile_delete_selection)
        self.assertNotIn('2,2', editor.tile_delete_selection)

        kept_row = set(editor.tile_delete_selection)
        editor._run_tile_cleanup_action('cleanup_invert')
        self.assertEqual(
            editor.tile_delete_selection,
            set(editor.tiles) - kept_row,
        )

    def test_delete_moves_files_to_recovery_and_undo_restores(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            tile_folder = root / 'MapTiles'
            tile_folder.mkdir()
            editor = self._make_editor()
            tile = self._write_tile(tile_folder, 3, 4, value=7)
            editor.tiles['3,4'] = tile
            editor.tile_delete_selection = {'3,4'}

            self.assertTrue(editor._delete_selected_tiles())
            self.assertFalse(tile.path.exists())
            self.assertNotIn('3,4', editor.tiles)
            self.assertIsInstance(editor.undo_stack[-1], TileDeleteRecord)

            recovery_files = list(
                (root / '_TileEditor_Deleted_Tiles').rglob('tile_3_4.data')
            )
            manifests = list(
                (root / '_TileEditor_Deleted_Tiles').rglob(
                    'restore-manifest.json'
                )
            )
            self.assertEqual(len(recovery_files), 1)
            self.assertEqual(len(manifests), 1)
            manifest = json.loads(manifests[0].read_text(encoding='utf-8'))
            self.assertEqual(manifest['tiles'][0]['tile'], '3,4')

            editor.undo()
            self.assertTrue(tile.path.exists())
            self.assertIn('3,4', editor.tiles)
            self.assertIsNotNone(load_tile(tile.path))

    def test_dirty_tile_recovery_preserves_current_pixels(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            tile_folder = Path(temp_dir) / 'MapTiles'
            tile_folder.mkdir()
            editor = self._make_editor()
            tile = self._write_tile(tile_folder, -1, 8, value=2)
            tile.r[:] = 211
            tile.g[:] = 19
            tile.dirty = True
            editor.tiles['-1,8'] = tile
            editor.tile_delete_selection = {'-1,8'}

            self.assertTrue(editor._delete_selected_tiles())
            editor.undo()

            restored = load_tile(tile.path)
            self.assertTrue(np.all(restored.r == 211))
            self.assertTrue(np.all(restored.g == 19))


if __name__ == '__main__':
    unittest.main()
