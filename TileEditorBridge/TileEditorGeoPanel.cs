using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private enum PanelTab
        {
            Geo,
            Scenery,
            Objects,
            Poles,
            Terrain,
            Operations,
            Desktop,
        }

        private enum GeoTool
        {
            Spliney,
            Pieces,
            Track,
            Grade,
            Arc,
            Parallel,
            FitArc,
            Turnout,
            Wye,
            Span,
            Turntable,
        }

        private sealed class NodePropertyClipboard
        {
            internal string SourceId = string.Empty;
            internal TileEditorGraphSession.NodePropertyFields Fields;
            internal float Elevation;
            internal Vector3 Rotation;
            internal bool FlipSwitchStand;
        }

        private PanelTab _panelTab = PanelTab.Geo;
        private GeoTool _geoTool = GeoTool.Track;
        private Vector2 _panelScroll;
        private string _connectStartId = string.Empty;
        private string _deleteConfirmId = string.Empty;
        private string _gradeLength = "100";
        private string _targetGrade = "0.0";
        private string _gradeSteps = "8";
        private string _arcRadius = "60";
        private string _arcDegrees = "30";
        private string _arcNodes = "3";
        private string _turnoutLength = "25";
        private string _turnoutDegrees = "10";
        private string _wyeLeftDegrees = "10";
        private string _wyeRightDegrees = "10";
        private string _wyeLength = "30";
        private int _wyePreset = 1;
        private string _wyeBaseLength = "140";
        private string _wyeDepth = "75";
        private string _wyeStubLength = "50";
        private string _wyeExitLength = "35";
        private bool _showSimpleWyeBuilder;
        private string _pieceLength = "30";
        private string _pieceSections = "1";
        private int _pieceType;
        private string _parallelSeparation = "4";
        private string _parallelTracks = "1";
        private int _parallelSide = 1;
        private readonly List<string> _fitArcNodeIds = new List<string>();
        private string _splinePositionX = "0";
        private string _splinePositionY = "0";
        private string _splinePositionZ = "0";
        private string _splineRotationX = "0";
        private string _splineRotationY = "0";
        private string _splineRotationZ = "0";
        private string _splineWidth = "10";
        private string _splineSelectionKey = string.Empty;
        private float _splineMoveStep = 1f;
        private float _splineRotationStep = 5f;
        private bool _showAdvancedSplineControls;
        private int _newSplineKind;
        private string _newSplineName = string.Empty;
        private string _newSplineProfile = "RAM Road profile";
        private string _newSplineLength = "50";
        private string _newSplineWidth = "8";
        private int _newSplineHeadStyle;
        private int _newSplineTailStyle;
        private string _trackBridgeName = string.Empty;
        private string _trackBridgeBelowRail = "0.30";
        private string _trackBridgePointSpacing = "8";
        private int _trackBridgeHeadStyle;
        private int _trackBridgeTailStyle;
        private string _nodePositionX = "0";
        private string _nodePositionY = "0";
        private string _nodePositionZ = "0";
        private string _nodeRotationX = "0";
        private string _nodeRotationY = "0";
        private string _nodeRotationZ = "0";
        private string _transformNodeId = string.Empty;
        private string _segmentGroupEditorId = string.Empty;
        private string _segmentGroupEditorValue = string.Empty;
        private string _segmentGroupObservedValue = string.Empty;
        private string _lastWorldSelectionKey = string.Empty;
        private float _movementStep = 1f;
        private float _rotationStep = 1f;
        private bool _moveInLocalAxes;
        private bool _showAdvancedNodeControls;
        private bool _showNodeClipboardControls;
        private NodePropertyClipboard _nodePropertyClipboard;
        private bool _turnRight = true;
        private int _graphModIndex;
        private string _selectedGraphModKey = string.Empty;
        private string _selectedGraphPath = string.Empty;
        private bool _showAdvancedGraphLayers;
        private bool _showGraphChooser;
        private string _trackBuildGauge = "Standard";

        private void DrawTileEditorWindow(int id)
        {
            DrawPanelBackdrop();
            GUILayout.BeginVertical();
            DrawPanelHeader();
            _mapEditor?.SetWorkspaceMode(
                _panelTab == PanelTab.Geo,
                _panelTab == PanelTab.Scenery,
                _panelTab == PanelTab.Poles,
                _panelTab == PanelTab.Geo
                && _geoTool == GeoTool.Spliney,
                _panelTab == PanelTab.Objects);
            _mapEditor?.SetOperationsMode(
                _panelTab == PanelTab.Operations);

            GUILayout.BeginHorizontal();
            DrawPanelTab("GEO", PanelTab.Geo);
            DrawPanelTab("SCENERY", PanelTab.Scenery);
            DrawPanelTab("OPERATIONS", PanelTab.Operations);
            DrawPanelTab("OBJECTS", PanelTab.Objects);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawPanelTab("POLES", PanelTab.Poles);
            DrawPanelTab("TERRAIN", PanelTab.Terrain);
            DrawPanelTab("DESKTOP", PanelTab.Desktop);
            GUILayout.EndHorizontal();

            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            var desktopGraphLocked =
                _panelTab != PanelTab.Terrain
                && _panelTab != PanelTab.Desktop
                && DesktopGraphHasUnsavedChanges;
            if (desktopGraphLocked)
            {
                GUILayout.Label(
                    "DESKTOP HAS UNSAVED MAP CHANGES",
                    _offlineStyle);
                GUILayout.Label(
                    "Save or undo them in the desktop editor before making "
                    + "track, scenery, Spliney, or pole edits in-game.",
                    _mutedStyle);
                GUI.enabled = false;
            }
            if (_panelTab != PanelTab.Desktop)
                DrawGraphSelectionBar();
            var waitingForGraphChoice =
                _panelTab != PanelTab.Desktop
                && _mapEditor != null
                && _mapEditor.Available
                && !_mapEditor.GraphOpen;
            if (!waitingForGraphChoice)
            {
                switch (_panelTab)
                {
                    case PanelTab.Geo:
                        DrawGeoPanel();
                        break;
                    case PanelTab.Scenery:
                        DrawSceneryPanel();
                        break;
                    case PanelTab.Objects:
                        DrawMandelaPanel();
                        break;
                    case PanelTab.Poles:
                        DrawPolePanel();
                        break;
                    case PanelTab.Terrain:
                        DrawTerrainPanel();
                        break;
                    case PanelTab.Operations:
                        DrawOperationsPanel();
                        break;
                    default:
                        DrawDesktopPanel();
                        break;
                }
            }
            GUI.enabled = true;
            GUILayout.EndScrollView();

            if (_panelTab != PanelTab.Terrain
                && _mapEditor != null
                && _mapEditor.Available
                && _mapEditor.GraphOpen)
            {
                DrawNativeChangeBar();
            }

            GUILayout.Label(Shorten(_lastPanelMessage, 72), _mutedStyle);
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width - 42f, 28f));
            GUILayout.EndVertical();
            DrawResizeHandle();
        }

        private void DrawPanelBackdrop()
        {
            var bounds = new Rect(0f, 0f, _windowRect.width, _windowRect.height);
            GUI.DrawTexture(
                new Rect(1f, 18f, bounds.width - 2f, bounds.height - 19f),
                _windowBackgroundTexture,
                ScaleMode.StretchToFill,
                true);
            const float border = 2f;
            GUI.DrawTexture(
                new Rect(0f, 0f, bounds.width, border),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(0f, bounds.height - border, bounds.width, border),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(0f, 0f, border, bounds.height),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(bounds.width - border, 0f, border, bounds.height),
                _windowBorderTexture);
        }

        private void DrawResizeHandle()
        {
            var handle = new Rect(
                _windowRect.width - 27f,
                _windowRect.height - 27f,
                23f,
                23f);
            var controlId = GUIUtility.GetControlID(
                0x544552,
                FocusType.Passive,
                handle);
            var currentEvent = Event.current;
            switch (currentEvent.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (handle.Contains(currentEvent.mousePosition)
                        && currentEvent.button == 0)
                    {
                        GUIUtility.hotControl = controlId;
                        _resizingWindow = true;
                        _resizeStartMouse = GUIUtility.GUIToScreenPoint(
                            currentEvent.mousePosition);
                        _resizeStartSize = new Vector2(
                            _windowRect.width,
                            _windowRect.height);
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_resizingWindow && GUIUtility.hotControl == controlId)
                    {
                        var mouse = GUIUtility.GUIToScreenPoint(
                            currentEvent.mousePosition);
                        var delta = mouse - _resizeStartMouse;
                        _windowRect.width = Mathf.Clamp(
                            _resizeStartSize.x + delta.x,
                            MinWindowWidth,
                            Mathf.Max(
                                MinWindowWidth,
                                Screen.width - _windowRect.x - 4f));
                        _windowRect.height = Mathf.Clamp(
                            _resizeStartSize.y + delta.y,
                            MinWindowHeight,
                            Mathf.Max(
                                MinWindowHeight,
                                Screen.height - _windowRect.y - 4f));
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_resizingWindow && GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        _resizingWindow = false;
                        PlayerPrefs.SetFloat(WindowWidthKey, _windowRect.width);
                        PlayerPrefs.SetFloat(WindowHeightKey, _windowRect.height);
                        PlayerPrefs.Save();
                        currentEvent.Use();
                    }
                    break;

                case EventType.Repaint:
                    GUI.skin.button.Draw(
                        handle,
                        new GUIContent("\u2198"),
                        controlId,
                        false);
                    break;
            }
        }

        private void DrawPanelHeader()
        {
            var available = _mapEditor != null && _mapEditor.Available;
            var graphOpen = available && _mapEditor.GraphOpen;
            var terrainReady =
                _panelTab == PanelTab.Terrain && available;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                terrainReady
                    ? "LIVE TERRAIN - PAINT IN THE WORLD"
                    : graphOpen
                    ? _panelTab == PanelTab.Scenery
                        ? "LIVE SCENERY - CLICK CYAN MARKERS"
                        : _panelTab == PanelTab.Objects
                            ? "LIVE OBJECTS - CLICK BASE-GAME OBJECTS"
                        : _panelTab == PanelTab.Poles
                            ? "LIVE POLES - CLICK AMBER MARKERS"
                        : _panelTab == PanelTab.Operations
                            ? "LIVE OPERATIONS - CLICK COLORED MARKERS"
                            : _geoTool == GeoTool.Spliney
                                ? "LIVE SPLINEY - CLICK ROAD/RIVER POINTS"
                                : "LIVE GRAPH - CLICK TRACK IN THE WORLD"
                    : available ? "RAILROADER TRACK GRAPH READY" : "WAITING FOR MAP",
                graphOpen || terrainReady
                    ? _onlineStyle
                    : _offlineStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", GUILayout.Width(28f), GUILayout.Height(22f)))
                SetVisible(false);
            GUILayout.EndHorizontal();
        }

        private void DrawPanelTab(string label, PanelTab tab)
        {
            var oldColor = GUI.backgroundColor;
            if (_panelTab == tab)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(28f)))
            {
                if (_panelTab == PanelTab.Terrain && tab != PanelTab.Terrain)
                    EndTerrainStroke();
                if (_panelTab != tab)
                    CancelPointerPlacement(false);
                _panelTab = tab;
                _deleteConfirmId = string.Empty;
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawGeoPanel()
        {
            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Space(8f);
                GUILayout.Label(
                    "Railroader's live track graph is not ready yet.",
                    _titleStyle);
                GUILayout.Label(
                    "Load into a map, then reopen F9. Tile Editor does not require "
                    + "Alina's Map Editor.",
                    _mutedStyle);
                return;
            }

            if (!_mapEditor.GraphOpen)
            {
                return;
            }

            var rebuildEnabled = GUI.enabled;
            GUILayout.BeginHorizontal();
            GUI.enabled = rebuildEnabled
                          && !_mapEditor.TerrainRebuildPending;
            if (GUILayout.Button(
                    _mapEditor.TerrainRebuildPending
                        ? "Rebuilding Terrain..."
                        : "Rebuild Terrain",
                    GUILayout.Height(28f)))
            {
                RunGameAction(_mapEditor.RebuildTerrain);
            }
            GUI.enabled = rebuildEnabled;
            if (GUILayout.Button(
                    "Rebuild Track",
                    GUILayout.Height(28f)))
            {
                RunGameAction(
                    "Rebuilt track and refreshed overlays",
                    _mapEditor.RebuildTrack);
            }
            GUI.enabled = rebuildEnabled;
            GUILayout.EndHorizontal();
            DrawWorldSelection();
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            DrawGeoToolTab("Spliney", GeoTool.Spliney);
            DrawGeoToolTab("Pieces", GeoTool.Pieces);
            DrawGeoToolTab("Arc", GeoTool.Arc);
            DrawGeoToolTab("Parallel", GeoTool.Parallel);
            DrawGeoToolTab("Fit Arc", GeoTool.FitArc);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawGeoToolTab("Node", GeoTool.Track);
            DrawGeoToolTab("Grade", GeoTool.Grade);
            DrawGeoToolTab("Turnout", GeoTool.Turnout);
            DrawGeoToolTab("Wye", GeoTool.Wye);
            DrawGeoToolTab("Span", GeoTool.Span);
            DrawGeoToolTab("Turntable", GeoTool.Turntable);
            GUILayout.EndHorizontal();
            GUILayout.Space(5f);
            if (_geoTool != GeoTool.Spliney
                && _geoTool != GeoTool.Span
                && _geoTool != GeoTool.Turntable)
            {
                DrawTrackGaugeBar();
                GUILayout.Space(5f);
                DrawNodeEditorLauncher();
                GUILayout.Space(5f);
            }

            switch (_geoTool)
            {
                case GeoTool.Spliney:
                    DrawSplineyTool();
                    break;
                case GeoTool.Pieces:
                    DrawPiecesTool();
                    break;
                case GeoTool.Grade:
                    DrawGradeTool();
                    break;
                case GeoTool.Arc:
                    DrawArcTool();
                    break;
                case GeoTool.Parallel:
                    DrawParallelTool();
                    break;
                case GeoTool.FitArc:
                    DrawFitArcTool();
                    break;
                case GeoTool.Turnout:
                    DrawTurnoutTool();
                    break;
                case GeoTool.Wye:
                    DrawWyeTool();
                    break;
                case GeoTool.Span:
                    DrawSpanBuilder();
                    break;
                case GeoTool.Turntable:
                    DrawTurntableBuilder();
                    break;
                default:
                    DrawTrackTool();
                    break;
            }
        }

        private void DrawGraphSelectionBar()
        {
            if (_mapEditor == null || !_mapEditor.Available)
                return;

            var graphOpen = _mapEditor.GraphOpen;
            if (graphOpen)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    "EDITING MOD: " + _mapEditor.GraphName,
                    _mutedStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(
                        _showGraphChooser
                            ? "CLOSE"
                            : "CHANGE MOD / GRAPH",
                        GUILayout.Width(
                            _showGraphChooser ? 74f : 155f),
                        GUILayout.Height(25f)))
                {
                    _showGraphChooser = !_showGraphChooser;
                }
                GUILayout.EndHorizontal();
                if (!_showGraphChooser)
                    return;
            }
            else
            {
                _showGraphChooser = true;
                GUILayout.Space(7f);
                GUILayout.Label(
                    "CHOOSE A MOD / GAME GRAPH",
                    _titleStyle);
                GUILayout.Label(
                    "The F9 editor works by itself. Choose the installed "
                    + "RailLoader mod whose game-graph JSON should receive "
                    + "track, scenery, Spliney, pole, and terrain changes.",
                    _lineStyle);
            }

            GUILayout.Space(4f);
            var graphControlsEnabled = GUI.enabled;
            if (_mapEditor.HasUnsavedContent)
            {
                GUILayout.Label(
                    "SAVE OR UNDO CURRENT IN-GAME CHANGES BEFORE SWITCHING",
                    _offlineStyle);
                GUILayout.Label(
                    UnsavedGraphSelectionSummary(),
                    _mutedStyle);
            }

            var online = IsEditorOnline();
            if (online
                && _state != null
                && !string.IsNullOrWhiteSpace(_state.layerPath))
            {
                GUI.enabled =
                    graphControlsEnabled
                    && !_mapEditor.HasUnsavedContent;
                if (GUILayout.Button(
                        "Use Live Desktop Layer: "
                        + Shorten(_state.layerName, 32),
                        GUILayout.Height(31f)))
                {
                    OpenGraphFromPanel(
                        _state.layerPath,
                        "desktop " + Safe(_state.layerName),
                        false);
                }
                GUI.enabled = graphControlsEnabled;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Installed map mods",
                _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    "Refresh",
                    GUILayout.Width(78f),
                    GUILayout.Height(25f)))
            {
                RunGameAction(
                    "Refreshed installed mod graphs",
                    _mapEditor.RefreshGraphChoices);
            }
            GUILayout.EndHorizontal();

            var choices = _mapEditor.GraphChoices;
            if (choices.Count == 0)
            {
                GUILayout.Label(
                    "No installed Definition.json game-graph mixintos were "
                    + "found. Install or create a RailLoader map mod first.",
                    _mutedStyle);
                GUI.enabled = graphControlsEnabled;
                return;
            }

            var mods = choices
                .GroupBy(choice => choice.ModKey)
                .Select(group => new
                {
                    Key = group.Key,
                    Name = group.First().ModName,
                    Layers = group.ToList(),
                })
                .OrderBy(
                    group => group.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(_selectedGraphModKey))
            {
                var openChoice = choices.FirstOrDefault(choice =>
                    string.Equals(
                        choice.Path,
                        _mapEditor.GraphPath,
                        StringComparison.OrdinalIgnoreCase));
                _selectedGraphModKey =
                    openChoice?.ModKey ?? mods[0].Key;
            }
            _graphModIndex = mods.FindIndex(group =>
                string.Equals(
                    group.Key,
                    _selectedGraphModKey,
                    StringComparison.OrdinalIgnoreCase));
            if (_graphModIndex < 0)
                _graphModIndex = 0;
            var modLabels = mods
                .Select(group =>
                    group.Layers.Any(choice => string.Equals(
                        choice.Path,
                        _mapEditor.GraphPath,
                        StringComparison.OrdinalIgnoreCase))
                        ? "\u2713 " + group.Name
                        : group.Name)
                .ToArray();
            var previousModIndex = _graphModIndex;
            _graphModIndex = GUILayout.SelectionGrid(
                _graphModIndex,
                modLabels,
                1);
            var selectedMod = mods[_graphModIndex];
            if (_graphModIndex != previousModIndex
                || !string.Equals(
                    _selectedGraphModKey,
                    selectedMod.Key,
                    StringComparison.OrdinalIgnoreCase))
            {
                _selectedGraphModKey = selectedMod.Key;
                _selectedGraphPath = string.Empty;
                _showAdvancedGraphLayers = false;
            }

            var primary = selectedMod.Layers.FirstOrDefault(
                              choice => choice.IsPrimary)
                          ?? selectedMod.Layers[0];
            var currentInMod = selectedMod.Layers.FirstOrDefault(choice =>
                string.Equals(
                    choice.Path,
                    _mapEditor.GraphPath,
                    StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(_selectedGraphPath)
                || !selectedMod.Layers.Any(choice => string.Equals(
                    choice.Path,
                    _selectedGraphPath,
                    StringComparison.OrdinalIgnoreCase)))
            {
                _selectedGraphPath =
                    currentInMod?.Path ?? primary.Path;
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Layer: "
                + (selectedMod.Layers.FirstOrDefault(choice =>
                       string.Equals(
                           choice.Path,
                           _selectedGraphPath,
                           StringComparison.OrdinalIgnoreCase))
                   ?? primary).LayerName,
                _lineStyle);
            GUILayout.FlexibleSpace();
            if (selectedMod.Layers.Count > 1
                && GUILayout.Button(
                    _showAdvancedGraphLayers
                        ? "HIDE LAYERS"
                        : "MORE LAYERS (" + selectedMod.Layers.Count + ")",
                    GUILayout.Width(135f),
                    GUILayout.Height(25f)))
            {
                _showAdvancedGraphLayers = !_showAdvancedGraphLayers;
            }
            GUILayout.EndHorizontal();

            if (_showAdvancedGraphLayers)
            {
                GUILayout.Label(
                    "Advanced: choose the JSON mixinto layer that should "
                    + "receive edits.",
                    _mutedStyle);
                var layerIndex = Mathf.Max(
                    0,
                    selectedMod.Layers.FindIndex(choice =>
                        string.Equals(
                            choice.Path,
                            _selectedGraphPath,
                            StringComparison.OrdinalIgnoreCase)));
                var layerLabels = selectedMod.Layers
                    .Select(choice =>
                        (choice.IsPrimary ? "\u2605 " : string.Empty)
                        + choice.LayerName)
                    .ToArray();
                layerIndex = GUILayout.SelectionGrid(
                    layerIndex,
                    layerLabels,
                    1);
                _selectedGraphPath =
                    selectedMod.Layers[layerIndex].Path;
            }

            var selected = selectedMod.Layers.FirstOrDefault(choice =>
                               string.Equals(
                                   choice.Path,
                                   _selectedGraphPath,
                                   StringComparison.OrdinalIgnoreCase))
                           ?? primary;
            var alreadyOpen = string.Equals(
                selected.Path,
                _mapEditor.GraphPath,
                StringComparison.OrdinalIgnoreCase);
            GUI.enabled =
                graphControlsEnabled
                &&
                !_mapEditor.HasUnsavedContent
                && !alreadyOpen;
            if (GUILayout.Button(
                    alreadyOpen
                        ? "CURRENT GRAPH"
                        : "EDIT " + Shorten(selected.ModName, 34),
                    GUILayout.Height(33f)))
            {
                OpenGraphFromPanel(
                    selected.Path,
                    selected.DisplayName,
                    true);
            }
            GUI.enabled = graphControlsEnabled;
            GUILayout.Label(
                "The selected graph is remembered and opens automatically "
                + "next time when the desktop editor is offline.",
                _mutedStyle);
        }

        private string UnsavedGraphSelectionSummary()
        {
            var parts = new List<string>();
            if (_mapEditor.Dirty)
                parts.Add("track/scenery");
            if (_mapEditor.SplineyDirty)
                parts.Add("roads/rivers/bridges");
            if (_mapEditor.TelegraphPoleDirty)
                parts.Add("telegraph poles");
            if (_mapEditor.TerrainDirty)
                parts.Add("terrain");
            return parts.Count == 0
                ? "Current graph is clean."
                : "Unsaved: " + string.Join(", ", parts.ToArray());
        }

        private void OpenGraphFromPanel(
            string path,
            string displayName,
            bool remember)
        {
            if (_mapEditor.HasUnsavedContent)
            {
                _lastPanelMessage =
                    "Save or undo in-game changes before switching graphs";
                return;
            }
            RunGameAction(
                "Opened " + displayName,
                () =>
                {
                    _mapEditor.OpenGraph(path);
                    _showGraphChooser = false;
                    _autoOpenAttemptPath = path;
                    if (!remember)
                        return;
                    _preferredGraphPath = path;
                    PlayerPrefs.SetString(
                        LastGraphPathKey,
                        _preferredGraphPath);
                    PlayerPrefs.Save();
                });
        }

        private void DrawWorldSelection()
        {
            var node = _mapEditor.SelectedNode;
            var segment = _mapEditor.SelectedSegment;
            var spline = _geoTool == GeoTool.Spliney
                ? _mapEditor.SelectedSplinePoint
                : null;
            var selectionKey = spline != null
                ? "spline:" + spline.Id + ":" + spline.Index
                : node != null
                ? "node:" + node.Id
                : segment != null
                    ? "segment:" + segment.Id
                    : "none";
            if (!string.Equals(
                    selectionKey,
                    _lastWorldSelectionKey,
                    StringComparison.Ordinal))
            {
                _lastWorldSelectionKey = selectionKey;
                _panelScroll = Vector2.zero;
                _deleteConfirmId = string.Empty;
                _showAdvancedNodeControls = false;
                _showAdvancedSplineControls = false;
            }
            if (spline != null)
            {
                GUILayout.Label(
                    spline.Style.ToUpperInvariant()
                    + "  " + spline.Id,
                    _titleStyle);
                GUILayout.Label(
                    "Point " + (spline.Index + 1) + " / " + spline.Count
                    + (spline.HasWidth
                        ? "   Width "
                          + spline.Width.ToString(
                              "0.##",
                              CultureInfo.InvariantCulture)
                          + " m"
                        : "   Ends "
                          + spline.HeadStyle + " / " + spline.TailStyle),
                    _lineStyle);
                GUILayout.Label(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Position  {0:F2}, {1:F2}, {2:F2}",
                        spline.Position.x,
                        spline.Position.y,
                        spline.Position.z),
                    _lineStyle);
            }
            else if (_geoTool == GeoTool.Spliney
                     && _mapEditor.SplineTrackPickMode
                     && segment != null)
            {
                GUILayout.Label(
                    "TRACK FOR BRIDGE  " + segment.Id,
                    _titleStyle);
                GUILayout.Label(
                    "Length "
                    + segment.Length.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)
                    + " m   Selected track is green.",
                    _lineStyle);
            }
            else if (_geoTool == GeoTool.Spliney)
            {
                GUILayout.Label("SPLINEY WORKSPACE", _titleStyle);
                GUILayout.Label(
                    "Road points are orange; river points are blue. "
                    + "Bridge/trestle points are green. The selected point "
                    + "is magenta.",
                    _lineStyle);
            }
            else if (node != null)
            {
                GUILayout.Label("NODE  " + node.Id, _titleStyle);
                GUILayout.Label(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Position  {0:F2}, {1:F2}, {2:F2}",
                        node.Position.x, node.Position.y, node.Position.z),
                    _lineStyle);
                GUILayout.Label(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Heading {0:F2} deg    Grade {1:+0.000;-0.000;0.000}%",
                        node.Rotation.y,
                        _mapEditor.SelectedNodeGrade()),
                    _lineStyle);
            }
            else if (segment != null)
            {
                GUILayout.Label("SEGMENT  " + segment.Id, _titleStyle);
                GUILayout.Label(
                    "Group "
                    + (string.IsNullOrWhiteSpace(segment.GroupId)
                        ? "(none)"
                        : segment.GroupId)
                    + "   \u2022   Gauge " + segment.Gauge
                    + "   \u2022   Selected track is green.",
                    _lineStyle);
            }
            else
            {
                GUILayout.Label("SELECT TRACK IN THE WORLD", _titleStyle);
                GUILayout.Label(
                    "Click a cyan first node, then Shift-click a second node "
                    + "to connect them. Ctrl-drag a node over terrain to move "
                    + "it, or release it over another cyan node to connect. "
                    + "Selected nodes are magenta; selected segments are green.",
                    _lineStyle);
            }
            if (_geoTool != GeoTool.Spliney
                && !string.IsNullOrWhiteSpace(
                    _mapEditor.WorldNodeShortcutStatus))
            {
                GUILayout.Label(
                    _mapEditor.WorldNodeShortcutStatus,
                    _mutedStyle);
            }
        }

        private void DrawGeoToolTab(string label, GeoTool tool)
        {
            var oldColor = GUI.backgroundColor;
            if (_geoTool == tool)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(27f)))
            {
                if (_geoTool != tool)
                {
                    _geoTool = tool;
                    _panelScroll = Vector2.zero;
                    _deleteConfirmId = string.Empty;
                }
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawTrackTool()
        {
            var node = _mapEditor.SelectedNode;
            var segment = _mapEditor.SelectedSegment;
            if (node != null)
            {
                SyncNodeTransformFields(node, false);
                DrawSelectedNodeTrackBuilder(node);
                return;
            }
            _transformNodeId = string.Empty;
            if (segment != null)
            {
                DrawCompactSegmentEditor(segment);
                return;
            }

            GUILayout.Label("Start laying track", _titleStyle);
            GUILayout.Label(
                "Click an existing cyan node, or arm placement and click "
                + "the exact ground position with the mouse pointer.",
                _lineStyle);
            _repeatPointerPlacement = GUILayout.Toggle(
                _repeatPointerPlacement,
                " Keep placing / build a continuous chain");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "Place Node with Mouse",
                    GUILayout.Height(31f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.FreeTrackNode,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            if (GUILayout.Button(
                    "Camera Target",
                    GUILayout.Width(110f),
                    GUILayout.Height(31f)))
            {
                RunGameAction(
                    "Added a free node at the camera target",
                    _mapEditor.AddNodeAtCamera);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Overlays", GUILayout.Height(30f)))
                RunGameAction("Rebuilt track overlays", _mapEditor.RebuildTrack);
            GUILayout.EndHorizontal();
            DrawPointerPlacementStatus();
        }

        private void DrawNodeEditorLauncher()
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label(
                "NODE TOOLS",
                _titleStyle,
                GUILayout.Width(92f));
            GUILayout.Label(
                _mapEditor.SelectedNode == null
                    ? "Next ID: " + _mapEditor.NextNodeIdPreview
                    : "Selected: "
                      + Shorten(_mapEditor.SelectedNode.Id, 28),
                _mutedStyle);
            GUILayout.FlexibleSpace();
            var oldColor = GUI.backgroundColor;
            if (_nodeEditorVisible)
            {
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(
                    _nodeEditorVisible
                        ? "NODE EDITOR OPEN"
                        : "OPEN NODE EDITOR",
                    GUILayout.Width(165f),
                    GUILayout.Height(29f)))
            {
                OpenNodeEditor();
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();
        }

        private void DrawSelectedNodeTrackBuilder(
            TileEditorGraphSession.SelectionInfo node)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SELECTED TRACK NODE", _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(node.Id, _titleStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Position "
                + node.Position.x.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)
                + ", "
                + node.Position.y.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)
                + ", "
                + node.Position.z.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)
                + "   Heading "
                + node.Rotation.y.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture)
                + "°",
                _mutedStyle);
            if (GUILayout.Button(
                    "OPEN TRANSFORM / NODE EDITOR",
                    GUILayout.Height(32f)))
            {
                OpenNodeEditor();
            }
            GUILayout.EndVertical();

            GUILayout.Space(6f);
            GUILayout.Label("Continue laying track", _titleStyle);
            _repeatPointerPlacement = GUILayout.Toggle(
                _repeatPointerPlacement,
                " Keep placing / build a continuous chain");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "PLACE NEXT WITH MOUSE",
                    GUILayout.Height(32f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.ConnectedTrackNode,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor =
                new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    "ADD +10 m",
                    GUILayout.Width(135f),
                    GUILayout.Height(32f)))
            {
                RunGameAction(
                    "Added connected node 10 m ahead",
                    _mapEditor.AddNextNode);
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Movement, rotation, exact coordinates, copy/paste, "
                + "split, level, flip, and delete are in the separate "
                + "Node Editor.",
                _mutedStyle);
            DrawPointerPlacementStatus();
        }

        private void DrawNodeNamingControls()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("NEW NODE IDS", _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Next: " + _mapEditor.NextNodeIdPreview,
                _mutedStyle);
            GUILayout.EndHorizontal();

            var previousPrefix = _nodeIdPrefix;
            var previousBaseName = _nodeIdBaseName;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Prefix", GUILayout.Width(52f));
            _nodeIdPrefix = GUILayout.TextField(
                _nodeIdPrefix ?? string.Empty,
                GUILayout.MinWidth(90f));
            GUILayout.Label("Name", GUILayout.Width(45f));
            _nodeIdBaseName = GUILayout.TextField(
                _nodeIdBaseName ?? string.Empty,
                GUILayout.MinWidth(110f));
            GUILayout.EndHorizontal();
            if (!string.Equals(
                    previousPrefix,
                    _nodeIdPrefix,
                    StringComparison.Ordinal)
                || !string.Equals(
                    previousBaseName,
                    _nodeIdBaseName,
                    StringComparison.Ordinal))
            {
                _mapEditor.ConfigureNewNodeIds(
                    _nodeIdPrefix,
                    _nodeIdBaseName);
                PlayerPrefs.SetString(
                    NodeIdPrefixKey,
                    _nodeIdPrefix ?? string.Empty);
                PlayerPrefs.SetString(
                    NodeIdBaseNameKey,
                    _nodeIdBaseName ?? string.Empty);
            }
            GUILayout.Label(
                "Every new Node, Piece, Arc, Grade, Parallel, Turnout, and "
                + "Wye node uses this pattern. Invalid characters are "
                + "removed and a unique number is added automatically.",
                _mutedStyle);
            GUILayout.EndVertical();
        }

        private void DrawCompactNodeEditor(
            TileEditorGraphSession.SelectionInfo node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("NODE TRANSFORM", _titleStyle);
            GUILayout.FlexibleSpace();
            var oldColor = GUI.backgroundColor;
            if (_showAdvancedNodeControls)
                GUI.backgroundColor = new Color(0.72f, 0.55f, 0.18f);
            if (GUILayout.Button(
                    _showAdvancedNodeControls ? "LESS" : "MORE...",
                    GUILayout.Width(170f),
                    GUILayout.Height(30f)))
            {
                _showAdvancedNodeControls = !_showAdvancedNodeControls;
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();

            DrawPrimaryMoveControls();
            GUILayout.Space(5f);
            DrawPrimaryRotationControls();

            GUILayout.Space(6f);
            GUILayout.Label("Node actions", _mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "PLACE NEXT (CONNECTED)",
                    GUILayout.Height(31f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.ConnectedTrackNode,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            if (GUILayout.Button(
                    "PLACE FREE NODE",
                    GUILayout.Height(31f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.FreeTrackNode,
                    string.Empty,
                    false);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var actionColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button("Add +10 m", GUILayout.Height(30f)))
            {
                RunGameAction(
                    "Added connected node 10 m ahead",
                    _mapEditor.AddNextNode);
            }
            GUI.backgroundColor = actionColor;
            if (GUILayout.Button("Split", GUILayout.Height(30f)))
                RunGameAction("Split selected junction", _mapEditor.SplitSelectedNode);
            if (GUILayout.Button("Level", GUILayout.Height(30f)))
                RunNodeTransformAction(
                    "Leveled selected node",
                    _mapEditor.LevelSelectedNode);
            if (GUILayout.Button("Flip", GUILayout.Height(30f)))
                RunNodeTransformAction(
                    "Flipped selected node",
                    _mapEditor.FlipSelectedNode);
            GUILayout.EndHorizontal();
            DrawPointerPlacementStatus();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Show", GUILayout.Height(29f)))
                RunGameAction("Centered selected track", _mapEditor.ShowSelected);
            DrawDeleteControl(node, null);
            GUILayout.EndHorizontal();
            DrawNodeClipboardControls(node);

            if (!string.IsNullOrWhiteSpace(_connectStartId))
                DrawConnectCompletion(node);

            if (_showAdvancedNodeControls)
            {
                GUILayout.Space(8f);
                DrawAdvancedNodeControls();
            }
        }

        private void DrawNodeClipboardControls(
            TileEditorGraphSession.SelectionInfo node)
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "COPY ALL SETTINGS",
                    GUILayout.Height(29f)))
            {
                RunGameAction(
                    () => CopyNodeProperties(
                        node,
                        TileEditorGraphSession.NodePropertyFields.All,
                        "all settings"));
            }
            var oldColor = GUI.backgroundColor;
            if (_showNodeClipboardControls)
            {
                GUI.backgroundColor =
                    new Color(0.72f, 0.55f, 0.18f);
            }
            if (GUILayout.Button(
                    _showNodeClipboardControls
                        ? "HIDE CLIPBOARD"
                        : "COPY / PASTE...",
                    GUILayout.Width(145f),
                    GUILayout.Height(29f)))
            {
                _showNodeClipboardControls =
                    !_showNodeClipboardControls;
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();

            if (!_showNodeClipboardControls)
                return;

            GUILayout.Label(
                "COPY ONLY FROM CURRENT NODE",
                _titleStyle);
            GUILayout.BeginHorizontal();
            NodeClipboardCopyButton(
                node,
                "ELEVATION",
                TileEditorGraphSession.NodePropertyFields.Elevation);
            NodeClipboardCopyButton(
                node,
                "GRADE",
                TileEditorGraphSession.NodePropertyFields.Grade);
            NodeClipboardCopyButton(
                node,
                "HEADING",
                TileEditorGraphSession.NodePropertyFields.Heading);
            NodeClipboardCopyButton(
                node,
                "BANK",
                TileEditorGraphSession.NodePropertyFields.Bank);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            NodeClipboardCopyButton(
                node,
                "ROTATION X/Y/Z",
                TileEditorGraphSession.NodePropertyFields.Rotation);
            NodeClipboardCopyButton(
                node,
                "ELEV + GRADE",
                TileEditorGraphSession.NodePropertyFields
                    .ElevationAndGrade);
            NodeClipboardCopyButton(
                node,
                "ELEV + ROTATION",
                TileEditorGraphSession.NodePropertyFields
                    .ElevationAndRotation);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            NodeClipboardCopyButton(
                node,
                "SWITCH FLAG",
                TileEditorGraphSession.NodePropertyFields.SwitchStand);
            NodeClipboardCopyButton(
                node,
                "ALL SETTINGS",
                TileEditorGraphSession.NodePropertyFields.All);
            GUILayout.EndHorizontal();

            if (_nodePropertyClipboard == null)
            {
                GUILayout.Label(
                    "Clipboard empty. Choose exactly what to copy above.",
                    _mutedStyle);
                return;
            }

            var copied = _nodePropertyClipboard;
            GUILayout.Label(
                "COPIED FROM " + copied.SourceId
                + "  \u2022  "
                + DescribeNodeClipboardFields(copied.Fields),
                _mutedStyle);
            GUILayout.Label(
                "PASTE TO CURRENT NODE  \u2022  unavailable combinations "
                + "are disabled",
                _titleStyle);

            GUILayout.BeginHorizontal();
            NodeClipboardPasteButton(
                "ELEVATION",
                TileEditorGraphSession.NodePropertyFields.Elevation);
            NodeClipboardPasteButton(
                "GRADE",
                TileEditorGraphSession.NodePropertyFields.Grade);
            NodeClipboardPasteButton(
                "HEADING",
                TileEditorGraphSession.NodePropertyFields.Heading);
            NodeClipboardPasteButton(
                "BANK",
                TileEditorGraphSession.NodePropertyFields.Bank);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            NodeClipboardPasteButton(
                "ROTATION X/Y/Z",
                TileEditorGraphSession.NodePropertyFields.Rotation);
            NodeClipboardPasteButton(
                "ELEV + GRADE",
                TileEditorGraphSession.NodePropertyFields
                    .ElevationAndGrade);
            NodeClipboardPasteButton(
                "ELEV + ROTATION",
                TileEditorGraphSession.NodePropertyFields
                    .ElevationAndRotation);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            NodeClipboardPasteButton(
                "SWITCH FLAG",
                TileEditorGraphSession.NodePropertyFields.SwitchStand);
            NodeClipboardPasteButton(
                "ALL SETTINGS",
                TileEditorGraphSession.NodePropertyFields.All);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "X/Z position, node ID, and graph connections are never "
                + "changed by paste.",
                _mutedStyle);
        }

        private void NodeClipboardCopyButton(
            TileEditorGraphSession.SelectionInfo node,
            string label,
            TileEditorGraphSession.NodePropertyFields fields)
        {
            if (GUILayout.Button(
                    label,
                    GUILayout.Height(29f)))
            {
                RunGameAction(
                    () => CopyNodeProperties(
                        node,
                        fields,
                        label.ToLowerInvariant()));
            }
        }

        private string CopyNodeProperties(
            TileEditorGraphSession.SelectionInfo node,
            TileEditorGraphSession.NodePropertyFields fields,
            string description)
        {
            if (node == null)
            {
                throw new InvalidOperationException(
                    "Click a cyan node first.");
            }
            if (fields
                == TileEditorGraphSession.NodePropertyFields.None)
            {
                throw new InvalidOperationException(
                    "Choose at least one node property to copy.");
            }
            var rotation = Vector3.zero;
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Grade) != 0)
            {
                rotation.x = node.Rotation.x;
            }
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Heading) != 0)
            {
                rotation.y = node.Rotation.y;
            }
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Bank) != 0)
            {
                rotation.z = node.Rotation.z;
            }
            _nodePropertyClipboard =
                new NodePropertyClipboard
                {
                    SourceId = node.Id,
                    Fields = fields,
                    Elevation =
                        (fields
                         & TileEditorGraphSession.NodePropertyFields
                             .Elevation) != 0
                            ? node.Position.y
                            : 0f,
                    Rotation = rotation,
                    FlipSwitchStand =
                        (fields
                         & TileEditorGraphSession.NodePropertyFields
                             .SwitchStand) != 0
                        && node.FlipSwitchStand,
                };
            _showNodeClipboardControls = true;
            return "Copied " + description
                   + " from " + node.Id;
        }

        private static string DescribeNodeClipboardFields(
            TileEditorGraphSession.NodePropertyFields fields)
        {
            var names = new List<string>();
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Elevation) != 0)
            {
                names.Add("Elevation");
            }
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Grade) != 0)
            {
                names.Add("Grade");
            }
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Heading) != 0)
            {
                names.Add("Heading");
            }
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.Bank) != 0)
            {
                names.Add("Bank");
            }
            if ((fields
                 & TileEditorGraphSession.NodePropertyFields.SwitchStand) != 0)
            {
                names.Add("Switch Flag");
            }
            return names.Count == 0
                ? "Nothing"
                : string.Join(" + ", names.ToArray());
        }

        private void NodeClipboardPasteButton(
            string label,
            TileEditorGraphSession.NodePropertyFields fields)
        {
            var oldEnabled = GUI.enabled;
            GUI.enabled =
                oldEnabled
                && _nodePropertyClipboard != null
                && (_nodePropertyClipboard.Fields & fields) == fields;
            if (!GUILayout.Button(
                    label,
                    GUILayout.Height(29f)))
            {
                GUI.enabled = oldEnabled;
                return;
            }
            GUI.enabled = oldEnabled;
            var copied = _nodePropertyClipboard;
            if (copied == null)
            {
                _lastPanelMessage =
                    "Copy node settings first.";
                return;
            }
            RunNodeTransformAction(
                "Pasted " + label.ToLowerInvariant()
                + " from " + copied.SourceId,
                () => _mapEditor.PasteSelectedNodeProperties(
                    copied.Elevation,
                    copied.Rotation,
                    copied.FlipSwitchStand,
                    fields));
        }

        private void DrawPrimaryMoveControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "MOVE   Step "
                + _movementStep.ToString("0.##", CultureInfo.InvariantCulture)
                + " m",
                _titleStyle);
            GUILayout.FlexibleSpace();
            var oldColor = GUI.backgroundColor;
            if (!_moveInLocalAxes)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    "WORLD",
                    GUILayout.Width(75f),
                    GUILayout.Height(27f)))
            {
                _moveInLocalAxes = false;
            }
            GUI.backgroundColor = oldColor;
            if (_moveInLocalAxes)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    "LOCAL",
                    GUILayout.Width(75f),
                    GUILayout.Height(27f)))
            {
                _moveInLocalAxes = true;
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                _moveInLocalAxes
                    ? "Local X/Z follows the selected node's heading."
                    : "World X/Y/Z follows the map coordinate axes.",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton("0.01", 0.01f, ref _movementStep);
            DrawQuickStepButton("0.1", 0.1f, ref _movementStep);
            DrawQuickStepButton("0.5", 0.5f, ref _movementStep);
            DrawQuickStepButton("1", 1f, ref _movementStep);
            DrawQuickStepButton("5", 5f, ref _movementStep);
            DrawQuickStepButton("10", 10f, ref _movementStep);
            DrawQuickStepButton("25", 25f, ref _movementStep);
            DrawQuickStepButton("50", 50f, ref _movementStep);
            DrawQuickStepButton("100", 100f, ref _movementStep);
            DrawQuickStepButton("1000", 1000f, ref _movementStep);
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);
            GUILayout.Label("DIRECTION PAD", _mutedStyle);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(
                GUI.skin.box,
                GUILayout.Width(138f));
            GUILayout.Label("ELEVATION  Y", _mutedStyle);
            NodeMovePadButton(
                "\u25B2\nRAISE  +Y",
                new Vector3(0f, _movementStep, 0f),
                "Raise the selected node by the active movement step.");
            NodeMovePadButton(
                "\u25BC\nLOWER  -Y",
                new Vector3(0f, -_movementStep, 0f),
                "Lower the selected node by the active movement step.");
            GUILayout.EndVertical();
            GUILayout.Space(5f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(
                _moveInLocalAxes
                    ? "LOCAL PLAN  X / Z"
                    : "WORLD PLAN  X / Z",
                _mutedStyle);
            NodeMovePadButton(
                "\u25B2\nFORWARD  +Z",
                new Vector3(0f, 0f, _movementStep),
                _moveInLocalAxes
                    ? "Move forward along the node's heading."
                    : "Move toward positive world Z.");
            GUILayout.BeginHorizontal();
            NodeMovePadButton(
                "\u25C0\nLEFT  -X",
                new Vector3(-_movementStep, 0f, 0f),
                _moveInLocalAxes
                    ? "Move left across the node's heading."
                    : "Move toward negative world X.");
            NodeMovePadButton(
                "\u25BC\nBACK  -Z",
                new Vector3(0f, 0f, -_movementStep),
                _moveInLocalAxes
                    ? "Move backward along the node's heading."
                    : "Move toward negative world Z.");
            NodeMovePadButton(
                "\u25B6\nRIGHT  +X",
                new Vector3(_movementStep, 0f, 0f),
                _moveInLocalAxes
                    ? "Move right across the node's heading."
                    : "Move toward positive world X.");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawPrimaryRotationControls()
        {
            GUILayout.Label(
                "ROTATE   Step "
                + _rotationStep.ToString("0.##", CultureInfo.InvariantCulture)
                + " degrees",
                _titleStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton("0.01", 0.01f, ref _rotationStep);
            DrawQuickStepButton("0.1", 0.1f, ref _rotationStep);
            DrawQuickStepButton("0.5", 0.5f, ref _rotationStep);
            DrawQuickStepButton("1", 1f, ref _rotationStep);
            DrawQuickStepButton("5", 5f, ref _rotationStep);
            DrawQuickStepButton("10", 10f, ref _rotationStep);
            DrawQuickStepButton("15", 15f, ref _rotationStep);
            DrawQuickStepButton("30", 30f, ref _rotationStep);
            DrawQuickStepButton("45", 45f, ref _rotationStep);
            DrawQuickStepButton("60", 60f, ref _rotationStep);
            DrawQuickStepButton("90", 90f, ref _rotationStep);
            DrawQuickStepButton("180", 180f, ref _rotationStep);
            GUILayout.EndHorizontal();

            GUILayout.Space(3f);
            GUILayout.Label("ROTATION AXES", _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawNodeRotationAxisGroup(
                "PITCH  X",
                new Vector3(-_rotationStep, 0f, 0f),
                new Vector3(_rotationStep, 0f, 0f),
                "Decrease pitch/grade.",
                "Increase pitch/grade.");
            DrawNodeRotationAxisGroup(
                "HEADING  Y",
                new Vector3(0f, -_rotationStep, 0f),
                new Vector3(0f, _rotationStep, 0f),
                "Turn heading left.",
                "Turn heading right.");
            DrawNodeRotationAxisGroup(
                "ROLL  Z",
                new Vector3(0f, 0f, -_rotationStep),
                new Vector3(0f, 0f, _rotationStep),
                "Decrease track bank.",
                "Increase track bank.");
            GUILayout.EndHorizontal();
            GUILayout.Label(
                string.IsNullOrWhiteSpace(GUI.tooltip)
                    ? "Hover an arrow for its axis action. Every nudge is "
                      + "undoable."
                    : GUI.tooltip,
                _mutedStyle);
        }

        private void DrawAdvancedNodeControls()
        {
            GUILayout.Label("Advanced node controls", _titleStyle);
            GUILayout.Label(
                _moveInLocalAxes
                    ? "Movement axes: LOCAL (X is sideways, Z follows node heading)"
                    : "Movement axes: WORLD (X/Y/Z map coordinates)",
                _mutedStyle);

            DrawVectorFields(
                "Position",
                ref _nodePositionX,
                ref _nodePositionY,
                ref _nodePositionZ);
            DrawVectorFields(
                "Rotation",
                ref _nodeRotationX,
                ref _nodeRotationY,
                ref _nodeRotationZ);

            if (GUILayout.Button("Apply Exact Position + Rotation", GUILayout.Height(30f)))
            {
                RunNodeTransformAction(
                    "Applied exact node transform",
                    () => _mapEditor.SetSelectedNodeTransform(
                        new Vector3(
                            ParseFloat(_nodePositionX, "position X"),
                            ParseFloat(_nodePositionY, "position Y"),
                            ParseFloat(_nodePositionZ, "position Z")),
                        new Vector3(
                            ParseFloat(_nodeRotationX, "rotation X"),
                            ParseFloat(_nodeRotationY, "rotation Y"),
                            ParseFloat(_nodeRotationZ, "rotation Z"))));
            }

            GUILayout.Space(4f);
            GUILayout.Label("All movement steps (m)", _mutedStyle);
            _movementStep = DrawStepSelector(
                _movementStep,
                new[]
                {
                    0.01f, 0.1f, 0.5f, 1f, 5f,
                    10f, 25f, 50f, 100f, 1000f,
                },
                5);
            GUILayout.Label("All rotation steps (degrees)", _mutedStyle);
            _rotationStep = DrawStepSelector(
                _rotationStep,
                new[]
                {
                    0.01f, 0.1f, 0.5f, 1f, 5f, 10f,
                    15f, 30f, 45f, 60f, 90f, 180f,
                },
                6);

            if (GUILayout.Button("Reset Rotation", GUILayout.Height(28f)))
                RunNodeTransformAction(
                    "Reset selected node rotation",
                    _mapEditor.ResetSelectedNodeRotation);

            GUILayout.Space(5f);
            GUILayout.Label("Connect nodes", _mutedStyle);
            if (GUILayout.Button("Use Selected Node as Connect Start", GUILayout.Height(29f)))
            {
                RunGameAction("Connect start saved", () =>
                {
                    _mapEditor.SetConnectStart(out _connectStartId);
                });
            }
            GUILayout.Label(
                "Set the start, click another cyan node in the world, then "
                + "use the connect bar that appears in the main controls.",
                _mutedStyle);
        }

        private void DrawConnectCompletion(
            TileEditorGraphSession.SelectionInfo node)
        {
            GUILayout.Space(5f);
            GUILayout.Label("CONNECT FROM  " + _connectStartId, _mutedStyle);
            GUILayout.BeginHorizontal();
            GUI.enabled = node != null
                          && !string.Equals(
                              node.Id, _connectStartId, StringComparison.Ordinal);
            if (GUILayout.Button("Connect to Selected", GUILayout.Height(31f)))
            {
                var start = _connectStartId;
                RunGameAction("Connected " + start + " to selected node", () =>
                {
                    _mapEditor.ConnectFrom(start);
                    _connectStartId = string.Empty;
                });
            }
            GUI.enabled = true;
            if (GUILayout.Button("Cancel", GUILayout.Width(82f), GUILayout.Height(31f)))
                _connectStartId = string.Empty;
            GUILayout.EndHorizontal();
        }

        private void DrawCompactSegmentEditor(
            TileEditorGraphSession.SelectionInfo segment)
        {
            SyncSegmentGroupEditor(segment);
            GUILayout.Label("Track segment", _titleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Inject Control Node", GUILayout.Height(32f)))
                RunGameAction(
                    "Injected a node at segment midpoint",
                    _mapEditor.InjectSelectedSegment);
            if (GUILayout.Button("Show", GUILayout.Height(32f)))
                RunGameAction("Centered selected track", _mapEditor.ShowSelected);
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.Label(
                "Segment gauge: " + segment.Gauge,
                _titleStyle);
            GUILayout.Label(
                "Narrow-gauge metadata is consumed by FUSE Narrow Gauge. "
                + "Dual L/R selects the shared outside rail; DUAL T is one "
                + "fixed L-to-R transition segment.",
                _mutedStyle);
            var gaugeRuntimeStatus =
                _mapEditor.DescribeGaugeRuntime(segment.Gauge);
            if (!string.IsNullOrWhiteSpace(gaugeRuntimeStatus))
            {
                GUILayout.Label(
                    gaugeRuntimeStatus,
                    _mapEditor.NarrowGaugeRuntimeReady
                        ? _onlineStyle
                        : _offlineStyle);
            }
            GUILayout.BeginHorizontal();
            GUI.enabled = !string.Equals(
                segment.Gauge,
                _trackBuildGauge,
                StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Button(
                    "Apply " + TrackGaugeShortLabel(
                        _trackBuildGauge)
                    + " to Segment",
                    GUILayout.Height(30f)))
            {
                RunGameAction(() =>
                {
                    _mapEditor.SetSelectedSegmentGauge(
                        _trackBuildGauge);
                    var runtime = _mapEditor.DescribeGaugeRuntime(
                        _trackBuildGauge);
                    return "Set segment gauge to "
                           + _trackBuildGauge
                           + (string.IsNullOrWhiteSpace(runtime)
                               ? string.Empty
                               : ". " + runtime);
                });
            }
            GUI.enabled = true;
            var chainCount =
                _mapEditor.SelectedGaugeChainCount();
            var isTransition =
                _mapEditor.IsDualGaugeTransition(
                    _trackBuildGauge);
            GUI.enabled = !isTransition;
            if (GUILayout.Button(
                    isTransition
                        ? "DUAL T Is One Segment Only"
                        : "Apply Through Chain (" + chainCount + ")",
                    GUILayout.Height(30f)))
            {
                RunGameAction(() =>
                {
                    _mapEditor.SetSelectedGaugeThroughChain(
                        _trackBuildGauge);
                    var runtime = _mapEditor.DescribeGaugeRuntime(
                        _trackBuildGauge);
                    return "Set connected track gauge to "
                           + _trackBuildGauge
                           + (string.IsNullOrWhiteSpace(runtime)
                               ? string.Empty
                               : ". " + runtime);
                });
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (_mapEditor.IsDualGaugeTransition(segment.Gauge))
            {
                var transitionStatus =
                    _mapEditor.DescribeSelectedDualGaugeTransition();
                if (!string.IsNullOrWhiteSpace(transitionStatus))
                {
                    GUILayout.Label(
                        transitionStatus,
                        transitionStatus.IndexOf(
                            "ready",
                            StringComparison.OrdinalIgnoreCase) >= 0
                            ? _onlineStyle
                            : _offlineStyle);
                }
            }

            GUILayout.Space(5f);
            GUILayout.Label("Track style", _mutedStyle);
            GUILayout.BeginHorizontal();
            SegmentStyleButton("Standard", 0);
            SegmentStyleButton("Bridge", 1);
            SegmentStyleButton("Tunnel", 2);
            SegmentStyleButton("Yard", 3);
            GUILayout.EndHorizontal();

            GUILayout.Space(7f);
            GUILayout.Label(
                "Track class: " + segment.TrackClass,
                _mutedStyle);
            GUILayout.Label(
                "Controls Railroader's operating class and default speed "
                + "(Mainline 35, Branch 25, Industrial 15 mph when "
                + "speedLimit is 0).",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            SegmentTrackClassButton(
                "Mainline",
                "Mainline",
                segment.TrackClass);
            SegmentTrackClassButton(
                "Branch",
                "Branch",
                segment.TrackClass);
            SegmentTrackClassButton(
                "Industrial",
                "Industrial",
                segment.TrackClass);
            GUILayout.EndHorizontal();

            GUILayout.Space(7f);
            GUILayout.Label("Segment group", _mutedStyle);
            GUILayout.Label(
                "Assign the groupId written to this segment in the graph JSON.",
                _mutedStyle);
            _segmentGroupEditorValue = GUILayout.TextField(
                _segmentGroupEditorValue ?? string.Empty,
                GUILayout.Height(29f));
            var normalizedGroup =
                (_segmentGroupEditorValue ?? string.Empty).Trim();
            GUILayout.BeginHorizontal();
            var controlsEnabled = GUI.enabled;
            GUI.enabled = controlsEnabled
                          && !string.Equals(
                              normalizedGroup,
                              segment.GroupId ?? string.Empty,
                              StringComparison.Ordinal);
            if (GUILayout.Button("Apply Group", GUILayout.Height(30f)))
            {
                RunGameAction(
                    string.IsNullOrWhiteSpace(normalizedGroup)
                        ? "Cleared segment group"
                        : "Set segment group to " + normalizedGroup,
                    () => _mapEditor.SetSelectedSegmentGroup(
                        normalizedGroup));
            }
            GUI.enabled = controlsEnabled
                          && (!string.IsNullOrWhiteSpace(segment.GroupId)
                              || !string.IsNullOrWhiteSpace(
                                  _segmentGroupEditorValue));
            if (GUILayout.Button(
                    "Clear",
                    GUILayout.Width(100f),
                    GUILayout.Height(30f)))
            {
                _segmentGroupEditorValue = string.Empty;
                if (!string.IsNullOrWhiteSpace(segment.GroupId))
                {
                    RunGameAction(
                        "Cleared segment group",
                        () => _mapEditor.SetSelectedSegmentGroup(
                            string.Empty));
                }
            }
            GUI.enabled = controlsEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Space(7f);
            DrawDeleteControl(null, segment);
            GUILayout.Space(5f);
            if (GUILayout.Button("Refresh Track Overlays", GUILayout.Height(28f)))
                RunGameAction("Rebuilt track overlays", _mapEditor.RebuildTrack);
        }

        private void SyncSegmentGroupEditor(
            TileEditorGraphSession.SelectionInfo segment)
        {
            var current = segment?.GroupId ?? string.Empty;
            if (segment == null
                || !string.Equals(
                    _segmentGroupEditorId,
                    segment.Id,
                    StringComparison.Ordinal))
            {
                _segmentGroupEditorId = segment?.Id ?? string.Empty;
                _segmentGroupEditorValue = current;
                _segmentGroupObservedValue = current;
                return;
            }
            if (!string.Equals(
                    current,
                    _segmentGroupObservedValue,
                    StringComparison.Ordinal)
                && string.Equals(
                    _segmentGroupEditorValue,
                    _segmentGroupObservedValue,
                    StringComparison.Ordinal))
            {
                _segmentGroupEditorValue = current;
            }
            _segmentGroupObservedValue = current;
        }

        private void DrawTrackGaugeBar()
        {
            _mapEditor.NewTrackGauge = _trackBuildGauge;
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "BUILD GAUGE  "
                + TrackGaugeDisplayLabel(_trackBuildGauge),
                _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "New segments inherit this",
                _mutedStyle);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TrackGaugeButton("STD", "Standard");
            TrackGaugeButton("3-FT", "Narrow");
            TrackGaugeButton("DUAL AUTO", "DualGauge");
            TrackGaugeButton("DUAL L", "DualGauge_L");
            TrackGaugeButton("DUAL R", "DualGauge_R");
            TrackGaugeButton("DUAL T", "DualGauge_T");
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Orange overlays are 3-foot narrow gauge; blue overlays are "
                + "dual gauge; pink marks a dual shared-rail transition.",
                _mutedStyle);
            var runtimeStatus =
                _mapEditor.DescribeGaugeRuntime(_trackBuildGauge);
            if (!string.IsNullOrWhiteSpace(runtimeStatus))
            {
                GUILayout.Label(
                    runtimeStatus,
                    _mapEditor.NarrowGaugeRuntimeReady
                        ? _onlineStyle
                        : _offlineStyle);
                if (_mapEditor.NarrowGaugeRuntimeReady
                    && GUILayout.Button(
                        "SYNC GAUGE VISUALS",
                        GUILayout.Height(28f)))
                {
                    RunGameAction(
                        _mapEditor.SynchronizeNarrowGaugeRuntime);
                }
            }
            if (_mapEditor.IsDualGaugeTransition(_trackBuildGauge))
            {
                GUILayout.Label(
                    "DUAL T is a single short segment between DUAL L and "
                    + "DUAL R. Do not apply it to an entire curve or chain.",
                    _offlineStyle);
            }
        }

        private void TrackGaugeButton(string label, string gauge)
        {
            var oldColor = GUI.backgroundColor;
            if (string.Equals(
                    _trackBuildGauge,
                    gauge,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(label, GUILayout.Height(28f)))
            {
                _trackBuildGauge = gauge;
                _mapEditor.NewTrackGauge = gauge;
                PlayerPrefs.SetString(
                    TrackBuildGaugeKey,
                    gauge);
                PlayerPrefs.Save();
                _lastPanelMessage =
                    "New track gauge set to "
                    + TrackGaugeDisplayLabel(gauge);
            }
            GUI.backgroundColor = oldColor;
        }

        private static string TrackGaugeShortLabel(string gauge)
        {
            switch (gauge)
            {
                case "Narrow":
                    return "3-FT";
                case "DualGauge":
                    return "DUAL AUTO";
                case "DualGauge_L":
                    return "DUAL L";
                case "DualGauge_R":
                    return "DUAL R";
                case "DualGauge_T":
                    return "DUAL T";
                default:
                    return "STD";
            }
        }

        private static string TrackGaugeDisplayLabel(string gauge)
        {
            switch (gauge)
            {
                case "Narrow":
                    return "3-foot narrow";
                case "DualGauge":
                    return "Dual (automatic shared rail)";
                case "DualGauge_L":
                    return "Dual (left shared rail)";
                case "DualGauge_R":
                    return "Dual (right shared rail)";
                case "DualGauge_T":
                    return "Dual shared-rail transition";
                default:
                    return "Standard";
            }
        }

        private void DrawQuickStepButton(
            string label,
            float value,
            ref float current)
        {
            var oldColor = GUI.backgroundColor;
            if (Mathf.Abs(current - value) < 0.0001f)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(27f)))
                current = value;
            GUI.backgroundColor = oldColor;
        }

        private void DrawVectorFields(
            string label,
            ref string x,
            ref string y,
            ref string z)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(58f));
            GUILayout.Label("X", GUILayout.Width(12f));
            x = GUILayout.TextField(x ?? string.Empty, GUILayout.Width(78f));
            GUILayout.Label("Y", GUILayout.Width(12f));
            y = GUILayout.TextField(y ?? string.Empty, GUILayout.Width(78f));
            GUILayout.Label("Z", GUILayout.Width(12f));
            z = GUILayout.TextField(z ?? string.Empty, GUILayout.Width(78f));
            GUILayout.EndHorizontal();
        }

        private float DrawStepSelector(float current, float[] values, int columns)
        {
            var labels = new string[values.Length];
            var selected = 0;
            var bestDistance = float.MaxValue;
            for (var index = 0; index < values.Length; index++)
            {
                labels[index] = values[index].ToString(
                    "0.##", CultureInfo.InvariantCulture);
                var distance = Mathf.Abs(values[index] - current);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    selected = index;
                }
            }
            selected = GUILayout.SelectionGrid(selected, labels, columns);
            return values[Mathf.Clamp(selected, 0, values.Length - 1)];
        }

        private void NodeMovePadButton(
            string label,
            Vector3 offset,
            string tooltip = "")
        {
            if (GUILayout.Button(
                    new GUIContent(label, tooltip),
                    _directionButtonStyle,
                    GUILayout.Height(50f)))
            {
                RunNodeTransformAction(
                    "Moved selected node",
                    () => _mapEditor.MoveSelectedNode(offset, _moveInLocalAxes));
            }
        }

        private void NodeRotatePadButton(
            string label,
            Vector3 offset,
            string tooltip = "")
        {
            if (GUILayout.Button(
                    new GUIContent(label, tooltip),
                    _directionButtonStyle,
                    GUILayout.Height(48f)))
            {
                RunNodeTransformAction(
                    "Rotated selected node",
                    () => _mapEditor.RotateSelectedNode(offset));
            }
        }

        private void DrawNodeRotationAxisGroup(
            string axisLabel,
            Vector3 negativeOffset,
            Vector3 positiveOffset,
            string negativeTooltip,
            string positiveTooltip)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(axisLabel, _mutedStyle);
            GUILayout.BeginHorizontal();
            NodeRotatePadButton(
                "\u21B6\n\u2212",
                negativeOffset,
                negativeTooltip);
            NodeRotatePadButton(
                "\u21B7\n+",
                positiveOffset,
                positiveTooltip);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void RunNodeTransformAction(string message, Action action)
        {
            RunGameAction(
                message,
                () =>
                {
                    action();
                    var selected = _mapEditor.SelectedNode;
                    if (selected != null)
                        SyncNodeTransformFields(selected, true);
                });
        }

        private void SyncNodeTransformFields(
            TileEditorGraphSession.SelectionInfo node,
            bool force)
        {
            if (node == null)
                return;
            if (!force
                && string.Equals(
                    _transformNodeId, node.Id, StringComparison.Ordinal))
            {
                return;
            }
            _transformNodeId = node.Id;
            _nodePositionX = FormatTransformValue(node.Position.x);
            _nodePositionY = FormatTransformValue(node.Position.y);
            _nodePositionZ = FormatTransformValue(node.Position.z);
            _nodeRotationX = FormatTransformValue(node.Rotation.x);
            _nodeRotationY = FormatTransformValue(node.Rotation.y);
            _nodeRotationZ = FormatTransformValue(node.Rotation.z);
        }

        private static string FormatTransformValue(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void DrawSplineyTool()
        {
            var point = _mapEditor.SelectedSplinePoint;
            if (point == null)
            {
                _splineSelectionKey = string.Empty;
                GUILayout.Label("Road, river, and bridge splineys", _titleStyle);
                GUILayout.Label(
                    _mapEditor.RoadSplineyCount + " roads   "
                    + _mapEditor.RiverSplineyCount + " rivers   "
                    + _mapEditor.TrestleSplineyCount
                    + " bridges/trestles",
                    _lineStyle);
                GUILayout.Label(
                    "Click a colored control point to edit an existing spline, "
                    + "or place a new one at the camera target below.",
                    _mutedStyle);

                GUILayout.Space(6f);
                GUILayout.Label("BRIDGE DIRECTLY FROM TRACK", _titleStyle);
                var pickColor = GUI.backgroundColor;
                if (_mapEditor.SplineTrackPickMode)
                    GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
                if (GUILayout.Button(
                        _mapEditor.SplineTrackPickMode
                            ? "CANCEL TRACK PICKING"
                            : "PICK A TRACK SEGMENT...",
                        GUILayout.Height(33f)))
                {
                    var enable = !_mapEditor.SplineTrackPickMode;
                    RunGameAction(
                        enable
                            ? "Click a yellow track segment for the bridge"
                            : "Stopped track picking",
                        () => _mapEditor.SetSplineTrackPickMode(enable));
                }
                GUI.backgroundColor = pickColor;

                if (_mapEditor.SplineTrackPickMode)
                {
                    var segment = _mapEditor.SelectedSegment;
                    GUILayout.Label(
                        segment == null
                            ? "Click the yellow track segment in the world. "
                              + "It turns green when selected."
                            : "Selected " + segment.Id + "   "
                              + segment.Length.ToString(
                                  "0.0",
                                  CultureInfo.InvariantCulture)
                              + " m. The bridge will follow its exact 3D curve.",
                        segment == null ? _mutedStyle : _lineStyle);
                    DrawTextField(
                        "Bridge name (optional)",
                        ref _trackBridgeName);
                    DrawTextField(
                        "Below rail (m)",
                        ref _trackBridgeBelowRail);
                    DrawTextField(
                        "Control point spacing (m)",
                        ref _trackBridgePointSpacing);
                    GUILayout.Label(
                        "A 0.30 m offset places the bridge deck just below "
                        + "the rail. Smaller spacing follows tight curves "
                        + "more closely.",
                        _mutedStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Start", GUILayout.Width(48f));
                    _trackBridgeHeadStyle = GUILayout.SelectionGrid(
                        Mathf.Clamp(_trackBridgeHeadStyle, 0, 1),
                        new[] { "Block", "Bent" },
                        2);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("End", GUILayout.Width(48f));
                    _trackBridgeTailStyle = GUILayout.SelectionGrid(
                        Mathf.Clamp(_trackBridgeTailStyle, 0, 1),
                        new[] { "Block", "Bent" },
                        2);
                    GUILayout.EndHorizontal();

                    GUI.enabled = segment != null;
                    if (GUILayout.Button(
                            "BUILD BRIDGE ON SELECTED TRACK",
                            GUILayout.Height(36f)))
                    {
                        RunGameAction(
                            "Built bridge below selected track",
                            () =>
                            {
                                _mapEditor.CreateTrestleFromSelectedSegment(
                                    _trackBridgeName,
                                    ParseFloat(
                                        _trackBridgeBelowRail,
                                        "below-rail offset"),
                                    ParseFloat(
                                        _trackBridgePointSpacing,
                                        "bridge point spacing"),
                                    _trackBridgeHeadStyle == 0
                                        ? "Block"
                                        : "Bent",
                                    _trackBridgeTailStyle == 0
                                        ? "Block"
                                        : "Bent");
                                _trackBridgeName = string.Empty;
                            });
                    }
                    GUI.enabled = true;
                    DrawSplineyChangeBar();
                    return;
                }

                GUILayout.Space(6f);
                GUILayout.Label("PLACE NEW SPLINEY", _titleStyle);
                var kinds = new[] { "Road", "River", "Bridge / Trestle" };
                var oldKind = _newSplineKind;
                _newSplineKind = GUILayout.SelectionGrid(
                    Mathf.Clamp(_newSplineKind, 0, kinds.Length - 1),
                    kinds,
                    kinds.Length);
                if (_newSplineKind != oldKind)
                {
                    _newSplineProfile = _newSplineKind == 0
                        ? "RAM Road profile"
                        : _newSplineKind == 1
                            ? "R2_Profile_River_Mountain"
                            : string.Empty;
                }

                DrawTextField(
                    "Name (optional)",
                    ref _newSplineName);
                DrawTextField("Initial length (m)", ref _newSplineLength);
                var createKind = _newSplineKind == 0
                    ? "Road"
                    : _newSplineKind == 1
                        ? "River"
                        : "Trestle";
                if (_newSplineKind < 2)
                {
                    DrawTextField("Loaded profile", ref _newSplineProfile);
                    var profiles = _mapEditor.GetSplineProfiles(createKind);
                    if (profiles.Count > 0)
                    {
                        GUILayout.Label(
                            "Quick profiles already used by this mod:",
                            _mutedStyle);
                        var visibleProfiles = Mathf.Min(profiles.Count, 4);
                        for (var index = 0;
                             index < visibleProfiles;
                             index += 2)
                        {
                            GUILayout.BeginHorizontal();
                            DrawSplineProfileButton(profiles[index]);
                            if (index + 1 < visibleProfiles)
                                DrawSplineProfileButton(profiles[index + 1]);
                            GUILayout.EndHorizontal();
                        }
                    }
                    DrawTextField("Initial width (m)", ref _newSplineWidth);
                }
                else
                {
                    GUILayout.Label("Bridge end styles", _mutedStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Start", GUILayout.Width(48f));
                    _newSplineHeadStyle = GUILayout.SelectionGrid(
                        Mathf.Clamp(_newSplineHeadStyle, 0, 1),
                        new[] { "Block", "Bent" },
                        2);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("End", GUILayout.Width(48f));
                    _newSplineTailStyle = GUILayout.SelectionGrid(
                        Mathf.Clamp(_newSplineTailStyle, 0, 1),
                        new[] { "Block", "Bent" },
                        2);
                    GUILayout.EndHorizontal();
                }

                if (GUILayout.Button(
                        "PLACE NEW " + createKind.ToUpperInvariant(),
                        GUILayout.Height(35f)))
                {
                    RunGameAction("Placed new " + createKind + " spliney", () =>
                    {
                        _mapEditor.CreateSplineyAtCamera(
                            _newSplineName,
                            createKind,
                            _newSplineProfile,
                            ParseFloat(
                                _newSplineLength,
                                "new spline length"),
                            _newSplineKind < 2
                                ? ParseFloat(
                                    _newSplineWidth,
                                    "new spline width")
                                : 0f,
                            _newSplineHeadStyle == 0 ? "Block" : "Bent",
                            _newSplineTailStyle == 0 ? "Block" : "Bent");
                        _newSplineName = string.Empty;
                    });
                }

                GUILayout.Space(6f);
                if (GUILayout.Button(
                        "Refresh Spliney Overlays",
                        GUILayout.Height(30f)))
                {
                    RunGameAction(
                        "Refreshed spliney overlays",
                        _mapEditor.RefreshSplineySources);
                }
                DrawSplineyChangeBar();
                return;
            }

            SyncSplineFields(point, false);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New...", GUILayout.Width(70f), GUILayout.Height(29f)))
            {
                RunSplineAction(
                    "Ready to place a new spliney",
                    _mapEditor.ClearSplineSelection);
            }
            GUI.enabled = point.Index > 0;
            if (GUILayout.Button("Previous Point", GUILayout.Height(29f)))
                RunSplineAction("Selected previous spline point",
                    _mapEditor.SelectPreviousSplinePoint);
            GUI.enabled = point.Index + 1 < point.Count;
            if (GUILayout.Button("Next Point", GUILayout.Height(29f)))
                RunSplineAction("Selected next spline point",
                    _mapEditor.SelectNextSplinePoint);
            GUI.enabled = true;
            var oldColor = GUI.backgroundColor;
            if (_showAdvancedSplineControls)
                GUI.backgroundColor = new Color(0.72f, 0.55f, 0.18f);
            if (GUILayout.Button(
                    _showAdvancedSplineControls ? "Less" : "More...",
                    GUILayout.Width(80f),
                    GUILayout.Height(29f)))
            {
                _showAdvancedSplineControls = !_showAdvancedSplineControls;
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();

            DrawPrimarySplineMoveControls();
            GUILayout.Space(5f);
            DrawPrimarySplineRotationControls(point);

            GUILayout.Space(5f);
            if (point.HasWidth)
            {
                GUILayout.Label(
                    "Width "
                    + point.Width.ToString(
                        "0.##",
                        CultureInfo.InvariantCulture)
                    + " m",
                    _mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Width -1", GUILayout.Height(29f)))
                    RunSplineAction(
                        "Reduced spline width",
                        () => _mapEditor.SetSelectedSplinePointWidth(
                            Mathf.Max(0.5f, point.Width - 1f)));
                if (GUILayout.Button("Width +1", GUILayout.Height(29f)))
                    RunSplineAction(
                        "Increased spline width",
                        () => _mapEditor.SetSelectedSplinePointWidth(
                            point.Width + 1f));
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(
                    "BRIDGE ENDS   Start " + point.HeadStyle
                    + "   End " + point.TailStyle,
                    _mutedStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        "Start: " + point.HeadStyle,
                        GUILayout.Height(29f)))
                {
                    RunSplineAction(
                        "Changed bridge start style",
                        () => _mapEditor.SetTrestleEndStyles(
                            ToggleEndStyle(point.HeadStyle),
                            point.TailStyle));
                }
                if (GUILayout.Button(
                        "End: " + point.TailStyle,
                        GUILayout.Height(29f)))
                {
                    RunSplineAction(
                        "Changed bridge end style",
                        () => _mapEditor.SetTrestleEndStyles(
                            point.HeadStyle,
                            ToggleEndStyle(point.TailStyle)));
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Insert Point After", GUILayout.Height(30f)))
                RunSplineAction(
                    "Inserted spline control point",
                    _mapEditor.InsertSplinePointAfter);
            if (GUILayout.Button("Delete Point", GUILayout.Height(30f)))
                RunSplineAction(
                    "Deleted spline control point",
                    _mapEditor.DeleteSelectedSplinePoint);
            GUILayout.EndHorizontal();

            if (_showAdvancedSplineControls)
                DrawAdvancedSplineControls(point);

            GUILayout.Space(6f);
            var deleteKey = "spline:" + point.Id;
            if (string.Equals(
                    _deleteConfirmId,
                    deleteKey,
                    StringComparison.Ordinal))
            {
                GUILayout.BeginHorizontal();
                var deleteColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.8f, 0.2f, 0.18f);
                if (GUILayout.Button(
                        "CONFIRM DELETE ENTIRE SPLINEY",
                        GUILayout.Height(31f)))
                {
                    RunSplineAction(
                        "Deleted " + point.Id,
                        _mapEditor.DeleteSelectedSpliney);
                }
                GUI.backgroundColor = deleteColor;
                if (GUILayout.Button(
                        "Cancel",
                        GUILayout.Width(74f),
                        GUILayout.Height(31f)))
                {
                    _deleteConfirmId = string.Empty;
                }
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button(
                         "Delete Entire Spliney...",
                         GUILayout.Height(29f)))
            {
                _deleteConfirmId = deleteKey;
            }

            DrawSplineyChangeBar();
            GUILayout.Label(
                "Source: " + point.FileName
                + "   Live geometry rebuilds after every adjustment.",
                _mutedStyle);
        }

        private void DrawAdvancedSplineControls(
            TileEditorGraphSession.SplinePointInfo point)
        {
            GUILayout.Space(7f);
            GUILayout.Label("Exact spline point", _titleStyle);
            DrawVectorFields(
                "Position",
                ref _splinePositionX,
                ref _splinePositionY,
                ref _splinePositionZ);
            DrawVectorFields(
                "Rotation",
                ref _splineRotationX,
                ref _splineRotationY,
                ref _splineRotationZ);
            if (point.HasWidth)
                DrawTextField("Width (m)", ref _splineWidth);
            if (GUILayout.Button("Apply Exact Spline Point", GUILayout.Height(31f)))
            {
                RunSplineAction(
                    "Applied exact spline point transform",
                    () => _mapEditor.SetSelectedSplinePointTransform(
                        new Vector3(
                            ParseFloat(_splinePositionX, "spline position X"),
                            ParseFloat(_splinePositionY, "spline position Y"),
                            ParseFloat(_splinePositionZ, "spline position Z")),
                        new Vector3(
                            ParseFloat(_splineRotationX, "spline rotation X"),
                            ParseFloat(_splineRotationY, "spline rotation Y"),
                            ParseFloat(_splineRotationZ, "spline rotation Z")),
                        point.HasWidth
                            ? ParseFloat(_splineWidth, "spline width")
                            : 0f));
            }

        }

        private void DrawSplineProfileButton(string profile)
        {
            var oldColor = GUI.backgroundColor;
            if (string.Equals(
                    _newSplineProfile,
                    profile,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(
                    Shorten(profile, 28),
                    GUILayout.Height(25f)))
            {
                _newSplineProfile = profile;
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawPrimarySplineMoveControls()
        {
            GUILayout.Label(
                "MOVE   Step "
                + _splineMoveStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " m   WORLD",
                _titleStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton("0.01", 0.01f, ref _splineMoveStep);
            DrawQuickStepButton("0.1", 0.1f, ref _splineMoveStep);
            DrawQuickStepButton("0.5", 0.5f, ref _splineMoveStep);
            DrawQuickStepButton("1", 1f, ref _splineMoveStep);
            DrawQuickStepButton("5", 5f, ref _splineMoveStep);
            DrawQuickStepButton("10", 10f, ref _splineMoveStep);
            DrawQuickStepButton("25", 25f, ref _splineMoveStep);
            DrawQuickStepButton("50", 50f, ref _splineMoveStep);
            DrawQuickStepButton("100", 100f, ref _splineMoveStep);
            DrawQuickStepButton("1000", 1000f, ref _splineMoveStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            SplineMovePadButton(
                "Y-\nLOWER",
                new Vector3(0f, -_splineMoveStep, 0f));
            SplineMovePadButton(
                "\u2191\nFORWARD Z+",
                new Vector3(0f, 0f, _splineMoveStep));
            SplineMovePadButton(
                "Y+\nRAISE",
                new Vector3(0f, _splineMoveStep, 0f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SplineMovePadButton(
                "\u2190\nLEFT X-",
                new Vector3(-_splineMoveStep, 0f, 0f));
            SplineMovePadButton(
                "\u2193\nBACK Z-",
                new Vector3(0f, 0f, -_splineMoveStep));
            SplineMovePadButton(
                "\u2192\nRIGHT X+",
                new Vector3(_splineMoveStep, 0f, 0f));
            GUILayout.EndHorizontal();
        }

        private void DrawPrimarySplineRotationControls(
            TileEditorGraphSession.SplinePointInfo point)
        {
            GUILayout.Label(
                "ROTATE   Step "
                + _splineRotationStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " degrees",
                _titleStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton("0.01", 0.01f, ref _splineRotationStep);
            DrawQuickStepButton("0.1", 0.1f, ref _splineRotationStep);
            DrawQuickStepButton("0.5", 0.5f, ref _splineRotationStep);
            DrawQuickStepButton("1", 1f, ref _splineRotationStep);
            DrawQuickStepButton("5", 5f, ref _splineRotationStep);
            DrawQuickStepButton("10", 10f, ref _splineRotationStep);
            DrawQuickStepButton("15", 15f, ref _splineRotationStep);
            DrawQuickStepButton("30", 30f, ref _splineRotationStep);
            DrawQuickStepButton("45", 45f, ref _splineRotationStep);
            DrawQuickStepButton("60", 60f, ref _splineRotationStep);
            DrawQuickStepButton("90", 90f, ref _splineRotationStep);
            DrawQuickStepButton("180", 180f, ref _splineRotationStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            SplineRotateButton(
                "\u21B6\nPITCH X-",
                new Vector3(-_splineRotationStep, 0f, 0f));
            SplineRotateButton(
                "\u21B6\nHEADING Y-",
                new Vector3(0f, -_splineRotationStep, 0f));
            SplineRotateButton(
                "\u21B6\nROLL Z-",
                new Vector3(0f, 0f, -_splineRotationStep));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SplineRotateButton(
                "\u21B7\nPITCH X+",
                new Vector3(_splineRotationStep, 0f, 0f));
            SplineRotateButton(
                "\u21B7\nHEADING Y+",
                new Vector3(0f, _splineRotationStep, 0f));
            SplineRotateButton(
                "\u21B7\nROLL Z+",
                new Vector3(0f, 0f, _splineRotationStep));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "Level X/Z",
                    GUILayout.Height(29f)))
            {
                SetSplinePointRotation(
                    point,
                    new Vector3(
                        0f,
                        point.Rotation.y,
                        0f),
                    "Leveled spline pitch and roll");
            }
            if (GUILayout.Button(
                    "Reset Rotation",
                    GUILayout.Height(29f)))
            {
                SetSplinePointRotation(
                    point,
                    Vector3.zero,
                    "Reset spline rotation");
            }
            if (GUILayout.Button(
                    "Flip Y 180",
                    GUILayout.Height(29f)))
            {
                SetSplinePointRotation(
                    point,
                    new Vector3(
                        point.Rotation.x,
                        point.Rotation.y + 180f,
                        point.Rotation.z),
                    "Flipped spline heading");
            }
            GUILayout.EndHorizontal();
        }

        private void SetSplinePointRotation(
            TileEditorGraphSession.SplinePointInfo point,
            Vector3 rotation,
            string message)
        {
            RunSplineAction(
                message,
                () => _mapEditor.SetSelectedSplinePointTransform(
                    point.Position,
                    rotation,
                    point.HasWidth ? point.Width : 0f));
        }

        private void DrawSplineyChangeBar()
        {
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor.CanUndoSpliney;
            if (GUILayout.Button("Spline Undo", GUILayout.Height(30f)))
                RunSplineAction("Undid spline edit", _mapEditor.UndoSpliney);
            GUI.enabled = _mapEditor.CanRedoSpliney;
            if (GUILayout.Button("Spline Redo", GUILayout.Height(30f)))
                RunSplineAction("Redid spline edit", _mapEditor.RedoSpliney);
            GUI.enabled = _mapEditor.SplineyDirty;
            if (GUILayout.Button(
                    "Save Splineys"
                    + (_mapEditor.SplineyDirty ? " *" : ""),
                    GUILayout.Height(30f)))
            {
                SaveSplineysAndSyncDesktop();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private static string ToggleEndStyle(string style)
        {
            return string.Equals(
                style,
                "Bent",
                StringComparison.OrdinalIgnoreCase)
                ? "Block"
                : "Bent";
        }

        private void SplineMovePadButton(string label, Vector3 offset)
        {
            if (GUILayout.Button(
                    label,
                    _directionButtonStyle,
                    GUILayout.Height(52f)))
            {
                RunSplineAction(
                    "Moved spline control point",
                    () => _mapEditor.MoveSelectedSplinePoint(offset));
            }
        }

        private void SplineRotateButton(string label, Vector3 offset)
        {
            if (GUILayout.Button(
                    label,
                    _directionButtonStyle,
                    GUILayout.Height(52f)))
            {
                RunSplineAction(
                    "Rotated spline control point",
                    () => _mapEditor.RotateSelectedSplinePoint(offset));
            }
        }

        private void RunSplineAction(string message, Action action)
        {
            RunGameAction(
                message,
                () =>
                {
                    action();
                    var selected = _mapEditor.SelectedSplinePoint;
                    if (selected != null)
                        SyncSplineFields(selected, true);
                });
        }

        private void SyncSplineFields(
            TileEditorGraphSession.SplinePointInfo point,
            bool force)
        {
            var key = point == null
                ? string.Empty
                : point.Id + ":" + point.Index;
            if (!force
                && string.Equals(
                    _splineSelectionKey,
                    key,
                    StringComparison.Ordinal))
            {
                return;
            }
            _splineSelectionKey = key;
            if (point == null)
                return;
            _splinePositionX = FormatTransformValue(point.Position.x);
            _splinePositionY = FormatTransformValue(point.Position.y);
            _splinePositionZ = FormatTransformValue(point.Position.z);
            _splineRotationX = FormatTransformValue(point.Rotation.x);
            _splineRotationY = FormatTransformValue(point.Rotation.y);
            _splineRotationZ = FormatTransformValue(point.Rotation.z);
            _splineWidth = FormatTransformValue(point.Width);
        }

        private void DrawPiecesTool()
        {
            var node = _mapEditor.SelectedNode;
            GUILayout.Label("Live track pieces", _titleStyle);
            GUILayout.Label(
                "Add one piece from the selected endpoint. The new endpoint "
                + "becomes selected so you can immediately add the next piece.",
                _lineStyle);

            var labels = new[] { "Straight", "Arc", "Turnout" };
            _pieceType = GUILayout.SelectionGrid(
                Mathf.Clamp(_pieceType, 0, labels.Length - 1),
                labels,
                labels.Length);

            if (_pieceType == 0)
            {
                DrawTextField("Length (m)", ref _pieceLength);
                DrawTextField("Grade (%)", ref _targetGrade);
                DrawTextField("Control sections", ref _pieceSections);
                GUI.enabled = node != null;
                if (GUILayout.Button("Add Straight Piece", GUILayout.Height(34f)))
                {
                    RunGameAction("Added straight track piece", () =>
                    {
                        var endpoint = _mapEditor.BuildStraightPiece(
                            ParseFloat(_pieceLength, "piece length"),
                            ParseFloat(_targetGrade, "piece grade"),
                            ParseInt(_pieceSections, "control sections"));
                        _lastPanelMessage = "Straight piece ends at " + endpoint;
                    });
                }
                GUI.enabled = true;
            }
            else if (_pieceType == 1)
            {
                DrawTextField("Radius (m)", ref _arcRadius);
                DrawTextField("Turn angle (degrees)", ref _arcDegrees);
                DrawTextField("New control nodes", ref _arcNodes);
                DrawTextField("Target grade (%)", ref _targetGrade);
                DrawDirectionButtons();
                GUI.enabled = node != null;
                if (GUILayout.Button("Add Arc Piece", GUILayout.Height(34f)))
                {
                    RunGameAction("Added arc track piece", () =>
                    {
                        var endpoint = _mapEditor.BuildArc(
                            ParseFloat(_arcRadius, "radius"),
                            ParseFloat(_arcDegrees, "arc angle"),
                            _turnRight,
                            ParseFloat(_targetGrade, "target grade"),
                            ParseInt(_arcNodes, "arc nodes"));
                        _lastPanelMessage = "Arc piece ends at " + endpoint;
                    });
                }
                GUI.enabled = true;
            }
            else
            {
                DrawTextField("Lead length (m)", ref _turnoutLength);
                DrawTextField("Divergence (degrees)", ref _turnoutDegrees);
                DrawTextField("Target grade (%)", ref _targetGrade);
                DrawDirectionButtons();
                GUI.enabled = node != null;
                if (GUILayout.Button("Add Turnout Piece", GUILayout.Height(34f)))
                {
                    RunGameAction("Added turnout track piece", () =>
                    {
                        var endpoint = _mapEditor.BuildTurnout(
                            ParseFloat(_turnoutLength, "turnout length"),
                            ParseFloat(_turnoutDegrees, "turnout angle"),
                            _turnRight,
                            ParseFloat(_targetGrade, "target grade"));
                        _lastPanelMessage = "Turnout branch ends at " + endpoint;
                    });
                }
                GUI.enabled = true;
            }

            GUILayout.Label(
                node == null
                    ? "Select a cyan endpoint before adding a piece."
                    : "Each added piece is one undoable live operation.",
                _mutedStyle);
        }

        private void DrawParallelTool()
        {
            var segment = _mapEditor.SelectedSegment;
            GUILayout.Label("Parallel track", _titleStyle);
            GUILayout.Label(
                "Offsets the selected segment while preserving its endpoint "
                + "elevations, rotations, style, class, and speed.",
                _lineStyle);
            DrawTextField("Track separation (m)", ref _parallelSeparation);
            DrawTextField("Additional tracks", ref _parallelTracks);

            GUILayout.Label("Side", _mutedStyle);
            var sideLabels = new[] { "Left", "Right", "Both" };
            _parallelSide = GUILayout.SelectionGrid(
                Mathf.Clamp(_parallelSide, 0, 2),
                sideLabels,
                3);

            GUI.enabled = segment != null;
            if (GUILayout.Button("Build Parallel Track", GUILayout.Height(35f)))
            {
                RunGameAction("Built parallel track", () =>
                {
                    _lastPanelMessage = _mapEditor.BuildParallelSelectedSegment(
                        ParseFloat(_parallelSeparation, "track separation"),
                        ParseInt(_parallelTracks, "additional tracks"),
                        _parallelSide);
                });
            }
            GUI.enabled = true;
            GUILayout.Label(
                segment == null
                    ? "Select a yellow track segment first."
                    : "Both creates the requested count on each side.",
                _mutedStyle);
        }

        private void DrawFitArcTool()
        {
            var node = _mapEditor.SelectedNode;
            GUILayout.Label("Fit connected nodes to one arc", _titleStyle);
            GUILayout.Label(
                "Select nodes along one route in order and add each to the "
                + "chain. Fit Arc moves X/Z and heading while preserving every "
                + "node's elevation, pitch, and roll.",
                _lineStyle);

            GUILayout.BeginHorizontal();
            GUI.enabled = node != null
                          && !_fitArcNodeIds.Contains(node.Id);
            if (GUILayout.Button("Add Selected", GUILayout.Height(31f)))
                _fitArcNodeIds.Add(node.Id);
            GUI.enabled = _fitArcNodeIds.Count > 0;
            if (GUILayout.Button("Remove Last", GUILayout.Height(31f)))
                _fitArcNodeIds.RemoveAt(_fitArcNodeIds.Count - 1);
            if (GUILayout.Button("Clear", GUILayout.Height(31f)))
                _fitArcNodeIds.Clear();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label(
                "Chain: " + _fitArcNodeIds.Count + " node"
                + (_fitArcNodeIds.Count == 1 ? string.Empty : "s"),
                _mutedStyle);
            var start = Mathf.Max(0, _fitArcNodeIds.Count - 6);
            for (var index = start; index < _fitArcNodeIds.Count; index++)
            {
                GUILayout.Label(
                    (index + 1) + ". " + Shorten(_fitArcNodeIds[index], 42),
                    _mutedStyle);
            }

            GUI.enabled = _fitArcNodeIds.Count >= 3;
            if (GUILayout.Button("Fit Arc Live", GUILayout.Height(35f)))
            {
                RunGameAction("Fitted node chain to an arc", () =>
                {
                    _lastPanelMessage = "Fit Arc: "
                                        + _mapEditor.FitArcToNodes(
                                            _fitArcNodeIds.ToArray());
                });
            }
            GUI.enabled = true;
            GUILayout.Label(
                "Junction nodes and disconnected node orders are blocked. "
                + "The whole fit is one undo operation.",
                _mutedStyle);
        }

        private void DrawGradeTool()
        {
            var node = _mapEditor.SelectedNode;
            GUILayout.Label("Smooth grade transition", _titleStyle);
            GUILayout.Label(
                "Builds a vertical easement from the selected node's current grade "
                + "to the target grade. The easing removes the abrupt kink at the start "
                + "and settles exactly on the requested grade.",
                _lineStyle);
            DrawTextField("Transition length (m)", ref _gradeLength);
            DrawTextField("Target grade (%)", ref _targetGrade);
            DrawTextField("Control sections", ref _gradeSteps);

            GUI.enabled = node != null;
            if (GUILayout.Button("Build Smooth Grade Track", GUILayout.Height(34f)))
            {
                RunGameAction("Built smooth grade transition", () =>
                {
                    var newNode = _mapEditor.BuildGradeTransition(
                        ParseFloat(_gradeLength, "transition length"),
                        ParseFloat(_targetGrade, "target grade"),
                        ParseInt(_gradeSteps, "control sections"));
                    _lastPanelMessage = "Built grade transition to " + newNode;
                });
            }
            if (GUILayout.Button("Maintain Current Grade", GUILayout.Height(31f)))
            {
                RunGameAction("Extended the current grade", () =>
                {
                    var grade = _mapEditor.SelectedNodeGrade();
                    _targetGrade = grade.ToString("0.###", CultureInfo.InvariantCulture);
                    var newNode = _mapEditor.BuildGradeTransition(
                        ParseFloat(_gradeLength, "transition length"),
                        grade,
                        ParseInt(_gradeSteps, "control sections"));
                    _lastPanelMessage = "Maintained "
                                        + grade.ToString("0.###", CultureInfo.InvariantCulture)
                                        + "% grade to " + newNode;
                });
            }
            GUI.enabled = true;
            GUILayout.Label(
                "Use 6-12 sections for long crest/sag transitions. Undo removes the "
                + "entire generated transition as one operation.",
                _mutedStyle);
        }

        private void DrawArcTool()
        {
            var node = _mapEditor.SelectedNode;
            GUILayout.Label("Circular arc with vertical easing", _titleStyle);
            DrawArcProfileControls();
            GUILayout.Label(
                "The arc starts on the selected node's heading and grade. It creates "
                + "exactly the requested number of evenly spaced control nodes while "
                + "holding radius and easing to the target grade.",
                _lineStyle);
            DrawTextField("Radius (m)", ref _arcRadius);
            DrawTextField("Turn angle (degrees)", ref _arcDegrees);
            DrawTextField("New control nodes", ref _arcNodes);
            DrawTextField("Target grade (%)", ref _targetGrade);
            DrawDirectionButtons();

            GUI.enabled = node != null;
            if (GUILayout.Button("Build Arc", GUILayout.Height(34f)))
            {
                RunGameAction("Built circular arc", () =>
                {
                    var newNode = _mapEditor.BuildArc(
                        ParseFloat(_arcRadius, "radius"),
                        ParseFloat(_arcDegrees, "arc angle"),
                        _turnRight,
                        ParseFloat(_targetGrade, "target grade"),
                        ParseInt(_arcNodes, "arc nodes"));
                    _lastPanelMessage = "Built arc to " + newNode;
                });
            }
            GUI.enabled = true;
        }

        private void DrawTurnoutTool()
        {
            var node = _mapEditor.SelectedNode;
            GUILayout.Label("Turnout branch", _titleStyle);
            DrawTurnoutProfileControls();
            GUILayout.Label(
                "Adds a diverging leg from the selected switch node without disturbing "
                + "the existing route. The endpoint tangent is aligned to the branch.",
                _lineStyle);
            DrawTextField("Curved lead length (m)", ref _turnoutLength);
            DrawTextField("Divergence angle (degrees)", ref _turnoutDegrees);
            DrawTextField("Target grade (%)", ref _targetGrade);
            DrawDirectionButtons();

            GUI.enabled = node != null;
            if (GUILayout.Button("Build Turnout Branch", GUILayout.Height(34f)))
            {
                RunGameAction("Built turnout branch", () =>
                {
                    var newNode = _mapEditor.BuildTurnout(
                        ParseFloat(_turnoutLength, "turnout length"),
                        ParseFloat(_turnoutDegrees, "turnout angle"),
                        _turnRight,
                        ParseFloat(_targetGrade, "target grade"));
                    _lastPanelMessage = "Built turnout branch to " + newNode;
                });
            }
            GUI.enabled = true;
            GUILayout.Label(
                "After building, select the new magenta endpoint and continue with "
                + "Grade, Arc, or Track tools.",
                _mutedStyle);
        }

        private void DrawWyeTool()
        {
            var node = _mapEditor.SelectedNode;
            GUILayout.Label("Complete three-turnout wye", _titleStyle);
            DrawWyeProfileControls();
            GUILayout.Label(
                "Select an approach endpoint or a normal node in an existing "
                + "through track. Build creates both triangle legs, three true "
                + "turnout junctions, a through exit, and a stub-ended tail.",
                _lineStyle);
            if (node != null)
            {
                if (node.ConnectedSegments == 1)
                {
                    GUILayout.Label(
                        "ENDPOINT MODE • New through track will be generated.",
                        _onlineStyle);
                }
                else if (node.ConnectedSegments == 2)
                {
                    GUILayout.Label(
                        "THROUGH-TRACK MODE • The forward segment will be split "
                        + "and reused.",
                        _onlineStyle);
                    GUILayout.Label(
                        _mapEditor.DescribeSelectedWyeForwardSpace(),
                        _mutedStyle);
                }
                else
                {
                    GUILayout.Label(
                        "SELECT AN ENDPOINT OR NORMAL TWO-SEGMENT TRACK NODE",
                        _offlineStyle);
                }
            }

            GUILayout.Label("Starting shape", _mutedStyle);
            var selectedPreset = GUILayout.SelectionGrid(
                _wyePreset,
                new[] { "Compact", "Standard", "Broad" },
                3);
            if (selectedPreset != _wyePreset)
            {
                _wyePreset = selectedPreset;
                ApplyWyePreset(selectedPreset);
            }

            DrawTextField("Through length T1-T2 (m)", ref _wyeBaseLength);
            DrawTextField("Triangle depth (m)", ref _wyeDepth);
            DrawTextField("Tail stub length (m)", ref _wyeStubLength);
            DrawTextField("Through exit length (m)", ref _wyeExitLength);
            if (node == null || node.ConnectedSegments == 1)
                DrawTextField("Mainline grade (%)", ref _targetGrade);
            else
                GUILayout.Label(
                    "Mainline curve and grade: preserve existing track",
                    _mutedStyle);
            DrawWyeTailSideButtons();

            GUILayout.Label(
                "Custom: " + _wyeBaseLength + " m through × "
                + _wyeDepth + " m deep  •  "
                + _wyeStubLength + " m stub  •  "
                + _wyeExitLength + " m exit",
                _mutedStyle);
            GUILayout.Label(
                node != null && node.ConnectedSegments == 2
                    ? "Through + Exit must fit in the forward segment. The split "
                      + "retains its current alignment, style, class, and grade."
                    : "The triangle follows the mainline elevation plane; its tail "
                      + "is level so all three frogs remain vertically smooth.",
                _mutedStyle);

            GUI.enabled = node != null
                          && (node.ConnectedSegments == 1
                              || node.ConnectedSegments == 2);
            if (GUILayout.Button(
                    "BUILD COMPLETE WYE",
                    GUILayout.Height(38f)))
            {
                RunGameAction("Built complete wye", () =>
                {
                    var result = _mapEditor.BuildPerfectWye(
                        ParseFloat(_wyeBaseLength, "through length"),
                        ParseFloat(_wyeDepth, "triangle depth"),
                        ParseFloat(_wyeStubLength, "tail stub length"),
                        ParseFloat(_wyeExitLength, "through exit length"),
                        ParseFloat(_targetGrade, "target grade"),
                        _turnRight);
                    _lastPanelMessage = "Built " + result;
                });
            }
            GUI.enabled = true;

            GUILayout.Space(8f);
            if (GUILayout.Button(
                    _showSimpleWyeBuilder
                        ? "Hide Simple Frog Builder"
                        : "Simple Frog Builder...",
                    GUILayout.Height(27f)))
            {
                _showSimpleWyeBuilder = !_showSimpleWyeBuilder;
            }
            if (!_showSimpleWyeBuilder)
                return;

            GUILayout.Label("Simple three-way frog", _titleStyle);
            GUILayout.Label(
                "Legacy tool: adds only two diverging legs at the selected node.",
                _mutedStyle);
            DrawTextField("Left angle (degrees)", ref _wyeLeftDegrees);
            DrawTextField("Right angle (degrees)", ref _wyeRightDegrees);
            DrawTextField("Leg length (m)", ref _wyeLength);
            DrawTextField("Target grade (%)", ref _targetGrade);
            GUI.enabled = node != null;
            if (GUILayout.Button("Build Simple Frog", GUILayout.Height(31f)))
            {
                RunGameAction("Built simple wye frog", () =>
                {
                    var result = _mapEditor.BuildWye(
                        ParseFloat(_wyeLength, "wye leg length"),
                        ParseFloat(_wyeLeftDegrees, "left angle"),
                        ParseFloat(_wyeRightDegrees, "right angle"),
                        ParseFloat(_targetGrade, "target grade"));
                    _lastPanelMessage = "Built simple endpoints " + result;
                });
            }
            GUI.enabled = true;
        }

        private void ApplyWyePreset(int preset)
        {
            switch (preset)
            {
                case 0:
                    _wyeBaseLength = "90";
                    _wyeDepth = "45";
                    _wyeStubLength = "35";
                    _wyeExitLength = "25";
                    break;
                case 2:
                    _wyeBaseLength = "220";
                    _wyeDepth = "120";
                    _wyeStubLength = "75";
                    _wyeExitLength = "50";
                    break;
                default:
                    _wyeBaseLength = "140";
                    _wyeDepth = "75";
                    _wyeStubLength = "50";
                    _wyeExitLength = "35";
                    break;
            }
        }

        private void DrawWyeTailSideButtons()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tail side", GUILayout.Width(180f));
            var oldColor = GUI.backgroundColor;
            if (!_turnRight)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button("Left"))
                _turnRight = false;
            GUI.backgroundColor = _turnRight
                ? new Color(0.18f, 0.72f, 0.82f)
                : oldColor;
            if (GUILayout.Button("Right"))
                _turnRight = true;
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();
        }

        private void DrawSceneryPanel()
        {
            DrawLiveSceneryPanel();
        }

        private void DrawDesktopPanel()
        {
            var online = IsEditorOnline();
            GUILayout.Space(5f);
            GUILayout.Label(
                online ? "DESKTOP EDITOR ONLINE" : "DESKTOP EDITOR OFFLINE",
                online ? _onlineStyle : _offlineStyle);
            if (_state != null && online)
            {
                GUILayout.Label(
                    _state.projectLoaded
                        ? Safe(_state.projectName) + " / " + Safe(_state.layerName)
                        : "No mod project loaded",
                    _titleStyle);
                GUILayout.Label(
                    "Track: " + _state.nodeCount + " nodes / "
                    + _state.segmentCount + " segments    Scenery: "
                    + _state.sceneryCount,
                    _lineStyle);
                var selection = string.IsNullOrWhiteSpace(_state.selectionId)
                    ? "none"
                    : Safe(_state.selectionKind) + " " + Shorten(_state.selectionId, 28);
                GUILayout.Label("Desktop selection: " + selection, _mutedStyle);
            }
            else
            {
                GUILayout.Label(
                    "Start the packaged desktop Tile Editor to use remote panels.",
                    _lineStyle);
                GUILayout.Label("Shared path: Mods/TrackBridge", _mutedStyle);
            }

            GUILayout.Space(7f);
            GUILayout.BeginHorizontal();
            ToolButton("Arc", "set_geo_mode", "curve", online);
            ToolButton("Grade", "set_geo_mode", "grade", online);
            ToolButton("Turnout", "set_geo_mode", "turnout", online);
            ToolButton("Wye", "set_geo_mode", "wye", online);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ToolButton("Desktop Geo", "open_panel", "geo", online);
            ToolButton("Scenery", "open_panel", "scenery", online);
            ToolButton("Bring Forward", "focus_editor", "", online);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = online && _state != null && _state.canUndo;
            if (GUILayout.Button("Desktop Undo", GUILayout.Height(30f)))
                SendCommand("undo", "");
            GUI.enabled = online && _state != null && _state.projectLoaded;
            if (GUILayout.Button("Desktop Save + Reload", GUILayout.Height(30f)))
                SendCommand("save_reload", "");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawNativeChangeBar()
        {
            GUILayout.Space(3f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor.ChangeCount > 0;
            if (GUILayout.Button(
                    "Undo (" + _mapEditor.ChangeCount + ")",
                    GUILayout.Height(30f)))
                RunGameAction("Undid last in-game edit", _mapEditor.Undo);
            GUI.enabled = true;
            if (GUILayout.Button("Redo", GUILayout.Height(30f)))
                RunGameAction("Redid in-game edit", _mapEditor.Redo);
            if (GUILayout.Button(
                    _mapEditor.Dirty ? "Save Graph *" : "Save Graph",
                    GUILayout.Height(30f)))
                SaveGraphAndSyncDesktop();
            GUILayout.EndHorizontal();
        }

        private void DrawDirectionButtons()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Direction", GUILayout.Width(150f));
            var oldColor = GUI.backgroundColor;
            if (!_turnRight)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button("Left"))
                _turnRight = false;
            GUI.backgroundColor = _turnRight
                ? new Color(0.18f, 0.72f, 0.82f)
                : oldColor;
            if (GUILayout.Button("Right"))
                _turnRight = true;
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();
        }

        private void DrawTextField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(180f));
            value = GUILayout.TextField(value ?? string.Empty, GUILayout.Width(120f));
            GUILayout.EndHorizontal();
        }

        private void SegmentStyleButton(string label, int style)
        {
            if (GUILayout.Button(label, GUILayout.Height(27f)))
                RunGameAction("Changed track style to " + label,
                    () => _mapEditor.SetSelectedSegmentStyle(style));
        }

        private void SegmentTrackClassButton(
            string label,
            string trackClass,
            string selectedTrackClass)
        {
            var oldColor = GUI.backgroundColor;
            var selected = string.Equals(
                trackClass,
                selectedTrackClass,
                StringComparison.OrdinalIgnoreCase);
            if (selected)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(29f)) && !selected)
            {
                RunGameAction(
                    "Changed track class to " + label,
                    () => _mapEditor.SetSelectedSegmentTrackClass(
                        trackClass));
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawDeleteControl(
            TileEditorGraphSession.SelectionInfo node,
            TileEditorGraphSession.SelectionInfo segment)
        {
            var selectedId = node != null ? node.Id : segment?.Id;
            GUI.enabled = !string.IsNullOrWhiteSpace(selectedId);
            if (_deleteConfirmId == selectedId)
            {
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.28f, 0.20f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("CONFIRM DELETE " + selectedId, GUILayout.Height(31f)))
                {
                    RunGameAction("Deleted " + selectedId, () =>
                    {
                        if (node != null)
                            _mapEditor.DeleteSelectedNode(false);
                        else
                            _mapEditor.DeleteSelectedSegment();
                        _deleteConfirmId = string.Empty;
                    });
                }
                GUI.backgroundColor = oldColor;
                if (GUILayout.Button("Cancel", GUILayout.Width(82f), GUILayout.Height(31f)))
                    _deleteConfirmId = string.Empty;
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button("Delete Selected", GUILayout.Height(29f)))
            {
                _deleteConfirmId = selectedId;
            }
            GUI.enabled = true;
        }

        private void RunGameAction(string successMessage, Action action)
        {
            RunGameAction(
                () =>
                {
                    action();
                    return successMessage;
                });
        }

        private void RunGameAction(Func<string> action)
        {
            try
            {
                _lastPanelMessage = action() ?? string.Empty;
                _deleteConfirmId = string.Empty;
                _mapEditor?.RefreshEditMode();
            }
            catch (TargetInvocationException ex)
            {
                var actual = ex.InnerException ?? ex;
                _lastPanelMessage = actual.Message;
                _logger?.Warning("In-game Geo action failed: " + actual);
            }
            catch (Exception ex)
            {
                _lastPanelMessage = ex.Message;
                _logger?.Warning("In-game Geo action failed: " + ex);
            }
        }

        private static float ParseFloat(string text, string label)
        {
            if (float.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
                return value;
            throw new InvalidOperationException(
                "Enter a valid number for " + label + ".");
        }

        private static int ParseInt(string text, string label)
        {
            if (int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var value))
                return value;
            throw new InvalidOperationException(
                "Enter a whole number for " + label + ".");
        }
    }
}
