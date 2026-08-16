using System;
using System.Globalization;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private enum TerrainWorkspace
        {
            Sculpt,
            Surface,
        }

        private static readonly TileEditorGraphSession.TerrainBrushMode[]
            SculptBrushModes =
            {
                TileEditorGraphSession.TerrainBrushMode.Flatten,
                TileEditorGraphSession.TerrainBrushMode.LevelPath,
                TileEditorGraphSession.TerrainBrushMode.GradePlane,
                TileEditorGraphSession.TerrainBrushMode.Ditch,
                TileEditorGraphSession.TerrainBrushMode.Berm,
                TileEditorGraphSession.TerrainBrushMode.Raise,
                TileEditorGraphSession.TerrainBrushMode.Lower,
                TileEditorGraphSession.TerrainBrushMode.Smooth,
                TileEditorGraphSession.TerrainBrushMode.SetHeight,
                TileEditorGraphSession.TerrainBrushMode.Noise,
            };

        private static readonly string[] SculptBrushLabels =
        {
            "Building Pad",
            "Path / Road",
            "Grade Plane",
            "Ditch",
            "Berm",
            "Raise",
            "Lower",
            "Smooth",
            "Set Height",
            "Noise",
        };

        private TerrainWorkspace _terrainWorkspace =
            TerrainWorkspace.Sculpt;
        private TileEditorGraphSession.TerrainBrushMode _terrainBrushMode =
            TileEditorGraphSession.TerrainBrushMode.Flatten;
        private TileEditorGraphSession.TerrainBrushFalloff _terrainFalloff =
            TileEditorGraphSession.TerrainBrushFalloff.Smooth;
        private TileEditorGraphSession.TerrainBrushShape _terrainBrushShape =
            TileEditorGraphSession.TerrainBrushShape.Circle;
        private string _terrainBrushRadius = "15";
        private string _terrainBrushStrength = "0.35";
        private string _terrainHeightRate = "3";
        private string _terrainMaximumCutFill = "10";
        private string _terrainFeatureDepth = "1.25";
        private string _terrainTargetHeight = "700";
        private string _terrainGradePercent = "0";
        private string _terrainGradeHeading = "0";
        private string _terrainNoiseScale = "25";
        private string _terrainNoiseAmplitude = "2";
        private string _terrainBrushSpacing = "0.18";
        private int _terrainVegetationId;
        private bool _terrainPaintWater = true;
        private bool _terrainPadAutoSample = true;
        private bool _terrainStrokeActive;
        private bool _terrainHasLastDab;
        private Vector3 _terrainLastDab;
        private float _terrainLastDabAt;
        private float _nextTerrainDabAt;
        private float _terrainStrokeTargetHeight;
        private Vector3 _terrainStrokeReference;
        private TileEditorGraphSession.TerrainPointerInfo _terrainPointerInfo;

        private bool DesktopTerrainHasUnsavedChanges =>
            IsEditorOnline()
            && _state != null
            && _state.terrainDirty;

        private void DrawTerrainPanel()
        {
            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Space(8f);
                GUILayout.Label(
                    "Railroader's terrain is not ready yet.",
                    _titleStyle);
                GUILayout.Label(
                    "Load into a map, then reopen F9.",
                    _mutedStyle);
                return;
            }

            DrawTerrainSaveBar();
            DrawTerrainPointerReadout();
            DrawOsmOverlayControls();

            var syncBlocked = DesktopTerrainHasUnsavedChanges;
            if (syncBlocked)
            {
                GUILayout.Label(
                    "DESKTOP TERRAIN HAS UNSAVED CHANGES",
                    _offlineStyle);
                GUILayout.Label(
                    "Save or undo those desktop terrain edits before painting "
                    + "the same tile data in-game.",
                    _mutedStyle);
            }

            GUILayout.Space(4f);
            _terrainWorkspace = (TerrainWorkspace)GUILayout.SelectionGrid(
                (int)_terrainWorkspace,
                new[] { "SCULPT TERRAIN", "SURFACE PAINT" },
                2);
            GUILayout.Space(4f);

            GUI.enabled = !syncBlocked;
            if (_terrainWorkspace == TerrainWorkspace.Sculpt)
                DrawTerrainSculptWorkspace();
            else
                DrawTerrainSurfaceWorkspace();
            GUI.enabled = true;

            GUILayout.Space(5f);
            GUILayout.Label(
                "Hold LEFT MOUSE over the world to paint. "
                + "Alt + wheel changes radius; Shift + Alt + wheel changes "
                + "strength. [ and ] also change radius.",
                _lineStyle);
            GUILayout.Label(
                _terrainWorkspace == TerrainWorkspace.Sculpt
                    ? "The ring is the brush footprint. Construction tools "
                      + "use a cut/fill safety limit and a fixed stroke target "
                      + "so holding the mouse cannot create runaway spikes."
                    : "Surface Paint changes vegetation or water only; it "
                      + "never modifies terrain elevation.",
                _mutedStyle);

            GUILayout.FlexibleSpace();
            DrawTerrainUndoBar(syncBlocked);
        }

        private void DrawTerrainSaveBar()
        {
            var syncBlocked = DesktopTerrainHasUnsavedChanges;
            GUILayout.BeginHorizontal();
            GUI.enabled = !_mapEditor.TerrainDirty
                          && !_mapEditor.TerrainRebuildPending
                          && !syncBlocked;
            if (GUILayout.Button(
                    _mapEditor.TerrainRebuildPending
                        ? "Rebuilding Terrain..."
                        : _mapEditor.TerrainDirty
                        ? "Save or Undo Before Rebuild"
                        : "Rebuild Terrain",
                    GUILayout.Height(30f)))
            {
                EndTerrainStroke();
                RunGameAction(_mapEditor.RebuildTerrain);
            }
            GUI.enabled = !syncBlocked;
            if (GUILayout.Button(
                    TerrainSaveLabel(),
                    GUILayout.Height(30f)))
            {
                EndTerrainStroke();
                SaveTerrainAndSyncDesktop();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private string TerrainSaveLabel()
        {
            return _mapEditor.TerrainDirty
                ? "Save + Rebuild * ("
                  + _mapEditor.DirtyTerrainTileCount + ")"
                : "Save + Rebuild";
        }

        private void DrawTerrainPointerReadout()
        {
            var info = _terrainPointerInfo;
            if (info == null || !info.Available)
            {
                GUILayout.Label(
                    "Move the mouse over a loaded terrain tile.",
                    _mutedStyle);
                return;
            }
            GUILayout.Label(
                "Tile " + info.Tile.x + ", " + info.Tile.y
                + "   Height "
                + info.Height.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture)
                + " m   Vegetation " + info.VegetationId
                + "   Water " + (info.Water ? "yes" : "no"),
                _lineStyle);
            GUILayout.Label(
                "Map: " + info.MapName
                + (string.IsNullOrWhiteSpace(info.SourceFile)
                    ? string.Empty
                    : "   Source: " + info.SourceFile),
                _mutedStyle);
        }

        private void DrawTerrainSculptWorkspace()
        {
            GUILayout.Label("Construction tools", _titleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Building", GUILayout.Height(27f)))
                ApplyTerrainUsePreset("building");
            if (GUILayout.Button("Track / Road", GUILayout.Height(27f)))
                ApplyTerrainUsePreset("track");
            if (GUILayout.Button("Walkway", GUILayout.Height(27f)))
                ApplyTerrainUsePreset("walkway");
            if (GUILayout.Button("Ditch", GUILayout.Height(27f)))
                ApplyTerrainUsePreset("ditch");
            if (GUILayout.Button("Embankment", GUILayout.Height(27f)))
                ApplyTerrainUsePreset("berm");
            GUILayout.EndHorizontal();

            var selected = Array.IndexOf(
                SculptBrushModes,
                _terrainBrushMode);
            if (selected < 0)
                selected = 0;
            selected = GUILayout.SelectionGrid(
                selected,
                SculptBrushLabels,
                5);
            _terrainBrushMode = SculptBrushModes[
                Mathf.Clamp(
                    selected,
                    0,
                    SculptBrushModes.Length - 1)];

            DrawTerrainBrushGeometry();
            DrawTerrainStrengthControls("Sculpt strength");
            DrawTerrainRateControls();
            DrawTerrainSculptModeOptions();

            GUILayout.Label("Cut / fill safety", _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawTerrainValueButton(
                "1",
                ref _terrainMaximumCutFill);
            DrawTerrainValueButton(
                "2",
                ref _terrainMaximumCutFill);
            DrawTerrainValueButton(
                "5",
                ref _terrainMaximumCutFill);
            DrawTerrainValueButton(
                "10",
                ref _terrainMaximumCutFill);
            DrawTerrainValueButton(
                "25",
                ref _terrainMaximumCutFill);
            DrawTerrainValueButton(
                "100",
                ref _terrainMaximumCutFill);
            GUILayout.EndHorizontal();
            DrawTextField(
                "Max change / stroke (m)",
                ref _terrainMaximumCutFill);
            GUILayout.Label(
                "Every point is limited relative to its original height at "
                + "the start of this stroke.",
                _mutedStyle);
            DrawTextField(
                "Brush spacing (0.05-1)",
                ref _terrainBrushSpacing);
        }

        private void DrawTerrainSurfaceWorkspace()
        {
            GUILayout.Label(
                "Vegetation and water masks",
                _titleStyle);
            var surfaceIndex =
                _terrainBrushMode
                == TileEditorGraphSession.TerrainBrushMode.Water
                    ? 1
                    : 0;
            surfaceIndex = GUILayout.SelectionGrid(
                surfaceIndex,
                new[] { "VEGETATION", "WATER" },
                2);
            _terrainBrushMode = surfaceIndex == 0
                ? TileEditorGraphSession.TerrainBrushMode.Vegetation
                : TileEditorGraphSession.TerrainBrushMode.Water;

            DrawTerrainBrushGeometry();
            DrawTerrainStrengthControls("Paint opacity");

            if (_terrainBrushMode
                == TileEditorGraphSession.TerrainBrushMode.Vegetation)
            {
                GUILayout.Label(
                    "Vegetation mask ID (0 clears vegetation)",
                    _mutedStyle);
                _terrainVegetationId = GUILayout.SelectionGrid(
                    Mathf.Clamp(_terrainVegetationId, 0, 7),
                    new[]
                    {
                        "0 Clear",
                        "1",
                        "2",
                        "3",
                        "4",
                        "5",
                        "6",
                        "7",
                    },
                    4);
                GUI.enabled = _terrainPointerInfo != null
                              && _terrainPointerInfo.Available;
                if (GUILayout.Button(
                        "Sample Vegetation Under Pointer",
                        GUILayout.Height(28f)))
                {
                    _terrainVegetationId =
                        _terrainPointerInfo.VegetationId;
                    _lastPanelMessage =
                        "Sampled vegetation ID "
                        + _terrainVegetationId;
                }
                GUI.enabled = true;
            }
            else
            {
                var oldColor = GUI.backgroundColor;
                GUILayout.BeginHorizontal();
                if (_terrainPaintWater)
                    GUI.backgroundColor =
                        new Color(0.10f, 0.58f, 0.84f);
                if (GUILayout.Button(
                        "Paint Water",
                        GUILayout.Height(30f)))
                {
                    _terrainPaintWater = true;
                }
                GUI.backgroundColor = !_terrainPaintWater
                    ? new Color(0.72f, 0.34f, 0.22f)
                    : oldColor;
                if (GUILayout.Button(
                        "Clear Water",
                        GUILayout.Height(30f)))
                {
                    _terrainPaintWater = false;
                }
                GUI.backgroundColor = oldColor;
                GUILayout.EndHorizontal();
            }
            DrawTextField(
                "Brush spacing (0.05-1)",
                ref _terrainBrushSpacing);
        }

        private void DrawTerrainBrushGeometry()
        {
            GUILayout.Label("Brush size", _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawTerrainValueButton("3", ref _terrainBrushRadius);
            DrawTerrainValueButton("5", ref _terrainBrushRadius);
            DrawTerrainValueButton("10", ref _terrainBrushRadius);
            DrawTerrainValueButton("15", ref _terrainBrushRadius);
            DrawTerrainValueButton("25", ref _terrainBrushRadius);
            DrawTerrainValueButton("50", ref _terrainBrushRadius);
            GUILayout.EndHorizontal();
            DrawTextField("Radius (m)", ref _terrainBrushRadius);

            GUILayout.Label("Falloff", _mutedStyle);
            _terrainFalloff =
                (TileEditorGraphSession.TerrainBrushFalloff)
                GUILayout.SelectionGrid(
                    (int)_terrainFalloff,
                    new[] { "Hard", "Linear", "Smooth", "Gaussian" },
                    4);
            GUILayout.Label("Shape", _mutedStyle);
            _terrainBrushShape =
                (TileEditorGraphSession.TerrainBrushShape)
                GUILayout.SelectionGrid(
                    (int)_terrainBrushShape,
                    new[] { "Circle", "Square" },
                    2);
        }

        private void DrawTerrainStrengthControls(string label)
        {
            GUILayout.Label(label, _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawTerrainValueButton("0.1", ref _terrainBrushStrength);
            DrawTerrainValueButton("0.25", ref _terrainBrushStrength);
            DrawTerrainValueButton("0.5", ref _terrainBrushStrength);
            DrawTerrainValueButton("0.75", ref _terrainBrushStrength);
            DrawTerrainValueButton("1", ref _terrainBrushStrength);
            GUILayout.EndHorizontal();
            DrawTextField("Strength (0-1)", ref _terrainBrushStrength);
        }

        private void DrawTerrainRateControls()
        {
            GUILayout.Label("Vertical speed", _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawTerrainValueButton("0.5", ref _terrainHeightRate);
            DrawTerrainValueButton("1", ref _terrainHeightRate);
            DrawTerrainValueButton("3", ref _terrainHeightRate);
            DrawTerrainValueButton("10", ref _terrainHeightRate);
            DrawTerrainValueButton("30", ref _terrainHeightRate);
            GUILayout.EndHorizontal();
            DrawTextField(
                "Vertical rate (m/sec)",
                ref _terrainHeightRate);
        }

        private void DrawTerrainSculptModeOptions()
        {
            switch (_terrainBrushMode)
            {
                case TileEditorGraphSession.TerrainBrushMode.Flatten:
                    _terrainPadAutoSample = GUILayout.Toggle(
                        _terrainPadAutoSample,
                        " Sample pad height from the first click");
                    if (!_terrainPadAutoSample)
                        DrawTerrainFixedHeightControls();
                    GUILayout.Label(
                        "Building Pad locks one elevation for the entire "
                        + "stroke and blends its edge.",
                        _mutedStyle);
                    break;

                case TileEditorGraphSession.TerrainBrushMode.LevelPath:
                    GUILayout.Label(
                        "Path / Road samples the center height at every dab, "
                        + "removing cross-slope while following the land.",
                        _mutedStyle);
                    break;

                case TileEditorGraphSession.TerrainBrushMode.GradePlane:
                    DrawTextField(
                        "Grade (%)",
                        ref _terrainGradePercent);
                    DrawTextField(
                        "Heading (degrees)",
                        ref _terrainGradeHeading);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(
                            "Use Camera Heading",
                            GUILayout.Height(27f)))
                    {
                        var camera = Camera.main;
                        if (camera != null)
                        {
                            _terrainGradeHeading =
                                camera.transform.eulerAngles.y.ToString(
                                    "0.###",
                                    CultureInfo.InvariantCulture);
                        }
                    }
                    if (GUILayout.Button(
                            "Reverse Grade",
                            GUILayout.Height(27f)))
                    {
                        var grade = -ReadTerrainFloat(
                            _terrainGradePercent,
                            0f,
                            -25f,
                            25f);
                        _terrainGradePercent =
                            FormatTerrainNumber(grade);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Label(
                        "The first click anchors elevation. The heading and "
                        + "grade define one stable plane for the whole stroke.",
                        _mutedStyle);
                    break;

                case TileEditorGraphSession.TerrainBrushMode.Ditch:
                case TileEditorGraphSession.TerrainBrushMode.Berm:
                    DrawTextField(
                        _terrainBrushMode
                        == TileEditorGraphSession.TerrainBrushMode.Ditch
                            ? "Ditch depth (m)"
                            : "Berm height (m)",
                        ref _terrainFeatureDepth);
                    GUILayout.Label(
                        "Depth is measured from each point's original "
                        + "elevation, so holding the brush cannot dig or pile "
                        + "forever.",
                        _mutedStyle);
                    break;

                case TileEditorGraphSession.TerrainBrushMode.SetHeight:
                    DrawTerrainFixedHeightControls();
                    break;

                case TileEditorGraphSession.TerrainBrushMode.Noise:
                    DrawTextField(
                        "Noise scale (m)",
                        ref _terrainNoiseScale);
                    DrawTextField(
                        "Noise amplitude (m)",
                        ref _terrainNoiseAmplitude);
                    GUILayout.Label(
                        "Noise converges to a fixed offset from the original "
                        + "surface instead of accumulating into spikes.",
                        _mutedStyle);
                    break;
            }
        }

        private void DrawTerrainFixedHeightControls()
        {
            DrawTextField(
                "Target height (m)",
                ref _terrainTargetHeight);
            GUI.enabled = _terrainPointerInfo != null
                          && _terrainPointerInfo.Available;
            if (GUILayout.Button(
                    "Sample Height Under Pointer",
                    GUILayout.Height(28f)))
            {
                _terrainTargetHeight =
                    _terrainPointerInfo.Height.ToString(
                        "0.###",
                        CultureInfo.InvariantCulture);
                _lastPanelMessage =
                    "Sampled terrain height "
                    + _terrainTargetHeight + " m";
            }
            GUI.enabled = true;
        }

        private void ApplyTerrainUsePreset(string preset)
        {
            switch (preset)
            {
                case "building":
                    _terrainBrushMode =
                        TileEditorGraphSession.TerrainBrushMode.Flatten;
                    _terrainBrushRadius = "25";
                    _terrainBrushStrength = "0.5";
                    _terrainHeightRate = "3";
                    _terrainMaximumCutFill = "10";
                    _terrainFalloff =
                        TileEditorGraphSession.TerrainBrushFalloff.Smooth;
                    _terrainPadAutoSample = true;
                    break;
                case "track":
                    _terrainBrushMode =
                        TileEditorGraphSession.TerrainBrushMode.LevelPath;
                    _terrainBrushRadius = "8";
                    _terrainBrushStrength = "0.4";
                    _terrainHeightRate = "2";
                    _terrainMaximumCutFill = "4";
                    _terrainFalloff =
                        TileEditorGraphSession.TerrainBrushFalloff.Gaussian;
                    break;
                case "walkway":
                    _terrainBrushMode =
                        TileEditorGraphSession.TerrainBrushMode.LevelPath;
                    _terrainBrushRadius = "3";
                    _terrainBrushStrength = "0.35";
                    _terrainHeightRate = "1";
                    _terrainMaximumCutFill = "2";
                    _terrainFalloff =
                        TileEditorGraphSession.TerrainBrushFalloff.Smooth;
                    break;
                case "ditch":
                    _terrainBrushMode =
                        TileEditorGraphSession.TerrainBrushMode.Ditch;
                    _terrainBrushRadius = "5";
                    _terrainBrushStrength = "0.5";
                    _terrainHeightRate = "2";
                    _terrainFeatureDepth = "1.25";
                    _terrainMaximumCutFill = "3";
                    _terrainFalloff =
                        TileEditorGraphSession.TerrainBrushFalloff.Gaussian;
                    break;
                case "berm":
                    _terrainBrushMode =
                        TileEditorGraphSession.TerrainBrushMode.Berm;
                    _terrainBrushRadius = "8";
                    _terrainBrushStrength = "0.5";
                    _terrainHeightRate = "2";
                    _terrainFeatureDepth = "1.5";
                    _terrainMaximumCutFill = "4";
                    _terrainFalloff =
                        TileEditorGraphSession.TerrainBrushFalloff.Gaussian;
                    break;
            }
            _lastPanelMessage =
                "Loaded " + Pretty(preset) + " terrain preset";
        }

        private static void DrawTerrainValueButton(
            string value,
            ref string target)
        {
            var oldColor = GUI.backgroundColor;
            if (string.Equals(
                    value,
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(value, GUILayout.Height(25f)))
                target = value;
            GUI.backgroundColor = oldColor;
        }

        private void UpdateTerrainPointerEditing()
        {
            if (_mapEditor == null
                || !_mapEditor.Available
                || DesktopTerrainHasUnsavedChanges)
            {
                EndTerrainStroke();
                _terrainPointerInfo = null;
                HideWorldPointerMarker();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EndTerrainStroke();
            }

            if (!IsPointerOverEditorWindow())
                AdjustTerrainBrushFromShortcuts();

            if (!TryGetPointerSurfaceHit(true, out var hit))
            {
                EndTerrainStroke();
                _terrainPointerInfo = null;
                HideWorldPointerMarker();
                return;
            }

            _terrainPointerInfo = _mapEditor.InspectTerrainAt(hit.point);
            var radius = ReadTerrainFloat(
                _terrainBrushRadius,
                15f,
                1f,
                250f);
            if (!IsPointerOverEditorWindow())
            {
                ShowWorldPointerMarker(
                    hit.point,
                    hit.normal,
                    radius,
                    TerrainBrushColor(_terrainBrushMode));
            }
            else
            {
                HideWorldPointerMarker();
                EndTerrainStroke();
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _mapEditor.BeginTerrainEdit();
                _terrainStrokeActive = true;
                _terrainHasLastDab = false;
                _terrainLastDabAt = Time.unscaledTime;
                _nextTerrainDabAt = 0f;
                if (_terrainPointerInfo != null
                    && _terrainPointerInfo.Available)
                {
                    _terrainStrokeTargetHeight =
                        _terrainPointerInfo.Height;
                    _terrainStrokeReference =
                        _terrainPointerInfo.GamePosition;
                    _terrainStrokeReference.y =
                        _terrainPointerInfo.Height;
                }
            }
            if (!_terrainStrokeActive)
                return;
            if (!Input.GetMouseButton(0))
            {
                EndTerrainStroke();
                return;
            }

            var spacing = ReadTerrainFloat(
                _terrainBrushSpacing,
                0.18f,
                0.05f,
                1f);
            var movedEnough = !_terrainHasLastDab
                              || Vector3.Distance(
                                  hit.point,
                                  _terrainLastDab)
                              >= radius * spacing;
            var timedDab =
                Time.unscaledTime >= _nextTerrainDabAt;
            if (!movedEnough && !timedDab)
                return;

            try
            {
                var now = Time.unscaledTime;
                var elapsed = Mathf.Clamp(
                    now - _terrainLastDabAt,
                    0.016f,
                    0.1f);
                var changed = _mapEditor.ApplyTerrainBrush(
                    hit.point,
                    BuildTerrainBrushParameters(radius),
                    elapsed);
                _terrainLastDab = hit.point;
                _terrainHasLastDab = true;
                _terrainLastDabAt = now;
                _nextTerrainDabAt = now + 0.035f;
                if (changed > 0)
                {
                    _lastPanelMessage =
                        (_terrainWorkspace
                         == TerrainWorkspace.Sculpt
                            ? "Terrain sculpt"
                            : "Surface paint")
                        + " active - "
                        + _mapEditor.DirtyTerrainTileCount
                        + " tile"
                        + (_mapEditor.DirtyTerrainTileCount == 1
                            ? string.Empty
                            : "s")
                        + " changed";
                }
            }
            catch (Exception ex)
            {
                EndTerrainStroke();
                _lastPanelMessage =
                    "Terrain brush failed: " + ex.Message;
                _logger?.Warning(
                    "Terrain brush failed: " + ex);
            }
        }

        private TileEditorGraphSession.TerrainBrushParameters
            BuildTerrainBrushParameters(float radius)
        {
            var targetHeight = ReadTerrainFloat(
                _terrainTargetHeight,
                _terrainStrokeTargetHeight,
                500f,
                1500f);
            if (_terrainBrushMode
                == TileEditorGraphSession.TerrainBrushMode.Flatten
                && _terrainPadAutoSample)
            {
                targetHeight = _terrainStrokeTargetHeight;
            }
            else if (_terrainBrushMode
                     == TileEditorGraphSession.TerrainBrushMode.LevelPath
                     && _terrainPointerInfo != null
                     && _terrainPointerInfo.Available)
            {
                targetHeight = _terrainPointerInfo.Height;
            }

            return new TileEditorGraphSession.TerrainBrushParameters
            {
                Mode = _terrainBrushMode,
                Falloff = _terrainFalloff,
                Shape = _terrainBrushShape,
                Radius = radius,
                Strength = ReadTerrainFloat(
                    _terrainBrushStrength,
                    0.35f,
                    0.01f,
                    1f),
                HeightRate = ReadTerrainFloat(
                    _terrainHeightRate,
                    3f,
                    0.05f,
                    100f),
                TargetHeight = targetHeight,
                MaximumCutFill = ReadTerrainFloat(
                    _terrainMaximumCutFill,
                    10f,
                    0.05f,
                    1000f),
                FeatureDepth = ReadTerrainFloat(
                    _terrainFeatureDepth,
                    1.25f,
                    0.05f,
                    100f),
                GradePercent = ReadTerrainFloat(
                    _terrainGradePercent,
                    0f,
                    -25f,
                    25f),
                GradeHeading = ReadTerrainFloat(
                    _terrainGradeHeading,
                    0f,
                    -3600f,
                    3600f),
                ReferencePosition = _terrainStrokeReference,
                NoiseScale = ReadTerrainFloat(
                    _terrainNoiseScale,
                    25f,
                    1f,
                    500f),
                NoiseAmplitude = ReadTerrainFloat(
                    _terrainNoiseAmplitude,
                    2f,
                    0.05f,
                    100f),
                VegetationId = Mathf.Clamp(
                    _terrainVegetationId,
                    0,
                    7),
                WaterEnabled = _terrainPaintWater,
            };
        }

        private void AdjustTerrainBrushFromShortcuts()
        {
            var radius = ReadTerrainFloat(
                _terrainBrushRadius,
                15f,
                1f,
                250f);
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                radius = Mathf.Max(1f, radius / 1.25f);
                _terrainBrushRadius = FormatTerrainNumber(radius);
            }
            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                radius = Mathf.Min(250f, radius * 1.25f);
                _terrainBrushRadius = FormatTerrainNumber(radius);
            }
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f
                || !(Input.GetKey(KeyCode.LeftAlt)
                     || Input.GetKey(KeyCode.RightAlt)))
            {
                return;
            }
            if (Input.GetKey(KeyCode.LeftShift)
                || Input.GetKey(KeyCode.RightShift))
            {
                var strength = ReadTerrainFloat(
                    _terrainBrushStrength,
                    0.35f,
                    0.01f,
                    1f);
                strength = Mathf.Clamp(
                    strength + scroll * 0.05f,
                    0.01f,
                    1f);
                _terrainBrushStrength =
                    FormatTerrainNumber(strength);
            }
            else
            {
                radius = Mathf.Clamp(
                    radius * Mathf.Pow(1.15f, scroll),
                    1f,
                    250f);
                _terrainBrushRadius =
                    FormatTerrainNumber(radius);
            }
        }

        private void EndTerrainStroke()
        {
            if (_terrainStrokeActive)
                _mapEditor?.EndTerrainEdit();
            _terrainStrokeActive = false;
            _terrainHasLastDab = false;
        }

        private void DrawTerrainUndoBar(bool syncBlocked)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor.CanUndoTerrain;
            if (GUILayout.Button("Undo Terrain", GUILayout.Height(31f)))
            {
                EndTerrainStroke();
                RunGameAction(
                    "Undid terrain stroke",
                    _mapEditor.UndoTerrain);
            }
            GUI.enabled = _mapEditor.CanRedoTerrain;
            if (GUILayout.Button("Redo Terrain", GUILayout.Height(31f)))
            {
                EndTerrainStroke();
                RunGameAction(
                    "Redid terrain stroke",
                    _mapEditor.RedoTerrain);
            }
            GUI.enabled = !syncBlocked;
            if (GUILayout.Button(
                    TerrainSaveLabel(),
                    GUILayout.Height(31f)))
            {
                EndTerrainStroke();
                SaveTerrainAndSyncDesktop();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void SaveTerrainAndSyncDesktop()
        {
            try
            {
                var message = _mapEditor.SaveTerrainTiles();
                NotifyDesktopFilesSaved(
                    "terrain",
                    _mapEditor.LastSavedTerrainPaths);
                _lastPanelMessage = message
                                    + SyncNotificationSuffix(
                                        _mapEditor
                                            .LastSavedTerrainPaths);
            }
            catch (Exception ex)
            {
                _lastPanelMessage = ex.Message;
                _logger?.Warning(
                    "In-game terrain action failed: " + ex);
            }
        }

        private static float ReadTerrainFloat(
            string value,
            float fallback,
            float minimum,
            float maximum)
        {
            if (!float.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                || float.IsNaN(parsed)
                || float.IsInfinity(parsed))
            {
                parsed = fallback;
            }
            return Mathf.Clamp(parsed, minimum, maximum);
        }

        private static string FormatTerrainNumber(float value)
        {
            return value.ToString(
                "0.###",
                CultureInfo.InvariantCulture);
        }

        private static Color TerrainBrushColor(
            TileEditorGraphSession.TerrainBrushMode mode)
        {
            switch (mode)
            {
                case TileEditorGraphSession.TerrainBrushMode.Lower:
                case TileEditorGraphSession.TerrainBrushMode.Ditch:
                    return new Color(1f, 0.42f, 0.20f, 1f);
                case TileEditorGraphSession.TerrainBrushMode.Berm:
                    return new Color(1f, 0.70f, 0.20f, 1f);
                case TileEditorGraphSession.TerrainBrushMode.Vegetation:
                    return new Color(0.30f, 1f, 0.22f, 1f);
                case TileEditorGraphSession.TerrainBrushMode.Water:
                    return new Color(0.18f, 0.58f, 1f, 1f);
                case TileEditorGraphSession.TerrainBrushMode.Noise:
                    return new Color(0.95f, 0.48f, 1f, 1f);
                case TileEditorGraphSession.TerrainBrushMode.Smooth:
                    return new Color(1f, 0.88f, 0.20f, 1f);
                case TileEditorGraphSession.TerrainBrushMode.GradePlane:
                    return new Color(0.72f, 0.45f, 1f, 1f);
                default:
                    return new Color(0.10f, 0.95f, 1f, 1f);
            }
        }
    }
}
