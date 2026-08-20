import json
import tempfile
import unittest
import zipfile
from pathlib import Path

from jsonschema import Draft202012Validator

from edit_tiles.generate import sync_map_json_tile_list
from mod_project import ModProject, mandela_set, scenery_set, spliney_add_road
from mod_project.validation import export_clean_zip, validate_mod


class CompleteMapWorkflowTests(unittest.TestCase):
    def test_native_map_can_be_authored_reopened_validated_and_exported(self):
        """Golden desktop workflow for a small but complete standalone map."""
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            folder = root / "CompleteMap"
            project = ModProject.new_mod(
                folder,
                "Tests.CompleteMap",
                "Complete Map",
                author="Test Author",
                loader="fuse",
                complete_map=True,
                map_origin_lat=40.43,
                map_origin_lon=-77.72,
            )
            layer = project.get_graph_layer()

            # Terrain manifest and one generated tile.
            map_folder = folder / "Map"
            (map_folder / "tile_000_000.data").write_bytes(b"terrain-fixture")
            sync_map_json_tile_list(
                map_folder,
                origin_lat=40.43,
                origin_lon=-77.72,
                tile_dimension_m=500,
                origin_e_bias=0,
                origin_n_bias=0,
            )

            # Track, a branch, span, area, and an interchange-capable industry.
            layer.set_node("n-west", 0, 10, 0, 0, 90, 0)
            layer.set_node("n-east", 100, 11, 0, 0, 90, 0)
            layer.set_node("n-yard", 100, 10, 40, 0, 0, 0)
            layer.set_segment("s-main", "n-west", "n-east", "Mainline")
            layer.set_segment("s-yard", "n-east", "n-yard", "Industrial")
            tracks = layer._raw["tracks"]
            tracks["spans"]["town-track"] = {
                "lower": {"segmentId": "s-main", "distance": 0},
                "upper": {"segmentId": "s-main", "distance": 100},
            }
            tracks["areas"] = {
                "test-town": {
                    "name": "Test Town",
                    "position": {"x": 50, "y": 10, "z": 10},
                    "radius": 250,
                },
            }

            layer._raw["operations"] = {
                "loads": {
                    "freight": {"name": "Freight", "units": "Quantity"},
                    "passengers": {"name": "Passengers", "units": "Quantity"},
                },
                "industries": {
                    "test-depot": {
                        "name": "Test Depot",
                        "areaId": "test-town",
                        "position": {"x": 50, "y": 10, "z": 5},
                        "components": {
                            "freight-loader": {
                                "type": "loader",
                                "name": "Freight Loader",
                                "trackSpanIds": ["town-track"],
                                "loadId": "freight",
                                "maxStorage": 100,
                            },
                            "interchange": {
                                "type": "interchange",
                                "name": "Interchange",
                                "trackSpanIds": ["town-track"],
                            },
                            "passenger": {
                                "type": "passengerStop",
                                "name": "Test Depot",
                                "trackSpanIds": ["town-track"],
                                "loadId": "passengers",
                                "passengerStopId": "test-stop",
                                "timetableCode": "TST",
                                "neighborIds": [],
                            },
                        },
                    },
                },
                "stations": {
                    "test-station-agent": {
                        "passengerStopId": "test-stop",
                        "prefab": "empty://stationAgent",
                        "position": {"x": 50, "y": 10, "z": 4},
                    },
                },
                "loaders": {
                    "test-water": {
                        "industryId": "test-depot",
                        "prefab": "vanilla://waterTower",
                        "position": {"x": 60, "y": 10, "z": 3},
                    },
                },
            }

            # Scenery, road, a moved/cloned town sign, visible lake, and a
            # player-selectable modular scenery section.
            scenery_set(layer, "test-building", "scenery://freight-house-general", 45, 10, 12)
            scenery_set(layer, "custom-service", "asset://test-fuel-stand", 60, 10, 3)
            spliney_add_road(layer, "town-road", "RAM Road profile", [
                {"position": {"x": 0, "y": 9, "z": 20}, "rotation": {"x": 0, "y": 90, "z": 0}, "width": 6},
                {"position": {"x": 100, "y": 9, "z": 20}, "rotation": {"x": 0, "y": 90, "z": 0}, "width": 6},
            ])
            mandela_set(
                layer,
                "World/Test/TownSign",
                instantiate_from="World/Base/TownSign",
                x=40,
                y=10,
                z=2,
            )
            world = layer._raw.setdefault("world", {})
            world["waterSurfaces"] = {
                "test-lake": {
                    "points": [
                        {"x": 0, "y": 8, "z": 60},
                        {"x": 0, "y": 8, "z": 100},
                        {"x": 40, "y": 8, "z": 100},
                        {"x": 40, "y": 8, "z": 60},
                    ],
                },
            }
            layer._raw["settings"] = {
                "showTownBuilding": {
                    "type": "bool",
                    "label": "Show town building",
                    "default": True,
                    "reloadRequired": True,
                },
            }
            layer._raw["featureRules"] = {
                "optionalTownBuilding": {
                    "setting": "showTownBuilding",
                    "operator": "equals",
                    "value": True,
                    "targets": {"scenery": ["test-building"]},
                },
            }
            layer._raw["progression"] = {
                "sections": [{"id": "test-section", "displayName": "Test Section"}],
            }
            layer.dirty = True

            # Portable signal/CTC documents and a Toolshed custom-loader binding.
            info = project.definition
            info["Requirements"].extend([{"Id": "AITraffic"}, {"Id": "Toolshed"}])
            info["LoadAfter"].extend(["AITraffic", "Toolshed"])
            (folder / "train-signals.json").write_text(json.dumps({
                "formatVersion": 1,
                "signals": [
                    {"id": "signal:west", "headCount": 1, "initialAspect": "stop", "direction": "forward", "protectedNodeId": "n-west", "protectedSegmentId": "s-main"},
                    {"id": "signal:east", "headCount": 1, "initialAspect": "stop", "direction": "reverse", "protectedNodeId": "n-east", "protectedSegmentId": "s-main"},
                ],
                "interlockings": [],
            }, indent=2), encoding="utf-8")
            (folder / "ctc-system.json").write_text(json.dumps({
                "formatVersion": 1,
                "territories": [{"id": "territory:main", "name": "Main", "mode": "ctc", "controlPointIds": ["cp:yard"], "blockIds": ["block:main"]}],
                "controlPoints": [{
                    "id": "cp:yard",
                    "name": "Yard",
                    "switches": [{"nodeId": "n-east"}],
                    "routes": [
                        {"id": "normal", "entrySignalId": "signal:west", "blockIds": ["block:main"], "switchSettings": [{"nodeId": "n-east", "thrown": False}]},
                        {"id": "reverse", "entrySignalId": "signal:east", "blockIds": ["block:main"], "switchSettings": [{"nodeId": "n-east", "thrown": True}]},
                    ],
                }],
                "blocks": [{"id": "block:main", "name": "Main", "mode": "ctc", "segmentIds": ["s-main"], "signals": {"a": "signal:west", "b": "signal:east"}, "nextBlocks": {"fromA": "", "fromB": ""}}],
                "trainOrders": [],
            }, indent=2), encoding="utf-8")
            (folder / "ToolshedServiceFacilities.json").write_text(json.dumps({
                "facilities": [{
                    "id": "test-fuel",
                    "targetObjectName": "custom-service",
                    "modelIdentifier": "asset://test-fuel-stand",
                    "serviceLoadId": "diesel",
                    "sourceIndustryId": "test-depot",
                    "trackSpanIds": ["town-track"],
                }],
            }, indent=2), encoding="utf-8")

            project.save_all(force=True)

            reopened = ModProject.open_mod_folder(folder)
            self.assertEqual(set(reopened.merged_nodes), {"n-west", "n-east", "n-yard"})
            self.assertEqual(set(reopened.merged_segments), {"s-main", "s-yard"})
            self.assertIn("test-building", reopened.merged_scenery)
            self.assertIn("town-road", reopened.merged_splineys)

            errors = [message for severity, message in validate_mod(folder) if severity == "error"]
            self.assertEqual(errors, [])

            schema = json.loads((Path(__file__).resolve().parents[2] / "FUSE" / "schemas" / "fuse-mod.schema.json").read_text(encoding="utf-8"))
            authored = json.loads((folder / "map.fuse.json").read_text(encoding="utf-8"))
            schema_errors = list(Draft202012Validator(schema).iter_errors(authored))
            self.assertEqual(schema_errors, [], "\n".join(error.message for error in schema_errors))

            archive = root / "CompleteMap.zip"
            self.assertTrue(export_clean_zip(folder, archive))
            with zipfile.ZipFile(archive) as package:
                names = set(package.namelist())
            prefix = "Tests.CompleteMap/"
            for required in (
                "Info.json", "map.fuse.json", "Map/Map.json",
                "Map/tile_000_000.data", "train-signals.json",
                "ctc-system.json", "ToolshedServiceFacilities.json",
            ):
                self.assertIn(prefix + required, names)


if __name__ == "__main__":
    unittest.main()
