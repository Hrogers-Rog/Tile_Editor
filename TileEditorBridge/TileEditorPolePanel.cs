using System;
using System.Globalization;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private string _poleConnectionDistance = "150";
        private int _poleWireStartId = -1;
        private string _poleDeleteConfirm = string.Empty;
        private float _poleRotationStep = 5f;

        private void DrawPolePanel()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor != null
                          && _mapEditor.GraphOpen;
            if (GUILayout.Button(
                    "Refresh Pole Markers",
                    GUILayout.Height(31f)))
            {
                RunGameAction(
                    "Refreshed telegraph pole markers",
                    _mapEditor.RefreshTelegraphPoleMode);
            }
            GUI.enabled = _mapEditor != null
                          && _mapEditor.TelegraphPoleDirty;
            if (GUILayout.Button(
                    "Save Poles"
                    + (_mapEditor != null
                       && _mapEditor.TelegraphPoleDirty
                        ? " *"
                        : ""),
                    GUILayout.Height(31f)))
            {
                SavePolesAndSyncDesktop();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Space(7f);
                GUILayout.Label(
                    "Railroader's telegraph graph is not ready yet.",
                    _titleStyle);
                return;
            }
            if (!_mapEditor.GraphOpen)
            {
                return;
            }

            var pole = _mapEditor.SelectedTelegraphPole;
            if (pole == null)
            {
                _telegraphPoleSelectionId = -1;
                _poleDeleteConfirm = string.Empty;
                DrawPolePlacement();
                return;
            }

            GUILayout.Space(5f);
            GUILayout.Label("LAY POLE LINE", _titleStyle);
            GUILayout.Label(
                "Arm placement, then click the exact ground position. "
                + "The new pole connects to selected pole "
                + pole.Id + " and becomes the next selection.",
                _mutedStyle);
            _repeatPointerPlacement = GUILayout.Toggle(
                _repeatPointerPlacement,
                " Keep laying a continuous pole line");
            if (GUILayout.Button(
                    "Place Connected Pole with Mouse",
                    GUILayout.Height(34f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.ConnectedPole,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            DrawPointerPlacementStatus();

            DrawPoleWireTools(pole);
            DrawTelegraphPoleEditor(pole);
        }

        private void DrawPolePlacement()
        {
            GUILayout.Space(7f);
            GUILayout.Label("ADD TELEGRAPH POLES", _titleStyle);
            GUILayout.Label(
                _mapEditor.LiveTelegraphPoleCount
                + " live poles found; "
                + _mapEditor.CustomTelegraphPoleCount
                + " were created by the Tile Editor. Click an amber "
                + "marker to edit or continue its wire line.",
                _lineStyle);
            GUILayout.Space(5f);
            GUILayout.Label(
                "With no pole selected, Add connects to the nearest pole "
                + "inside this distance:",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Connect within (m)",
                GUILayout.Width(125f));
            _poleConnectionDistance = GUILayout.TextField(
                _poleConnectionDistance);
            GUILayout.EndHorizontal();
            if (GUILayout.Button(
                    "Place Pole with Mouse Pointer",
                    GUILayout.Height(35f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.ConnectedPole,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            if (GUILayout.Button(
                    "Place Standalone with Mouse",
                    GUILayout.Height(30f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.StandalonePole,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            DrawPointerPlacementStatus();
            GUILayout.Label(
                "After the first pole is placed, keep aiming and pressing "
                + "Add Connected Pole to lay a continuous line.",
                _mutedStyle);
        }

        private void CreatePoleFromPanel(bool standalone)
        {
            RunGameAction(
                standalone
                    ? "Added standalone telegraph pole"
                    : "Added connected telegraph pole",
                () =>
                {
                    var result =
                        _mapEditor.CreateTelegraphPoleAtCamera(
                            standalone,
                            ParseFloat(
                                _poleConnectionDistance,
                                "pole connection distance"));
                    _lastPanelMessage = result;
                    var selected = _mapEditor.SelectedTelegraphPole;
                    if (selected != null)
                        SyncTelegraphPoleFields(selected, true);
                });
        }

        private void DrawPoleWireTools(
            TileEditorGraphSession.TelegraphPoleInfo pole)
        {
            GUILayout.Space(6f);
            GUILayout.Label("WIRES", _titleStyle);
            GUILayout.BeginHorizontal();
            if (_poleWireStartId < 0)
            {
                if (GUILayout.Button(
                        "Set Wire Start",
                        GUILayout.Height(29f)))
                {
                    _poleWireStartId = pole.Id;
                    _lastPanelMessage =
                        "Wire start set to pole " + pole.Id
                        + "; click another pole";
                }
            }
            else
            {
                GUI.enabled = _poleWireStartId != pole.Id;
                if (GUILayout.Button(
                        "Connect "
                        + _poleWireStartId
                        + " to "
                        + pole.Id,
                        GUILayout.Height(29f)))
                {
                    var start = _poleWireStartId;
                    RunGameAction(
                        "Connected telegraph poles",
                        () => _mapEditor.ConnectTelegraphPoles(
                            start,
                            pole.Id));
                    _poleWireStartId = -1;
                }
                GUI.enabled = true;
                if (GUILayout.Button(
                        "Cancel",
                        GUILayout.Width(80f),
                        GUILayout.Height(29f)))
                {
                    _poleWireStartId = -1;
                }
            }
            GUILayout.EndHorizontal();

            if (_poleWireStartId >= 0
                && _poleWireStartId == pole.Id)
            {
                GUILayout.Label(
                    "Start is pole " + pole.Id
                    + ". Click another amber marker, then connect.",
                    _mutedStyle);
            }

            foreach (var connectedId in pole.ConnectedPoleIds)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    "Wire to pole " + connectedId,
                    _lineStyle);
                GUILayout.FlexibleSpace();
                var removable =
                    _mapEditor.IsCustomTelegraphWire(
                        pole.Id,
                        connectedId);
                GUI.enabled = removable;
                if (GUILayout.Button(
                        removable ? "Disconnect" : "Original",
                        GUILayout.Width(94f),
                        GUILayout.Height(25f)))
                {
                    var other = connectedId;
                    RunGameAction(
                        "Disconnected telegraph wire",
                        () => _mapEditor.DisconnectTelegraphPoles(
                            pole.Id,
                            other));
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }
    }
}
