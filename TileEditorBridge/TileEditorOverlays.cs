using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Helpers;
using Track;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed class TileEditorNodeOverlay : MonoBehaviour, IPickable
    {
        private TileEditorGraphSession _session;
        private TrackNode _node;
        private LineRenderer _line;
        private BoxCollider _collider;

        public float MaxPickDistance => 500f;
        public int Priority => 10;
        public PickableActivationFilter ActivationFilter => PickableActivationFilter.Any;
        internal TrackNode Node => _node;

        public TooltipInfo TooltipInfo
        {
            get
            {
                if (_node == null)
                    return TooltipInfo.Empty;
                var position = _node.transform.localPosition;
                var rotation = _node.transform.localEulerAngles;
                return new TooltipInfo(
                    "Tile Editor Node " + _node.id,
                    $"Position: {position:F2}\nRotation: {rotation:F2}\n"
                    + "Shift-click: connect  Ctrl-drag: move/connect");
            }
        }

        internal void Initialize(TileEditorGraphSession session, TrackNode node)
        {
            _session = session;
            _node = node;
            BuildVisual();
            RefreshColor();
        }

        internal bool IsHealthyFor(TrackNode node)
        {
            return _node == node
                   && _line != null
                   && _line.positionCount >= 2
                   && _collider != null;
        }

        internal void SetOverlayVisible(bool visible)
        {
            enabled = visible;
            if (_line != null)
                _line.enabled = visible;
            if (_collider != null)
                _collider.enabled = visible;
            if (visible)
                RefreshColor();
        }

        public void Activate(PickableActivateEvent evt)
        {
            if (!TileEditorCameraInput.EditorWorldInputBlocked
                && _session != null
                && _node != null
                && evt.Activation == PickableActivation.Primary)
            {
                if (evt.IsControlDown)
                    _session.BeginNodeDragFromWorld(_node);
                else
                    _session.ActivateNodeFromWorld(
                        _node,
                        evt.IsShiftDown);
            }
        }

        public void Deactivate()
        {
            if (_session != null && _node != null)
                _session.EndNodeDragFromWorld(_node);
        }

        internal void RefreshColor()
        {
            if (_line != null && _node != null && _session != null)
            {
                TileEditorOverlayVisuals.SetColor(
                    _line,
                    _session.IsNodeDragTarget(_node)
                        ? Color.green
                        : _session.IsSelected(_node)
                            ? Color.magenta
                            : Color.cyan);
            }
        }

        internal bool IsWithinWorldRange(
            Vector3 cameraPosition,
            float rangeSquared)
        {
            return _node != null
                   && (_node.transform.position - cameraPosition)
                       .sqrMagnitude <= rangeSquared;
        }

        private void BuildVisual()
        {
            gameObject.layer = Layers.Clickable;
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;

            _line = GetComponent<LineRenderer>() ?? gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.startWidth = 0.09f;
            _line.endWidth = 0.09f;
            _line.positionCount = 5;
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.SetPositions(new[]
            {
                new Vector3(0f, 0.06f, 0.85f),
                new Vector3(0.45f, 0.06f, -0.45f),
                new Vector3(0f, 0.06f, -0.20f),
                new Vector3(-0.45f, 0.06f, -0.45f),
                new Vector3(0f, 0.06f, 0.85f),
            });

            _collider = GetComponent<BoxCollider>() ?? gameObject.AddComponent<BoxCollider>();
            _collider.center = new Vector3(0f, 0.15f, 0.1f);
            _collider.size = new Vector3(0.9f, 0.5f, 1.7f);
        }

    }

    internal sealed class TileEditorSegmentOverlay : MonoBehaviour, IPickable
    {
        private readonly List<LineRenderer> _chevrons = new List<LineRenderer>();
        private readonly List<Collider> _colliders = new List<Collider>();
        private TileEditorGraphSession _session;
        private TrackSegment _segment;
        private LineRenderer _line;

        public float MaxPickDistance => 500f;
        public int Priority => -1;
        public PickableActivationFilter ActivationFilter => PickableActivationFilter.Any;
        internal TrackSegment Segment => _segment;

        public TooltipInfo TooltipInfo
        {
            get
            {
                if (_segment == null)
                    return TooltipInfo.Empty;
                return new TooltipInfo(
                    "Tile Editor Segment " + _segment.id,
                    $"A: {_segment.a?.id}\nB: {_segment.b?.id}\n"
                    + $"Length: {_segment.GetLength():F1} m\n"
                    + $"Style: {_segment.style}  Class: {_segment.trackClass}\n"
                    + "Gauge: "
                    + _session.GetSegmentGaugeDisplay(_segment)
                    + "\nGroup: "
                    + (string.IsNullOrWhiteSpace(_segment.groupId)
                        ? "(none)"
                        : _segment.groupId)
                    + "\nClick to edit segment properties");
            }
        }

        internal void Initialize(TileEditorGraphSession session, TrackSegment segment)
        {
            _session = session;
            _segment = segment;
            BuildVisual();
            RefreshColor();
        }

        internal bool IsHealthyFor(TrackSegment segment)
        {
            return _segment == segment
                   && _line != null
                   && _line.positionCount >= 2;
        }

        internal void Rebuild()
        {
            BuildVisual();
            RefreshColor();
        }

        internal void RefreshCurveLine()
        {
            if (_segment == null
                || _segment.a == null
                || _segment.b == null)
            {
                return;
            }
            if (_line == null)
            {
                BuildVisual();
                RefreshColor();
                return;
            }
            var points = SampleCurvePoints();
            _line.positionCount = points.Length;
            _line.SetPositions(points);
            RefreshColor();
        }

        internal bool IsWithinWorldRange(
            Vector3 cameraPosition,
            float rangeSquared)
        {
            if (_segment == null
                || _segment.a == null
                || _segment.b == null)
            {
                return false;
            }
            var a = _segment.a.transform.position;
            var b = _segment.b.transform.position;
            var ab = b - a;
            var denominator = ab.sqrMagnitude;
            var t = denominator < 0.0001f
                ? 0f
                : Mathf.Clamp01(
                    Vector3.Dot(cameraPosition - a, ab)
                    / denominator);
            var nearest = a + ab * t;
            if ((nearest - cameraPosition).sqrMagnitude
                <= rangeSquared)
            {
                return true;
            }
            var midpoint = transform.TransformPoint(
                _segment.Curve.GetPoint(0.5f));
            return (midpoint - cameraPosition).sqrMagnitude
                   <= rangeSquared;
        }

        internal void SetOverlayVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
            enabled = visible;
            if (_line != null)
                _line.enabled = visible;
            foreach (var renderer in _chevrons)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
            foreach (var collider in _colliders)
            {
                if (collider != null)
                    collider.enabled = visible;
            }
            if (visible)
                RefreshColor();
        }

        public void Activate(PickableActivateEvent evt)
        {
            if (!TileEditorCameraInput.EditorWorldInputBlocked
                && _session != null
                && _segment != null)
                _session.SelectSegment(_segment);
        }

        public void Deactivate()
        {
        }

        internal void RefreshColor()
        {
            if (_segment == null || _session == null)
                return;
            var color = _session.IsSelected(_segment)
                ? Color.green
                : _session.GetSegmentOverlayColor(_segment);
            if (_line != null)
                TileEditorOverlayVisuals.SetColor(_line, color);
            foreach (var renderer in _chevrons)
            {
                if (renderer != null)
                    TileEditorOverlayVisuals.SetColor(
                        renderer,
                        color);
            }
        }

        private void BuildVisual()
        {
            if (_segment == null || _segment.a == null || _segment.b == null)
                return;

            gameObject.layer = Layers.Clickable;
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;

            foreach (Transform child in transform.Cast<Transform>().ToArray())
            {
                if (child.name.StartsWith("TileEditorPick", StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }
            _chevrons.Clear();
            _colliders.Clear();

            var curve = _segment.Curve;
            var points = SampleCurvePoints();
            _line = GetComponent<LineRenderer>() ?? gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.startWidth = 0.075f;
            _line.endWidth = 0.075f;
            _line.useWorldSpace = false;
            _line.positionCount = points.Length;
            _line.SetPositions(points);

            var length = Mathf.Max(1f, _segment.GetLength());
            // The colliders span their section of track, so dense 12 m
            // markers only multiplied renderers and physics work without
            // materially improving picking.
            var markerCount = Mathf.Clamp(
                Mathf.CeilToInt(length / 45f),
                1,
                16);
            var chevronStride = Mathf.Max(
                1,
                Mathf.CeilToInt(markerCount / 4f));
            for (var index = 0; index < markerCount; index++)
            {
                var t = (index + 0.5f) / markerCount;
                var marker = new GameObject("TileEditorPick-" + index);
                marker.layer = Layers.Clickable;
                marker.transform.SetParent(transform, false);
                marker.transform.localPosition = curve.GetPoint(t) + new Vector3(0f, 0.05f, 0f);
                marker.transform.localRotation = curve.GetRotation(t);

                if (index % chevronStride == 0
                    && _chevrons.Count < 4)
                {
                    var arrow = marker.AddComponent<LineRenderer>();
                    arrow.sharedMaterial =
                        TileEditorOverlayVisuals.SharedLineMaterial;
                    arrow.startWidth = 0.07f;
                    arrow.endWidth = 0.07f;
                    arrow.useWorldSpace = false;
                    arrow.positionCount = 3;
                    arrow.SetPositions(new[]
                    {
                        new Vector3(-0.38f, 0f, -0.28f),
                        new Vector3(0f, 0f, 0.30f),
                        new Vector3(0.38f, 0f, -0.28f),
                    });
                    _chevrons.Add(arrow);
                }

                var collider = marker.AddComponent<BoxCollider>();
                collider.center = Vector3.zero;
                collider.size = new Vector3(0.9f, 0.45f, Mathf.Max(2f, length / markerCount));
                _colliders.Add(collider);
            }
        }

        private Vector3[] SampleCurvePoints()
        {
            return _segment.Curve
                .Approximate(1.000005f, 0.5f, 16, 20f)
                .Select(point =>
                    point.point + new Vector3(0f, 0.04f, 0f))
                .ToArray();
        }

    }
}
