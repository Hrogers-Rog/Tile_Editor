import json
import tempfile
import unittest
from pathlib import Path

from mod_project import ModProject
from mod_project.validation import validate_mod


class NativeOperationsValidationTests(unittest.TestCase):
    def _new_native(self, root: Path, *, complete_map=False) -> Path:
        folder = root / "NativeOperations"
        ModProject.new_mod(
            folder,
            "Tests.NativeOperations",
            "Native Operations",
            loader="fuse",
            complete_map=complete_map,
            map_origin_lat=40.0 if complete_map else None,
            map_origin_lon=-80.0 if complete_map else None,
        )
        return folder

    @staticmethod
    def _write_valid_operations(folder: Path):
        path = folder / "map.fuse.json"
        data = json.loads(path.read_text(encoding="utf-8"))
        data["tracks"].update({
            "nodes": {
                "n1": {"position": {"x": 0, "y": 0, "z": 0}},
                "n2": {"position": {"x": 100, "y": 0, "z": 0}},
            },
            "segments": {
                "s1": {"startNodeId": "n1", "endNodeId": "n2"},
            },
            "spans": {
                "station-span": {
                    "lower": {"segmentId": "s1", "distance": 0},
                    "upper": {"segmentId": "s1", "distance": 100},
                },
            },
            "areas": {
                "town": {"name": "Town"},
            },
        })
        data["operations"] = {
            "loads": {
                "passengers": {"name": "Passengers", "units": "people"},
            },
            "industries": {
                "depot-a": {
                    "name": "Depot A",
                    "areaId": "town",
                    "components": {
                        "passenger": {
                            "type": "passengerStop",
                            "name": "Depot A",
                            "trackSpanIds": ["station-span"],
                            "loadId": "passengers",
                            "passengerStopId": "stop-a",
                            "timetableCode": "A",
                            "neighborIds": ["stop-b"],
                        },
                    },
                },
                "depot-b": {
                    "name": "Depot B",
                    "areaId": "town",
                    "components": {
                        "passenger": {
                            "type": "passengerStop",
                            "name": "Depot B",
                            "trackSpanIds": ["station-span"],
                            "loadId": "passengers",
                            "passengerStopId": "stop-b",
                            "timetableCode": "B",
                            "neighborIds": ["stop-a"],
                        },
                    },
                },
            },
            "stations": {
                "agent-a": {
                    "passengerStopId": "stop-a",
                    "prefab": "empty://stationAgent",
                },
            },
            "loaders": {
                "water-a": {
                    "industryId": "depot-a",
                    "prefab": "vanilla://waterTower",
                },
            },
        }
        path.write_text(json.dumps(data, indent=2), encoding="utf-8")

    def test_valid_native_operations_have_no_reference_findings(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            self._write_valid_operations(folder)

            issues = validate_mod(folder)

            operation_issues = [
                message for _, message in issues
                if any(word in message for word in (
                    "Native industry", "Native station", "Native physical loader",
                    "Passenger stop", "Passenger link",
                ))
            ]
            self.assertEqual(operation_issues, [])

    def test_addon_unresolved_native_references_are_warnings(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            path = folder / "map.fuse.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            data["operations"] = {
                "industries": {
                    "patch": {
                        "areaId": "base-town",
                        "components": {
                            "dock": {
                                "type": "loader",
                                "trackSpanIds": ["base-span"],
                                "loadId": "base-load",
                            },
                        },
                    },
                },
            }
            path.write_text(json.dumps(data, indent=2), encoding="utf-8")

            issues = validate_mod(folder)

            reference_issues = [
                (severity, message) for severity, message in issues
                if "base-" in message
            ]
            self.assertTrue(reference_issues)
            self.assertTrue(all(severity == "warning" for severity, _ in reference_issues))
            self.assertTrue(all("may be supplied" in message for _, message in reference_issues))

    def test_standalone_map_requires_local_operations_references(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir), complete_map=True)
            path = folder / "map.fuse.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            data["operations"] = {
                "industries": {
                    "depot-a": {
                        "areaId": "missing-town",
                        "components": {
                            "passenger": {
                                "type": "passengerStop",
                                "trackSpanIds": ["missing-span"],
                                "loadId": "missing-load",
                                "passengerStopId": "same-stop",
                                "timetableCode": "DUP",
                            },
                        },
                    },
                    "depot-b": {
                        "areaId": "missing-town",
                        "components": {
                            "passenger": {
                                "type": "passengerStop",
                                "trackSpanIds": ["missing-span"],
                                "loadId": "missing-load",
                                "passengerStopId": "same-stop",
                                "timetableCode": "DUP",
                            },
                        },
                    },
                },
            }
            path.write_text(json.dumps(data, indent=2), encoding="utf-8")

            issues = validate_mod(folder)
            errors = [message for severity, message in issues if severity == "error"]

            self.assertTrue(any("missing-town" in message for message in errors))
            self.assertTrue(any("missing-span" in message for message in errors))
            self.assertTrue(any("same-stop" in message for message in errors))
            self.assertTrue(any("timetable code 'DUP'" in message for message in errors))
            self.assertTrue(all("standalone map" in message
                                for message in errors
                                if "missing-" in message))

    def test_native_passenger_branch_details_are_validated(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            self._write_valid_operations(folder)
            path = folder / "map.fuse.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            passenger = data["operations"]["industries"]["depot-a"][
                "components"]["passenger"]
            passenger["branchDefinitions"] = [{
                "branch": "Main",
                "traverseTimeToNext": -2,
                "intermediates": {
                    "flag-stop": {"code": "", "traverseTimeToNext": -1},
                },
            }]
            path.write_text(json.dumps(data, indent=2), encoding="utf-8")

            errors = [
                message for severity, message in validate_mod(folder)
                if severity == "error"
            ]

            self.assertTrue(any("branchDefinitions[0].traverseTimeToNext" in message
                                for message in errors))
            self.assertTrue(any("flag-stop' needs a code" in message
                                for message in errors))
            self.assertTrue(any("flag-stop' traverseTimeToNext" in message
                                for message in errors))

    def test_native_water_surfaces_validate_shape_and_tessellation(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            path = folder / "map.fuse.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            data.setdefault("world", {})["waterSurfaces"] = {
                "good-lake": {
                    "points": [
                        {"x": 0, "y": 10, "z": 0},
                        {"x": 0, "y": 10, "z": 20},
                        {"x": 20, "y": 10, "z": 20},
                    ],
                },
                "crossed-lake": {
                    "points": [
                        {"x": 0, "y": 10, "z": 0},
                        {"x": 20, "y": 10, "z": 20},
                        {"x": 0, "y": 10, "z": 20},
                        {"x": 20, "y": 10, "z": 0},
                    ],
                    "triangleDensity": 2,
                },
            }
            path.write_text(json.dumps(data, indent=2), encoding="utf-8")

            issues = validate_mod(folder)
            errors = [message for severity, message in issues if severity == "error"]

            self.assertFalse(any("good-lake" in message for message in errors))
            self.assertTrue(any("crossed-lake: triangleDensity must be at most 1" in message
                                for message in errors))
            self.assertTrue(any("crossed-lake: boundary crosses itself" in message
                                for message in errors))

    def test_native_feature_rule_accepts_authored_targets(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            path = folder / "map.fuse.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            data.setdefault("world", {}).setdefault("scenery", {})["optional-prop"] = {
                "assetIdentifier": "vanilla://prop",
            }
            data["settings"] = {
                "showProps": {
                    "type": "bool",
                    "label": "Show props",
                    "default": True,
                    "reloadRequired": True,
                },
            }
            data["featureRules"] = {
                "optionalProps": {
                    "setting": "showProps",
                    "operator": "equals",
                    "value": True,
                    "targets": {"scenery": ["optional-prop"]},
                },
            }
            path.write_text(json.dumps(data, indent=2), encoding="utf-8")

            issues = [message for _, message in validate_mod(folder)
                      if "feature rule" in message.lower()]

            self.assertEqual(issues, [])

    def test_native_feature_rule_reports_missing_setting_target_and_bad_operator(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            path = folder / "map.fuse.json"
            data = json.loads(path.read_text(encoding="utf-8"))
            data["featureRules"] = {
                "broken": {
                    "setting": "missing",
                    "operator": "greaterThan",
                    "value": 3,
                    "targets": {"scenery": ["missing-prop"]},
                },
            }
            path.write_text(json.dumps(data, indent=2), encoding="utf-8")

            errors = [message for severity, message in validate_mod(folder)
                      if severity == "error" and "feature rule" in message.lower()]

            self.assertTrue(any("setting 'missing'" in message for message in errors))
            self.assertTrue(any("number setting" in message for message in errors))
            self.assertTrue(any("missing-prop" in message for message in errors))

    def test_valid_portable_signals_and_ctc_cross_references_pass(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            self._write_valid_operations(folder)
            info_path = folder / "Info.json"
            info = json.loads(info_path.read_text(encoding="utf-8"))
            info.setdefault("Requirements", []).append({"Id": "AITraffic"})
            info_path.write_text(json.dumps(info, indent=2), encoding="utf-8")
            (folder / "train-signals.json").write_text(json.dumps({
                "formatVersion": 1,
                "signals": [
                    {
                        "id": "signal:a",
                        "headCount": 1,
                        "initialAspect": "stop",
                        "direction": "forward",
                        "protectedNodeId": "n1",
                        "protectedSegmentId": "s1",
                        "protectedSegmentIds": ["s1"],
                        "approachSegmentIds": ["s1"],
                        "trackAttachment": {
                            "locked": True,
                            "segmentId": "s1",
                            "parameter": 0.25,
                        },
                    },
                    {
                        "id": "signal:b",
                        "headCount": 1,
                        "initialAspect": "stop",
                        "direction": "reverse",
                        "protectedNodeId": "n2",
                        "protectedSegmentId": "s1",
                    },
                ],
                "interlockings": [],
            }, indent=2), encoding="utf-8")
            (folder / "ctc-system.json").write_text(json.dumps({
                "formatVersion": 1,
                "territories": [{
                    "id": "territory:main",
                    "name": "Main",
                    "mode": "ctc",
                    "controlPointIds": ["cp:one"],
                    "blockIds": ["block:one"],
                }],
                "controlPoints": [{
                    "id": "cp:one",
                    "name": "CP One",
                    "switches": [{"nodeId": "n1"}],
                    "routes": [
                        {
                            "id": "normal",
                            "entrySignalId": "signal:a",
                            "blockIds": ["block:one"],
                            "switchSettings": [
                                {"nodeId": "n1", "thrown": False},
                            ],
                        },
                        {
                            "id": "reverse",
                            "entrySignalId": "signal:b",
                            "blockIds": ["block:one"],
                            "switchSettings": [
                                {"nodeId": "n1", "thrown": True},
                            ],
                        },
                    ],
                }],
                "blocks": [{
                    "id": "block:one",
                    "name": "Block One",
                    "mode": "ctc",
                    "segmentIds": ["s1"],
                    "signals": {"a": "signal:a", "b": "signal:b"},
                    "nextBlocks": {"fromA": "", "fromB": ""},
                }],
                "trainOrders": [],
            }, indent=2), encoding="utf-8")

            signaling_issues = [
                message for _, message in validate_mod(folder)
                if "signal" in message.lower() or "ctc-system" in message.lower()
            ]

            self.assertEqual(signaling_issues, [])

    def test_signaling_validation_reports_dependency_schema_and_references(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            folder = self._new_native(Path(temp_dir))
            self._write_valid_operations(folder)
            (folder / "train-signals.json").write_text(json.dumps({
                "formatVersion": 1,
                "signals": [{
                    "id": "signal:broken",
                    "headCount": 4,
                    "initialAspect": "purple",
                    "direction": "sideways",
                    "protectedNodeId": "missing-node",
                    "protectedSegmentId": "missing-segment",
                }],
                "interlockings": [],
            }, indent=2), encoding="utf-8")
            (folder / "ctc-system.json").write_text(json.dumps({
                "formatVersion": 1,
                "territories": [],
                "controlPoints": [],
                "blocks": [],
            }, indent=2), encoding="utf-8")

            errors = [
                message for severity, message in validate_mod(folder)
                if severity == "error"
            ]

            self.assertTrue(any("must require Railroad Operations" in message
                                for message in errors))
            self.assertTrue(any("headCount" in message for message in errors))
            self.assertTrue(any("initialAspect" in message for message in errors))
            self.assertTrue(any("missing-node" in message for message in errors))
            self.assertTrue(any("missing-segment" in message for message in errors))
            self.assertTrue(any("trainOrders must be an array" in message
                                for message in errors))


if __name__ == "__main__":
    unittest.main()
