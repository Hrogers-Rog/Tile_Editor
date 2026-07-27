using System;
using System.Globalization;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private void OpenNodeEditor()
        {
            if (!_nodeEditorVisible
                && !PlayerPrefs.HasKey(NodeWindowXKey))
            {
                _nodeWindowRect.x = Mathf.Clamp(
                    _windowRect.xMax + 12f,
                    0f,
                    Mathf.Max(
                        0f,
                        Screen.width - MinNodeWindowWidth));
                _nodeWindowRect.y = Mathf.Clamp(
                    _windowRect.y,
                    0f,
                    Mathf.Max(
                        0f,
                        Screen.height - MinNodeWindowHeight));
            }
            _nodeEditorVisible = true;
        }

        private void DrawNodeEditorWindow(int id)
        {
            DrawNodeWindowBackdrop();
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("NODE EDITOR", _onlineStyle);
            GUILayout.FlexibleSpace();
            var selected = _mapEditor?.SelectedNode;
            if (selected != null)
            {
                GUILayout.Label(
                    Shorten(selected.Id, 32),
                    _titleStyle);
            }
            if (GUILayout.Button(
                    "X",
                    GUILayout.Width(28f),
                    GUILayout.Height(22f)))
            {
                _nodeEditorVisible = false;
                SaveNodeWindowGeometry();
            }
            GUILayout.EndHorizontal();

            _nodeWindowScroll = GUILayout.BeginScrollView(
                _nodeWindowScroll);
            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Space(8f);
                GUILayout.Label(
                    "Railroader's live track graph is not ready yet.",
                    _titleStyle);
                GUILayout.Label(
                    "Load into a map and choose a graph from the main "
                    + "Tile Editor panel.",
                    _mutedStyle);
            }
            else if (!_mapEditor.GraphOpen)
            {
                GUILayout.Space(8f);
                GUILayout.Label("Choose a graph first", _titleStyle);
                GUILayout.Label(
                    "The Node Editor uses the graph selected in the main "
                    + "Tile Editor window.",
                    _mutedStyle);
            }
            else
            {
                DrawNodeNamingControls();
                GUILayout.Space(6f);
                selected = _mapEditor.SelectedNode;
                if (selected == null)
                {
                    _transformNodeId = string.Empty;
                    DrawEmptyNodeEditor();
                }
                else
                {
                    SyncNodeTransformFields(selected, false);
                    DrawNodeSelectionSummary(selected);
                    GUILayout.Space(6f);
                    DrawCompactNodeEditor(selected);
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Label(
                Shorten(_lastPanelMessage, 76),
                _mutedStyle);
            GUI.DragWindow(
                new Rect(
                    0f,
                    0f,
                    _nodeWindowRect.width - 42f,
                    28f));
            GUILayout.EndVertical();
            DrawNodeResizeHandle();
        }

        private void DrawEmptyNodeEditor()
        {
            GUILayout.Label("NO NODE SELECTED", _offlineStyle);
            GUILayout.Label(
                "Click a cyan node in the world, or place a new free node "
                + "at the mouse pointer and build from it.",
                _lineStyle);
            _repeatPointerPlacement = GUILayout.Toggle(
                _repeatPointerPlacement,
                " Keep placing / build a continuous chain");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "PLACE FREE NODE WITH MOUSE",
                    GUILayout.Height(32f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.FreeTrackNode,
                    string.Empty,
                    _repeatPointerPlacement);
            }
            if (GUILayout.Button(
                    "CAMERA TARGET",
                    GUILayout.Width(125f),
                    GUILayout.Height(32f)))
            {
                RunGameAction(
                    "Added a free node at the camera target",
                    _mapEditor.AddNodeAtCamera);
            }
            GUILayout.EndHorizontal();
            DrawPointerPlacementStatus();
        }

        private void DrawNodeSelectionSummary(
            TileEditorGraphSession.SelectionInfo node)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SELECTED NODE", _mutedStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(node.Id, _titleStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Position  "
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
                    CultureInfo.InvariantCulture),
                _lineStyle);
            GUILayout.Label(
                "Pitch "
                + node.Rotation.x.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture)
                + "°   Heading "
                + node.Rotation.y.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture)
                + "°   Roll "
                + node.Rotation.z.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture)
                + "°",
                _mutedStyle);
            GUILayout.EndVertical();
        }

        private void DrawNodeWindowBackdrop()
        {
            var bounds = new Rect(
                0f,
                0f,
                _nodeWindowRect.width,
                _nodeWindowRect.height);
            GUI.DrawTexture(
                new Rect(
                    1f,
                    18f,
                    bounds.width - 2f,
                    bounds.height - 19f),
                _windowBackgroundTexture,
                ScaleMode.StretchToFill,
                true);
            const float border = 2f;
            GUI.DrawTexture(
                new Rect(0f, 0f, bounds.width, border),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(
                    0f,
                    bounds.height - border,
                    bounds.width,
                    border),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(0f, 0f, border, bounds.height),
                _windowBorderTexture);
            GUI.DrawTexture(
                new Rect(
                    bounds.width - border,
                    0f,
                    border,
                    bounds.height),
                _windowBorderTexture);
        }

        private void DrawNodeResizeHandle()
        {
            var handle = new Rect(
                _nodeWindowRect.width - 27f,
                _nodeWindowRect.height - 27f,
                23f,
                23f);
            var controlId = GUIUtility.GetControlID(
                0x54454F,
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
                        _resizingNodeWindow = true;
                        _nodeResizeStartMouse =
                            GUIUtility.GUIToScreenPoint(
                                currentEvent.mousePosition);
                        _nodeResizeStartSize = new Vector2(
                            _nodeWindowRect.width,
                            _nodeWindowRect.height);
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_resizingNodeWindow
                        && GUIUtility.hotControl == controlId)
                    {
                        var mouse = GUIUtility.GUIToScreenPoint(
                            currentEvent.mousePosition);
                        var delta = mouse - _nodeResizeStartMouse;
                        _nodeWindowRect.width = Mathf.Clamp(
                            _nodeResizeStartSize.x + delta.x,
                            MinNodeWindowWidth,
                            Mathf.Max(
                                MinNodeWindowWidth,
                                Screen.width - _nodeWindowRect.x - 4f));
                        _nodeWindowRect.height = Mathf.Clamp(
                            _nodeResizeStartSize.y + delta.y,
                            MinNodeWindowHeight,
                            Mathf.Max(
                                MinNodeWindowHeight,
                                Screen.height - _nodeWindowRect.y - 4f));
                        currentEvent.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_resizingNodeWindow
                        && GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        _resizingNodeWindow = false;
                        SaveNodeWindowGeometry();
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

        private void SaveNodeWindowGeometry()
        {
            PlayerPrefs.SetFloat(NodeWindowXKey, _nodeWindowRect.x);
            PlayerPrefs.SetFloat(NodeWindowYKey, _nodeWindowRect.y);
            PlayerPrefs.SetFloat(
                NodeWindowWidthKey,
                _nodeWindowRect.width);
            PlayerPrefs.SetFloat(
                NodeWindowHeightKey,
                _nodeWindowRect.height);
            PlayerPrefs.Save();
        }
    }
}
