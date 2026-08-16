import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


def railroad_operations_signal_runtime_root():
    root = Path(__file__).resolve().parent.parent
    runtime = root.parent / "AI_Traffic" / "SignalRuntime"
    if not runtime.exists():
        raise unittest.SkipTest(
            "Railroad Operations SignalRuntime source is not checked out "
            "beside Tile Editor"
        )
    return runtime


class PackageVersionTests(unittest.TestCase):
    def test_packaged_launcher_finds_python_without_path_requirement(self):
        if os.name != "nt":
            self.skipTest("Windows launcher test")
        powershell = shutil.which("powershell.exe") or shutil.which(
            "powershell"
        )
        if not powershell:
            self.skipTest("Windows PowerShell is unavailable")

        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        finder = bridge / "Find Tile Editor Python.ps1"
        launch_source = (
            bridge / "Launch Tile Editor.bat"
        ).read_text(encoding="utf-8")
        repair_source = (
            bridge / "Repair Tile Editor Environment.bat"
        ).read_text(encoding="utf-8")
        package_source = (
            bridge / "package_complete_mod.ps1"
        ).read_text(encoding="utf-8")
        environment = os.environ.copy()
        environment["TILE_EDITOR_PYTHON"] = sys.executable
        result = subprocess.run(
            [
                powershell,
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                str(finder),
            ],
            check=False,
            capture_output=True,
            text=True,
            env=environment,
            timeout=20,
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            Path(result.stdout.strip()).resolve(),
            Path(sys.executable).resolve(),
        )
        self.assertIn(
            "TILE_EDITOR_PYTHON",
            finder.read_text(encoding="utf-8"),
        )
        self.assertIn("--diagnose-python", launch_source)
        self.assertIn("call :find_python", launch_source)
        self.assertIn("REBUILD_VENV", repair_source)
        self.assertIn("Find Tile Editor Python.ps1", package_source)
        self.assertIn(
            "Push-Location -LiteralPath $repoRoot",
            package_source,
        )
        self.assertIn(
            '& $pythonExe -m unittest discover -s "tests"',
            package_source,
        )
        self.assertIn("Pop-Location", package_source)

    def test_release_version_is_synchronized(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        expected = (bridge / "VERSION.txt").read_text(
            encoding="utf-8"
        ).strip()
        info = json.loads((bridge / "Info.json").read_text(encoding="utf-8"))
        manifest = json.loads(
            (bridge / "PackageManifest.json").read_text(encoding="utf-8")
        )
        suite_source = (bridge / "SuiteVersion.cs").read_text(
            encoding="utf-8"
        )
        desktop_source = (root / "edit_tiles" / "version.py").read_text(
            encoding="utf-8"
        )
        project = ET.parse(
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).getroot()
        project_version = project.findtext(".//Version")

        self.assertRegex(expected, r"^\d+\.\d+\.\d+$")
        self.assertEqual(info["Version"], expected)
        self.assertEqual(manifest["version"], expected)
        self.assertEqual(project_version, expected)
        self.assertEqual(
            re.search(r'Value = "([^"]+)"', suite_source).group(1),
            expected,
        )
        self.assertEqual(
            re.search(r'__version__ = "([^"]+)"', desktop_source).group(1),
            expected,
        )

    def test_desktop_dirty_terrain_count_is_thread_safe(self):
        root = Path(__file__).resolve().parent.parent
        app_source = (
            root / "edit_tiles" / "app.py"
        ).read_text(encoding="utf-8")
        reload_check = app_source.split(
            "def _reload_discard_items(self):", 1
        )[1].split(
            "def _has_reloadable_source", 1
        )[0]

        self.assertIn(
            "for tile in list(self.tiles.values())",
            reload_check,
        )
        self.assertNotIn(
            "for tile in self.tiles.values()",
            reload_check,
        )

    def test_in_game_graph_selection_does_not_require_desktop(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("LastGraphPathKey", panel_source)
        self.assertIn(
            "var editorOnline = IsEditorOnline();",
            panel_source,
        )
        self.assertIn(
            ": _preferredGraphPath;",
            panel_source,
        )
        self.assertIn("CHANGE MOD / GRAPH", geo_source)
        self.assertIn("CHOOSE A MOD / GAME GRAPH", geo_source)
        self.assertIn(
            "PlayerPrefs.SetString(",
            geo_source,
        )
        self.assertIn("HasUnsavedContent", graph_source)
        self.assertIn("RefreshGraphChoices()", graph_source)
        self.assertIn("Installed map mods", geo_source)
        self.assertIn("MORE LAYERS (", geo_source)
        self.assertIn("IsPrimary", graph_source)
        self.assertIn("GraphLayerPriority", graph_source)

    def test_operations_workspace_discovers_and_builds_dual_format_content(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorOperationsPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            bridge / "TileEditorOperationsSession.cs"
        ).read_text(encoding="utf-8")
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("PanelTab.Operations", geo_source)
        self.assertIn('"OPERATIONS"', geo_source)
        for label in (
            "Towns",
            "Spans",
            "Industries",
            "Passenger",
            "Facilities",
        ):
            self.assertIn(f'"{label}"', panel_source)
        self.assertIn(
            'DrawGeoToolTab("Turntable", GeoTool.Turntable)',
            geo_source,
        )
        self.assertIn(
            'DrawGeoToolTab("Span", GeoTool.Span)',
            geo_source,
        )
        self.assertIn(
            "case GeoTool.Turntable:",
            geo_source,
        )
        self.assertIn(
            "case GeoTool.Span:",
            geo_source,
        )
        self.assertIn("SearchOperations(", session_source)
        self.assertIn("DiscoverAreasAndSpans", session_source)
        self.assertIn("DiscoverFuseOperations", session_source)
        self.assertIn("DiscoverLegacyOperations", session_source)
        self.assertIn("CreateTown(", session_source)
        self.assertIn("CreateSpanFromSelectedSegment(", session_source)
        self.assertIn(
            "CreatePartialSpanOnSelectedSegment(",
            session_source,
        )
        self.assertIn(
            "CreateSpanBetweenSegments(",
            session_source,
        )
        self.assertIn(
            "SegmentsShareConnectedGraph(",
            session_source,
        )
        self.assertIn(
            '"CREATE PARTIAL SPAN"',
            panel_source,
        )
        self.assertIn(
            '"MARK SELECTED AS SPAN START"',
            panel_source,
        )
        self.assertIn("CreateIndustry(", session_source)
        self.assertIn("AddIndustryComponent(", session_source)
        self.assertIn("CreatePhysicalLoader(", session_source)
        self.assertIn("ExecuteOperationsEdit(", session_source)
        self.assertIn("BeforeDocument", session_source)
        self.assertIn(
            "_document = (JObject)beforeDocument.DeepClone();",
            session_source,
        )
        self.assertIn("AlinasMapMod.LoaderBuilder", session_source)
        self.assertIn("PointerPlacementKind.OperationsTown", pointer_source)
        self.assertIn(
            "PointerPlacementKind.OperationsPhysicalLoader",
            pointer_source,
        )
        for field in (
            "StorageChangeRate",
            "MaxStorage",
            "CarTransferRate",
            "OrderAroundEmpties",
            "OrderAroundLoaded",
            "FormulaInputs",
            "FormulaOutputs",
            "IdealCars",
            "TeamProfiles",
            "BasePopulation",
            "NeighborIds",
            "OutputSpanIds",
            "ConvertedLoadId",
            "CustomFieldsJson",
        ):
            self.assertIn(field, session_source)
        self.assertIn("ParseOperationRateMap(", session_source)
        self.assertIn("ParseTeamProfiles(", session_source)
        for label in (
            "Daily storage change",
            "Maximum storage",
            "Car transfer rate",
            "DAILY INPUTS",
            "DAILY OUTPUTS",
            "TEAM PROFILES - one per line",
            "Base population",
            "Neighbor stop IDs",
            "ADVANCED COMPONENT FIELDS...",
        ):
            self.assertIn(f'"{label}"', panel_source)

    def test_turntable_builder_supports_fuse_legacy_and_narrow_gauge(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        session_source = (
            bridge / "TileEditorOperationsSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorOperationsPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("CreateTurntable(", session_source)
        self.assertIn('["turntables"]', session_source)
        self.assertIn('["subdivisions"]', session_source)
        self.assertIn('["bridgeTrackGauge"]', session_source)
        self.assertIn('["roundhouse"]', session_source)
        self.assertIn(
            "AlinasMapMod.Turntable.TurntableBuilder",
            session_source,
        )
        self.assertIn('"Narrow 21.4 m"', panel_source)
        self.assertIn('"0.9144"', panel_source)
        self.assertIn("subdivisions < 4 || subdivisions > 32", session_source)

    def test_operations_overlay_refresh_is_explicit_not_timed_rebuild(self):
        root = Path(__file__).resolve().parent.parent
        source = (
            root
            / "TileEditorBridge"
            / "TileEditorOperationsSession.cs"
        ).read_text(encoding="utf-8")
        refresh = source.split(
            "private void RefreshOperationsMode(bool force)", 1
        )[1].split(
            "private void ResetOperationsSession()", 1
        )[0]

        self.assertIn("if (!force)", refresh)
        self.assertIn("return;", refresh)
        self.assertIn("DiscoverOperations();", refresh)
        self.assertIn("RebuildOperationOverlays();", refresh)

    def test_splineys_prefer_fuse_and_keep_legacy_json_compatibility(self):
        root = Path(__file__).resolve().parent.parent
        source = (
            root / "TileEditorBridge" / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            '"StrangeCustoms.AutoTrestleBuilder"',
            source,
        )
        self.assertIn(
            '"FUSE.Runtime.API.SplineyAPI"',
            source,
        )
        self.assertIn("TryBuildLiveSplineWithFuse", source)
        build_live = source.split(
            "private void BuildLiveSpline(SplineSource source)", 1
        )[1].split(
            "private bool TryBuildLiveSplineWithFuse", 1
        )[0]
        self.assertLess(
            build_live.index("TryBuildLiveSplineWithFuse(source)"),
            build_live.index("source.Handler"),
        )
        self.assertIn(
            '"FUSE.Authoring.Data.FuseSpliney"',
            source,
        )
        self.assertIn("TryRemoveFuseSpliney", source)
        self.assertNotIn(
            "<Reference Include=\"FUSE",
            (
                root
                / "TileEditorBridge"
                / "Hrogers.TileEditorBridge.csproj"
            ).read_text(encoding="utf-8"),
        )

    def test_f9_segment_group_is_editable_and_undoable(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("SetSelectedSegmentGroup", graph_source)
        self.assertIn('segment.groupId = normalized;', graph_source)
        self.assertIn('"Change segment group"', graph_source)
        self.assertIn("ExecuteEdit(", graph_source)
        self.assertIn('"Segment group"', panel_source)
        self.assertIn('"Apply Group"', panel_source)
        self.assertIn('"Clear"', panel_source)
        self.assertIn("RenameSelectedSegment", graph_source)
        self.assertIn("RenameSegmentReferencesInGraphDocument", graph_source)
        self.assertIn('"Segment ID / name"', panel_source)
        self.assertIn('"Rename Segment ID"', panel_source)

    def test_f9_track_class_is_editable_and_schema_safe(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "TrackClass = _selectedSegment.trackClass.ToString()",
            graph_source,
        )
        self.assertIn(
            "SetSelectedSegmentTrackClass",
            graph_source,
        )
        self.assertIn(
            '"Change segment track class"',
            graph_source,
        )
        self.assertIn(
            "segment.trackClass = normalized;",
            graph_source,
        )
        setter = graph_source.split(
            "internal void SetSelectedSegmentTrackClass(", 1
        )[1].split(
            "internal void SetSelectedSegmentGroup(", 1
        )[0]
        self.assertIn("TrackClass.Mainline", setter)
        self.assertIn("TrackClass.Branch", setter)
        self.assertIn("TrackClass.Industrial", setter)
        self.assertIn("useTargetedTrackRebuild: true", setter)
        self.assertIn('"Track class: "', panel_source)
        self.assertIn(
            'SegmentTrackClassButton(\n                "Mainline"',
            panel_source,
        )
        self.assertIn(
            'SegmentTrackClassButton(\n                "Branch"',
            panel_source,
        )
        self.assertIn(
            'SegmentTrackClassButton(\n                "Industrial"',
            panel_source,
        )
        self.assertIn('entry["trackClass"]', graph_source)
        self.assertIn(
            "segment.trackClass == TrackClass.Mainline",
            graph_source,
        )

    def test_f9_track_movement_is_local_or_world_and_lightweight(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        drag_source = (
            bridge / "TileEditorNodeDrag.cs"
        ).read_text(encoding="utf-8")
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('"WORLD"', panel_source)
        self.assertIn('"LOCAL"', panel_source)
        self.assertIn("_moveInLocalAxes = false;", panel_source)
        self.assertIn("_moveInLocalAxes = true;", panel_source)
        self.assertIn(
            "MoveSelectedNode(offset, _moveInLocalAxes)",
            panel_source,
        )
        self.assertIn(
            "useLightweightTrackUpdate: true",
            graph_source,
        )
        self.assertIn("trackManager.SetNeedsRebuild(node)", graph_source)
        self.assertIn(
            "BeforeDocument = useLightweightTrackUpdate",
            graph_source,
        )
        self.assertIn("RefreshPendingSegmentGeometry", graph_source)
        self.assertIn("1f / 30f", drag_source)
        self.assertIn("RefreshCurveLine", drag_source)
        self.assertIn(
            "internal void RefreshCurveLine()",
            overlay_source,
        )

    def test_f9_node_property_clipboard_is_targeted_and_undoable(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('"COPY ALL SETTINGS"', panel_source)
        self.assertIn('"COPY / PASTE..."', panel_source)
        self.assertIn(
            '"COPY ONLY FROM CURRENT NODE"',
            panel_source,
        )
        for label in (
            "ELEVATION",
            "GRADE",
            "HEADING",
            "BANK",
            "ROTATION X/Y/Z",
            "ELEV + GRADE",
            "ELEV + ROTATION",
            "SWITCH FLAG",
            "ALL SETTINGS",
        ):
            self.assertIn(f'"{label}"', panel_source)
        self.assertIn("NodePropertyClipboard", panel_source)
        self.assertIn("Fields = fields", panel_source)
        self.assertIn(
            "(_nodePropertyClipboard.Fields & fields) == fields",
            panel_source,
        )
        self.assertIn(
            "PasteSelectedNodeProperties(",
            panel_source,
        )
        self.assertIn(
            "internal enum NodePropertyFields",
            graph_source,
        )
        self.assertIn(
            "position.y = elevation;",
            graph_source,
        )
        self.assertNotIn(
            "position.x = elevation;",
            graph_source,
        )
        self.assertIn(
            'EditSelectedNodeTransform(\n'
            '                "Paste node properties"',
            graph_source,
        )
        self.assertIn(
            "useLightweightTrackUpdate: true",
            graph_source,
        )

    def test_f9_can_place_and_select_a_free_starting_node(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('"PLACE FREE NODE"', panel_source)
        self.assertIn(
            "PointerPlacementKind.FreeTrackNode",
            panel_source,
        )
        self.assertIn(
            "PointerPlacementKind.FreeTrackNode:",
            pointer_source,
        )
        self.assertIn(
            "var start = connectFromSelected ? RequireNode() : null;",
            graph_source,
        )
        self.assertIn("_selectedNode = node;", graph_source)
        self.assertIn(
            '? "Placed free node " + id',
            graph_source,
        )

    def test_f9_add_button_extends_from_selected_node_heading(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        add_next = graph_source.split(
            "internal void AddNextNode()", 1
        )[1].split(
            "internal void AddNodeAtCamera()", 1
        )[0]

        self.assertIn('"Add +10 m"', panel_source)
        self.assertIn("_mapEditor.AddNextNode", panel_source)
        self.assertIn(
            "+ HorizontalForward(rotation.y) * 10f",
            add_next,
        )
        self.assertIn("var grade = SelectedNodeGrade();", add_next)
        self.assertIn("position.y += 10f * grade / 100f;", add_next)
        self.assertIn("rotation.x = PitchFromGrade(grade);", add_next)
        self.assertIn(
            "var rotation = start.transform.localEulerAngles;",
            add_next,
        )
        self.assertIn("_selectedNode = node;", add_next)
        self.assertIn(
            'ExecuteEdit(\n                "Add connected node"',
            add_next,
        )
        self.assertIn(
            "useTargetedTrackRebuild: true",
            add_next,
        )

    def test_narrow_gauge_edits_batch_metadata_and_avoid_full_map_rebuilds(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        gauge_source = (
            bridge / "TileEditorGaugeSession.cs"
        ).read_text(encoding="utf-8")

        add_next = graph_source.split(
            "internal void AddNextNode()", 1
        )[1].split(
            "internal void AddNodeAtCamera()", 1
        )[0]
        place_node = graph_source.split(
            "internal string AddNodeAtPosition(", 1
        )[1].split(
            "internal void SetConnectStart", 1
        )[0]
        write_segment = graph_source.split(
            "private void WriteSegment(TrackSegment segment)", 1
        )[1].split(
            "private void WriteNodeDeletion", 1
        )[0]
        rebuild = graph_source.split(
            "private void RebuildLiveGraph(", 1
        )[1].split(
            "private void RebuildOverlays(", 1
        )[0]
        publish_batch = gauge_source.split(
            "private void PublishFuseSegmentDefinitions(", 1
        )[1].split(
            "private void PublishFuseSegmentDefinitionInBatch(", 1
        )[0]

        self.assertIn(
            "useTargetedTrackRebuild: true",
            add_next,
        )
        self.assertIn(
            "useTargetedTrackRebuild: true",
            place_node,
        )
        self.assertIn(
            "QueueFuseSegmentDefinition(segment.id);",
            write_segment,
        )
        self.assertNotIn(
            "PublishFuseSegmentDefinition(segment, gauge)",
            write_segment,
        )
        self.assertEqual(
            publish_batch.count("begin.Invoke(null, null);"),
            1,
        )
        self.assertEqual(
            publish_batch.count(
                "end.Invoke(null, new object[] { false });"
            ),
            1,
        )
        self.assertIn(
            '"ConsumePendingRebuildRequest"',
            publish_batch,
        )
        self.assertIn(
            "RefreshNarrowGaugeMetadata();",
            gauge_source,
        )
        self.assertIn(
            '"RefreshGaugeMetadata"',
            gauge_source,
        )
        self.assertIn(
            "Only dual-gauge edits need the expensive",
            gauge_source,
        )
        self.assertIn(
            '_graph.SegmentsConnectedTo(node).Any(segment =>',
            gauge_source,
        )
        self.assertIn(
            "Time.unscaledTime + 0.65f",
            gauge_source,
        )
        self.assertIn(
            "FlushPendingNarrowGaugeSynchronization();",
            graph_source,
        )
        self.assertIn(
            "foreach (var segmentId in segmentIds)",
            rebuild,
        )
        self.assertIn(
            "trackManager.SetNeedsRebuild(node);",
            rebuild,
        )

    def test_f9_node_controls_use_accessible_direction_and_rotation_pads(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        shell_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        move_controls = panel_source.split(
            "private void DrawPrimaryMoveControls()", 1
        )[1].split(
            "private void DrawPrimaryRotationControls()", 1
        )[0]
        rotate_controls = panel_source.split(
            "private void DrawPrimaryRotationControls()", 1
        )[1].split(
            "private void DrawAdvancedNodeControls()", 1
        )[0]

        self.assertIn('"DIRECTION PAD"', move_controls)
        self.assertIn('"ELEVATION  Y"', move_controls)
        self.assertIn('"LOCAL PLAN  X / Z"', move_controls)
        self.assertIn('"WORLD PLAN  X / Z"', move_controls)
        for glyph in (
            "\\u25B2\\nRAISE  +Y",
            "\\u25BC\\nLOWER  -Y",
            "\\u25B2\\nFORWARD  +Z",
            "\\u25C0\\nLEFT  -X",
            "\\u25BC\\nBACK  -Z",
            "\\u25B6\\nRIGHT  +X",
        ):
            self.assertIn(glyph, move_controls)
        self.assertIn('"ROTATION AXES"', rotate_controls)
        self.assertIn('"PITCH  X"', rotate_controls)
        self.assertIn('"HEADING  Y"', rotate_controls)
        self.assertIn('"ROLL  Z"', rotate_controls)
        self.assertIn("DrawNodeRotationAxisGroup(", rotate_controls)
        self.assertIn(
            "new GUIContent(label, tooltip)",
            panel_source,
        )
        self.assertIn("fontSize = 17", shell_source)
        self.assertIn("hover =", shell_source)
        self.assertIn("active =", shell_source)

    def test_f9_scenery_catalog_paginates_all_assets(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorSceneryPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            bridge / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("const int pageSize = 16;", panel_source)
        self.assertIn('"Showing "', panel_source)
        self.assertIn('"\\u2190 Previous"', panel_source)
        self.assertIn('"Next \\u2192"', panel_source)
        self.assertIn("out var totalMatches", panel_source)
        self.assertIn("totalMatches = matchCount;", session_source)
        self.assertIn("var page = new List<string>(maximum);", session_source)
        self.assertIn("_cachedScenerySearchResults", session_source)
        self.assertIn(
            "DiscoverRailLoaderSceneryIdentifiers()",
            session_source,
        )
        self.assertIn('"SCAssetPacks"', session_source)
        self.assertIn(
            "manager.TryGetSceneryDefinition(",
            session_source,
        )
        self.assertIn(
            "SceneryAssetLibrarySummary",
            panel_source,
        )
        self.assertNotIn(
            "SearchSceneryAssets(\n                _scenerySearch,\n"
            "                12)",
            panel_source,
        )

    def test_world_nodes_connect_with_click_then_shift_click(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        shortcut = graph_source.split(
            "internal void ActivateNodeFromWorld(", 1
        )[1].split(
            "internal void SelectSegment(", 1
        )[0]

        self.assertIn("evt.IsShiftDown", overlay_source)
        self.assertIn("ActivateNodeFromWorld(", overlay_source)
        self.assertIn("var start = _selectedNode;", shortcut)
        self.assertIn("SelectNode(node);", shortcut)
        self.assertIn("ConnectFrom(start.id);", shortcut)
        self.assertIn(
            "Shift-click a second node",
            panel_source,
        )

    def test_world_nodes_ctrl_drag_as_one_edit_and_connect_on_drop(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")
        drag_source = (
            bridge / "TileEditorNodeDrag.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("evt.IsControlDown", overlay_source)
        self.assertIn("BeginNodeDragFromWorld(", overlay_source)
        self.assertIn("EndNodeDragFromWorld(", overlay_source)
        self.assertIn(
            "UpdateNodeDragFromPointer(",
            panel_source,
        )
        self.assertIn(
            "WorldTransformer.WorldToGame(terrainPoint)",
            drag_source,
        )
        self.assertIn("_nodeDragHasMoved", drag_source)
        self.assertIn('ExecuteEdit(\n                    createConnection', drag_source)
        self.assertIn('"Drag node and connect"', drag_source)
        self.assertIn("CreateSegmentLive(", drag_source)
        self.assertIn("Ctrl-drag a node over terrain", geo_source)
        self.assertNotIn("private void Update()", overlay_source)

    def test_in_game_scenery_keeps_persistent_game_coordinates(self):
        root = Path(__file__).resolve().parent.parent
        source = (
            root
            / "TileEditorBridge"
            / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "Position = SceneryPositionToGame(scenery)",
            source,
        )
        self.assertIn(
            "WorldTransformer.WorldToGame(",
            source,
        )
        self.assertIn(
            "WorldTransformer.GameToWorld(gamePosition)",
            source,
        )
        self.assertIn(
            "VerifySceneryCoordinateRoundTrip(",
            source,
        )
        self.assertIn("_selectedSceneryId", source)

    def test_in_game_overlay_colors_are_event_driven(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        for name in (
            "TileEditorOverlays.cs",
            "TileEditorSplineySession.cs",
            "TileEditorScenerySession.cs",
        ):
            source = (bridge / name).read_text(encoding="utf-8")
            self.assertNotIn(
                "private void Update()",
                source,
                msg=f"{name} must not add per-overlay frame updates",
            )

        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        scenery_source = (
            bridge / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")
        self.assertIn(
            "_nextDynamicOverlayRefreshAt",
            graph_source,
        )
        self.assertIn(
            "_workspaceModeInitialized",
            scenery_source,
        )
        self.assertIn(
            "_sceneryOverlaySignature",
            scenery_source,
        )

    def test_in_game_spliney_supports_creation_and_auto_trestles(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        session_source = (
            bridge / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("StrangeCustoms.FlowyThingBuilder", session_source)
        self.assertIn("StrangeCustoms.AutoTrestleBuilder", session_source)
        self.assertIn("CreateSplineyAtCamera(", session_source)
        self.assertIn("CreateSplineyBetweenPositions(", session_source)
        self.assertIn("AppendSplinePointAtPosition(", session_source)
        self.assertIn("WorldTransformer.WorldToGame(", session_source)
        self.assertIn("source.LiveTrestle.Generate();", session_source)
        self.assertIn("SetTrestleEndStyles(", session_source)
        self.assertIn("DeleteSelectedSpliney()", session_source)
        self.assertIn("BeforeExists = false", session_source)
        self.assertIn("token.Remove(\"width\")", session_source)
        self.assertIn("_nextSplineAttachRetryAt", session_source)
        self.assertIn("Time.unscaledTime + 5f", session_source)
        self.assertIn("PLACE NEW SPLINEY", panel_source)
        self.assertIn("DRAW NEW ", panel_source)
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("PointerPlacementKind.NewSpliney", pointer_source)
        self.assertIn("First spline point placed", pointer_source)
        self.assertIn("AppendSplinePointAtPosition", pointer_source)
        self.assertIn("Bridge / Trestle", panel_source)
        self.assertIn("Delete Entire Spliney...", panel_source)

    def test_in_game_bridge_matches_selected_segment_endpoint_nodes(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        session_source = (
            bridge / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")

        builder = session_source.split(
            "internal string CreateTrestleFromSelectedSegment(", 1
        )[1].split(
            "internal void DeleteSelectedSpliney()", 1
        )[0]
        self.assertIn(
            "segment.GetPositionRotationAtDistance(",
            builder,
        )
        self.assertIn("PositionAccuracy.High", builder)
        self.assertIn("position.y -= belowRail;", builder)
        self.assertIn("for (var index = 0; index < 2; index++)", builder)
        self.assertIn(
            "var distance = index == 0 ? 0f : length;",
            builder,
        )
        self.assertNotIn("pointSpacing", builder)
        self.assertIn("BeginTrackBridgePicking();", builder)
        self.assertNotIn("SetSplineTrackPickMode(false);", builder)
        self.assertIn("PICK A TRACK SEGMENT...", panel_source)
        self.assertIn("BUILD ANOTHER BRIDGE...", panel_source)
        self.assertIn(
            "_mapEditor.BeginTrackBridgePicking();",
            panel_source,
        )
        self.assertIn(
            'private string _trackBridgeBelowRail = "0.30"',
            panel_source,
        )
        self.assertNotIn("_trackBridgePointSpacing", panel_source)
        self.assertIn(
            "two endpoint nodes",
            panel_source,
        )
        self.assertIn(
            "BUILD BRIDGE ON SELECTED TRACK",
            panel_source,
        )
        self.assertIn(
            "(!_splineyMode || _splineTrackPickMode)",
            graph_source,
        )

    def test_right_click_universally_clears_editor_state(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "HandleUniversalDeselectInput();",
            panel_source,
        )
        cancel = pointer_source.split(
            "private void HandleUniversalDeselectInput()", 1
        )[1].split(
            "private bool TryGetPointerSurfaceHit(", 1
        )[0]
        self.assertIn("Input.GetMouseButtonDown(1)", cancel)
        self.assertIn("Input.GetMouseButtonUp(1)", cancel)
        self.assertIn("WorldRightClickMaxSeconds", cancel)
        self.assertIn("WorldRightClickMaxTravel", cancel)
        self.assertIn("camera orbit/pan gesture", cancel)
        self.assertIn("IsPointerOverEditorWindow()", cancel)
        self.assertIn("CancelPointerPlacement(false);", cancel)
        self.assertIn("EndTerrainStroke();", cancel)
        self.assertIn("_connectStartId = string.Empty;", cancel)
        self.assertIn("_fitArcNodeIds.Clear();", cancel)
        self.assertIn("_poleWireStartId = -1;", cancel)
        self.assertIn(
            "_opsMarkedSpanStartSegment = string.Empty;",
            cancel,
        )
        self.assertIn("_mapEditor?.ClearAllSelections();", cancel)

        clear = graph_source.split(
            "internal void ClearAllSelections()", 1
        )[1].split(
            "internal bool IsSelected(TrackNode node)", 1
        )[0]
        for action in (
            "CancelNodeDrag();",
            "ClearTrackSelection();",
            "ClearSplineSelection();",
            "ClearSelectedScenery();",
            "ClearSelectedTelegraphPole();",
            "ClearSelectedMandela();",
            'SelectOperation(string.Empty);',
        ):
            self.assertIn(action, clear)

    def test_f9_camera_has_middle_mouse_navigation_toggle(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        camera_source = (
            bridge / "TileEditorCameraInput.cs"
        ).read_text(encoding="utf-8")
        lock_source = (
            bridge / "TileEditorInputLock.cs"
        ).read_text(encoding="utf-8")
        main_source = (
            bridge / "Main.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")
        project_source = (
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).read_text(encoding="utf-8")

        self.assertIn('Reference Include="0Harmony"', project_source)
        self.assertIn(
            'HarmonyPatch(typeof(StrategyCameraController), "UpdateInput")',
            camera_source,
        )
        self.assertIn("GameInput.shared.GetMovement(", camera_source)
        self.assertIn("____movementInput = new Vector3(", camera_source)
        self.assertIn("-Input.mouseScrollDelta.y", camera_source)
        self.assertIn("____angleXInput = 0f;", camera_source)
        self.assertIn("____angleYInput = movement.y / 5f;", camera_source)
        self.assertIn("CameraNavigationUnlocked", camera_source)
        self.assertIn("SetMouseCameraLocked", camera_source)
        world_block = camera_source.split(
            "internal static bool EditorWorldInputBlocked", 1
        )[1].split(
            "internal static void SetMouseCameraLocked", 1
        )[0]
        self.assertIn("PointerOverEditorWindow", world_block)
        self.assertNotIn("CameraNavigationUnlocked", world_block)
        self.assertIn("WorldEditPointerActive", camera_source)
        self.assertIn(
            "SuppressMouseCameraForWorldEdit",
            camera_source,
        )
        self.assertIn(
            "Input.GetMouseButtonDown(2)",
            panel_source,
        )
        self.assertIn("ToggleCameraNavigationLock", panel_source)
        update_loop = panel_source.split(
            "private void Update()", 1
        )[1].split("private void OnGUI()", 1)[0]
        self.assertNotIn("CameraNavigationUnlocked", update_loop)
        self.assertIn("HandleUniversalDeselectInput();", update_loop)
        self.assertIn("UpdateNodeDragFromPointer(", update_loop)
        self.assertIn("UpdateWorldPointerTools();", update_loop)
        camera_toggle = panel_source.split(
            "private void ToggleCameraNavigationLock()", 1
        )[1].split("private void DrawWindow", 1)[0]
        self.assertNotIn("EndTerrainStroke();", camera_toggle)
        self.assertNotIn("CancelNodeDrag();", camera_toggle)
        self.assertIn("editing remains active", camera_toggle)
        self.assertIn(
            "!TileEditorCameraInput.EditorWorldInputBlocked",
            overlay_source,
        )
        self.assertIn("____panStartPosition = null;", camera_source)
        self.assertIn("____rotateStarted = false;", camera_source)
        self.assertIn("selected = false;", camera_source)
        self.assertIn("TileEditorCameraInput.EditorInputActive =", lock_source)
        self.assertNotIn(
            "TileEditorCameraInput.MouseCameraLocked =\n                locked",
            lock_source,
        )
        self.assertIn("_editorWorldInputBlocked", lock_source)
        allowed = lock_source.split(
            "EditorAllowedGameActions", 1
        )[1].split(
            "private readonly HashSet<InputAction>", 1
        )[0]
        self.assertIn('"Move"', allowed)
        self.assertIn('"LeanLeft"', allowed)
        self.assertIn('"LeanRight"', allowed)
        self.assertIn('"ActivatePrimary"', allowed)
        self.assertNotIn('"ActivateSecondary"', allowed)
        self.assertIn("_harmony.PatchAll(", main_source)
        self.assertIn("_harmony?.UnpatchAll(", main_source)

    def test_shift_question_mark_opens_pointer_track_survey(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        survey_source = (
            bridge / "TileEditorSurveyHud.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("Input.GetKey(KeyCode.Slash)", survey_source)
        self.assertIn("Input.GetKey(KeyCode.LeftShift)", survey_source)
        self.assertIn("TryGetPointerSurfaceHit", survey_source)
        self.assertIn("InspectPointerSurvey", survey_source)
        self.assertIn("TilePositionFromPoint", survey_source)
        self.assertIn("TryGetLocationFromGamePoint", survey_source)
        self.assertIn("GradePercent", survey_source)
        self.assertIn("HeadingDegrees", survey_source)
        self.assertIn("GraphLocalPosition", survey_source)
        self.assertIn("UpdateSurveyHud();", panel_source)
        self.assertIn("DrawSurveyHud();", panel_source)
        self.assertIn("Shift+? survey", geo_source)

    def test_in_game_telegraph_poles_use_cumulative_mover_offsets(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        session_source = (
            bridge / "TileEditorTelegraphPoleSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorSceneryPanel.cs"
        ).read_text(encoding="utf-8")
        pole_panel_source = (
            bridge / "TileEditorPolePanel.cs"
        ).read_text(encoding="utf-8")
        creation_source = (
            bridge / "TileEditorTelegraphPoleCreation.cs"
        ).read_text(encoding="utf-8")
        geo_panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        project_source = (
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).read_text(encoding="utf-8")

        self.assertIn("TelegraphPoleManager", session_source)
        self.assertIn("SimpleGraph.Runtime.SimpleGraph", session_source)
        self.assertIn("TelegraphPoleMover", session_source)
        self.assertIn('"polesToMove"', session_source)
        self.assertIn('"poleMovement"', session_source)
        self.assertIn(
            "AfterOffset = previousOffset + localOffset",
            session_source,
        )
        self.assertIn(
            "WorldTransformer.WorldToGame(",
            session_source,
        )
        self.assertIn(
            "WorldTransformer.GameToWorld(gamePosition)",
            session_source,
        )
        self.assertIn("NotifyDidChangeNodes(", session_source)
        self.assertIn(
            'GetMethod(\n                "Rebuild"',
            session_source,
        )
        self.assertIn("SaveTelegraphPoles()", session_source)
        self.assertIn("tile-editor-backup-", session_source)
        self.assertIn("MAP POLE  ", panel_source)
        self.assertIn("Save Pole Edits", panel_source)
        self.assertIn("PanelTab.Poles", geo_panel_source)
        self.assertIn('DrawPanelTab("POLES"', geo_panel_source)
        self.assertIn(
            "Place Connected Pole with Mouse",
            pole_panel_source,
        )
        self.assertIn("Set Wire Start", pole_panel_source)
        self.assertIn(
            '"tile-editor-telegraph-poles.json"',
            creation_source,
        )
        self.assertIn(
            "_telegraphPoleGraph.CreateNode(",
            creation_source,
        )
        self.assertIn(
            "_telegraphPoleGraph.AddEdge(",
            creation_source,
        )
        self.assertIn(
            "RunTelegraphGraphBatch(ApplyCustomPoleDocuments)",
            creation_source,
        )
        self.assertIn(
            "WorldTransformer.GameToWorld(position)",
            creation_source,
        )
        self.assertIn(
            "CurrentCameraGroundPosition",
            creation_source,
        )
        self.assertIn(
            '<Reference Include="SimpleGraph.Runtime">',
            project_source,
        )

    def test_scenery_and_poles_support_local_movement_and_pole_rotation(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        scenery_session = (
            bridge / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")
        pole_session = (
            bridge / "TileEditorTelegraphPoleSession.cs"
        ).read_text(encoding="utf-8")
        pole_creation = (
            bridge / "TileEditorTelegraphPoleCreation.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorSceneryPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("bool localAxes", scenery_session)
        self.assertIn("model.Rotation.y", scenery_session)
        self.assertIn("bool localAxes", pole_session)
        self.assertIn("node.eulerAngles.y", pole_session)
        self.assertIn("_sceneryLocalAxes", panel_source)
        self.assertIn("_telegraphPoleLocalAxes", panel_source)
        self.assertIn("DrawWorldLocalAxesButtons(", panel_source)
        self.assertIn(
            "DrawPrimaryTelegraphPoleRotationControls",
            panel_source,
        )
        self.assertIn("PITCH X-", panel_source)
        self.assertIn("HEADING Y-", panel_source)
        self.assertIn("ROLL Z-", panel_source)
        self.assertIn(
            "RotateSelectedTelegraphPole(",
            pole_creation,
        )
        self.assertIn(
            "SetSelectedTelegraphPoleRotation(",
            pole_creation,
        )
        self.assertIn('"basePoleOverrides"', pole_creation)
        self.assertIn(
            "ExecuteBaseTelegraphPoleRotation(",
            pole_creation,
        )
        self.assertIn(
            "RestoreBaseTelegraphPoleRotation(",
            pole_creation,
        )
        self.assertIn("BasePoleOverrideArray(", pole_creation)

    def test_mouse_pointer_places_nodes_scenery_and_poles(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        scenery_source = (
            bridge / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")
        pole_source = (
            bridge / "TileEditorTelegraphPoleCreation.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("camera.ScreenPointToRay", pointer_source)
        self.assertIn("Physics.RaycastNonAlloc(", pointer_source)
        self.assertIn("PointerPlacementKind.FreeTrackNode", pointer_source)
        self.assertIn("PointerPlacementKind.Scenery", pointer_source)
        self.assertIn("PointerPlacementKind.ConnectedPole", pointer_source)
        self.assertIn("AddNodeAtPosition(", graph_source)
        self.assertIn("CreateSceneryAtPosition(", scenery_source)
        self.assertIn("CreateTelegraphPoleAtPosition(", pole_source)
        self.assertIn("_repeatPointerPlacement", pointer_source)

    def test_terrain_tab_has_live_height_and_mask_brushes(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorTerrainPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            bridge / "TileEditorTerrainSession.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        project_source = (
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).read_text(encoding="utf-8")

        self.assertIn("PanelTab.Terrain", geo_source)
        self.assertIn('DrawPanelTab("TERRAIN"', geo_source)
        self.assertIn("TerrainBrushMode.Raise", panel_source)
        self.assertIn("TerrainBrushMode.Smooth", panel_source)
        self.assertIn("TerrainBrushMode.Vegetation", panel_source)
        self.assertIn("TerrainBrushMode.Water", panel_source)
        self.assertIn("TerrainWorkspace.Sculpt", panel_source)
        self.assertIn('"SURFACE PAINT"', panel_source)
        self.assertIn("Building Pad", panel_source)
        self.assertIn("Path / Road", panel_source)
        self.assertIn("Grade Plane", panel_source)
        self.assertIn("TerrainBrushMode.Ditch", panel_source)
        self.assertIn("TerrainBrushMode.Berm", panel_source)
        self.assertIn("Sample Height Under Pointer", panel_source)
        self.assertIn("ApplyTerrainBrush(", panel_source)
        self.assertIn("SetHeightsDelayLOD(", session_source)
        self.assertIn("GetRawTextureData<byte>()", session_source)
        self.assertIn("SaveTerrainTiles()", session_source)
        self.assertIn("tile-editor-backup-", session_source)
        self.assertIn("ImageConversion.EncodeToPNG", session_source)
        self.assertIn("Mathf.MoveTowards(", session_source)
        self.assertIn("original - maximumCutFill", session_source)
        self.assertIn("noiseTarget", session_source)
        self.assertIn("original + signedDepth * falloff", session_source)
        self.assertIn(
            "Dictionary<int, float> Heights",
            session_source,
        )
        self.assertIn(
            '<Reference Include="UnityEngine.ImageConversionModule">',
            project_source,
        )

    def test_terrain_rebuild_requeues_camera_tiles_and_reports_completion(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        session_source = (
            bridge / "TileEditorTerrainSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        rebuild = session_source.split(
            "internal string RebuildTerrain()", 1
        )[1].split(
            "internal string PollTerrainRebuildStatus()", 1
        )[0]
        self.assertIn("CameraSelector.shared.CurrentCameraGroundPosition", rebuild)
        self.assertIn("manager.HasTileData(focusTile)", rebuild)
        self.assertIn("manager.RebuildAll();", rebuild)
        self.assertIn("manager.UpdateVisibleTilesForPosition(focusGame);", rebuild)
        self.assertLess(
            rebuild.index("manager.RebuildAll();"),
            rebuild.index("manager.UpdateVisibleTilesForPosition(focusGame);"),
        )
        self.assertIn(
            "mapTerrain.buildStatus == MapTerrain.BuildStatus.Ready",
            session_source,
        )
        self.assertIn("Terrain rebuild timed out", session_source)
        self.assertIn("PollTerrainRebuildStatus()", panel_source)
        self.assertIn('"Rebuilding Terrain..."', geo_source)

    def test_release_derives_game_path_and_rejects_build_machine_paths(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        launcher = (
            bridge / "Launch Tile Editor.bat"
        ).read_text(encoding="utf-8")
        project = (
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).read_text(encoding="utf-8")
        packaging = (
            bridge / "package_complete_mod.ps1"
        ).read_text(encoding="utf-8")
        python_bridge = (
            root / "railroader_bridge.py"
        ).read_text(encoding="utf-8")

        self.assertIn('set "TILE_EDITOR_GAME_DIR=%INSTALLED_GAME_DIR%"', launcher)
        self.assertIn("TILE_EDITOR_GAME_DIR", python_bridge)
        self.assertIn("(parent / 'Railroader_Data').is_dir()", python_bridge)
        self.assertIn("<DebugType>none</DebugType>", project)
        self.assertIn("<DebugSymbols>false</DebugSymbols>", project)
        self.assertIn("$privatePathTokens", packaging)
        self.assertIn("Release contains build-machine paths", packaging)
        self.assertIn(
            '"Hrogers.TileEditorBridge.dll.*.cache"',
            packaging,
        )
        self.assertIn(
            "'[\\\\/]Cache[\\\\/]OSM[\\\\/]'",
            packaging,
        )

    def test_osm_cache_has_guarded_manual_clear_in_both_editors(self):
        root = Path(__file__).resolve().parent.parent
        osm_source = (
            root / "edit_tiles" / "osm.py"
        ).read_text(encoding="utf-8")
        events_source = (
            root / "edit_tiles" / "events.py"
        ).read_text(encoding="utf-8")
        renderer_source = (
            root / "edit_tiles" / "renderer.py"
        ).read_text(encoding="utf-8")
        game_source = (
            root / "TileEditorBridge" / "TileEditorOsmOverlay.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("def clear_disk_cache(self)", osm_source)
        self.assertIn("self._cache_generation += 1", osm_source)
        self.assertIn("osm_clear_cache_confirm", events_source)
        self.assertIn('"CONFIRM CLEAR"', renderer_source)
        self.assertIn("ClearOsmDiskCache()", game_source)
        self.assertIn("_osmCacheGeneration++", game_source)
        self.assertIn('"CONFIRM CLEAR"', game_source)

    def test_all_editor_file_types_have_two_way_sync(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        game_sync = (
            bridge / "TileEditorFileSync.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        terrain_source = (
            bridge / "TileEditorTerrainSession.cs"
        ).read_text(encoding="utf-8")
        desktop_sync = (
            root / "edit_tiles" / "bridge.py"
        ).read_text(encoding="utf-8")
        python_bridge = (
            root / "railroader_bridge.py"
        ).read_text(encoding="utf-8")

        self.assertIn('"graph"', game_sync)
        self.assertIn('"spliney"', game_sync)
        self.assertIn('"poles"', game_sync)
        self.assertIn('"files_saved_in_game"', game_sync)
        self.assertIn('case "reload_terrain_tiles"', panel_source)
        self.assertIn(
            "ReloadTerrainTilesFromDesktop(",
            terrain_source,
        )
        self.assertIn(
            "ReloadGraphFromDesktop(",
            panel_source,
        )
        self.assertIn(
            "elif action == 'files_saved_in_game'",
            desktop_sync,
        )
        self.assertIn(
            "_reload_terrain_files_saved_in_game",
            desktop_sync,
        )
        self.assertIn(
            "def reload_terrain_tiles",
            python_bridge,
        )

    def test_complete_wye_builder_keeps_three_turnout_topology(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        complete_wye = graph_source.split(
            "internal string BuildPerfectWye(", 1
        )[1].split(
            "private string BuildPerfectWyeFromThroughTrack(", 1
        )[0]
        through_track_wye = graph_source.split(
            "private string BuildPerfectWyeFromThroughTrack(", 1
        )[1].split("public void Dispose()", 1)[0]
        self.assertIn("approachSegments.Count == 2", complete_wye)
        self.assertIn("approachSegments.Count != 1", complete_wye)
        self.assertEqual(
            complete_wye.count("CreateAndWriteStandardSegment("),
            5,
        )
        self.assertIn(
            '"Build complete three-turnout wye"',
            complete_wye,
        )
        self.assertIn(
            '"Build complete wye in existing through track"',
            through_track_wye,
        )
        self.assertIn("RemoveSegmentLive(outgoing.id)", through_track_wye)
        self.assertIn("requiredLength + 1f", through_track_wye)
        self.assertIn("ConnectedSegments", graph_source)
        self.assertIn("BUILD COMPLETE WYE", panel_source)
        self.assertIn("THROUGH-TRACK MODE", panel_source)
        self.assertIn("ApplyWyePreset", panel_source)
        self.assertIn("Simple Frog Builder...", panel_source)

    def test_track_tool_profiles_are_persistent_and_arc_nodes_are_explicit(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        profile_source = (
            bridge / "TileEditorToolProfiles.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("tile_editor_track_profiles.json", profile_source)
        self.assertIn("AtomicWrite(_trackProfilePath", profile_source)
        self.assertIn("class ArcProfile", profile_source)
        self.assertIn("class TurnoutProfile", profile_source)
        self.assertIn("class WyeProfile", profile_source)
        self.assertIn("SaveCurrentArcProfile", profile_source)
        self.assertIn("SaveCurrentTurnoutProfile", profile_source)
        self.assertIn("SaveCurrentWyeProfile", profile_source)
        self.assertIn("ControlNodes", profile_source)
        self.assertIn('private string _arcNodes = "3"', panel_source)
        self.assertIn("DrawArcProfileControls();", panel_source)
        self.assertIn("DrawTurnoutProfileControls();", panel_source)
        self.assertIn("DrawWyeProfileControls();", panel_source)
        self.assertIn("var steps = controlNodes;", graph_source)
        self.assertIn(
            "Arc control nodes must be between 1 and 64.",
            graph_source,
        )

    def test_track_and_scenery_show_move_and_rotate_together(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        scenery_source = (
            bridge / "TileEditorSceneryPanel.cs"
        ).read_text(encoding="utf-8")

        compact_node = geo_source.split(
            "private void DrawCompactNodeEditor(", 1
        )[1].split("private void DrawPrimaryMoveControls()", 1)[0]
        primary_scenery_rotation = scenery_source.split(
            "private void DrawPrimarySceneryRotationControls()", 1
        )[1].split("private void DrawSceneryGraphChooser()", 1)[0]

        self.assertNotIn("NodeControlMode", geo_source)
        self.assertIn("DrawPrimaryMoveControls();", compact_node)
        self.assertIn("DrawPrimaryRotationControls();", compact_node)
        self.assertIn(
            "DrawPrimarySceneryRotationControls();",
            scenery_source,
        )
        self.assertIn("PITCH X-", primary_scenery_rotation)
        self.assertIn("HEADING Y-", primary_scenery_rotation)
        self.assertIn("ROLL Z-", primary_scenery_rotation)
        self.assertIn("PITCH X+", primary_scenery_rotation)
        self.assertIn("HEADING Y+", primary_scenery_rotation)
        self.assertIn("ROLL Z+", primary_scenery_rotation)

    def test_spliney_shows_full_move_and_rotation_controls(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            bridge / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")
        rotation_controls = panel_source.split(
            "private void DrawPrimarySplineRotationControls(", 1
        )[1].split(
            "private void SetSplinePointRotation(", 1
        )[0]

        self.assertIn(
            "DrawPrimarySplineMoveControls();",
            panel_source,
        )
        self.assertIn(
            "DrawPrimarySplineRotationControls(point);",
            panel_source,
        )
        for label in (
            "PITCH X-",
            "HEADING Y-",
            "ROLL Z-",
            "PITCH X+",
            "HEADING Y+",
            "ROLL Z+",
        ):
            self.assertIn(label, rotation_controls)
        self.assertIn(
            'DrawQuickStepButton("0.01"',
            rotation_controls,
        )
        self.assertIn(
            'DrawQuickStepButton("180"',
            rotation_controls,
        )
        self.assertIn("Level X/Z", rotation_controls)
        self.assertIn("Reset Rotation", rotation_controls)
        self.assertIn("Flip Y 180", rotation_controls)
        self.assertIn(
            'token["rotation"] = Vector(',
            session_source,
        )
        self.assertIn(
            "SplineRotationToGame(",
            session_source,
        )

    def test_segment_overlays_repair_after_graph_object_replacement(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "ScheduleTrackOverlayRepair(",
            graph_source,
        )
        self.assertIn(
            "RepairPendingTrackOverlays();",
            graph_source,
        )
        self.assertIn(
            "_trackOverlayRepairPasses,\n                3",
            graph_source,
        )
        self.assertIn(
            "!overlay.IsHealthyFor(segment)",
            graph_source,
        )
        self.assertIn(
            "internal bool IsHealthyFor(TrackSegment segment)",
            overlay_source,
        )
        self.assertIn("_line.positionCount >= 2", overlay_source)
        refresh_mode = graph_source.split(
            "internal void RefreshEditMode()", 1
        )[1].split("internal bool TryOpenGraph", 1)[0]
        self.assertLess(
            refresh_mode.index("RepairPendingTrackOverlays();"),
            refresh_mode.index("_nextDynamicOverlayRefreshAt"),
        )

    def test_segment_overlays_live_outside_rebuilt_track_segments(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        drag_source = (
            bridge / "TileEditorNodeDrag.cs"
        ).read_text(encoding="utf-8")
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            '"TileEditorSegmentOverlays"',
            graph_source,
        )
        self.assertIn(
            "Dictionary<string, TileEditorSegmentOverlay>",
            graph_source,
        )
        self.assertIn(
            "root.transform,\n                    false",
            graph_source,
        )
        self.assertIn("GetSegmentOverlay(segment)", graph_source)
        self.assertIn("GetSegmentOverlay(segment)", drag_source)
        self.assertIn("RemoveSegmentOverlay(id);", graph_source)
        self.assertNotIn(
            "segment.GetComponentInChildren<\n"
            "                TileEditorSegmentOverlay",
            graph_source,
        )
        self.assertNotIn(
            "segment.GetComponentInChildren<\n"
            "                    TileEditorSegmentOverlay",
            drag_source,
        )
        self.assertIn(
            "gameObject.SetActive(visible);",
            overlay_source,
        )

    def test_f9_input_lock_blocks_game_shortcuts_and_restores_actions(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        input_source = (
            bridge / "TileEditorInputLock.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        project_source = (
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).read_text(encoding="utf-8")

        self.assertIn('FindActionMap(\n                "Game"', input_source)
        self.assertIn('"Global/ShowPauseMenu"', input_source)
        self.assertIn("action.Disable();", input_source)
        self.assertIn("action?.Enable();", input_source)
        self.assertIn("PanelTab.Objects", input_source)
        self.assertIn("PanelTab.Terrain", input_source)
        self.assertIn("SetGameInputLock(false);", panel_source)
        self.assertIn(
            '<Reference Include="Unity.InputSystem">',
            project_source,
        )

    def test_f9_mandelas_edit_base_objects_and_stay_fuse_compatible(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorMandelaPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            bridge / "TileEditorMandelaSession.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("PanelTab.Objects", geo_source)
        self.assertIn('DrawPanelTab("OBJECTS"', geo_source)
        self.assertIn("DrawMandelaPanel();", geo_source)
        self.assertIn("SelectMandelaUnderPointer", session_source)
        self.assertIn("FindObjectsOfTypeAll<Renderer>()", session_source)
        self.assertIn(
            "FindSmallMandelaScreenTarget(",
            session_source,
        )
        self.assertIn(
            "const float pickHalo = 18f;",
            session_source,
        )
        self.assertIn("MoveSelectedMandela", session_source)
        self.assertIn("RotateSelectedMandela", session_source)
        self.assertIn("CloneSelectedMandelaAtWorldPosition", session_source)
        self.assertIn("SetSelectedMandelaActive", session_source)
        self.assertIn("LooksLikeBaseGameSign", session_source)
        self.assertIn("SceneryAssetInstance", session_source)
        self.assertIn('"BASE-GAME SIGN"', panel_source)
        self.assertIn('"SIGN VISIBLE - TURN OFF"', panel_source)
        self.assertIn('"SIGN HIDDEN - TURN ON"', panel_source)
        self.assertIn('"instantiateFrom"', session_source)
        self.assertIn('["localPosition"]', session_source)
        self.assertIn('["localRotation"]', session_source)
        self.assertIn('["localScale"]', session_source)
        self.assertIn('"KeyValueObject"', session_source)
        self.assertIn("PointerPlacementKind.MandelaClone", panel_source)
        self.assertIn("RestoreMandelaModels(edit, after);", graph_source)
        self.assertIn("BeforeMandelas", graph_source)
        self.assertIn("AfterMandelas", graph_source)

    def test_geo_rebuild_track_button_is_beside_rebuild_terrain(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        rebuild_row = panel_source.split(
            "var rebuildEnabled = GUI.enabled;", 1
        )[1].split("DrawWorldSelection();", 1)[0]

        self.assertIn("GUILayout.BeginHorizontal();", rebuild_row)
        self.assertIn('"Rebuild Terrain"', rebuild_row)
        self.assertIn('"Rebuild Track"', rebuild_row)
        self.assertIn("_mapEditor.RebuildTrack", rebuild_row)
        self.assertIn("GUILayout.EndHorizontal();", rebuild_row)
        self.assertIn(
            "internal void RebuildTrack()",
            graph_source,
        )
        self.assertIn(
            "rebuildAllOverlays: true",
            graph_source,
        )

    def test_gauge_metadata_survives_legacy_and_fuse_segment_edits(self):
        from mod_project.layer import Layer

        with tempfile.TemporaryDirectory() as folder:
            legacy_path = Path(folder) / "game-graph.json"
            legacy_path.write_text(
                json.dumps({
                    "tracks": {
                        "nodes": {},
                        "segments": {
                            "s1": {
                                "startId": "n1",
                                "endId": "n2",
                                "trackClass": "Branch",
                                "style": "Yard",
                                "speedLimit": 15,
                                "priority": 0,
                                "groupId": "",
                                "gauge": "DualGauge_L",
                                "companionData": {"keep": True},
                            }
                        },
                    }
                }),
                encoding="utf-8",
            )
            legacy = Layer(
                legacy_path, "graph", (1, 2, 3), "legacy"
            )
            legacy.load()
            legacy.set_segment(
                "s1", "n1", "n2", "Branch", "Bridge", 20, 0, ""
            )
            legacy_raw = legacy._raw["tracks"]["segments"]["s1"]
            self.assertEqual(legacy.segments["s1"]["gauge"], "DualGauge_L")
            self.assertEqual(legacy_raw["gauge"], "DualGauge_L")
            self.assertEqual(
                legacy_raw["companionData"], {"keep": True}
            )

            fuse_path = Path(folder) / "track.fuse.json"
            fuse_path.write_text(
                json.dumps({
                    "schemaVersion": "1.0",
                    "tracks": {
                        "nodes": {},
                        "segments": {
                            "s2": {
                                "startNodeId": "n3",
                                "endNodeId": "n4",
                                "trackClass": "branch",
                                "style": "yard",
                                "gauge": "Narrow",
                            }
                        },
                    },
                }),
                encoding="utf-8",
            )
            fuse = Layer(fuse_path, "graph", (1, 2, 3), "fuse")
            fuse.load()
            fuse.set_segment(
                "s2", "n3", "n4", "Branch", "Yard", 10, 0, "",
                "DualGauge_R",
            )
            fuse_raw = fuse._raw["tracks"]["segments"]["s2"]
            self.assertEqual(fuse.track_schema, "fuse")
            self.assertEqual(fuse_raw["startNodeId"], "n3")
            self.assertEqual(fuse_raw["endNodeId"], "n4")
            self.assertNotIn("startId", fuse_raw)
            self.assertEqual(fuse_raw["gauge"], "DualGauge_R")

    def test_f9_supports_gauges_and_native_fuse_track_fragments(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        gauge_source = (
            bridge / "TileEditorGaugeSession.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        desktop_source = (
            root / "edit_tiles" / "app.py"
        ).read_text(encoding="utf-8")

        for gauge in (
            "Narrow", "DualGauge", "DualGauge_L",
            "DualGauge_R", "DualGauge_T",
        ):
            self.assertIn(gauge, gauge_source)
            self.assertIn(gauge, panel_source)
            self.assertIn(gauge, desktop_source)
        self.assertIn("CollectThroughChain", gauge_source)
        self.assertIn("RequestSynchronization", gauge_source)
        self.assertIn('info["FuseDataFiles"]', graph_source)
        self.assertIn('entry["startNodeId"]', graph_source)
        self.assertIn("AddFuseTrackRemoval", graph_source)
        self.assertIn("DeepClone()", graph_source)

    def test_f9_reports_and_resynchronizes_narrow_gauge_runtime(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        gauge_source = (
            bridge / "TileEditorGaugeSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("NarrowGaugeRuntimeReady", gauge_source)
        self.assertIn("DescribeGaugeRuntime", gauge_source)
        self.assertIn(
            "install/enable both and restart",
            gauge_source,
        )
        self.assertIn(
            "SynchronizeNarrowGaugeRuntime",
            gauge_source,
        )
        self.assertIn(
            "PublishFuseSegmentDefinitions(",
            gauge_source,
        )
        self.assertIn(
            "RequestNarrowGaugeSynchronization();",
            gauge_source,
        )
        self.assertIn('"SYNC GAUGE VISUALS"', panel_source)
        self.assertIn(
            "_mapEditor.DescribeGaugeRuntime(",
            panel_source,
        )

    def test_dual_transition_is_single_segment_and_reports_topology(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        gauge_source = (
            bridge / "TileEditorGaugeSession.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        desktop_source = (
            root / "edit_tiles" / "app.py"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "DescribeSelectedDualGaugeTransition",
            gauge_source,
        )
        self.assertIn(
            "exactly one non-transition",
            gauge_source,
        )
        self.assertIn(
            "Set one side to DUAL L",
            gauge_source,
        )
        self.assertIn(
            "DUAL T is a single shared-rail transition segment",
            graph_source,
        )
        self.assertIn(
            'TrackGaugeButton("DUAL T", "DualGauge_T")',
            panel_source,
        )
        self.assertNotIn(
            'TrackGaugeButton("FLIP", "DualGauge_T")',
            panel_source,
        )
        self.assertIn(
            "One short L-to-R shared-rail transition",
            desktop_source,
        )

    def test_mandela_selection_stops_before_shared_scene_roots(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        session_source = (
            bridge / "TileEditorMandelaSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorMandelaPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "WouldCrossMandelaAggregate",
            session_source,
        )
        self.assertIn(
            "IsUnsafeMandelaSelection",
            session_source,
        )
        self.assertIn(
            "Shared world/map containers cannot be edited",
            session_source,
        )
        self.assertIn(
            "Select and move each building or prop separately",
            session_source,
        )
        self.assertIn(
            "one click cannot",
            panel_source,
        )

    def test_f9_osm_overlay_streams_a_bounded_terrain_window(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        osm_source = (
            bridge / "TileEditorOsmOverlay.cs"
        ).read_text(encoding="utf-8")
        terrain_panel_source = (
            bridge / "TileEditorTerrainPanel.cs"
        ).read_text(encoding="utf-8")
        project_source = (
            bridge / "Hrogers.TileEditorBridge.csproj"
        ).read_text(encoding="utf-8")

        self.assertIn("BuildOsmGameTileWindow", osm_source)
        self.assertIn("SetOsmWindowSize(5)", osm_source)
        self.assertIn("SetOsmWindowSize(8)", osm_source)
        self.assertIn(
            "MaximumConcurrentOsmDownloads = 2",
            osm_source,
        )
        self.assertIn("DestroyOsmGameTile", osm_source)
        self.assertIn("TrimOsmTextureMemory", osm_source)
        self.assertIn("PrepareOsmLineworkTexture", osm_source)
        self.assertIn('"CLEAR LINES"', osm_source)
        self.assertIn('"FULL MAP"', osm_source)
        self.assertIn('"OVERVIEW z15"', osm_source)
        self.assertIn('"DETAIL z16"', osm_source)
        self.assertIn('"SHARP z17"', osm_source)
        self.assertIn('"ULTRA z18"', osm_source)
        self.assertIn("MaximumOsmZoom = 18", osm_source)
        self.assertIn("DefaultOsmZoom = 17", osm_source)
        self.assertIn("ResolveOsmZoomForGameTile", osm_source)
        self.assertIn("ClearQueuedOsmDownloads", osm_source)
        self.assertIn("changingResolution", osm_source)
        self.assertIn("!allTexturesReady", osm_source)
        self.assertIn("_osmDesiredTextures", osm_source)
        self.assertIn("used.UnionWith", osm_source)
        self.assertIn("FilterMode.Trilinear", osm_source)
        self.assertIn("texture.anisoLevel = 8", osm_source)
        self.assertIn("texture.mipMapBias = -0.25f", osm_source)
        self.assertIn("BlendMode.SrcAlpha", osm_source)
        self.assertIn("BlendMode.OneMinusSrcAlpha", osm_source)
        self.assertIn("Map.json", osm_source)
        self.assertIn('"Cache"', osm_source)
        self.assertIn(
            "\\u00a9 OpenStreetMap contributors",
            osm_source,
        )
        self.assertIn(
            "DrawOsmOverlayControls();",
            terrain_panel_source,
        )
        self.assertIn(
            "UnityEngine.UnityWebRequestTextureModule",
            project_source,
        )

    def test_in_game_overlays_share_materials_and_sleep_at_distance(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        overlay_source = (
            bridge / "TileEditorOverlays.cs"
        ).read_text(encoding="utf-8")
        scenery_source = (
            bridge / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")
        spline_source = (
            bridge / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")
        pole_source = (
            bridge / "TileEditorTelegraphPoleSession.cs"
        ).read_text(encoding="utf-8")
        visual_source = (
            bridge / "TileEditorOverlayVisuals.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("SharedLineMaterial", visual_source)
        self.assertIn('Shader.Find("Sprites/Default")', visual_source)
        for source in (
            overlay_source,
            scenery_source,
            spline_source,
            pole_source,
        ):
            self.assertIn("TileEditorOverlayVisuals", source)
            self.assertNotIn(
                "sharedMaterial.color =",
                source,
            )
        self.assertNotIn(
            "new Material(LineMaterial)",
            overlay_source,
        )
        self.assertIn("Mathf.CeilToInt(length / 45f)", overlay_source)
        self.assertIn("_chevrons.Count < 4", overlay_source)
        self.assertIn("_nodeOverlays", graph_source)
        self.assertIn("UpdateTrackOverlayCulling(", graph_source)
        self.assertIn("IsWithinWorldRange(", graph_source)
        self.assertIn("cameraHeight * 1.35f", graph_source)
        ensure_segment = graph_source.split(
            "private void EnsureSegmentOverlay(", 1
        )[1].split("private void ScheduleTrackOverlayRepair(", 1)[0]
        self.assertNotIn(
            "FindObjectsOfTypeAll<TileEditorSegmentOverlay>",
            ensure_segment,
        )

    def test_scenery_asset_search_is_cached_between_gui_passes(self):
        root = Path(__file__).resolve().parent.parent
        source = (
            root / "TileEditorBridge" / "TileEditorScenerySession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("_cachedScenerySearch", source)
        self.assertIn("_cachedScenerySearchResults", source)
        self.assertIn("InvalidateSceneryAssetSearch()", source)
        search = source.split(
            "internal IReadOnlyList<string> SearchSceneryAssets(", 1
        )[1].split(
            "internal void RefreshSceneryAssetLibrary()", 1
        )[0]
        self.assertIn("foreach (var identifier", search)
        self.assertNotIn(".Count()", search)
        self.assertNotIn(".Skip(", search)

    def test_live_base_roads_and_rivers_get_nodes_and_terrain_updates(self):
        root = Path(__file__).resolve().parent.parent
        source = (
            root / "TileEditorBridge" / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            root / "TileEditorBridge" / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("IsLiveMapSource", source)
        self.assertIn("CaptureLivePathEntry(path)", source)
        self.assertIn(
            "foreach (var pair in livePaths)",
            source,
        )
        self.assertIn(
            "source.LivePath.Rebuild();",
            source,
        )
        self.assertIn(
            "first edit writes a same-ID override",
            source,
        )
        self.assertIn(
            "Source: live base-map ",
            panel_source,
        )

    def test_new_track_nodes_use_persistent_prefix_and_name_pattern(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        node_panel_source = (
            bridge / "TileEditorNodePanel.cs"
        ).read_text(encoding="utf-8")
        bridge_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("ConfigureNewNodeIds(", graph_source)
        self.assertIn("FindAvailableNodeId(true)", graph_source)
        self.assertIn("NextNodeIdPreview", graph_source)
        self.assertIn("NormalizeNodeIdPart(", graph_source)
        self.assertIn('"000"', graph_source)
        self.assertIn("DrawNodeNamingControls();", node_panel_source)
        self.assertIn('"NEW NODE IDS"', panel_source)
        self.assertIn(
            "Every new Node, Piece, Arc, Grade, Parallel, Turnout, and ",
            panel_source,
        )
        self.assertIn("NodeIdPrefixKey", bridge_source)
        self.assertIn("NodeIdBaseNameKey", bridge_source)
        self.assertIn("PlayerPrefs.GetString(", bridge_source)

    def test_node_transform_controls_use_a_separate_resizable_window(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        panel_source = (
            bridge / "TileEditorBridgePanel.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        node_source = (
            bridge / "TileEditorNodePanel.cs"
        ).read_text(encoding="utf-8")
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("NodeWindowId", panel_source)
        self.assertIn("DrawNodeEditorWindow", panel_source)
        self.assertIn("DrawNodeResizeHandle();", node_source)
        self.assertIn("SaveNodeWindowGeometry();", node_source)
        self.assertIn("DrawCompactNodeEditor(selected);", node_source)
        self.assertIn("DrawNodeEditorLauncher();", geo_source)
        self.assertIn(
            "DrawSelectedNodeTrackBuilder(node);",
            geo_source,
        )
        self.assertNotIn(
            "DrawCompactNodeEditor(node);",
            geo_source.split(
                "private void DrawTrackTool()", 1
            )[1].split(
                "private void DrawNodeEditorLauncher()", 1
            )[0],
        )
        self.assertIn("_nodeWindowRect.Contains(guiMouse)", pointer_source)

    def test_f9_precision_track_objects_and_crossings_are_persistent(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        pointer_source = (
            bridge / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")
        geo_source = (
            bridge / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")
        camera_source = (
            bridge / "TileEditorCameraInput.cs"
        ).read_text(encoding="utf-8")
        backup_source = (
            bridge / "TileEditorBackupRetention.cs"
        ).read_text(encoding="utf-8")
        overrides_source = (
            bridge / "TileEditorTrackOverrides.cs"
        ).read_text(encoding="utf-8")
        crossings_source = (
            bridge / "TileEditorAutoEngineerCrossings.cs"
        ).read_text(encoding="utf-8")
        spline_source = (
            bridge / "TileEditorSplineySession.cs"
        ).read_text(encoding="utf-8")

        self.assertIn("InjectSelectedSegmentAtPosition", graph_source)
        self.assertIn("ClosestCurveParameter", graph_source)
        self.assertIn("PointerPlacementKind.SegmentControlNode", pointer_source)
        self.assertIn('"INSERT AT MOUSE..."', geo_source)
        self.assertIn("PointerOverEditorWindow", camera_source)
        self.assertIn("EditorWorldInputBlocked", camera_source)
        self.assertIn("MaximumBackups = 3", backup_source)
        self.assertIn("CreateBumperObject", overrides_source)
        self.assertIn("CreateBumperMasks", overrides_source)
        self.assertIn("disabledBumpers", overrides_source)
        self.assertIn("ToggleSelectedSwitchStand", graph_source)
        self.assertIn('"grade-crossings.json"', crossings_source)
        self.assertIn("ReloadPortableCrossingRuntime", crossings_source)
        self.assertNotIn('"AITraffic"', crossings_source)
        self.assertIn("bool localAxes", spline_source)
        self.assertIn("nodeTransformOnly", graph_source)

    def test_standalone_crossing_runtime_serves_all_auto_engineers(self):
        root = Path(__file__).resolve().parent.parent
        runtime = root / "CrossingRuntime"
        registry_source = (
            runtime / "CrossingRegistry.cs"
        ).read_text(encoding="utf-8")
        main_source = (runtime / "Main.cs").read_text(encoding="utf-8")
        project_source = (
            runtime / "Hrogers.CrossingRuntime.csproj"
        ).read_text(encoding="utf-8")
        editor_crossings = (
            root / "TileEditorBridge" / "TileEditorAutoEngineerCrossings.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('"grade-crossings.json"', registry_source)
        self.assertIn("TrackMarkerType.Crossing", registry_source)
        self.assertIn("graph.SegmentsConnectedTo(node)", registry_source)
        self.assertIn("foreach (var segment in segments)", registry_source)
        self.assertIn("public static void ReloadDefinitions()", main_source)
        self.assertNotIn("AITraffic", project_source)
        self.assertNotIn("TileEditorBridge", project_source)
        self.assertNotIn("IsOwnedByPlayer", registry_source)
        self.assertIn('"grade-crossings.json"', editor_crossings)
        self.assertNotIn('"traffic.json"', editor_crossings)

    def test_portable_train_signals_use_base_asset_without_editor_runtime(self):
        root = Path(__file__).resolve().parent.parent
        runtime = railroad_operations_signal_runtime_root()
        registry_source = (
            runtime / "TrainSignalRegistry.cs"
        ).read_text(encoding="utf-8")
        main_source = (runtime / "Main.cs").read_text(encoding="utf-8")
        project_source = (
            runtime / "Hrogers.SignalRuntime.csproj"
        ).read_text(encoding="utf-8")
        panel_source = (
            root / "TileEditorBridge" / "TileEditorTrainSignalPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            root / "TileEditorBridge" / "TileEditorTrainSignalSession.cs"
        ).read_text(encoding="utf-8")
        pointer_source = (
            root / "TileEditorBridge" / "TileEditorWorldPointer.cs"
        ).read_text(encoding="utf-8")
        geo_panel_source = (
            root / "TileEditorBridge" / "TileEditorGeoPanel.cs"
        ).read_text(encoding="utf-8")

        self.assertIn('"train-signals.json"', registry_source)
        self.assertIn("Signal BR-E Main", registry_source)
        self.assertIn("Signal BR-E Enter", registry_source)
        self.assertIn("GetComponentsInChildren<CTCSignal>", registry_source)
        self.assertIn("CTCSignalModelController", registry_source)
        self.assertIn("controller.Configure(definition.HeadCount)", registry_source)
        self.assertIn("public static bool TrySetAspect", main_source)
        self.assertIn("public static bool TryGetSignal", main_source)
        self.assertNotIn("TileEditorBridge", project_source)
        self.assertIn('"BASE-GAME SEMAPHORE SIGNALS"', panel_source)
        self.assertIn('"Interlocking ID"', panel_source)
        self.assertIn('"Protected node"', panel_source)
        self.assertIn('"Protected segment"', panel_source)
        self.assertIn("CreateTrainSignalAtPosition", session_source)
        self.assertIn("SnapSelectedTrainSignalToTrack", session_source)
        self.assertIn("SetSelectedTrainSignalTrackLocked", session_source)
        self.assertIn('"trackAttachment"', session_source)
        self.assertIn("TrackLocalPosition", session_source)
        self.assertIn('"Snap to track"', panel_source)
        self.assertIn('"Keep locked"', panel_source)
        self.assertIn('"SNAP ONCE"', panel_source)
        self.assertIn('"SNAP + LOCK"', panel_source)
        self.assertIn("RefreshAttachedSignalTransforms", registry_source)
        self.assertIn('entry["trackAttachment"]', registry_source)
        self.assertIn("BuildDiamondInterlocking", session_source)
        self.assertIn("CalculateDiamondCrossing", session_source)
        self.assertIn("TryLineIntersectionXZ", session_source)
        self.assertIn("TraceDiamondApproach", session_source)
        self.assertIn("ChooseDiamondContinuation", session_source)
        self.assertIn('"protectedSegmentIds"', session_source)
        self.assertIn('"approachSegmentIds"', session_source)
        self.assertIn('"segmentIds"', session_source)
        self.assertIn("signalSetback > 5000f", session_source)
        self.assertIn('_diamondSignalSetback = "600"', panel_source)
        self.assertIn('"BUILD 4 INDEPENDENT SIGNALS"', panel_source)
        self.assertIn("Main.Interlockings", (
            runtime / "README.md"
        ).read_text(encoding="utf-8"))
        self.assertIn("PlacedDiamondInterlocking", main_source)
        self.assertIn("ProtectedSegmentIds", main_source)
        self.assertIn("ApproachSegmentIds", main_source)
        self.assertIn("SegmentIds", main_source)
        self.assertIn("ReloadStandaloneSignalRuntime", session_source)
        self.assertIn("RecalculateSelectedTrainSignalRoute", session_source)
        self.assertIn(
            "SelectedTrainSignalInterlockingStatus",
            session_source,
        )
        self.assertIn(
            '"RECALCULATE BLOCK FROM MOVED MAST"',
            panel_source,
        )
        self.assertIn('"LIVE INTERLOCK CONTROL"', panel_source)
        self.assertIn(
            "RequestSelectedTrainSignalInterlockingRoute",
            panel_source,
        )
        self.assertIn(
            "ReleaseSelectedTrainSignalInterlocking",
            panel_source,
        )
        self.assertIn("PointerPlacementKind.TrainSignal", pointer_source)
        self.assertIn("DrawTrainSignalChangeBar", geo_panel_source)
        self.assertIn('"Undo Signals ("', geo_panel_source)
        self.assertIn('"Redo Signals ("', geo_panel_source)
        self.assertIn('"Signals Auto-Saved"', geo_panel_source)
        self.assertIn("TrainSignalUndoCount", session_source)
        self.assertIn("TrainSignalRedoCount", session_source)
        self.assertNotIn('GUILayout.Button("Undo Signal")', panel_source)

        interlock_source = (
            runtime / "DiamondInterlockingRuntime.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("TrainController.Shared", interlock_source)
        self.assertIn("car.WheelBoundsF", interlock_source)
        self.assertIn("car.WheelBoundsR", interlock_source)
        self.assertIn("location.GetPosition()", interlock_source)
        self.assertIn("interlocking.ReleaseLength", interlock_source)
        self.assertIn("Only", (
            runtime / "README.md"
        ).read_text(encoding="utf-8"))
        self.assertIn(
            "public static bool TryRequestInterlockingRoute",
            main_source,
        )
        self.assertIn(
            "public static bool TryReleaseInterlocking",
            main_source,
        )
        self.assertIn(
            "public static bool TrySetInterlockingAutomatic",
            main_source,
        )
        self.assertIn("DiamondInterlockingRuntime", registry_source)
        self.assertIn(
            "_interlockingRuntime.Tick(_interlockings, _signals)",
            registry_source,
        )

    def test_period_ctc_abs_and_train_orders_share_a_portable_model(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        runtime = railroad_operations_signal_runtime_root()
        session_source = (
            bridge / "TileEditorCtcSession.cs"
        ).read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorCtcPanel.cs"
        ).read_text(encoding="utf-8")
        operations_source = (
            bridge / "TileEditorOperationsPanel.cs"
        ).read_text(encoding="utf-8")
        graph_source = (
            bridge / "TileEditorGraphSession.cs"
        ).read_text(encoding="utf-8")
        runtime_source = (
            runtime / "PortableCtcRuntime.cs"
        ).read_text(encoding="utf-8")
        registry_source = (
            runtime / "TrainSignalRegistry.cs"
        ).read_text(encoding="utf-8")
        main_source = (runtime / "Main.cs").read_text(encoding="utf-8")

        self.assertIn('"ctc-system.json"', session_source)
        self.assertIn("CreateCtcControlPointFromSelectedNode", session_source)
        self.assertIn("CreateCtcBlockFromSelectedSegment", session_source)
        self.assertIn("AddSelectedSegmentToCtcBlock", session_source)
        self.assertIn('"train-orders", "abs", "ctc"', session_source)
        self.assertIn("ResetCtcSession();", graph_source)
        self.assertIn("DisposeCtcSession();", graph_source)
        self.assertIn("OperationsTool.Signals", operations_source)
        self.assertNotIn("OperationsTool.TrainOrders", operations_source)
        self.assertIn('"TERRITORY PREVIEW / SELECT CONTROL POINT"', panel_source)
        self.assertIn('"ABS / CTC BLOCKS"', panel_source)
        self.assertNotIn("CreateTrainOrder", session_source)
        self.assertNotIn('"TIMETABLE & TRAIN ORDER AUTHORING"', panel_source)
        self.assertNotIn('"FORM 19"', panel_source)
        self.assertNotIn('"FORM 31"', panel_source)

        self.assertIn('"ctc-system.json"', runtime_source)
        self.assertIn("controller.CanSetSwitch", runtime_source)
        self.assertIn("StateManager.ApplyLocal(new SetSwitch", runtime_source)
        self.assertIn("StopControlPointSignals", runtime_source)
        self.assertIn('return "approach";', runtime_source)
        self.assertIn('return "clear";', runtime_source)
        self.assertIn("BlocksFor(route).Any", runtime_source)
        self.assertIn("correspondence", runtime_source.lower())
        self.assertIn("_ctcRuntime.Tick", registry_source)
        self.assertIn("public static bool TrySetCtcSwitch", main_source)
        self.assertIn("public static bool TryLineCtcRoute", main_source)
        self.assertIn("public static bool TryCancelCtcRoute", main_source)
        self.assertIn("IReadOnlyList<PlacedTrainOrder>", main_source)

    def test_train_order_delivery_ack_authority_and_multiplayer_sync(self):
        root = Path(__file__).resolve().parent.parent
        bridge = root / "TileEditorBridge"
        runtime = railroad_operations_signal_runtime_root()
        order_runtime = (
            runtime / "TrainOrderRuntime.cs"
        ).read_text(encoding="utf-8")
        ctc_sync = (
            runtime / "CtcMultiplayerSync.cs"
        ).read_text(encoding="utf-8")
        main_source = (runtime / "Main.cs").read_text(encoding="utf-8")
        panel_source = (
            bridge / "TileEditorCtcPanel.cs"
        ).read_text(encoding="utf-8")
        session_source = (
            bridge / "TileEditorCtcSession.cs"
        ).read_text(encoding="utf-8")
        schema = json.loads(
            (runtime / "ctc-system.schema.json").read_text(encoding="utf-8")
        )

        self.assertIn("RegisterPropertyObject", order_runtime)
        self.assertIn("AuthorizationRequirement.PlayerIdKey", order_runtime)
        self.assertIn("AccessLevel.Dispatcher", order_runtime)
        self.assertIn("MemberPlayerIds.Contains(playerId)", order_runtime)
        self.assertIn('order.Status = "Delivered"', order_runtime)
        self.assertIn('order.Status = "Acknowledged"', order_runtime)
        self.assertIn("EnforceMovementAuthorities", order_runtime)
        self.assertIn('"aiManualStopDistance"', order_runtime)
        self.assertIn("PropertyChange.Control.TrainBrake", order_runtime)
        self.assertIn('locomotive.KeyValueObject["aiOrders"]', order_runtime)
        self.assertIn("Input.GetKeyDown(KeyCode.F6)", order_runtime)
        self.assertNotIn("Input.GetKeyDown(KeyCode.F8)", order_runtime)
        self.assertIn("TryDeliverTrainOrder", main_source)
        self.assertIn("TryAcknowledgeTrainOrder", main_source)
        self.assertNotIn("CreateTrainOrder", session_source)
        self.assertNotIn("TIMETABLE & TRAIN ORDER AUTHORING", panel_source)
        self.assertNotIn('"authority"', session_source)
        self.assertIn("RegisterPropertyObject", ctc_sync)
        self.assertIn("AccessLevel.Dispatcher", ctc_sync)
        self.assertIn("AuthorizationRequirement.PlayerIdKey", ctc_sync)
        self.assertIn("ApplyClientState", ctc_sync)

        order_properties = schema["properties"]["trainOrders"]["items"][
            "properties"
        ]
        self.assertIn("Delivered", order_properties["status"]["enum"])
        self.assertIn("Acknowledged", order_properties["status"]["enum"])
        self.assertIn("authority", order_properties)
        self.assertIn(
            "blockIds", order_properties["authority"]["properties"]
        )

    def test_signal_desk_joins_native_company_operations_window(self):
        root = Path(__file__).resolve().parent.parent
        runtime = railroad_operations_signal_runtime_root()
        native_panel = (
            runtime / "NativeOperationsPanel.cs"
        ).read_text(encoding="utf-8")
        main_source = (runtime / "Main.cs").read_text(encoding="utf-8")
        project_source = (
            runtime / "Hrogers.SignalRuntime.csproj"
        ).read_text(encoding="utf-8")

        self.assertIn(
            "HarmonyPatch(typeof(TabView), nameof(TabView.FinishedAddingTabs))",
            native_panel,
        )
        self.assertIn('HarmonyAfter("com.hrogers.aitraffic")', native_panel)
        self.assertIn("GetComponentInParent<CompanyWindow>()", native_panel)
        self.assertIn('"Operations"', native_panel)
        self.assertIn('"Dispatcher\'s Office"', native_panel)
        self.assertIn('"Treasurer\'s Office"', native_panel)
        self.assertIn('"BuildFinanceOffice"', native_panel)
        self.assertIn('"Signals & CTC"', native_panel)
        self.assertIn('"Train Orders"', native_panel)
        self.assertIn('"My Orders"', native_panel)
        self.assertIn("Main.TryLineCtcRoute", native_panel)
        self.assertIn("Main.TrySetCtcSwitch", native_panel)
        self.assertIn("Main.TryDeliverTrainOrder", native_panel)
        self.assertIn("Main.TryAcknowledgeTrainOrder", native_panel)
        self.assertIn("AccessLevel.Dispatcher", native_panel)
        self.assertIn("InitializeEmbedded", main_source)
        self.assertIn("EmbeddedHarmonyId", main_source)
        self.assertIn('<Reference Include="0Harmony">', project_source)


if __name__ == "__main__":
    unittest.main()
