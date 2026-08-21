import tempfile
import unittest
from pathlib import Path

import numpy as np

from edit_tiles.terrain import (
    Tile,
    interpolate_stroke_points,
    load_tile,
)
from edit_tiles.constants import VEG_DESCRIPTIONS, VEG_NAMES


class TerrainEditingTests(unittest.TestCase):
    def test_vegetation_presets_have_human_readable_names_and_guidance(self):
        self.assertEqual(len(VEG_NAMES), 8)
        self.assertEqual(len(VEG_DESCRIPTIONS), 8)
        for index, (name, description) in enumerate(
                zip(VEG_NAMES, VEG_DESCRIPTIONS)):
            self.assertTrue(name.strip(), f"preset {index} has no name")
            self.assertTrue(
                description.strip(),
                f"preset {index} has no description",
            )
        self.assertIn("Full", VEG_NAMES[0])
        self.assertIn("Clear", VEG_NAMES[7])
        self.assertIn("100%", VEG_DESCRIPTIONS[0])
        self.assertIn("0%", VEG_DESCRIPTIONS[7])

    def test_fast_stroke_is_filled_at_bounded_spacing(self):
        points = interpolate_stroke_points((0, 0), (25, 0), 10)

        self.assertEqual(points, [
            (25 / 3, 0),
            (50 / 3, 0),
            (25, 0),
        ])
        previous = (0, 0)
        for point in points:
            self.assertLessEqual(point[0] - previous[0], 10)
            previous = point

    def test_first_stroke_sample_is_the_pointer_position(self):
        self.assertEqual(
            interpolate_stroke_points(None, (12, 34), 5),
            [(12, 34)],
        )

    def test_tile_save_round_trips_all_vegetation_and_water_codes(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "tile_001_002.data"
            alpha = np.array(
                [
                    category << 4
                    for category in range(8)
                ]
                + [
                    0x80 | category << 4
                    for category in range(8)
                ],
                dtype=np.uint8,
            )
            zeros = np.zeros(alpha.size, dtype=np.uint8)
            tile = Tile(
                1,
                2,
                zeros,
                zeros,
                alpha,
                4,
                path=path,
            )
            tile.dirty = True

            self.assertTrue(tile.save())

            reloaded = load_tile(path)
            self.assertIsNotNone(reloaded)
            np.testing.assert_array_equal(reloaded.a, alpha)

    def test_tile_save_clears_dirty_state_and_cache_can_be_rebuilt(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "tile_003_004.data"
            pixels = np.arange(16, dtype=np.uint8)
            alpha = np.array(
                [0x80 | ((index % 8) << 4) for index in range(16)],
                dtype=np.uint8,
            )
            tile = Tile(3, 4, pixels, pixels, alpha, 4, path=path)
            tile.dirty = True
            tile.surf_overview = object()
            tile.surf_detail = object()
            tile._scaled_overview = object()
            self.assertTrue(tile.save())
            self.assertFalse(tile.dirty)
            self.assertIsNone(tile.surf_overview)
            self.assertIsNone(tile.surf_detail)
            self.assertIsNone(tile._scaled_overview)
            self.assertEqual(tile.dom_preset, 0)

            reloaded = load_tile(path)
            self.assertIsNotNone(reloaded)
            np.testing.assert_array_equal(reloaded.a, alpha)


if __name__ == "__main__":
    unittest.main()
