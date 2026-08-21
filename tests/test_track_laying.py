import copy
import json
import math
import tempfile
import unittest
from pathlib import Path

from edit_tiles.app import TileEditor
from edit_tiles.bridge import BridgeMixin
from edit_tiles.generate import (
    _uses_stock_height_correction,
    sync_map_json_tile_list,
)
from mod_project import (
    ModProject,
    ProgressionProject,
    _bezier_control_points,
    _cubic_point,
    build_vertical_alignment,
    create_trestle_from_segment,
    mandela_set,
    scenery_set,
    spliney_add_road,
    generate_turnout,
    generate_wye,
    turnout_leg_pose,
    turnout_radius_for_chord,
)
from railroader_bridge import BridgeState


class _BridgeHarness(BridgeMixin):
    def __init__(self):
        self.live_mod_apply = False
        self._pending_bridge_reload_paths = set()


class _FakeBridge:
    def __init__(self):
        self.reload_requests = []

    def reload_tracks(self, path):
        self.reload_requests.append(path)


class TrackLayingTests(unittest.TestCase):
    def _make_editor(self, project):
        editor = TileEditor.__new__(TileEditor)
        editor.mod_project = project
        editor.bridge = None
        editor._mod_undo_stack = []
        editor._mod_undo_max = 50
        editor.sel_mod_node_id = None
        editor.sel_mod_seg_id = None
        editor.sel_mod_layer_idx = None
        editor.sel_scenery_id = None
        editor.sel_scenery_layer = None
        editor._connect_from_node = None
        editor._mark_measure_cache_dirty = lambda: None
        editor._set_status = lambda _message: None
        return editor

    def test_new_mod_defaults_to_fuse_railloader_compatible_manifest(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            mod_folder = Path(temp_dir) / "TestMod"
            project = ModProject.new_mod(
                mod_folder,
                "Tests.RailLoader",
                "RailLoader Test",
            )

            self.assertTrue((mod_folder / "Definition.json").is_file())
            self.assertFalse((mod_folder / "Info.json").exists())
            self.assertTrue((mod_folder / "game-graph.json").is_file())
            self.assertEqual(project.definition["manifestVersion"], 8)
            self.assertEqual(
                project.definition["mixintos"]["game-graph"],
                ["file(game-graph.json)"],
            )
            required_ids = {
                entry["id"] for entry in project.definition["requires"]
            }
            self.assertEqual(
                required_ids,
                {"railloader"},
            )
            definition_text = (mod_folder / "Definition.json").read_text(
                encoding="utf-8"
            )
            self.assertNotIn("StrangeCustoms", definition_text)
            self.assertNotIn("AlinasMapMod", definition_text)

    def test_new_mod_can_create_and_reopen_native_fuse_package(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            mod_folder = Path(temp_dir) / "NativeFuseMap"
            project = ModProject.new_mod(
                mod_folder,
                "Tests.NativeFuseMap",
                "Native FUSE Map",
                author="Test Author",
                loader="fuse",
            )

            self.assertTrue((mod_folder / "Info.json").is_file())
            self.assertTrue((mod_folder / "map.fuse.json").is_file())
            self.assertFalse((mod_folder / "Definition.json").exists())
            self.assertFalse((mod_folder / "game-graph.json").exists())
            self.assertEqual(
                project.definition["FuseDataFiles"],
                ["map.fuse.json"],
            )
            self.assertEqual(
                project.definition["Requirements"],
                [{"Id": "FUSE", "NotBefore": "1.0.0"}],
            )
            self.assertEqual(project.get_graph_layer().track_schema, "fuse")

            reopened = ModProject.open_mod_folder(mod_folder)
            reopened_graph = reopened.get_graph_layer()
            self.assertIsNotNone(reopened_graph)
            self.assertEqual(reopened_graph.path.name, "map.fuse.json")
            self.assertEqual(reopened_graph.track_schema, "fuse")

    def test_new_mod_can_scaffold_complete_native_map_package(self):
        from jsonschema import Draft202012Validator

        with tempfile.TemporaryDirectory() as temp_dir:
            mod_folder = Path(temp_dir) / "StandaloneMap"
            project = ModProject.new_mod(
                mod_folder,
                "Tests.StandaloneMap",
                "Standalone Map",
                author="Test Author",
                loader="fuse",
                complete_map=True,
                map_origin_lat=40.43,
                map_origin_lon=-77.72,
            )

            definition = json.loads(
                (mod_folder / "map.fuse.json").read_text(encoding="utf-8")
            )
            self.assertEqual(definition["map"], {
                "displayName": "Standalone Map",
                "mapFolder": "Map",
                "suppressBaseWorld": True,
            })
            manifest = json.loads(
                (mod_folder / "Map" / "Map.json").read_text(encoding="utf-8")
            )
            self.assertEqual(manifest["origin"], {
                "latitude": 40.43,
                "longitude": -77.72,
            })
            self.assertEqual(manifest["tileDimension"], 500.0)
            self.assertEqual(manifest["tiles"], [])
            self.assertEqual(project.get_graph_layer().track_schema, "fuse")

            schema = json.loads(
                (
                    Path(__file__).resolve().parents[2]
                    / "FUSE"
                    / "schemas"
                    / "fuse-mod.schema.json"
                ).read_text(encoding="utf-8")
            )
            Draft202012Validator(schema).validate(definition)

    def test_map_manifest_sync_tracks_signed_tile_files(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            map_folder = Path(temp_dir) / "Map"
            map_folder.mkdir()
            (map_folder / "tile_002_-003.data").write_bytes(b"tile")
            (map_folder / "tile_-001_010.data").write_bytes(b"tile")
            (map_folder / "ignore.data").write_bytes(b"not a tile")

            result = sync_map_json_tile_list(
                map_folder,
                origin_lat=41.0,
                origin_lon=-78.0,
                tile_dimension_m=500,
                origin_e_bias=0,
                origin_n_bias=0,
            )

            self.assertEqual(result, map_folder / "Map.json")
            document = json.loads(result.read_text(encoding="utf-8"))
            self.assertEqual(document["tiles"], [
                {"x": -1, "y": 10},
                {"x": 2, "y": -3},
            ])
            self.assertEqual(document["origin"]["eastBiasMeters"], 0.0)
            self.assertEqual(document["origin"]["northBiasMeters"], 0.0)

    def test_stock_height_correction_does_not_leak_into_custom_maps(self):
        self.assertTrue(
            _uses_stock_height_correction(35.382614, -83.49541)
        )
        self.assertFalse(_uses_stock_height_correction(40.43, -77.72))

    def test_native_desktop_world_tools_write_fuse_containers(self):
        from jsonschema import Draft202012Validator

        with tempfile.TemporaryDirectory() as temp_dir:
            mod_folder = Path(temp_dir) / "NativeWorldTools"
            project = ModProject.new_mod(
                mod_folder,
                "Tests.NativeWorldTools",
                "Native World Tools",
                author="Test Author",
                loader="fuse",
            )
            graph = project.get_graph_layer()
            graph.set_node("n1", 0, 1, 0, 0, 0, 0)
            graph.set_node("n2", 20, 1, 0, 0, 0, 0)
            graph.set_segment(
                "s1", "n1", "n2", "Mainline", "Standard", 20, 0, ""
            )
            scenery_set(
                graph, "scenery.test", "freight-house-general",
                1, 2, 3, rotY=45,
            )
            spliney_add_road(
                graph,
                "road.test",
                "RAM Road profile",
                [
                    {
                        "position": {"x": 0, "y": 1, "z": 0},
                        "rotation": {"x": 0, "y": 0, "z": 0},
                        "width": 5,
                    },
                    {
                        "position": {"x": 20, "y": 1, "z": 0},
                        "rotation": {"x": 0, "y": 0, "z": 0},
                        "width": 5,
                    },
                ],
            )
            mandela_set(
                graph,
                "World/Test/TownSign",
                instantiate_from="World/Base/TownSign",
                x=5,
                y=2,
                z=7,
            )
            graph.save()

            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertNotIn("scenery", saved)
            self.assertNotIn("splineys", saved)
            self.assertNotIn("mandelas", saved)
            self.assertEqual(
                saved["world"]["scenery"]["scenery.test"]["assetIdentifier"],
                "scenery://freight-house-general",
            )
            self.assertEqual(
                saved["world"]["splineys"]["road.test"]["type"],
                "road",
            )
            clone = next(iter(saved["world"]["sceneClones"].values()))
            self.assertEqual(clone["targetPath"], "World/Test/TownSign")
            self.assertEqual(
                clone["source"], "path://scene/World/Base/TownSign"
            )
            self.assertNotIn(
                "groupId", saved["tracks"]["segments"]["s1"]
            )

            schema_path = (
                Path(__file__).resolve().parents[2]
                / "FUSE" / "schemas" / "fuse-mod.schema.json"
            )
            schema = json.loads(schema_path.read_text(encoding="utf-8"))
            errors = list(Draft202012Validator(schema).iter_errors(saved))
            self.assertEqual(errors, [], "\n".join(error.message for error in errors))

    def test_native_desktop_towns_split_tracks_and_operations(self):
        from jsonschema import Draft202012Validator
        from mod_project import Area

        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "NativeTowns",
                "Tests.NativeTowns",
                "Native Towns",
                author="Test Author",
                loader="fuse",
            )
            graph = project.get_graph_layer()
            editor = self._make_editor(project)
            editor._area_dirty_layers = set()
            area = Area("town.test", {
                "name": "Test Town",
                "position": {"x": 100, "y": 5, "z": 200},
                "radius": 400,
                "order": 0,
                "tagColor": [0.2, 0.4, 0.6],
                "industries": {
                    "industry.test": {
                        "name": "Test Industry",
                        "localPosition": {"x": 10, "y": 0, "z": -20},
                        "usesContract": True,
                        "components": {
                            "loader.test": {
                                "type": "Model.Ops.IndustryLoader",
                                "name": "Test Loader",
                                "trackSpans": ["span.test"],
                                "loadId": "coal",
                                "sharedStorage": True,
                                "customSwitch": "preserved",
                            }
                        },
                    }
                },
            })
            editor.prog_project = type("ProgressionStub", (), {
                "areas": {"town.test": area},
                "area_layer": {"town.test": project.layers.index(graph)},
            })()

            layer_index, layer = editor._ensure_town_layer("ignored-town.json")
            self.assertIs(layer, graph)
            self.assertEqual(layer_index, project.layers.index(graph))
            editor._sync_area_to_layer("town.test")
            graph.save()

            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertNotIn("areas", saved)
            self.assertIn("town.test", saved["tracks"]["areas"])
            industry = saved["operations"]["industries"]["industry.test"]
            self.assertEqual(industry["areaId"], "town.test")
            self.assertEqual(
                industry["position"], {"x": 110.0, "y": 5.0, "z": 180.0}
            )
            component = industry["components"]["loader.test"]
            self.assertEqual(component["trackSpanIds"], ["span.test"])
            self.assertNotIn("trackSpans", component)
            self.assertEqual(
                component["fields"]["customSwitch"], "preserved"
            )

            schema_path = (
                Path(__file__).resolve().parents[2]
                / "FUSE" / "schemas" / "fuse-mod.schema.json"
            )
            schema = json.loads(schema_path.read_text(encoding="utf-8"))
            errors = list(Draft202012Validator(schema).iter_errors(saved))
            self.assertEqual(errors, [], "\n".join(error.message for error in errors))

    def test_native_desktop_progression_uses_progression_contract(self):
        from jsonschema import Draft202012Validator

        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "NativeProgression",
                "Tests.NativeProgression",
                "Native Progression",
                author="Test Author",
                loader="fuse",
            )
            progression = ProgressionProject(project)
            self.assertIs(progression._prog_layer, project.get_graph_layer())
            progression.add_feature("feature.test", "Test Feature")
            progression.add_section(
                "section.test", "Test Section", [], 2500, "feature.test"
            )
            progression.save()

            saved = json.loads(
                project.get_graph_layer().path.read_text(encoding="utf-8")
            )
            self.assertNotIn("mapFeatures", saved)
            self.assertNotIn("progressions", saved)
            native = saved["progression"]
            self.assertIn("feature.test", native["mapFeatures"])
            progression_id = "Tests.NativeProgression"
            section = native["progressions"][progression_id]["sections"][
                "section.test"
            ]
            self.assertEqual(
                section["enableFeaturesOnUnlock"], ["feature.test"]
            )
            self.assertEqual(section["prerequisiteSectionIds"], [])

            schema_path = (
                Path(__file__).resolve().parents[2]
                / "FUSE" / "schemas" / "fuse-mod.schema.json"
            )
            schema = json.loads(schema_path.read_text(encoding="utf-8"))
            errors = list(Draft202012Validator(schema).iter_errors(saved))
            self.assertEqual(errors, [], "\n".join(error.message for error in errors))

    def test_new_mod_rejects_invalid_id_and_non_empty_target(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            with self.assertRaises(ValueError):
                ModProject.new_mod(
                    root / "Invalid",
                    "Invalid Mod-ID",
                    "Invalid",
                )

            occupied = root / "Occupied"
            occupied.mkdir()
            (occupied / "keep.txt").write_text("keep", encoding="utf-8")
            with self.assertRaises(FileExistsError):
                ModProject.new_mod(
                    occupied,
                    "Tests.Occupied",
                    "Occupied",
                )
            self.assertEqual(
                (occupied / "keep.txt").read_text(encoding="utf-8"),
                "keep",
            )

    def test_create_node_is_saved_merged_selected_and_visible(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.TrackLaying",
                "Track Laying Test",
                loader="railloader",
            )
            graph = project.get_graph_layer()

            editor = self._make_editor(project)
            editor.place_y_lock = False
            editor.place_y_inherit = False
            editor.show_tracks = False
            editor.show_nodes = False
            editor.screen_to_unity = lambda _sx, _sy: (125.0, 250.0)
            editor._resolve_measure_anchor = lambda: None
            editor._apply_measure_constraints = (
                lambda ux, uz, anchor=None: (ux, uz, None)
            )
            editor._sample_terrain_y = lambda _ux, _uz: 42.5

            TileEditor.create_node_at(editor, 10.0, 20.0)

            node_id = editor.sel_mod_node_id
            self.assertIsNotNone(node_id)
            self.assertIn(node_id, graph.nodes)
            self.assertIn(node_id, project.merged_nodes)
            self.assertTrue(editor.show_tracks)
            self.assertTrue(editor.show_nodes)

            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            saved_node = saved["tracks"]["nodes"][node_id]
            self.assertEqual(
                saved_node["position"],
                {"x": 125.0, "y": 42.5, "z": 250.0},
            )

    def test_connect_delete_and_undo_are_saved_and_merged(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.TrackEditing",
                "Track Editing Test",
            )
            graph = project.get_graph_layer()
            graph.set_node("node-1", 0.0, 10.0, 0.0, 0.0, 0.0, 0.0, False)
            graph.set_node("node-2", 100.0, 10.0, 0.0, 0.0, 0.0, 0.0, False)
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor._connect_from_node = "node-1"
            TileEditor.finish_connect(editor, "node-2")

            segment_id = editor.sel_mod_seg_id
            self.assertIsNotNone(segment_id)
            self.assertIn(segment_id, project.merged_segments)
            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertEqual(
                saved["tracks"]["segments"][segment_id]["startId"],
                "node-1",
            )
            self.assertEqual(
                saved["tracks"]["segments"][segment_id]["endId"],
                "node-2",
            )

            editor._mod_undo_stack.clear()
            TileEditor.delete_selected(editor)
            self.assertNotIn(segment_id, project.merged_segments)
            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertIsNone(saved["tracks"]["segments"][segment_id])

            TileEditor._pop_undo(editor)
            self.assertIn(segment_id, project.merged_segments)
            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertIsInstance(
                saved["tracks"]["segments"][segment_id],
                dict,
            )

    def test_node_move_is_saved_merged_and_undoable(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.NodeMove",
                "Node Move Test",
            )
            graph = project.get_graph_layer()
            graph.set_node("node-1", 1.0, 5.0, 2.0, 0.0, 45.0, 0.0, False)
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.sel_mod_node_id = "node-1"
            editor._sample_terrain_y = lambda _ux, _uz: 12.5

            TileEditor._commit_node_drag(editor, "node-1", 20.0, 30.0)

            moved = project.merged_nodes["node-1"]
            self.assertEqual(
                (moved["x"], moved["y"], moved["z"]),
                (20.0, 12.5, 30.0),
            )
            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertEqual(
                saved["tracks"]["nodes"]["node-1"]["position"],
                {"x": 20.0, "y": 12.5, "z": 30.0},
            )

            TileEditor._pop_undo(editor)
            restored = project.merged_nodes["node-1"]
            self.assertEqual(
                (restored["x"], restored["y"], restored["z"]),
                (1.0, 5.0, 2.0),
            )

    def test_scenery_transform_is_saved_merged_and_undoable(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.SceneryPlacement",
                "Scenery Placement Test",
            )
            graph = project.get_graph_layer()
            editor = self._make_editor(project)
            editor.scenery_place_model = "freight-house-general"
            editor.scenery_place_rotY = 135.0
            editor.scenery_place_scale = 1.7
            editor.screen_to_unity = lambda _sx, _sy: (321.0, 654.0)
            editor._sample_terrain_y = lambda _ux, _uz: 17.25

            TileEditor._place_scenery_at(editor, 10.0, 20.0)

            scenery_id = editor.sel_scenery_id
            self.assertIsNotNone(scenery_id)
            self.assertIn(scenery_id, project.merged_scenery)
            placed = project.merged_scenery[scenery_id]
            self.assertEqual(
                placed["position"],
                {"x": 321.0, "y": 17.25, "z": 654.0},
            )
            self.assertEqual(
                placed["rotation"],
                {"x": 0.0, "y": 135.0, "z": 0.0},
            )
            self.assertEqual(
                placed["scale"],
                {"x": 1.7, "y": 1.7, "z": 1.7},
            )

            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertEqual(
                saved["scenery"][scenery_id]["rotation"]["y"],
                135.0,
            )
            self.assertEqual(
                saved["scenery"][scenery_id]["scale"],
                {"x": 1.7, "y": 1.7, "z": 1.7},
            )

            TileEditor._pop_undo(editor)
            self.assertNotIn(scenery_id, project.merged_scenery)
            self.assertIsNone(editor.sel_scenery_id)
            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertNotIn(scenery_id, saved.get("scenery", {}))

    def test_trestle_samples_the_exact_3d_track_bezier(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.TrestleCurve",
                "Trestle Curve Test",
            )
            graph = project.get_graph_layer()
            graph.set_node(
                "start", 0.0, 100.0, 0.0, -1.0, 80.0, 2.0, False
            )
            graph.set_node(
                "end", 42.0, 103.0, 2.0, -3.0, 104.0, -2.0, False
            )
            graph.set_segment(
                "bridge",
                "start",
                "end",
                "Mainline",
                "Bridge",
                15,
                0,
                "",
            )
            project._rebuild_merge()

            segment = project.merged_segments["bridge"]
            trestle_id = create_trestle_from_segment(
                graph,
                segment,
                project.merged_nodes,
                "TEST",
            )
            points = graph.splineys[trestle_id]["points"]
            self.assertGreater(len(points), 2)

            n0 = project.merged_nodes["start"]
            n1 = project.merged_nodes["end"]
            p0, p1, p2, p3 = _bezier_control_points(n0, n1)
            for index, point in enumerate(points):
                t = index / (len(points) - 1)
                expected = _cubic_point(p0, p1, p2, p3, t)
                actual = point["position"]
                self.assertAlmostEqual(actual["x"], expected[0], places=6)
                self.assertAlmostEqual(
                    actual["y"], expected[1] - 0.3, places=6
                )
                self.assertAlmostEqual(actual["z"], expected[2], places=6)

    def test_existing_trestle_can_be_refit_to_nearest_track(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.TrestleRefit",
                "Trestle Refit Test",
            )
            graph = project.get_graph_layer()
            graph.set_node(
                "start", 0.0, 100.0, 0.0, 0.0, 80.0, 0.0, False
            )
            graph.set_node(
                "end", 42.0, 102.0, 2.0, -2.0, 104.0, 0.0, False
            )
            graph.set_segment(
                "bridge",
                "start",
                "end",
                "Mainline",
                "Bridge",
                15,
                0,
                "",
            )
            legacy = {
                "handler": "StrangeCustoms.AutoTrestleBuilder",
                "points": [
                    {
                        "position": {"x": 0.0, "y": 99.7, "z": 0.0},
                        "rotation": {"x": 0.0, "y": 80.0, "z": 0.0},
                    },
                    {
                        "position": {"x": 42.0, "y": 101.7, "z": 2.0},
                        "rotation": {"x": -2.0, "y": 104.0, "z": 0.0},
                    },
                ],
                "headstyle": "bent",
                "tailstyle": "bent",
            }
            graph._raw.setdefault("splineys", {})["legacy"] = legacy
            graph.splineys["legacy"] = copy.deepcopy(legacy)
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.sel_spliney_id = "legacy"
            editor.sel_spliney_layer = project.layers.index(graph)
            editor.sel_spliney_pt = 0
            editor.sel_mod_seg_id = "bridge"

            TileEditor._fit_selected_trestle_to_track(editor)

            points = graph.splineys["legacy"]["points"]
            self.assertGreater(len(points), 2)
            self.assertIn("legacy", project.merged_splineys)
            self.assertEqual(
                graph.splineys["legacy"]["headstyle"],
                "bent",
            )

    def test_vertical_alignment_transitions_grade_continuously(self):
        result = build_vertical_alignment(
            [0.0, 50.0, 100.0, 200.0, 250.0, 300.0],
            start_y=100.0,
            start_grade_pct=0.0,
            target_grade_pct=2.0,
            end_grade_pct=0.0,
            transition_in_m=100.0,
            transition_out_m=100.0,
        )

        self.assertEqual(result["errors"], [])
        points = result["points"]
        expected = [
            (100.0, 0.0),
            (100.25, 1.0),
            (101.0, 2.0),
            (103.0, 2.0),
            (103.75, 1.0),
            (104.0, 0.0),
        ]
        for point, (expected_y, expected_grade) in zip(points, expected):
            self.assertAlmostEqual(point["y"], expected_y, places=6)
            self.assertAlmostEqual(
                point["grade_pct"], expected_grade, places=6
            )

    def test_vertical_alignment_rejects_overlapping_transitions(self):
        result = build_vertical_alignment(
            [0.0, 150.0],
            start_y=0.0,
            start_grade_pct=0.0,
            target_grade_pct=2.0,
            end_grade_pct=0.0,
            transition_in_m=100.0,
            transition_out_m=100.0,
        )

        self.assertTrue(result["errors"])
        self.assertEqual(result["points"], [])

    def test_grade_pitch_respects_node_facing_direction(self):
        facing_with_chain = [
            {"x": 0.0, "z": 0.0, "rotY": 90.0, "rotX": 0.0},
            {"x": 100.0, "z": 0.0, "rotY": 90.0, "rotX": 0.0},
        ]
        facing_against_chain = [
            {"x": 0.0, "z": 0.0, "rotY": 270.0, "rotX": 0.0},
            {"x": 100.0, "z": 0.0, "rotY": 270.0, "rotX": 0.0},
        ]
        pitch = math.degrees(math.atan(0.02))

        self.assertAlmostEqual(
            TileEditor._grade_pitch_for_node(
                facing_with_chain, 0, 2.0
            ),
            -pitch,
            places=6,
        )
        self.assertAlmostEqual(
            TileEditor._grade_pitch_for_node(
                facing_against_chain, 0, 2.0
            ),
            pitch,
            places=6,
        )

    def test_turnout_leg_uses_circular_chord_geometry(self):
        x, y, z, rot_x, rot_y = turnout_leg_pose(
            0.0,
            100.0,
            0.0,
            0.0,
            10.0,
            30.0,
            grade_pct=2.0,
        )

        self.assertAlmostEqual(math.hypot(x, z), 30.0, places=6)
        self.assertAlmostEqual(
            math.degrees(math.atan2(x, z)),
            5.0,
            places=6,
        )
        self.assertAlmostEqual(rot_y, 10.0, places=6)
        self.assertAlmostEqual(y, 100.6, places=6)
        self.assertAlmostEqual(
            rot_x,
            -math.degrees(math.atan(0.02)),
            places=6,
        )
        self.assertAlmostEqual(
            turnout_radius_for_chord(30.0, 10.0),
            30.0 / (2.0 * math.sin(math.radians(5.0))),
            places=6,
        )

    def test_generated_turnout_holds_grade_across_all_legs(self):
        nodes, segments, switch_id, entry_id, through_id, diverge_id = (
            generate_turnout(
                0.0,
                100.0,
                0.0,
                0.0,
                diverge_angle=10.0,
                leg_length=30.0,
                direction="right",
                through_curve_angle=4.0,
                grade_pct=2.0,
                existing_ids=set(),
            )
        )
        by_id = {node["id"]: node for node in nodes}
        expected_pitch = math.degrees(math.atan(0.02))

        self.assertEqual(len(segments), 3)
        self.assertAlmostEqual(by_id[switch_id]["y"], 100.0, places=6)
        self.assertAlmostEqual(by_id[entry_id]["y"], 99.4, places=6)
        self.assertAlmostEqual(by_id[through_id]["y"], 100.6, places=6)
        self.assertAlmostEqual(by_id[diverge_id]["y"], 100.6, places=6)
        self.assertAlmostEqual(
            by_id[switch_id]["rotX"], -expected_pitch, places=6
        )
        self.assertAlmostEqual(
            by_id[entry_id]["rotX"], expected_pitch, places=6
        )
        self.assertAlmostEqual(
            by_id[through_id]["rotY"], 4.0, places=6
        )
        self.assertAlmostEqual(
            by_id[diverge_id]["rotY"], 10.0, places=6
        )

    def test_generated_wye_uses_circular_chords_and_holds_grade(self):
        nodes, segments, switch_id, entry_id, left_id, right_id = (
            generate_wye(
                0.0,
                100.0,
                0.0,
                0.0,
                left_angle=8.0,
                right_angle=12.0,
                leg_length=30.0,
                grade_pct=2.0,
                existing_ids=set(),
            )
        )
        by_id = {node["id"]: node for node in nodes}
        self.assertEqual(len(segments), 3)
        self.assertAlmostEqual(by_id[entry_id]["y"], 99.4, places=6)
        self.assertAlmostEqual(by_id[left_id]["y"], 100.6, places=6)
        self.assertAlmostEqual(by_id[right_id]["y"], 100.6, places=6)
        self.assertAlmostEqual(
            math.degrees(
                math.atan2(by_id[left_id]["x"], by_id[left_id]["z"])
            ),
            -4.0,
            places=6,
        )
        self.assertAlmostEqual(
            math.degrees(
                math.atan2(by_id[right_id]["x"], by_id[right_id]["z"])
            ),
            6.0,
            places=6,
        )
        self.assertAlmostEqual(by_id[switch_id]["rotY"], 0.0, places=6)
        self.assertAlmostEqual(by_id[left_id]["rotY"], 352.0, places=6)
        self.assertAlmostEqual(by_id[right_id]["rotY"], 12.0, places=6)

    def test_turnout_preview_on_forward_segment_faces_forward_and_holds_grade(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.Turnout",
                "Turnout Test",
            )
            graph = project.get_graph_layer()
            graph.set_node(
                "switch", 0.0, 100.0, 0.0, 0.0, 0.0, 0.0, False
            )
            graph.set_node(
                "forward", 0.0, 102.0, 100.0, 0.0, 0.0, 0.0, False
            )
            graph.set_segment(
                "existing",
                "switch",
                "forward",
                "Mainline",
                "Standard",
                45,
                0,
                "",
            )
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.sel_mod_node_id = "switch"
            editor.turnout_direction = "right"
            editor.turnout_diverge_angle = 10.0
            editor.turnout_leg_length = 30.0
            editor.turnout_track_class = "Mainline"
            editor.turnout_div_class = "Branch"
            editor.turnout_speed = 45
            editor.turnout_div_speed = 15
            editor.turnout_flip = False
            editor.turnout_through_curve = 0.0
            editor.turnout_min_leg_m = 18.0
            editor.turnout_warn_angle_deg = 12.0
            editor.turnout_max_angle_deg = 15.0
            editor.alignment_min_radius_m = 60.0
            editor.geo_preview = []
            editor.geo_preview_meta = {}

            TileEditor._commit_turnout_preview(editor)

            nodes_out, segments_out, updates = editor.geo_preview[0]
            switch_update = updates[0]
            expected_pitch = -math.degrees(math.atan(0.02))
            self.assertAlmostEqual(
                switch_update["rotY"], 0.0, places=6
            )
            self.assertAlmostEqual(
                switch_update["rotX"], expected_pitch, places=6
            )

            diverge_segment = next(
                segment for segment in segments_out
                if segment["trackClass"] == "Branch"
            )
            diverge_node = next(
                node for node in nodes_out
                if node["id"] == diverge_segment["endId"]
            )
            entry_segment = next(
                segment for segment in segments_out
                if segment["endId"] == "switch"
            )
            entry_node = next(
                node for node in nodes_out
                if node["id"] == entry_segment["startId"]
            )

            self.assertAlmostEqual(diverge_node["y"], 100.6, places=6)
            self.assertAlmostEqual(entry_node["y"], 99.4, places=6)
            self.assertEqual(editor.geo_preview_meta["errors"], [])
            self.assertAlmostEqual(
                editor.geo_preview_meta["approach_grade_pct"],
                2.0,
                places=6,
            )

    def test_forward_endpoint_wye_stays_three_way_and_holds_grade(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.Wye",
                "Wye Test",
            )
            graph = project.get_graph_layer()
            graph.set_node(
                "switch", 0.0, 100.0, 0.0, 0.0, 180.0, 0.0, False
            )
            graph.set_node(
                "approach", 0.0, 99.0, -100.0, 0.0, 180.0, 0.0, False
            )
            graph.set_segment(
                "existing",
                "switch",
                "approach",
                "Mainline",
                "Standard",
                25,
                0,
                "",
            )
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.sel_mod_node_id = "switch"
            editor.wye_left_angle = 10.0
            editor.wye_right_angle = 10.0
            editor.wye_leg_length = 30.0
            editor.wye_track_class = "Mainline"
            editor.wye_style = "Standard"
            editor.wye_speed = 25
            editor.wye_flip = False
            editor.alignment_min_radius_m = 60.0
            editor.geo_preview = []
            editor.geo_preview_meta = {}

            TileEditor._generate_wye_preview(editor)

            nodes_out, segments_out, updates = editor.geo_preview[0]
            self.assertEqual(len(nodes_out), 2)
            self.assertEqual(len(segments_out), 2)
            self.assertEqual(editor.geo_preview_meta["errors"], [])
            self.assertAlmostEqual(
                editor.geo_preview_meta["approach_grade_pct"],
                1.0,
                places=6,
            )
            expected_pitch = -math.degrees(math.atan(0.01))
            self.assertAlmostEqual(updates[0]["rotX"], expected_pitch, places=6)
            for node in nodes_out:
                self.assertAlmostEqual(node["y"], 100.3, places=6)

    def test_turnout_segment_insert_saves_once_and_undo_restores_track(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.TurnoutInsert",
                "Turnout Insert Test",
            )
            graph = project.get_graph_layer()
            graph.set_node(
                "start", 0.0, 100.0, 0.0, 0.0, 0.0, 0.0, False
            )
            graph.set_node(
                "end", 0.0, 102.0, 100.0, 0.0, 0.0, 0.0, False
            )
            graph.set_node(
                "switch", 0.0, 101.0, 50.0, 0.0, 0.0, 0.0, False
            )
            graph.set_segment(
                "original",
                "start",
                "end",
                "Mainline",
                "Standard",
                45,
                0,
                "",
            )
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.turnout_direction = "right"
            editor.turnout_diverge_angle = 10.0
            editor.turnout_leg_length = 30.0
            editor.turnout_div_class = "Branch"
            editor.turnout_div_speed = 15
            editor.turnout_flip = False
            editor.turnout_min_leg_m = 18.0
            editor.turnout_max_angle_deg = 15.0
            editor.alignment_min_radius_m = 60.0

            save_calls = []
            original_save = graph.save

            def counted_save(*args, **kwargs):
                save_calls.append(True)
                return original_save(*args, **kwargs)

            graph.save = counted_save
            TileEditor._push_undo(editor, "turnout switch into original")
            result = TileEditor._insert_turnout_into_segment(
                editor, "switch", "original"
            )

            self.assertTrue(result)
            self.assertEqual(len(save_calls), 1)
            live_segments = {
                segment_id: segment
                for segment_id, segment in graph.segments.items()
                if segment and not segment.get("deleted")
            }
            self.assertEqual(len(live_segments), 3)
            self.assertTrue(any(
                segment.get("trackClass") == "Branch"
                for segment in live_segments.values()
            ))
            self.assertNotIn("original", project.merged_segments)

            TileEditor._pop_undo(editor)

            self.assertIn("original", project.merged_segments)
            self.assertEqual(
                project.merged_segments["original"]["startId"], "start"
            )
            self.assertEqual(
                project.merged_segments["original"]["endId"], "end"
            )
            self.assertEqual(len(editor._mod_undo_stack), 0)

    def test_smooth_grade_button_path_updates_y_and_pitch(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.SmoothGrade",
                "Smooth Grade Test",
            )
            graph = project.get_graph_layer()
            nodes = [
                ("node-1", 0.0, 100.0),
                ("node-2", 100.0, 130.0),
                ("node-3", 200.0, 104.0),
            ]
            for index, (node_id, x, y) in enumerate(nodes):
                graph.set_node(
                    node_id, x, y, 0.0, 0.0, 90.0, 0.0, False
                )
                if index:
                    graph.set_segment(
                        f"segment-{index}",
                        nodes[index - 1][0],
                        node_id,
                        "Mainline",
                        "Standard",
                        45,
                        0,
                        "",
                    )
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.grade_chain = [node[0] for node in nodes]
            editor.grade_fix_first = True
            editor.grade_fix_last = True

            TileEditor._commit_grade_smooth(editor)

            self.assertAlmostEqual(
                project.merged_nodes["node-2"]["y"], 102.0
            )
            expected_pitch = -math.degrees(math.atan(0.02))
            self.assertAlmostEqual(
                project.merged_nodes["node-2"]["rotX"],
                expected_pitch,
                places=6,
            )

    def test_vertical_curve_apply_saves_elevation_pitch_and_undo(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            project = ModProject.new_mod(
                Path(temp_dir) / "TestMod",
                "Tests.VerticalCurve",
                "Vertical Curve Test",
            )
            graph = project.get_graph_layer()
            node_ids = ["node-1", "node-2", "node-3", "node-4"]
            for index, node_id in enumerate(node_ids):
                graph.set_node(
                    node_id,
                    float(index * 100),
                    100.0,
                    0.0,
                    0.0,
                    90.0,
                    0.0,
                    False,
                )
                if index:
                    graph.set_segment(
                        f"segment-{index}",
                        node_ids[index - 1],
                        node_id,
                        "Mainline",
                        "Standard",
                        45,
                        0,
                        "",
                    )
            project._rebuild_merge()
            graph.save()

            editor = self._make_editor(project)
            editor.grade_chain = node_ids
            editor.grade_start_pct = 0.0
            editor.grade_target_pct = 2.0
            editor.grade_end_pct = 0.0
            editor.grade_transition_in_m = 100.0
            editor.grade_transition_out_m = 100.0
            editor.grade_transition_preview_active = True
            editor._profile_cache_key = None
            editor._profile_cache_data = None

            profile = build_vertical_alignment(
                [0.0, 100.0, 200.0, 300.0],
                start_y=100.0,
                start_grade_pct=0.0,
                target_grade_pct=2.0,
                end_grade_pct=0.0,
                transition_in_m=100.0,
                transition_out_m=100.0,
            )
            node_points = [
                dict(point, node_id=node_id)
                for point, node_id in zip(profile["points"], node_ids)
            ]
            editor._build_profile_data = lambda: {
                "vertical_preview": dict(profile, node_points=node_points)
            }

            TileEditor._commit_grade_transition(editor)

            self.assertAlmostEqual(
                project.merged_nodes["node-1"]["y"], 100.0
            )
            self.assertAlmostEqual(
                project.merged_nodes["node-2"]["y"], 101.0
            )
            self.assertAlmostEqual(
                project.merged_nodes["node-3"]["y"], 103.0
            )
            self.assertAlmostEqual(
                project.merged_nodes["node-4"]["y"], 104.0
            )
            expected_pitch = -math.degrees(math.atan(0.02))
            self.assertAlmostEqual(
                project.merged_nodes["node-2"]["rotX"],
                expected_pitch,
                places=6,
            )
            saved = json.loads(graph.path.read_text(encoding="utf-8"))
            self.assertAlmostEqual(
                saved["tracks"]["nodes"]["node-2"]["rotation"]["x"],
                expected_pitch,
                places=6,
            )

            TileEditor._pop_undo(editor)
            for node_id in node_ids:
                self.assertAlmostEqual(
                    project.merged_nodes[node_id]["y"], 100.0
                )

    def test_bridge_fingerprint_changes_when_existing_node_moves(self):
        original = BridgeState(
            {
                "nodes": [
                    {
                        "id": "node-1",
                        "x": 10,
                        "y": 20,
                        "z": 30,
                        "rotX": 0,
                        "rotY": 90,
                        "rotZ": 0,
                    }
                ],
                "segments": [],
                "cars": [],
            }
        )
        moved = BridgeState(
            {
                "nodes": [
                    {
                        "id": "node-1",
                        "x": 11,
                        "y": 20,
                        "z": 30,
                        "rotX": 0,
                        "rotY": 90,
                        "rotZ": 0,
                    }
                ],
                "segments": [],
                "cars": [],
            }
        )

        self.assertNotEqual(
            BridgeMixin._bridge_track_fingerprint(original),
            BridgeMixin._bridge_track_fingerprint(moved),
        )

    def test_bridge_reload_is_deferred_when_auto_save_is_off(self):
        harness = _BridgeHarness()
        bridge = harness._configure_bridge(_FakeBridge())

        bridge.reload_tracks("Mods/Test/game-graph.json")

        self.assertEqual(bridge.reload_requests, [])
        self.assertEqual(
            harness._pending_bridge_reload_paths,
            {"Mods/Test/game-graph.json"},
        )

        harness.live_mod_apply = True
        bridge.reload_tracks("Mods/Test/game-graph.json")
        self.assertEqual(
            bridge.reload_requests,
            ["Mods/Test/game-graph.json"],
        )


if __name__ == "__main__":
    unittest.main()
