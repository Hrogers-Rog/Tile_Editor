using System;
using System.Globalization;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private string _scenerySearch = string.Empty;
        private string _lastScenerySearch = string.Empty;
        private int _sceneryAssetPage;
        private string _sceneryModelIdentifier = string.Empty;
        private string _sceneryPositionX = "0";
        private string _sceneryPositionY = "0";
        private string _sceneryPositionZ = "0";
        private string _sceneryRotationX = "0";
        private string _sceneryRotationY = "0";
        private string _sceneryRotationZ = "0";
        private string _sceneryScaleX = "1";
        private string _sceneryScaleY = "1";
        private string _sceneryScaleZ = "1";
        private string _scenerySelectionKey = string.Empty;
        private string _sceneryDeleteConfirm = string.Empty;
        private float _sceneryMoveStep = 1f;
        private float _sceneryRotationStep = 5f;
        private bool _sceneryLocalAxes;
        private bool _showAdvancedSceneryControls;
        private string _telegraphPolePositionX = "0";
        private string _telegraphPolePositionY = "0";
        private string _telegraphPolePositionZ = "0";
        private string _telegraphPoleRotationX = "0";
        private string _telegraphPoleRotationY = "0";
        private string _telegraphPoleRotationZ = "0";
        private int _telegraphPoleSelectionId = -1;
        private bool _telegraphPoleLocalAxes;
        private bool _showAdvancedTelegraphPoleControls;

        private void DrawLiveSceneryPanel()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            var oldColor = GUI.backgroundColor;
            var rebuildEnabled = GUI.enabled;
            GUI.backgroundColor = new Color(0.52f, 0.34f, 0.10f);
            GUI.enabled = rebuildEnabled
                          && _mapEditor != null
                          && !_mapEditor.TerrainRebuildPending;
            if (GUILayout.Button(
                    _mapEditor != null
                    && _mapEditor.TerrainRebuildPending
                        ? "Rebuilding Terrain..."
                        : "Rebuild Terrain",
                    GUILayout.Height(31f)))
            {
                RunGameAction(_mapEditor.RebuildTerrain);
            }
            GUI.backgroundColor = oldColor;
            GUI.enabled = rebuildEnabled;
            GUI.enabled = _mapEditor != null && _mapEditor.GraphOpen;
            if (GUILayout.Button(
                    "Refresh Markers",
                    GUILayout.Height(31f)))
            {
                RunGameAction(
                    "Refreshed scenery markers",
                    _mapEditor.RefreshSceneryOverlays);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Terrain temporarily unloads, reloads its map store, and "
                + "queues fresh tiles around the camera.",
                _mutedStyle);

            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Space(7f);
                GUILayout.Label(
                    "Railroader's map is not ready yet.",
                    _titleStyle);
                return;
            }

            if (!_mapEditor.GraphOpen)
            {
                return;
            }

            var scenery = _mapEditor.SelectedScenery;
            if (scenery == null)
            {
                _scenerySelectionKey = string.Empty;
                _sceneryDeleteConfirm = string.Empty;
                GUILayout.Space(6f);
                GUILayout.Label(
                    "PLACE SCENERY",
                    _titleStyle);
                GUILayout.Label(
                    _mapEditor.LiveSceneryCount
                    + " live scenery objects found. Click a cyan scenery "
                    + "marker, or choose an asset below.",
                    _lineStyle);
                DrawSceneryAssetPicker(true);
                return;
            }

            SyncSceneryFields(scenery, false);
            GUILayout.Space(4f);
            GUILayout.Label(
                scenery.Id,
                _titleStyle);
            GUILayout.Label(
                "Model: " + scenery.ModelIdentifier,
                _lineStyle);
            GUILayout.Label(
                "Position "
                + FormatSceneryVector(scenery.Position)
                + "   Heading "
                + scenery.Rotation.y.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " deg",
                _mutedStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Show", GUILayout.Height(29f)))
            {
                RunGameAction(
                    "Focused selected scenery",
                    _mapEditor.ShowSelectedScenery);
            }
            if (GUILayout.Button(
                    "Snap to Terrain",
                    GUILayout.Height(29f)))
            {
                RunSceneryAction(
                    "Snapped scenery to terrain",
                    _mapEditor.SnapSelectedSceneryToTerrain);
            }
            if (GUILayout.Button("Duplicate", GUILayout.Height(29f)))
            {
                RunSceneryAction(
                    "Duplicated scenery",
                    () =>
                    {
                        var id = _mapEditor.DuplicateSelectedScenery();
                        _lastPanelMessage =
                            "Duplicated scenery as " + id;
                    });
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "MOVE   Step "
                + _sceneryMoveStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " m",
                _titleStyle);
            GUILayout.FlexibleSpace();
            DrawWorldLocalAxesButtons(ref _sceneryLocalAxes);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawQuickStepButton(
                "0.1",
                0.1f,
                ref _sceneryMoveStep);
            DrawQuickStepButton(
                "0.5",
                0.5f,
                ref _sceneryMoveStep);
            DrawQuickStepButton(
                "1",
                1f,
                ref _sceneryMoveStep);
            DrawQuickStepButton(
                "5",
                5f,
                ref _sceneryMoveStep);
            DrawQuickStepButton(
                "10",
                10f,
                ref _sceneryMoveStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            SceneryMoveButton(
                "Y-\nLOWER",
                new Vector3(0f, -_sceneryMoveStep, 0f));
            SceneryMoveButton(
                "\u2191\nFORWARD Z+",
                new Vector3(0f, 0f, _sceneryMoveStep));
            SceneryMoveButton(
                "Y+\nRAISE",
                new Vector3(0f, _sceneryMoveStep, 0f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SceneryMoveButton(
                "\u2190\nLEFT X-",
                new Vector3(-_sceneryMoveStep, 0f, 0f));
            SceneryMoveButton(
                "\u2193\nBACK Z-",
                new Vector3(0f, 0f, -_sceneryMoveStep));
            SceneryMoveButton(
                "\u2192\nRIGHT X+",
                new Vector3(_sceneryMoveStep, 0f, 0f));
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            DrawPrimarySceneryRotationControls();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Scale -0.1", GUILayout.Height(29f)))
            {
                RunSceneryAction(
                    "Reduced scenery scale",
                    () => _mapEditor.ScaleSelectedScenery(
                        Vector3.one * -0.1f));
            }
            if (GUILayout.Button("Scale +0.1", GUILayout.Height(29f)))
            {
                RunSceneryAction(
                    "Increased scenery scale",
                    () => _mapEditor.ScaleSelectedScenery(
                        Vector3.one * 0.1f));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            var advancedColor = GUI.backgroundColor;
            if (_showAdvancedSceneryControls)
            {
                GUI.backgroundColor =
                    new Color(0.72f, 0.55f, 0.18f);
            }
            if (GUILayout.Button(
                    _showAdvancedSceneryControls
                        ? "Less"
                        : "More...",
                    GUILayout.Height(30f)))
            {
                _showAdvancedSceneryControls =
                    !_showAdvancedSceneryControls;
            }
            GUI.backgroundColor = advancedColor;
            if (GUILayout.Button(
                    "Place Another",
                    GUILayout.Height(30f)))
            {
                _mapEditor.ClearSelectedScenery();
                _scenerySelectionKey = string.Empty;
                _panelScroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            if (_showAdvancedSceneryControls)
                DrawAdvancedSceneryControls();

            DrawSceneryDelete();
        }

        private void DrawTelegraphPoleEditor(
            TileEditorGraphSession.TelegraphPoleInfo pole)
        {
            SyncTelegraphPoleFields(pole, false);
            GUILayout.Space(4f);
            GUILayout.Label(
                (pole.IsCustom
                    ? "TILE EDITOR POLE  "
                    : "MAP POLE  ")
                + pole.Id,
                _titleStyle);
            GUILayout.Label(
                "Position " + FormatSceneryVector(pole.Position)
                + "   Heading "
                + pole.Rotation.y.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " deg",
                _lineStyle);
            GUILayout.Label(
                pole.IsCustom
                    ? "Persistent custom node   Source: " + pole.FileName
                    : "Saved offset "
                      + FormatSceneryVector(pole.Offset)
                      + "   Source: " + pole.FileName,
                _mutedStyle);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Show", GUILayout.Height(29f)))
            {
                RunGameAction(
                    "Focused telegraph pole " + pole.Id,
                    _mapEditor.ShowSelectedTelegraphPole);
            }
            if (GUILayout.Button(
                    "Done / Select Another",
                    GUILayout.Height(29f)))
            {
                _mapEditor.ClearSelectedTelegraphPole();
                _telegraphPoleSelectionId = -1;
                _panelScroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "MOVE   Step "
                + _sceneryMoveStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " m",
                _titleStyle);
            GUILayout.FlexibleSpace();
            DrawWorldLocalAxesButtons(
                ref _telegraphPoleLocalAxes);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawQuickStepButton("0.1", 0.1f, ref _sceneryMoveStep);
            DrawQuickStepButton("0.5", 0.5f, ref _sceneryMoveStep);
            DrawQuickStepButton("1", 1f, ref _sceneryMoveStep);
            DrawQuickStepButton("5", 5f, ref _sceneryMoveStep);
            DrawQuickStepButton("10", 10f, ref _sceneryMoveStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            TelegraphPoleMoveButton(
                "Y-\nLOWER",
                new Vector3(0f, -_sceneryMoveStep, 0f));
            TelegraphPoleMoveButton(
                "\u2191\nFORWARD Z+",
                new Vector3(0f, 0f, _sceneryMoveStep));
            TelegraphPoleMoveButton(
                "Y+\nRAISE",
                new Vector3(0f, _sceneryMoveStep, 0f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TelegraphPoleMoveButton(
                "\u2190\nLEFT X-",
                new Vector3(-_sceneryMoveStep, 0f, 0f));
            TelegraphPoleMoveButton(
                "\u2193\nBACK Z-",
                new Vector3(0f, 0f, -_sceneryMoveStep));
            TelegraphPoleMoveButton(
                "\u2192\nRIGHT X+",
                new Vector3(_sceneryMoveStep, 0f, 0f));
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            DrawPrimaryTelegraphPoleRotationControls();

            var advancedColor = GUI.backgroundColor;
            if (_showAdvancedTelegraphPoleControls)
                GUI.backgroundColor = new Color(0.72f, 0.55f, 0.18f);
            if (GUILayout.Button(
                    _showAdvancedTelegraphPoleControls
                        ? "Less"
                        : "More...",
                    GUILayout.Height(30f)))
            {
                _showAdvancedTelegraphPoleControls =
                    !_showAdvancedTelegraphPoleControls;
            }
            GUI.backgroundColor = advancedColor;

            if (_showAdvancedTelegraphPoleControls)
            {
                GUILayout.Space(5f);
                GUILayout.Label("EXACT TRANSFORM", _titleStyle);
                DrawVectorFields(
                    "Position",
                    ref _telegraphPolePositionX,
                    ref _telegraphPolePositionY,
                    ref _telegraphPolePositionZ);
                if (GUILayout.Button(
                        "Apply Exact Pole Position",
                        GUILayout.Height(31f)))
                {
                    RunTelegraphPoleAction(
                        "Applied exact telegraph pole position",
                        () => _mapEditor.SetSelectedTelegraphPolePosition(
                            new Vector3(
                                ParseFloat(
                                    _telegraphPolePositionX,
                                    "telegraph pole position X"),
                                ParseFloat(
                                    _telegraphPolePositionY,
                                    "telegraph pole position Y"),
                                ParseFloat(
                                    _telegraphPolePositionZ,
                                    "telegraph pole position Z"))));
                }
                DrawVectorFields(
                    "Rotation",
                    ref _telegraphPoleRotationX,
                    ref _telegraphPoleRotationY,
                    ref _telegraphPoleRotationZ);
                if (GUILayout.Button(
                        "Apply Exact Pole Rotation",
                        GUILayout.Height(31f)))
                {
                    RunTelegraphPoleAction(
                        "Applied exact telegraph pole rotation",
                        () => _mapEditor
                            .SetSelectedTelegraphPoleRotation(
                                new Vector3(
                                    ParseFloat(
                                        _telegraphPoleRotationX,
                                        "telegraph pole pitch"),
                                    ParseFloat(
                                        _telegraphPoleRotationY,
                                        "telegraph pole heading"),
                                    ParseFloat(
                                        _telegraphPoleRotationZ,
                                        "telegraph pole roll"))));
                }
                if (!pole.IsCustom
                    && GUILayout.Button(
                        "Reset Saved Offset to Original Position",
                        GUILayout.Height(30f)))
                {
                    RunTelegraphPoleAction(
                        "Reset telegraph pole to its original position",
                        _mapEditor.ResetSelectedTelegraphPoleOffset);
                }
                GUILayout.Label(
                    pole.IsCustom
                        ? "Custom poles save position and rotation in "
                          + "tile-editor-telegraph-poles.json."
                        : "Original poles use TelegraphPoleMover offsets. "
                          + "Tile Editor stores their rotation in its "
                          + "portable pole override file.",
                    _mutedStyle);

                if (pole.IsCustom)
                {
                    GUILayout.Space(7f);
                    var deleteKey = pole.Id.ToString(
                        CultureInfo.InvariantCulture);
                    var confirming = string.Equals(
                        _poleDeleteConfirm,
                        deleteKey,
                        StringComparison.Ordinal);
                    var oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = confirming
                        ? new Color(0.88f, 0.25f, 0.20f)
                        : new Color(0.52f, 0.20f, 0.18f);
                    if (GUILayout.Button(
                            confirming
                                ? "Confirm Delete Pole " + pole.Id
                                : "Delete This Custom Pole",
                            GUILayout.Height(31f)))
                    {
                        if (!confirming)
                        {
                            _poleDeleteConfirm = deleteKey;
                        }
                        else
                        {
                            RunGameAction(
                                "Deleted custom telegraph pole",
                                _mapEditor
                                    .DeleteSelectedCustomTelegraphPole);
                            _poleDeleteConfirm = string.Empty;
                            _telegraphPoleSelectionId = -1;
                        }
                    }
                    GUI.backgroundColor = oldColor;
                }
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor.CanUndoTelegraphPole;
            if (GUILayout.Button("Pole Undo", GUILayout.Height(30f)))
            {
                RunTelegraphPoleAction(
                    "Undid telegraph pole movement",
                    _mapEditor.UndoTelegraphPole);
            }
            GUI.enabled = _mapEditor.CanRedoTelegraphPole;
            if (GUILayout.Button("Pole Redo", GUILayout.Height(30f)))
            {
                RunTelegraphPoleAction(
                    "Redid telegraph pole movement",
                    _mapEditor.RedoTelegraphPole);
            }
            GUI.enabled = _mapEditor.TelegraphPoleDirty;
            if (GUILayout.Button(
                    "Save Pole Edits"
                    + (_mapEditor.TelegraphPoleDirty ? " *" : ""),
                    GUILayout.Height(30f)))
            {
                SavePolesAndSyncDesktop();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void TelegraphPoleMoveButton(
            string label,
            Vector3 offset)
        {
            if (GUILayout.Button(
                    label,
                    _directionButtonStyle,
                    GUILayout.Height(48f)))
            {
                RunTelegraphPoleAction(
                    "Moved telegraph pole",
                    () => _mapEditor.MoveSelectedTelegraphPole(
                        offset,
                        _telegraphPoleLocalAxes));
            }
        }

        private void DrawWorldLocalAxesButtons(
            ref bool localAxes)
        {
            var oldColor = GUI.backgroundColor;
            if (!localAxes)
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    "WORLD",
                    GUILayout.Width(76f),
                    GUILayout.Height(27f)))
            {
                localAxes = false;
            }
            GUI.backgroundColor = localAxes
                ? new Color(0.18f, 0.72f, 0.82f)
                : oldColor;
            if (GUILayout.Button(
                    "LOCAL",
                    GUILayout.Width(76f),
                    GUILayout.Height(27f)))
            {
                localAxes = true;
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawPrimaryTelegraphPoleRotationControls()
        {
            GUILayout.Label(
                "ROTATE   Step "
                + _poleRotationStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " degrees",
                _titleStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton("0.1", 0.1f, ref _poleRotationStep);
            DrawQuickStepButton("1", 1f, ref _poleRotationStep);
            DrawQuickStepButton("5", 5f, ref _poleRotationStep);
            DrawQuickStepButton("15", 15f, ref _poleRotationStep);
            DrawQuickStepButton("45", 45f, ref _poleRotationStep);
            DrawQuickStepButton("90", 90f, ref _poleRotationStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            TelegraphPoleRotateButton(
                "\u21B6\nPITCH X-",
                new Vector3(-_poleRotationStep, 0f, 0f));
            TelegraphPoleRotateButton(
                "\u21B6\nHEADING Y-",
                new Vector3(0f, -_poleRotationStep, 0f));
            TelegraphPoleRotateButton(
                "\u21B6\nROLL Z-",
                new Vector3(0f, 0f, -_poleRotationStep));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TelegraphPoleRotateButton(
                "\u21B7\nPITCH X+",
                new Vector3(_poleRotationStep, 0f, 0f));
            TelegraphPoleRotateButton(
                "\u21B7\nHEADING Y+",
                new Vector3(0f, _poleRotationStep, 0f));
            TelegraphPoleRotateButton(
                "\u21B7\nROLL Z+",
                new Vector3(0f, 0f, _poleRotationStep));
            GUILayout.EndHorizontal();
        }

        private void TelegraphPoleRotateButton(
            string label,
            Vector3 offset)
        {
            if (GUILayout.Button(
                    label,
                    _directionButtonStyle,
                    GUILayout.Height(48f)))
            {
                RunTelegraphPoleAction(
                    "Rotated telegraph pole",
                    () => _mapEditor.RotateSelectedTelegraphPole(
                        offset));
            }
        }

        private void RunTelegraphPoleAction(
            string message,
            Action action)
        {
            RunGameAction(
                message,
                () =>
                {
                    action();
                    var selected = _mapEditor.SelectedTelegraphPole;
                    if (selected != null)
                        SyncTelegraphPoleFields(selected, true);
                });
        }

        private void SyncTelegraphPoleFields(
            TileEditorGraphSession.TelegraphPoleInfo pole,
            bool force)
        {
            if (pole == null)
                return;
            if (!force && _telegraphPoleSelectionId == pole.Id)
                return;
            var changed = _telegraphPoleSelectionId != pole.Id;
            _telegraphPoleSelectionId = pole.Id;
            if (changed)
            {
                _panelScroll = Vector2.zero;
                _showAdvancedTelegraphPoleControls = false;
                _poleDeleteConfirm = string.Empty;
            }
            _telegraphPolePositionX =
                FormatTransformValue(pole.Position.x);
            _telegraphPolePositionY =
                FormatTransformValue(pole.Position.y);
            _telegraphPolePositionZ =
                FormatTransformValue(pole.Position.z);
            _telegraphPoleRotationX =
                FormatTransformValue(pole.Rotation.x);
            _telegraphPoleRotationY =
                FormatTransformValue(pole.Rotation.y);
            _telegraphPoleRotationZ =
                FormatTransformValue(pole.Rotation.z);
        }

        private void DrawPrimarySceneryRotationControls()
        {
            GUILayout.Label(
                "ROTATE   Step "
                + _sceneryRotationStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " degrees",
                _titleStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton(
                "0.1",
                0.1f,
                ref _sceneryRotationStep);
            DrawQuickStepButton(
                "0.5",
                0.5f,
                ref _sceneryRotationStep);
            DrawQuickStepButton(
                "1",
                1f,
                ref _sceneryRotationStep);
            DrawQuickStepButton(
                "5",
                5f,
                ref _sceneryRotationStep);
            DrawQuickStepButton(
                "15",
                15f,
                ref _sceneryRotationStep);
            DrawQuickStepButton(
                "45",
                45f,
                ref _sceneryRotationStep);
            DrawQuickStepButton(
                "90",
                90f,
                ref _sceneryRotationStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            SceneryRotateButton(
                "\u21B6\nPITCH X-",
                new Vector3(
                    -_sceneryRotationStep,
                    0f,
                    0f));
            SceneryRotateButton(
                "\u21B6\nHEADING Y-",
                new Vector3(
                    0f,
                    -_sceneryRotationStep,
                    0f));
            SceneryRotateButton(
                "\u21B6\nROLL Z-",
                new Vector3(
                    0f,
                    0f,
                    -_sceneryRotationStep));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SceneryRotateButton(
                "\u21B7\nPITCH X+",
                new Vector3(
                    _sceneryRotationStep,
                    0f,
                    0f));
            SceneryRotateButton(
                "\u21B7\nHEADING Y+",
                new Vector3(
                    0f,
                    _sceneryRotationStep,
                    0f));
            SceneryRotateButton(
                "\u21B7\nROLL Z+",
                new Vector3(
                    0f,
                    0f,
                    _sceneryRotationStep));
            GUILayout.EndHorizontal();
        }

        private void DrawSceneryAssetPicker(bool showCreateButton)
        {
            GUILayout.Space(5f);
            GUILayout.Label(
                "ASSET LIBRARY",
                _titleStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(70f));
            _scenerySearch = GUILayout.TextField(
                _scenerySearch ?? string.Empty);
            if (GUILayout.Button(
                    "Refresh",
                    GUILayout.Width(78f)))
            {
                RunGameAction(
                    "Refreshed scenery asset library",
                    _mapEditor.RefreshSceneryAssetLibrary);
            }
            GUILayout.EndHorizontal();

            var normalizedSearch =
                (_scenerySearch ?? string.Empty).Trim();
            if (!string.Equals(
                    normalizedSearch,
                    _lastScenerySearch,
                    StringComparison.Ordinal))
            {
                _lastScenerySearch = normalizedSearch;
                _sceneryAssetPage = 0;
            }
            const int pageSize = 16;
            var matches = _mapEditor.SearchSceneryAssets(
                normalizedSearch,
                _sceneryAssetPage * pageSize,
                pageSize,
                out var totalMatches);
            var pageCount = Mathf.Max(
                1,
                Mathf.CeilToInt(totalMatches / (float)pageSize));
            if (_sceneryAssetPage >= pageCount)
            {
                _sceneryAssetPage = pageCount - 1;
                matches = _mapEditor.SearchSceneryAssets(
                    normalizedSearch,
                    _sceneryAssetPage * pageSize,
                    pageSize,
                    out totalMatches);
            }
            if (matches.Count == 0)
            {
                GUILayout.Label(
                    "No loaded scenery assets match that search.",
                    _mutedStyle);
            }
            else
            {
                GUILayout.Label(
                    "Showing "
                    + (_sceneryAssetPage * pageSize + 1)
                    + "-"
                    + (_sceneryAssetPage * pageSize
                       + matches.Count)
                    + " of " + totalMatches + " assets",
                    _mutedStyle);
            }
            for (var index = 0; index < matches.Count; index += 2)
            {
                GUILayout.BeginHorizontal();
                SceneryAssetButton(matches[index]);
                if (index + 1 < matches.Count)
                    SceneryAssetButton(matches[index + 1]);
                else
                    GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (pageCount > 1)
            {
                GUILayout.BeginHorizontal();
                var controlsEnabled = GUI.enabled;
                GUI.enabled =
                    controlsEnabled && _sceneryAssetPage > 0;
                if (GUILayout.Button(
                        "\u2190 Previous",
                        GUILayout.Height(28f)))
                {
                    _sceneryAssetPage--;
                }
                GUI.enabled = controlsEnabled;
                GUILayout.Label(
                    "Page " + (_sceneryAssetPage + 1)
                    + " / " + pageCount,
                    _titleStyle,
                    GUILayout.Width(105f));
                GUI.enabled =
                    controlsEnabled
                    && _sceneryAssetPage + 1 < pageCount;
                if (GUILayout.Button(
                        "Next \u2192",
                        GUILayout.Height(28f)))
                {
                    _sceneryAssetPage++;
                }
                GUI.enabled = controlsEnabled;
                GUILayout.EndHorizontal();
            }

            DrawTextField(
                "Model identifier",
                ref _sceneryModelIdentifier);
            if (showCreateButton)
            {
                GUI.enabled = !string.IsNullOrWhiteSpace(
                    _sceneryModelIdentifier);
                _repeatPointerPlacement = GUILayout.Toggle(
                    _repeatPointerPlacement,
                    " Keep placing this asset");
                if (GUILayout.Button(
                        "Place Model with Mouse Pointer",
                        GUILayout.Height(34f)))
                {
                    ArmPointerPlacement(
                        PointerPlacementKind.Scenery,
                        _sceneryModelIdentifier,
                        _repeatPointerPlacement);
                }
                if (GUILayout.Button(
                        "Place at Camera Target",
                        GUILayout.Height(29f)))
                {
                    RunSceneryAction(
                        "Placed scenery at camera target",
                        () =>
                        {
                            var id = _mapEditor.CreateSceneryAtCamera(
                                _sceneryModelIdentifier);
                            _lastPanelMessage = "Placed scenery " + id;
                        });
                }
                GUI.enabled = true;
                DrawPointerPlacementStatus();
            }
        }

        private void SceneryAssetButton(string identifier)
        {
            var oldColor = GUI.backgroundColor;
            if (string.Equals(
                    identifier,
                    _sceneryModelIdentifier,
                    StringComparison.Ordinal))
            {
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(
                    Shorten(identifier, 27),
                    GUILayout.Height(27f)))
            {
                _sceneryModelIdentifier = identifier;
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawAdvancedSceneryControls()
        {
            GUILayout.Space(7f);
            GUILayout.Label(
                "EXACT TRANSFORM",
                _titleStyle);
            DrawVectorFields(
                "Position",
                ref _sceneryPositionX,
                ref _sceneryPositionY,
                ref _sceneryPositionZ);
            DrawVectorFields(
                "Rotation",
                ref _sceneryRotationX,
                ref _sceneryRotationY,
                ref _sceneryRotationZ);
            DrawVectorFields(
                "Scale",
                ref _sceneryScaleX,
                ref _sceneryScaleY,
                ref _sceneryScaleZ);
            if (GUILayout.Button(
                    "Apply Exact Transform",
                    GUILayout.Height(31f)))
            {
                RunSceneryAction(
                    "Applied exact scenery transform",
                    () => _mapEditor.SetSelectedSceneryTransform(
                        new Vector3(
                            ParseFloat(
                                _sceneryPositionX,
                                "scenery position X"),
                            ParseFloat(
                                _sceneryPositionY,
                                "scenery position Y"),
                            ParseFloat(
                                _sceneryPositionZ,
                                "scenery position Z")),
                        new Vector3(
                            ParseFloat(
                                _sceneryRotationX,
                                "scenery rotation X"),
                            ParseFloat(
                                _sceneryRotationY,
                                "scenery rotation Y"),
                            ParseFloat(
                                _sceneryRotationZ,
                                "scenery rotation Z")),
                        new Vector3(
                            ParseFloat(
                                _sceneryScaleX,
                                "scenery scale X"),
                            ParseFloat(
                                _sceneryScaleY,
                                "scenery scale Y"),
                            ParseFloat(
                                _sceneryScaleZ,
                                "scenery scale Z"))));
            }

            GUILayout.Space(4f);
            GUILayout.Label(
                "All movement steps (m)",
                _mutedStyle);
            _sceneryMoveStep = DrawStepSelector(
                _sceneryMoveStep,
                new[]
                {
                    0.01f, 0.1f, 0.5f, 1f, 5f,
                    10f, 25f, 50f, 100f, 1000f,
                },
                5);
            GUILayout.Label(
                "All rotation steps (degrees)",
                _mutedStyle);
            _sceneryRotationStep = DrawStepSelector(
                _sceneryRotationStep,
                new[]
                {
                    0.01f, 0.1f, 0.5f, 1f, 5f, 10f,
                    15f, 30f, 45f, 60f, 90f, 180f,
                },
                6);

            DrawSceneryAssetPicker(false);
            GUI.enabled = !string.IsNullOrWhiteSpace(
                _sceneryModelIdentifier);
            if (GUILayout.Button(
                    "Change Selected Model",
                    GUILayout.Height(31f)))
            {
                RunSceneryAction(
                    "Changed scenery model",
                    () => _mapEditor.SetSelectedSceneryModel(
                        _sceneryModelIdentifier));
            }
            GUI.enabled = true;
        }

        private void DrawSceneryDelete()
        {
            var scenery = _mapEditor.SelectedScenery;
            if (scenery == null)
                return;
            GUILayout.Space(5f);
            if (_sceneryDeleteConfirm == scenery.Id)
            {
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor =
                    new Color(0.85f, 0.28f, 0.20f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        "CONFIRM DELETE " + scenery.Id,
                        GUILayout.Height(31f)))
                {
                    RunSceneryAction(
                        "Deleted scenery " + scenery.Id,
                        _mapEditor.DeleteSelectedScenery);
                    _sceneryDeleteConfirm = string.Empty;
                }
                GUI.backgroundColor = oldColor;
                if (GUILayout.Button(
                        "Cancel",
                        GUILayout.Width(82f),
                        GUILayout.Height(31f)))
                {
                    _sceneryDeleteConfirm = string.Empty;
                }
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button(
                         "Delete Selected Scenery",
                         GUILayout.Height(29f)))
            {
                _sceneryDeleteConfirm = scenery.Id;
            }
        }

        private void SceneryMoveButton(
            string label,
            Vector3 offset)
        {
            if (GUILayout.Button(
                    label,
                    _directionButtonStyle,
                    GUILayout.Height(48f)))
            {
                RunSceneryAction(
                    "Moved scenery",
                    () => _mapEditor.MoveSelectedScenery(
                        offset,
                        _sceneryLocalAxes));
            }
        }

        private void SceneryRotateButton(
            string label,
            Vector3 offset)
        {
            if (GUILayout.Button(
                    label,
                    _directionButtonStyle,
                    GUILayout.Height(48f)))
            {
                RunSceneryAction(
                    "Rotated scenery",
                    () => _mapEditor.RotateSelectedScenery(offset));
            }
        }

        private void RunSceneryAction(
            string message,
            Action action)
        {
            RunGameAction(
                message,
                () =>
                {
                    action();
                    var selected = _mapEditor.SelectedScenery;
                    if (selected != null)
                        SyncSceneryFields(selected, true);
                });
        }

        private void SyncSceneryFields(
            TileEditorGraphSession.SceneryInfo scenery,
            bool force)
        {
            var key = scenery?.Id ?? string.Empty;
            if (!force
                && string.Equals(
                    key,
                    _scenerySelectionKey,
                    StringComparison.Ordinal))
            {
                return;
            }
            var selectionChanged = !string.Equals(
                key,
                _scenerySelectionKey,
                StringComparison.Ordinal);
            _scenerySelectionKey = key;
            if (scenery == null)
                return;
            if (selectionChanged)
            {
                _panelScroll = Vector2.zero;
                _showAdvancedSceneryControls = false;
                _sceneryDeleteConfirm = string.Empty;
            }
            _sceneryModelIdentifier =
                scenery.ModelIdentifier ?? string.Empty;
            _sceneryPositionX =
                FormatTransformValue(scenery.Position.x);
            _sceneryPositionY =
                FormatTransformValue(scenery.Position.y);
            _sceneryPositionZ =
                FormatTransformValue(scenery.Position.z);
            _sceneryRotationX =
                FormatTransformValue(scenery.Rotation.x);
            _sceneryRotationY =
                FormatTransformValue(scenery.Rotation.y);
            _sceneryRotationZ =
                FormatTransformValue(scenery.Rotation.z);
            _sceneryScaleX =
                FormatTransformValue(scenery.Scale.x);
            _sceneryScaleY =
                FormatTransformValue(scenery.Scale.y);
            _sceneryScaleZ =
                FormatTransformValue(scenery.Scale.z);
        }

        private static string FormatSceneryVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:0.##}, {1:0.##}, {2:0.##})",
                value.x,
                value.y,
                value.z);
        }
    }
}
