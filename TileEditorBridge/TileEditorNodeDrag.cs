using System;
using System.Linq;
using Helpers;
using Track;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        private readonly RaycastHit[] _nodeDragRaycastHits =
            new RaycastHit[96];
        private TrackNode _draggedNode;
        private NodeModel _draggedNodeOrigin;
        private TrackNode _nodeDragDropTarget;
        private Vector3 _nodeDragStartMouse;
        private bool _nodeDragHasMoved;
        private float _nextNodeDragVisualUpdateAt;

        internal bool NodeDragActive => _draggedNode != null;

        internal bool IsNodeDragTarget(TrackNode node)
        {
            return node != null && node == _nodeDragDropTarget;
        }

        internal void BeginNodeDragFromWorld(TrackNode node)
        {
            if (!_editModeActive || node == null)
                return;
            try
            {
                RequireSession();
                RequireGraphEditOwnership();
                CancelNodeDrag();
                SelectNode(node);
                _draggedNode = node;
                _draggedNodeOrigin = CaptureNode(node);
                _nodeDragStartMouse = Input.mousePosition;
                _nodeDragHasMoved = false;
                _nextNodeDragVisualUpdateAt = 0f;
                _worldNodeShortcutStatus =
                    "Dragging " + node.id
                    + ". Release over terrain to move it, or over another "
                    + "cyan node to connect.";
            }
            catch (Exception ex)
            {
                _worldNodeShortcutStatus =
                    "Could not start node drag: " + ex.Message;
                _logger?.Warning(
                    "Ctrl-drag node start failed: " + ex);
            }
        }

        internal void UpdateNodeDragFromPointer(bool pointerOverPanel)
        {
            var dragged = _draggedNode;
            if (dragged == null || _draggedNodeOrigin == null)
            {
                ClearNodeDragState();
                return;
            }
            if (pointerOverPanel || Camera.main == null)
                return;
            if (!_nodeDragHasMoved)
            {
                var mouseDelta =
                    Input.mousePosition - _nodeDragStartMouse;
                if (mouseDelta.sqrMagnitude < 16f)
                    return;
                _nodeDragHasMoved = true;
            }

            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            var hitCount = Physics.RaycastNonAlloc(
                ray,
                _nodeDragRaycastHits,
                5000f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            TrackNode dropTarget = null;
            var dropDistance = float.PositiveInfinity;
            var terrainFound = false;
            var terrainDistance = float.PositiveInfinity;
            var terrainPoint = Vector3.zero;

            for (var index = 0; index < hitCount; index++)
            {
                var hit = _nodeDragRaycastHits[index];
                if (hit.collider == null)
                    continue;
                var nodeOverlay = hit.collider.GetComponentInParent<
                    TileEditorNodeOverlay>();
                if (nodeOverlay != null)
                {
                    var candidate = nodeOverlay.Node;
                    if (candidate != null
                        && candidate != dragged
                        && hit.distance < dropDistance)
                    {
                        dropTarget = candidate;
                        dropDistance = hit.distance;
                    }
                    continue;
                }
                if (hit.collider.GetComponentInParent<
                        TileEditorSegmentOverlay>() != null)
                {
                    continue;
                }
                if (!IsNodeDragTerrainHit(hit)
                    || hit.distance >= terrainDistance)
                {
                    continue;
                }
                terrainFound = true;
                terrainDistance = hit.distance;
                terrainPoint = hit.point;
            }

            SetNodeDragDropTarget(dropTarget);
            if (dropTarget != null)
            {
                _worldNodeShortcutStatus =
                    "Release to connect " + dragged.id
                    + " to " + dropTarget.id
                    + ". The dragged node stays at its last terrain position.";
                return;
            }
            if (!terrainFound)
                return;

            var gamePosition =
                WorldTransformer.WorldToGame(terrainPoint)
                + Vector3.up * 0.2f;
            if ((dragged.transform.localPosition - gamePosition)
                    .sqrMagnitude < 0.000001f)
            {
                return;
            }
            dragged.transform.localPosition = gamePosition;
            if (Time.unscaledTime >= _nextNodeDragVisualUpdateAt)
            {
                _nextNodeDragVisualUpdateAt =
                    Time.unscaledTime + 1f / 30f;
                RefreshNodeDragSegments(
                    dragged,
                    rebuildPickGeometry: false);
            }
            _worldNodeShortcutStatus =
                "Moving " + dragged.id
                + " to "
                + gamePosition.x.ToString("0.00") + ", "
                + gamePosition.y.ToString("0.00") + ", "
                + gamePosition.z.ToString("0.00")
                + ". Release to place.";
        }

        internal void EndNodeDragFromWorld(TrackNode node)
        {
            if (_draggedNode == null || node != _draggedNode)
                return;
            UpdateNodeDragFromPointer(false);

            var dragged = _draggedNode;
            var origin = _draggedNodeOrigin;
            var target = _nodeDragHasMoved
                ? _nodeDragDropTarget
                : null;
            var finalPosition = dragged.transform.localPosition;
            var moved =
                (finalPosition - origin.Position).sqrMagnitude
                > 0.000001f;
            var alreadyConnected =
                target != null
                && _graph.Segments.Any(segment =>
                    (segment.a == dragged && segment.b == target)
                    || (segment.a == target && segment.b == dragged));
            var createConnection =
                target != null && !alreadyConnected;

            var connectedSegmentIds = _graph
                .SegmentsConnectedTo(dragged)
                .Select(segment => segment.id)
                .ToList();
            var newSegmentId = createConnection
                ? NextSegmentId()
                : string.Empty;
            if (createConnection)
                connectedSegmentIds.Add(newSegmentId);

            dragged.transform.localPosition = origin.Position;
            ClearNodeDragState();

            if (!moved && !createConnection)
            {
                _worldNodeShortcutStatus =
                    alreadyConnected
                        ? dragged.id + " and " + target.id
                          + " are already connected."
                        : "Node drag ended without a move.";
                return;
            }

            try
            {
                ExecuteEdit(
                    createConnection
                        ? "Drag node and connect"
                        : "Drag node",
                    new[] { dragged.id },
                    connectedSegmentIds,
                    () =>
                    {
                        dragged.transform.localPosition =
                            finalPosition;
                        WriteNode(dragged);
                        if (!createConnection)
                            return;
                        var segment = CreateSegmentLive(
                            new SegmentModel
                            {
                                Id = newSegmentId,
                                A = dragged.id,
                                B = target.id,
                                GroupId = string.Empty,
                                Style = TrackSegment.Style.Standard,
                                TrackClass = TrackClass.Mainline,
                            });
                        WriteSegment(segment);
                    },
                    useLightweightTrackUpdate: !createConnection);
                _worldNodeShortcutStatus =
                    createConnection
                        ? "Moved " + dragged.id + " and connected it to "
                          + target.id + "."
                        : "Moved " + dragged.id + ".";
            }
            catch (Exception ex)
            {
                dragged.transform.localPosition = origin.Position;
                RefreshNodeDragSegments(
                    dragged,
                    rebuildPickGeometry: true);
                _worldNodeShortcutStatus =
                    "Could not finish node drag: " + ex.Message;
                _logger?.Warning(
                    "Ctrl-drag node finish failed: " + ex);
            }
        }

        internal void CancelNodeDrag()
        {
            if (_draggedNode == null || _draggedNodeOrigin == null)
            {
                ClearNodeDragState();
                return;
            }
            var dragged = _draggedNode;
            dragged.transform.localPosition =
                _draggedNodeOrigin.Position;
            RefreshNodeDragSegments(
                dragged,
                rebuildPickGeometry: true);
            ClearNodeDragState();
            _worldNodeShortcutStatus =
                "Node drag cancelled.";
        }

        private void SetNodeDragDropTarget(TrackNode target)
        {
            if (target == _nodeDragDropTarget)
                return;
            var previous = _nodeDragDropTarget;
            _nodeDragDropTarget = target;
            previous?.GetComponentInChildren<
                    TileEditorNodeOverlay>(true)
                ?.RefreshColor();
            target?.GetComponentInChildren<
                    TileEditorNodeOverlay>(true)
                ?.RefreshColor();
        }

        private void RefreshNodeDragSegments(
            TrackNode node,
            bool rebuildPickGeometry)
        {
            if (node == null || Graph.Shared == null)
                return;
            foreach (var segment in Graph.Shared
                         .SegmentsConnectedTo(node))
            {
                segment.InvalidateCurve();
                var overlay = GetSegmentOverlay(segment);
                if (rebuildPickGeometry)
                    overlay?.Rebuild();
                else
                    overlay?.RefreshCurveLine();
            }
        }

        private void ClearNodeDragState()
        {
            var target = _nodeDragDropTarget;
            _draggedNode = null;
            _draggedNodeOrigin = null;
            _nodeDragDropTarget = null;
            _nodeDragHasMoved = false;
            target?.GetComponentInChildren<
                    TileEditorNodeOverlay>(true)
                ?.RefreshColor();
        }

        private static bool IsNodeDragTerrainHit(
            RaycastHit hit)
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
    }
}
