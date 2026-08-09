using System;
using System.Globalization;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private enum PointerPlacementKind
        {
            None,
            FreeTrackNode,
            ConnectedTrackNode,
            SegmentControlNode,
            Scenery,
            TrainSignal,
            MandelaClone,
            ConnectedPole,
            StandalonePole,
            OperationsTown,
            OperationsIndustry,
            OperationsPhysicalLoader,
            OperationsStationAgent,
            OperationsTurntable,
        }

        private readonly RaycastHit[] _pointerRaycastHits =
            new RaycastHit[64];
        private PointerPlacementKind _pointerPlacementKind;
        private PanelTab _pointerPlacementTab;
        private string _pointerPlacementPayload = string.Empty;
        private bool _repeatPointerPlacement;
        private bool _hasPointerSurface;
        private RaycastHit _pointerSurfaceHit;
        private TileEditorPointerMarker _pointerMarker;

        private void UpdateWorldPointerTools()
        {
            if (!_visible || !_runtimeEnabled)
            {
                HideWorldPointerMarker();
                return;
            }
            if (_panelTab == PanelTab.Terrain)
            {
                UpdateTerrainPointerEditing();
                return;
            }
            if (_panelTab == PanelTab.Objects
                && _pointerPlacementKind
                   == PointerPlacementKind.None)
            {
                UpdateMandelaObjectPicking();
                return;
            }
            if (_mapEditor != null && _mapEditor.NodeDragActive)
            {
                HideWorldPointerMarker();
                return;
            }
            if (DesktopGraphHasUnsavedChanges)
            {
                CancelPointerPlacement(false);
                HideWorldPointerMarker();
                return;
            }
            if (_pointerPlacementKind == PointerPlacementKind.None)
            {
                HideWorldPointerMarker();
                return;
            }
            if (_panelTab != _pointerPlacementTab
                || Input.GetKeyDown(KeyCode.Escape)
                || (Input.GetMouseButtonDown(1)
                    && !IsPointerOverEditorWindow()))
            {
                CancelPointerPlacement();
                return;
            }

            _hasPointerSurface = TryGetPointerSurfaceHit(
                false,
                out _pointerSurfaceHit);
            if (_hasPointerSurface && !IsPointerOverEditorWindow())
            {
                var markerRadius = 1.75f;
                var markerColor =
                    new Color(0.10f, 0.95f, 1f, 1f);
                if (_pointerPlacementKind
                    == PointerPlacementKind.OperationsTurntable)
                {
                    if (float.TryParse(
                            _opsTurntableRadius,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var turntableRadius))
                    {
                        markerRadius = Mathf.Clamp(
                            turntableRadius,
                            2f,
                            100f);
                    }
                    markerColor =
                        new Color(0.95f, 0.28f, 1f, 1f);
                }
                ShowWorldPointerMarker(
                    _pointerSurfaceHit.point,
                    _pointerSurfaceHit.normal,
                    markerRadius,
                    markerColor);
            }
            else
            {
                HideWorldPointerMarker();
            }

            if (!_hasPointerSurface
                || IsPointerOverEditorWindow()
                || !Input.GetMouseButtonDown(0))
            {
                return;
            }
            ExecutePointerPlacement(_pointerSurfaceHit.point);
        }

        private void ArmPointerPlacement(
            PointerPlacementKind kind,
            string payload,
            bool repeat)
        {
            if (_mapEditor == null || !_mapEditor.GraphOpen)
            {
                _lastPanelMessage =
                    "Open an output graph before placing in the world";
                return;
            }
            _pointerPlacementKind = kind;
            _pointerPlacementTab = _panelTab;
            _pointerPlacementPayload = payload ?? string.Empty;
            _repeatPointerPlacement = repeat;
            _lastPanelMessage =
                "Pointer placement armed - click the world; Esc/right-click cancels";
        }

        private void ExecutePointerPlacement(Vector3 worldPosition)
        {
            try
            {
                var yaw = Camera.main == null
                    ? 0f
                    : Camera.main.transform.eulerAngles.y;
                string result;
                switch (_pointerPlacementKind)
                {
                    case PointerPlacementKind.FreeTrackNode:
                        result = _mapEditor.AddNodeAtPosition(
                            WorldTransformer.WorldToGame(worldPosition)
                            + Vector3.up * 0.2f,
                            yaw,
                            false);
                        break;
                    case PointerPlacementKind.ConnectedTrackNode:
                        result = _mapEditor.AddNodeAtPosition(
                            WorldTransformer.WorldToGame(worldPosition)
                            + Vector3.up * 0.2f,
                            yaw,
                            true);
                        break;
                    case PointerPlacementKind.SegmentControlNode:
                        result = _mapEditor.InjectSelectedSegmentAtPosition(
                            WorldTransformer.WorldToGame(worldPosition));
                        break;
                    case PointerPlacementKind.Scenery:
                        var sceneryId =
                            _mapEditor.CreateSceneryAtPosition(
                                _pointerPlacementPayload,
                                WorldTransformer.WorldToGame(
                                    worldPosition),
                                yaw);
                        result = "Placed scenery " + sceneryId;
                        break;
                    case PointerPlacementKind.TrainSignal:
                        result = _mapEditor.CreateTrainSignalAtPosition(
                            _trainSignalId,
                            WorldTransformer.WorldToGame(worldPosition),
                            yaw,
                            _trainSignalHeadCount,
                            _trainSignalAspect,
                            _trainSignalInterlockingId,
                            _trainSignalProtectedNodeId,
                            _trainSignalProtectedSegmentId,
                            _trainSignalDirection,
                            _trainSignalSnapOnPlace,
                            _trainSignalLockOnPlace,
                            ParseFloat(
                                _trainSignalSnapSideOffset,
                                "signal side offset"),
                            ParseFloat(
                                _trainSignalSnapVerticalOffset,
                                "signal vertical offset"),
                            _trainSignalSnapRight);
                        break;
                    case PointerPlacementKind.MandelaClone:
                        var targetPath =
                            _mapEditor.CloneSelectedMandelaAtWorldPosition(
                                worldPosition);
                        result = "Cloned object as " + targetPath;
                        break;
                    case PointerPlacementKind.ConnectedPole:
                    case PointerPlacementKind.StandalonePole:
                        result =
                            _mapEditor.CreateTelegraphPoleAtPosition(
                                worldPosition,
                                _pointerPlacementKind
                                == PointerPlacementKind.StandalonePole,
                                ParseFloat(
                                    _poleConnectionDistance,
                                    "pole connection distance"));
                        break;
                    case PointerPlacementKind.OperationsTown:
                        result = _mapEditor.CreateTown(
                            _opsTownId,
                            _opsTownName,
                            WorldTransformer.WorldToGame(
                                worldPosition),
                            ParseFloat(
                                _opsTownRadius,
                                "town radius"));
                        break;
                    case PointerPlacementKind.OperationsIndustry:
                        result = _mapEditor.CreateIndustry(
                            _opsIndustryId,
                            _opsIndustryName,
                            _opsAreaId,
                            WorldTransformer.WorldToGame(
                                worldPosition),
                            _opsUsesContract);
                        break;
                    case PointerPlacementKind.OperationsPhysicalLoader:
                        result = _mapEditor.CreatePhysicalLoader(
                            _opsPhysicalLoaderId,
                            _opsPhysicalLoaderPrefab,
                            _opsIndustryId,
                            WorldTransformer.WorldToGame(
                                worldPosition),
                            yaw);
                        break;
                    case PointerPlacementKind.OperationsStationAgent:
                        result = _mapEditor.CreateStationAgent(
                            _opsStationAgentId,
                            _opsStationAgentPrefab,
                            _opsIndustryId,
                            WorldTransformer.WorldToGame(
                                worldPosition),
                            yaw);
                        break;
                    case PointerPlacementKind.OperationsTurntable:
                        result = _mapEditor.CreateTurntable(
                            _opsTurntableId,
                            WorldTransformer.WorldToGame(
                                worldPosition),
                            yaw,
                            ParseFloat(
                                _opsTurntableRadius,
                                "turntable radius"),
                            ParseInt(
                                _opsTurntableSubdivisions,
                                "turntable subdivisions"),
                            ParseInt(
                                _opsTurntableStalls,
                                "roundhouse stalls"),
                            ParseFloat(
                                _opsTurntableStartAngle,
                                "roundhouse start angle"),
                            ParseFloat(
                                _opsTurntableStallAngle,
                                "roundhouse stall angle"),
                            ParseFloat(
                                _opsTurntableTrackLength,
                                "roundhouse track length"),
                            ParseFloat(
                                _opsTurntableGauge,
                                "bridge-track gauge"));
                        break;
                    default:
                        return;
                }
                _lastPanelMessage = result;
                if (!_repeatPointerPlacement)
                    CancelPointerPlacement(false);
            }
            catch (Exception ex)
            {
                _lastPanelMessage = "Placement failed: " + ex.Message;
                _logger?.Warning(
                    "World pointer placement failed: " + ex);
            }
        }

        private void DrawPointerPlacementStatus()
        {
            if (_pointerPlacementKind == PointerPlacementKind.None
                || _pointerPlacementTab != _panelTab)
            {
                return;
            }
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.08f, 0.70f, 0.78f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "POINTER ARMED - click terrain"
                + (_repeatPointerPlacement ? " (repeat)" : ""),
                _onlineStyle);
            if (GUILayout.Button(
                    "Cancel",
                    GUILayout.Width(80f),
                    GUILayout.Height(27f)))
            {
                CancelPointerPlacement();
            }
            GUILayout.EndHorizontal();
            GUI.backgroundColor = oldColor;
        }

        private void CancelPointerPlacement(bool updateStatus = true)
        {
            if (_pointerPlacementKind == PointerPlacementKind.None)
                return;
            _pointerPlacementKind = PointerPlacementKind.None;
            _pointerPlacementPayload = string.Empty;
            _hasPointerSurface = false;
            HideWorldPointerMarker();
            if (updateStatus)
                _lastPanelMessage = "Pointer placement cancelled";
        }

        private void HandleUniversalDeselectInput()
        {
            if (!_visible
                || !_runtimeEnabled
                || !Input.GetMouseButtonDown(1)
                || IsPointerOverEditorWindow())
            {
                return;
            }

            CancelPointerPlacement(false);
            EndTerrainStroke();
            _connectStartId = string.Empty;
            _fitArcNodeIds.Clear();
            _poleWireStartId = -1;
            _opsMarkedSpanStartSegment = string.Empty;
            _deleteConfirmId = string.Empty;
            _sceneryDeleteConfirm = string.Empty;
            _poleDeleteConfirm = string.Empty;
            _trainSignalDeleteConfirm = string.Empty;
            _mapEditor?.ClearAllSelections();
            HideWorldPointerMarker();
            _lastPanelMessage =
                "Selection and active editor tool cleared";
        }

        private bool TryGetPointerSurfaceHit(
            bool terrainOnly,
            out RaycastHit bestHit)
        {
            bestHit = default;
            var camera = Camera.main;
            if (camera == null)
                return false;
            var ray = camera.ScreenPointToRay(Input.mousePosition);
            var count = Physics.RaycastNonAlloc(
                ray,
                _pointerRaycastHits,
                5000f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var bestDistance = float.PositiveInfinity;
            var found = false;
            for (var index = 0; index < count; index++)
            {
                var hit = _pointerRaycastHits[index];
                if (hit.collider == null
                    || (terrainOnly && !IsTerrainHit(hit)))
                {
                    continue;
                }
                if (hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
            return found;
        }

        private static bool IsTerrainHit(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;
            if (hit.collider.GetComponent<Terrain>() != null
                || hit.collider.GetComponentInParent<Terrain>() != null)
            {
                return true;
            }
            var terrainLayer = LayerMask.NameToLayer("Terrain");
            var current = hit.collider.transform;
            while (current != null)
            {
                if (current.gameObject.layer == terrainLayer)
                    return true;
                current = current.parent;
            }
            return false;
        }

        private bool IsPointerOverEditorWindow()
        {
            var mouse = Input.mousePosition;
            var guiMouse =
                new Vector2(mouse.x, Screen.height - mouse.y);
            return _windowRect.Contains(guiMouse)
                   || (_nodeEditorVisible
                       && _nodeWindowRect.Contains(guiMouse));
        }

        private void ShowWorldPointerMarker(
            Vector3 center,
            Vector3 normal,
            float radius,
            Color color)
        {
            if (_pointerMarker == null)
                _pointerMarker = new TileEditorPointerMarker(transform);
            _pointerMarker.Show(center, normal, radius, color);
        }

        private void HideWorldPointerMarker()
        {
            _pointerMarker?.Hide();
        }

        private void UpdateMandelaObjectPicking()
        {
            if (_mapEditor == null || !_mapEditor.GraphOpen)
            {
                HideWorldPointerMarker();
                return;
            }
            if (_mapEditor.TryGetSelectedMandelaBounds(
                    out var bounds))
            {
                var radius = Mathf.Clamp(
                    Mathf.Max(
                        bounds.extents.x,
                        bounds.extents.z),
                    1.25f,
                    40f);
                ShowWorldPointerMarker(
                    bounds.center,
                    Vector3.up,
                    radius,
                    new Color(1f, 0.20f, 0.85f, 1f));
            }
            else
            {
                HideWorldPointerMarker();
            }

            if (IsPointerOverEditorWindow()
                || !Input.GetMouseButtonDown(0)
                || Camera.main == null)
            {
                return;
            }
            try
            {
                var ray = Camera.main.ScreenPointToRay(
                    Input.mousePosition);
                if (_mapEditor.SelectMandelaUnderPointer(
                        ray,
                        out var status))
                {
                    _lastPanelMessage = status;
                }
                else
                {
                    _lastPanelMessage = status;
                }
            }
            catch (Exception ex)
            {
                _lastPanelMessage =
                    "Object selection failed: " + ex.Message;
                _logger?.Warning(
                    "Base-game object selection failed: " + ex);
            }
        }

        private void DisposeWorldPointerTools()
        {
            _pointerMarker?.Dispose();
            _pointerMarker = null;
        }
    }

    internal sealed class TileEditorPointerMarker
    {
        private const int SegmentCount = 64;
        private readonly Vector3[] _positions =
            new Vector3[SegmentCount];
        private readonly GameObject _root;
        private readonly LineRenderer _ring;
        private readonly LineRenderer _cross;
        private readonly Material _material;

        internal TileEditorPointerMarker(Transform parent)
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Hidden/Internal-Colored");
            _material = shader == null
                ? null
                : new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    renderQueue = 5000,
                };
            if (_material != null)
            {
                if (_material.HasProperty("_ZWrite"))
                    _material.SetInt("_ZWrite", 0);
                if (_material.HasProperty("_ZTest"))
                {
                    _material.SetInt(
                        "_ZTest",
                        (int)CompareFunction.Always);
                }
            }
            _root = new GameObject("TileEditorWorldPointer");
            _root.hideFlags = HideFlags.HideAndDontSave;
            _root.transform.SetParent(parent, false);
            _ring = CreateLine("Ring", true);
            _cross = CreateLine("Cross", false);
            Hide();
        }

        internal void Show(
            Vector3 center,
            Vector3 normal,
            float radius,
            Color color)
        {
            if (_root == null)
                return;
            _root.SetActive(true);
            normal = normal.sqrMagnitude < 0.001f
                ? Vector3.up
                : normal.normalized;
            var tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.001f)
                tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            var bitangent = Vector3.Cross(normal, tangent).normalized;
            center += normal * Mathf.Clamp(radius * 0.02f, 0.15f, 0.6f);
            for (var index = 0; index < SegmentCount; index++)
            {
                var angle = index / (float)SegmentCount
                            * Mathf.PI * 2f;
                _positions[index] = center
                    + (Mathf.Cos(angle) * tangent
                       + Mathf.Sin(angle) * bitangent) * radius;
            }
            var width = Mathf.Clamp(radius * 0.025f, 0.08f, 0.35f);
            _ring.startColor = color;
            _ring.endColor = color;
            _ring.widthMultiplier = width;
            _ring.positionCount = SegmentCount;
            _ring.SetPositions(_positions);
            _cross.startColor = color;
            _cross.endColor = color;
            _cross.widthMultiplier = width;
            _cross.positionCount = 4;
            _cross.SetPosition(0, center - tangent * radius * 0.25f);
            _cross.SetPosition(1, center + tangent * radius * 0.25f);
            _cross.SetPosition(2, center - bitangent * radius * 0.25f);
            _cross.SetPosition(3, center + bitangent * radius * 0.25f);
        }

        internal void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        internal void Dispose()
        {
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            if (_material != null)
                UnityEngine.Object.Destroy(_material);
        }

        private LineRenderer CreateLine(string name, bool loop)
        {
            var child = new GameObject(name);
            child.hideFlags = HideFlags.HideAndDontSave;
            child.transform.SetParent(_root.transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = true;
            line.loop = loop;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 3;
            line.numCornerVertices = 3;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = short.MaxValue;
            return line;
        }
    }
}
