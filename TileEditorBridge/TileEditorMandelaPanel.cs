using System;
using System.Globalization;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private float _mandelaMoveStep = 1f;
        private float _mandelaRotationStep = 5f;
        private bool _mandelaLocalAxes;
        private bool _showAdvancedMandelaControls;
        private string _mandelaPositionX = "0";
        private string _mandelaPositionY = "0";
        private string _mandelaPositionZ = "0";
        private string _mandelaRotationX = "0";
        private string _mandelaRotationY = "0";
        private string _mandelaRotationZ = "0";
        private string _mandelaScaleX = "1";
        private string _mandelaScaleY = "1";
        private string _mandelaScaleZ = "1";
        private string _mandelaSelectionKey = string.Empty;
        private string _mandelaSearch = string.Empty;
        private string _lastMandelaSearch = string.Empty;
        private int _mandelaPage;

        private void DrawMandelaPanel()
        {
            GUILayout.Space(4f);
            GUILayout.Label("BASE-GAME OBJECTS", _titleStyle);
            GUILayout.Label(
                "Click a building, prop, or other base-game object in the "
                + "world. Native projects save a FUSE scene clone; legacy "
                + "projects save a RailLoader mandela.",
                _lineStyle);

            if (_mapEditor == null || !_mapEditor.GraphOpen)
                return;

            DrawPointerPlacementStatus();
            var selected = _mapEditor.SelectedMandela;
            if (selected == null)
            {
                _mandelaSelectionKey = string.Empty;
                GUILayout.Space(5f);
                GUILayout.Label(
                    "CLICK AN OBJECT IN THE WORLD TO SELECT IT",
                    _onlineStyle);
                GUILayout.Label(
                    "The closest individual asset is selected. Shared town, "
                    + "map, and world roots are blocked so one click cannot "
                    + "move the whole scene.",
                    _mutedStyle);
                DrawSavedMandelaOverrides();
                return;
            }

            SyncMandelaFields(selected, false);
            GUILayout.Space(5f);
            GUILayout.Label(
                ShortenPath(selected.TargetPath, 68),
                _titleStyle);
            GUILayout.Label(
                selected.IsClone
                    ? "CLONE FROM  " + ShortenPath(
                        selected.SourcePath,
                        62)
                    : "BASE-GAME OBJECT",
                _mutedStyle);
            GUILayout.Label(
                "Local "
                + FormatMandelaVector(selected.LocalPosition)
                + "   Heading "
                + selected.LocalRotation.y.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " deg",
                _lineStyle);
            GUILayout.Label(
                selected.SafetyMessage,
                selected.CloneSafe ? _mutedStyle : _offlineStyle);

            if (selected.IsBaseGameSign)
            {
                GUILayout.Space(4f);
                GUILayout.Label("BASE-GAME SIGN", _onlineStyle);
                if (GUILayout.Button(
                        selected.Active
                            ? "SIGN VISIBLE - TURN OFF"
                            : "SIGN HIDDEN - TURN ON",
                        GUILayout.Height(34f)))
                {
                    RunMandelaAction(
                        selected.Active
                            ? "Turned off base-game sign"
                            : "Turned on base-game sign",
                        () => _mapEditor.SetSelectedMandelaActive(
                            !selected.Active));
                }
                GUILayout.Label(
                    "This changes only Railroader's original scene sign and "
                    + "saves the enabled state as a map override. Loader "
                    + "scenery signs remain under SCENERY.",
                    _mutedStyle);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Show", GUILayout.Height(29f)))
                RunMandelaAction(
                    "Focused selected object",
                    _mapEditor.ShowSelectedMandela);
            if (GUILayout.Button(
                    "One Level Up",
                    GUILayout.Height(29f)))
                RunMandelaAction(
                    "Selected parent object",
                    _mapEditor.SelectMandelaParent);
            if (GUILayout.Button(
                    "Clicked Part",
                    GUILayout.Height(29f)))
                RunMandelaAction(
                    "Selected clicked object part",
                    _mapEditor.SelectMandelaClickedPart);
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "MOVE  Step "
                + _mandelaMoveStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + " m",
                _titleStyle);
            var oldColor = GUI.backgroundColor;
            if (!_mandelaLocalAxes)
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    "WORLD",
                    GUILayout.Width(76f),
                    GUILayout.Height(26f)))
                _mandelaLocalAxes = false;
            GUI.backgroundColor = _mandelaLocalAxes
                ? new Color(0.18f, 0.72f, 0.82f)
                : oldColor;
            if (GUILayout.Button(
                    "LOCAL",
                    GUILayout.Width(76f),
                    GUILayout.Height(26f)))
                _mandelaLocalAxes = true;
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawQuickStepButton(
                "0.1",
                0.1f,
                ref _mandelaMoveStep);
            DrawQuickStepButton(
                "0.5",
                0.5f,
                ref _mandelaMoveStep);
            DrawQuickStepButton(
                "1",
                1f,
                ref _mandelaMoveStep);
            DrawQuickStepButton(
                "5",
                5f,
                ref _mandelaMoveStep);
            DrawQuickStepButton(
                "10",
                10f,
                ref _mandelaMoveStep);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            MandelaMoveButton(
                "Y-\nLOWER",
                new Vector3(0f, -_mandelaMoveStep, 0f));
            MandelaMoveButton(
                "\u2191\nFORWARD Z+",
                new Vector3(0f, 0f, _mandelaMoveStep));
            MandelaMoveButton(
                "Y+\nRAISE",
                new Vector3(0f, _mandelaMoveStep, 0f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            MandelaMoveButton(
                "\u2190\nLEFT X-",
                new Vector3(-_mandelaMoveStep, 0f, 0f));
            MandelaMoveButton(
                "\u2193\nBACK Z-",
                new Vector3(0f, 0f, -_mandelaMoveStep));
            MandelaMoveButton(
                "\u2192\nRIGHT X+",
                new Vector3(_mandelaMoveStep, 0f, 0f));
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            DrawMandelaRotationControls();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUI.enabled = selected.CloneSafe;
            if (GUILayout.Button(
                    "Clone at Mouse...",
                    GUILayout.Height(31f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.MandelaClone,
                    selected.TargetPath,
                    false);
            }
            if (GUILayout.Button(
                    "Clone Beside",
                    GUILayout.Height(31f)))
            {
                RunMandelaAction(
                    "Cloned base-game object",
                    () =>
                    {
                        var path =
                            _mapEditor.CloneSelectedMandelaBeside();
                        _lastPanelMessage =
                            "Cloned object as "
                            + ShortenPath(path, 50);
                    });
            }
            GUI.enabled = true;
            if (GUILayout.Button(
                    selected.Active ? "Disable" : "Enable",
                    GUILayout.Height(31f)))
            {
                RunMandelaAction(
                    selected.Active
                        ? "Disabled base-game object"
                        : "Enabled base-game object",
                    () => _mapEditor.SetSelectedMandelaActive(
                        !selected.Active));
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Scale -0.1", GUILayout.Height(28f)))
                RunMandelaAction(
                    "Reduced object scale",
                    () => _mapEditor.ScaleSelectedMandela(
                        Vector3.one * -0.1f));
            if (GUILayout.Button("Scale +0.1", GUILayout.Height(28f)))
                RunMandelaAction(
                    "Increased object scale",
                    () => _mapEditor.ScaleSelectedMandela(
                        Vector3.one * 0.1f));
            if (GUILayout.Button(
                    _showAdvancedMandelaControls
                        ? "Less"
                        : "More...",
                    GUILayout.Height(28f)))
            {
                _showAdvancedMandelaControls =
                    !_showAdvancedMandelaControls;
            }
            GUILayout.EndHorizontal();

            if (_showAdvancedMandelaControls)
                DrawAdvancedMandelaControls();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "Select Another",
                    GUILayout.Height(29f)))
            {
                _mapEditor.ClearSelectedMandela();
                _mandelaSelectionKey = string.Empty;
            }
            GUI.enabled =
                _mapEditor.MandelaOverrideCount > 0;
            if (GUILayout.Button(
                    selected.IsClone
                        ? "Delete Clone"
                        : "Remove Saved Override",
                    GUILayout.Height(29f)))
            {
                RunMandelaAction(
                    selected.IsClone
                        ? "Deleted object clone"
                        : "Removed saved object override",
                    _mapEditor.RemoveSelectedMandelaOverride);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawSavedMandelaOverrides();
        }

        private void DrawMandelaRotationControls()
        {
            GUILayout.Label(
                "ROTATE  Step "
                + _mandelaRotationStep.ToString(
                    "0.##",
                    CultureInfo.InvariantCulture)
                + "\u00b0",
                _titleStyle);
            GUILayout.BeginHorizontal();
            DrawQuickStepButton(
                "0.1",
                0.1f,
                ref _mandelaRotationStep);
            DrawQuickStepButton(
                "1",
                1f,
                ref _mandelaRotationStep);
            DrawQuickStepButton(
                "5",
                5f,
                ref _mandelaRotationStep);
            DrawQuickStepButton(
                "15",
                15f,
                ref _mandelaRotationStep);
            DrawQuickStepButton(
                "45",
                45f,
                ref _mandelaRotationStep);
            DrawQuickStepButton(
                "90",
                90f,
                ref _mandelaRotationStep);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            MandelaRotateButton(
                "\u21ba\nPITCH X-",
                new Vector3(-_mandelaRotationStep, 0f, 0f));
            MandelaRotateButton(
                "\u21ba\nHEADING Y-",
                new Vector3(0f, -_mandelaRotationStep, 0f));
            MandelaRotateButton(
                "\u21ba\nROLL Z-",
                new Vector3(0f, 0f, -_mandelaRotationStep));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            MandelaRotateButton(
                "\u21bb\nPITCH X+",
                new Vector3(_mandelaRotationStep, 0f, 0f));
            MandelaRotateButton(
                "\u21bb\nHEADING Y+",
                new Vector3(0f, _mandelaRotationStep, 0f));
            MandelaRotateButton(
                "\u21bb\nROLL Z+",
                new Vector3(0f, 0f, _mandelaRotationStep));
            GUILayout.EndHorizontal();
        }

        private void DrawAdvancedMandelaControls()
        {
            var selected = _mapEditor.SelectedMandela;
            if (selected == null)
                return;
            SyncMandelaFields(selected, false);
            GUILayout.Space(5f);
            GUILayout.Label(
                "ADVANCED - PARENT-RELATIVE TRANSFORM",
                _titleStyle);
            DrawTextField("Local X", ref _mandelaPositionX);
            DrawTextField("Local Y", ref _mandelaPositionY);
            DrawTextField("Local Z", ref _mandelaPositionZ);
            DrawTextField("Pitch X", ref _mandelaRotationX);
            DrawTextField("Heading Y", ref _mandelaRotationY);
            DrawTextField("Roll Z", ref _mandelaRotationZ);
            DrawTextField("Scale X", ref _mandelaScaleX);
            DrawTextField("Scale Y", ref _mandelaScaleY);
            DrawTextField("Scale Z", ref _mandelaScaleZ);
            if (GUILayout.Button(
                    "Apply Exact Local Transform",
                    GUILayout.Height(30f)))
            {
                RunMandelaAction(
                    "Applied exact object transform",
                    () => _mapEditor.SetSelectedMandelaTransform(
                        new Vector3(
                            ParseFloat(
                                _mandelaPositionX,
                                "object local X"),
                            ParseFloat(
                                _mandelaPositionY,
                                "object local Y"),
                            ParseFloat(
                                _mandelaPositionZ,
                                "object local Z")),
                        new Vector3(
                            ParseFloat(
                                _mandelaRotationX,
                                "object pitch"),
                            ParseFloat(
                                _mandelaRotationY,
                                "object heading"),
                            ParseFloat(
                                _mandelaRotationZ,
                                "object roll")),
                        new Vector3(
                            ParseFloat(
                                _mandelaScaleX,
                                "object scale X"),
                            ParseFloat(
                                _mandelaScaleY,
                                "object scale Y"),
                            ParseFloat(
                                _mandelaScaleZ,
                                "object scale Z"))));
            }
        }

        private void DrawSavedMandelaOverrides()
        {
            GUILayout.Space(8f);
            GUILayout.Label(
                "SAVED OBJECT OVERRIDES ("
                + _mapEditor.MandelaOverrideCount
                + ")",
                _titleStyle);
            _mandelaSearch = GUILayout.TextField(
                _mandelaSearch ?? string.Empty);
            if (!string.Equals(
                    _mandelaSearch,
                    _lastMandelaSearch,
                    StringComparison.Ordinal))
            {
                _lastMandelaSearch = _mandelaSearch;
                _mandelaPage = 0;
            }
            const int pageSize = 8;
            var entries = _mapEditor.SearchMandelaOverrides(
                _mandelaSearch,
                _mandelaPage * pageSize,
                pageSize,
                out var total);
            var maxPage = Mathf.Max(
                0,
                (total - 1) / pageSize);
            _mandelaPage = Mathf.Clamp(
                _mandelaPage,
                0,
                maxPage);
            foreach (var targetPath in entries)
            {
                if (GUILayout.Button(
                        ShortenPath(targetPath, 66),
                        GUILayout.Height(25f)))
                {
                    RunMandelaAction(
                        "Selected saved object override",
                        () => _mapEditor.SelectMandelaOverride(
                            targetPath));
                }
            }
            if (total > pageSize)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = _mandelaPage > 0;
                if (GUILayout.Button("Previous"))
                    _mandelaPage--;
                GUI.enabled = true;
                GUILayout.Label(
                    "Page " + (_mandelaPage + 1)
                    + " of " + (maxPage + 1),
                    _mutedStyle);
                GUI.enabled = _mandelaPage < maxPage;
                if (GUILayout.Button("Next"))
                    _mandelaPage++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void MandelaMoveButton(
            string label,
            Vector3 offset)
        {
            if (GUILayout.Button(label, GUILayout.Height(50f)))
            {
                RunMandelaAction(
                    "Moved base-game object",
                    () => _mapEditor.MoveSelectedMandela(
                        offset,
                        _mandelaLocalAxes));
            }
        }

        private void MandelaRotateButton(
            string label,
            Vector3 offset)
        {
            if (GUILayout.Button(label, GUILayout.Height(43f)))
            {
                RunMandelaAction(
                    "Rotated base-game object",
                    () => _mapEditor.RotateSelectedMandela(offset));
            }
        }

        private void RunMandelaAction(
            string successMessage,
            Action action)
        {
            RunGameAction(
                successMessage,
                () =>
                {
                    action();
                    var selected = _mapEditor.SelectedMandela;
                    if (selected != null)
                        SyncMandelaFields(selected, true);
                });
        }

        private void SyncMandelaFields(
            TileEditorGraphSession.MandelaInfo selected,
            bool force)
        {
            if (selected == null)
                return;
            if (!force
                && string.Equals(
                    _mandelaSelectionKey,
                    selected.TargetPath,
                    StringComparison.Ordinal))
            {
                return;
            }
            _mandelaSelectionKey = selected.TargetPath;
            _mandelaPositionX = FormatMandelaFloat(
                selected.LocalPosition.x);
            _mandelaPositionY = FormatMandelaFloat(
                selected.LocalPosition.y);
            _mandelaPositionZ = FormatMandelaFloat(
                selected.LocalPosition.z);
            _mandelaRotationX = FormatMandelaFloat(
                selected.LocalRotation.x);
            _mandelaRotationY = FormatMandelaFloat(
                selected.LocalRotation.y);
            _mandelaRotationZ = FormatMandelaFloat(
                selected.LocalRotation.z);
            _mandelaScaleX = FormatMandelaFloat(
                selected.LocalScale.x);
            _mandelaScaleY = FormatMandelaFloat(
                selected.LocalScale.y);
            _mandelaScaleZ = FormatMandelaFloat(
                selected.LocalScale.z);
        }

        private static string FormatMandelaFloat(float value)
        {
            return value.ToString(
                "0.###",
                CultureInfo.InvariantCulture);
        }

        private static string FormatMandelaVector(Vector3 value)
        {
            return value.x.ToString(
                       "0.##",
                       CultureInfo.InvariantCulture)
                   + ", "
                   + value.y.ToString(
                       "0.##",
                       CultureInfo.InvariantCulture)
                   + ", "
                   + value.z.ToString(
                       "0.##",
                       CultureInfo.InvariantCulture);
        }

        private static string ShortenPath(string value, int maximum)
        {
            value = value ?? string.Empty;
            if (value.Length <= maximum)
                return value;
            return "\u2026"
                   + value.Substring(
                       value.Length - maximum + 1);
        }
    }
}
