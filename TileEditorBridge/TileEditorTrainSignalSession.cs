using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Core;
using Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class TrainSignalInfo
        {
            internal string Id = string.Empty;
            internal bool Enabled = true;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal Vector3 Scale = Vector3.one;
            internal int HeadCount = 1;
            internal string InitialAspect = "stop";
            internal string InterlockingId = string.Empty;
            internal string ProtectedNodeId = string.Empty;
            internal string ProtectedSegmentId = string.Empty;
            internal IReadOnlyList<string> ProtectedSegmentIds =
                Array.Empty<string>();
            internal IReadOnlyList<string> ApproachSegmentIds =
                Array.Empty<string>();
            internal string Direction = "forward";
            internal string ApproachId = string.Empty;
            internal bool TrackLocked;
            internal string TrackSegmentId = string.Empty;
            internal float TrackParameter;
            internal Vector3 TrackLocalPosition;
            internal Vector3 TrackLocalRotation;
        }

        private bool _trainSignalMode;
        private JObject _trainSignalsDocument;
        private string _trainSignalsPath = string.Empty;
        private string _trainSignalsBackupPath = string.Empty;
        private string _selectedTrainSignalId = string.Empty;
        private readonly Stack<JObject> _trainSignalUndo =
            new Stack<JObject>();
        private readonly Stack<JObject> _trainSignalRedo =
            new Stack<JObject>();
        private readonly Dictionary<string, TileEditorTrainSignalOverlay>
            _trainSignalOverlays =
                new Dictionary<string, TileEditorTrainSignalOverlay>(
                    StringComparer.OrdinalIgnoreCase);

        internal bool SignalRuntimeAvailable =>
            ResolveSignalRuntimeMainType() != null;

        internal int TrainSignalCount =>
            TrainSignalsArray.OfType<JObject>().Count();

        internal int DiamondInterlockingCount =>
            TrainInterlockingsArray.OfType<JObject>()
                .Count(entry => string.Equals(
                    (string)entry["type"],
                    "diamond",
                    StringComparison.OrdinalIgnoreCase));

        internal bool CanUndoTrainSignal => _trainSignalUndo.Count > 0;
        internal bool CanRedoTrainSignal => _trainSignalRedo.Count > 0;
        internal int TrainSignalUndoCount => _trainSignalUndo.Count;
        internal int TrainSignalRedoCount => _trainSignalRedo.Count;

        internal TrainSignalInfo SelectedTrainSignal =>
            ReadTrainSignal(FindTrainSignal(_selectedTrainSignalId));

        internal IReadOnlyList<TrainSignalInfo> SearchTrainSignals(
            string query,
            int offset,
            int maximum,
            out int totalMatches)
        {
            query = (query ?? string.Empty).Trim();
            var matches = TrainSignalsArray
                .OfType<JObject>()
                .Select(ReadTrainSignal)
                .Where(signal => signal != null)
                .Where(signal => query.Length == 0
                                 || Contains(signal.Id, query)
                                 || Contains(signal.InterlockingId, query)
                                 || Contains(signal.ProtectedNodeId, query)
                                 || Contains(signal.ProtectedSegmentId, query))
                .OrderBy(signal => signal.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            totalMatches = matches.Length;
            return matches
                .Skip(Mathf.Max(0, offset))
                .Take(Mathf.Clamp(maximum, 1, 100))
                .ToArray();
        }

        internal void SetTrainSignalMode(bool active)
        {
            if (_trainSignalMode == active)
                return;
            _trainSignalMode = active;
            if (active && GraphOpen)
                RefreshTrainSignalOverlays();
            SetTrainSignalOverlaysVisible(
                active && _editModeActive && GraphOpen);
        }

        internal string CreateTrainSignalAtPosition(
            string requestedId,
            Vector3 gamePosition,
            float yaw,
            int headCount,
            string initialAspect,
            string interlockingId,
            string protectedNodeId,
            string protectedSegmentId,
            string direction,
            bool snapToTrack,
            bool lockToTrack,
            float lateralOffset,
            float verticalOffset,
            bool rightSide)
        {
            RequireSession();
            ValidateVector(gamePosition, "train signal position");
            var id = NextTrainSignalId(requestedId);
            var signal = new TrainSignalInfo
            {
                Id = id,
                Enabled = true,
                Position = gamePosition,
                Rotation = new Vector3(0f, yaw, 0f),
                Scale = Vector3.one,
                HeadCount = Mathf.Clamp(headCount, 1, 3),
                InitialAspect = NormalizeSignalAspect(initialAspect),
                InterlockingId = (interlockingId ?? string.Empty).Trim(),
                ProtectedNodeId = (protectedNodeId ?? string.Empty).Trim(),
                ProtectedSegmentId =
                    (protectedSegmentId ?? string.Empty).Trim(),
                Direction = NormalizeSignalDirection(direction),
            };
            signal.ProtectedSegmentIds =
                string.IsNullOrWhiteSpace(signal.ProtectedSegmentId)
                    ? Array.Empty<string>()
                    : new[] { signal.ProtectedSegmentId };
            signal.ApproachSegmentIds = signal.ProtectedSegmentIds;
            if (snapToTrack)
            {
                SnapTrainSignalToTrack(
                    signal,
                    signal.ProtectedSegmentId,
                    lateralOffset,
                    verticalOffset,
                    rightSide,
                    lockToTrack);
            }
            else if (lockToTrack)
            {
                LockTrainSignalToTrack(
                    signal,
                    signal.ProtectedSegmentId);
            }
            var entry = new JObject();
            WriteTrainSignal(entry, signal);
            ExecuteTrainSignalEdit(
                "Create train signal",
                () =>
                {
                    TrainSignalsArray.Add(entry);
                    _selectedTrainSignalId = id;
                });
            return "Placed base-game semaphore " + id
                   + (signal.TrackLocked
                       ? " locked to " + signal.TrackSegmentId
                       : snapToTrack ? " snapped to track" : string.Empty);
        }

        internal string DescribeDiamondInterlockingSegments(
            string segmentAId,
            string segmentBId)
        {
            var crossing = CalculateDiamondCrossing(
                segmentAId,
                segmentBId);
            return "Diamond found at "
                   + FormatVector(crossing.Point)
                   + " / angle "
                   + crossing.AngleDegrees.ToString(
                       "0.0",
                       CultureInfo.InvariantCulture)
                   + " degrees / rail-height difference "
                   + crossing.VerticalGap.ToString(
                       "0.00",
                       CultureInfo.InvariantCulture)
                   + " m";
        }

        internal string BuildDiamondInterlocking(
            string requestedId,
            string segmentAId,
            string segmentBId,
            float signalSetback,
            float lateralOffset,
            float verticalOffset,
            float approachLength,
            float releaseLength,
            int headCount)
        {
            RequireSession();
            if (signalSetback < 3f || signalSetback > 5000f)
                throw new InvalidOperationException(
                    "Signal setback must be between 3 and 5000 m.");
            if (lateralOffset < 0.5f || lateralOffset > 15f)
                throw new InvalidOperationException(
                    "Signal side offset must be between 0.5 and 15 m.");
            if (verticalOffset < -5f || verticalOffset > 5f)
                throw new InvalidOperationException(
                    "Signal vertical offset must be between -5 and 5 m.");
            if (approachLength < 5f || approachLength > 5000f
                || releaseLength < 5f || releaseLength > 5000f)
            {
                throw new InvalidOperationException(
                    "Approach and release lengths must be between 5 and 5000 m.");
            }
            var interlockingId = NormalizeTrainSignalId(requestedId);
            if (FindTrainInterlocking(interlockingId) != null)
            {
                throw new InvalidOperationException(
                    "An interlocking already uses id '"
                    + interlockingId + "'.");
            }
            var crossing = CalculateDiamondCrossing(
                segmentAId,
                segmentBId);
            if (crossing.VerticalGap > 1.5f)
            {
                throw new InvalidOperationException(
                    "The rails cross in plan view but are "
                    + crossing.VerticalGap.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture)
                    + " m apart vertically. This appears to be an overpass, "
                    + "not a diamond crossing.");
            }
            var segmentA = RequireSegmentById(segmentAId);
            var segmentB = RequireSegmentById(segmentBId);
            var approaches = new[]
            {
                BuildDiamondApproach(
                    interlockingId,
                    "A1",
                    "A",
                    segmentA,
                    crossing.ParameterA,
                    true,
                    signalSetback,
                    lateralOffset,
                    verticalOffset,
                    approachLength,
                    releaseLength,
                    headCount),
                BuildDiamondApproach(
                    interlockingId,
                    "A2",
                    "A",
                    segmentA,
                    crossing.ParameterA,
                    false,
                    signalSetback,
                    lateralOffset,
                    verticalOffset,
                    approachLength,
                    releaseLength,
                    headCount),
                BuildDiamondApproach(
                    interlockingId,
                    "B1",
                    "B",
                    segmentB,
                    crossing.ParameterB,
                    true,
                    signalSetback,
                    lateralOffset,
                    verticalOffset,
                    approachLength,
                    releaseLength,
                    headCount),
                BuildDiamondApproach(
                    interlockingId,
                    "B2",
                    "B",
                    segmentB,
                    crossing.ParameterB,
                    false,
                    signalSetback,
                    lateralOffset,
                    verticalOffset,
                    approachLength,
                    releaseLength,
                    headCount),
            };
            var routeA = approaches.Where(item => item.RouteId == "A")
                .ToArray();
            var routeB = approaches.Where(item => item.RouteId == "B")
                .ToArray();
            var interlockingEntry = new JObject
            {
                ["id"] = interlockingId,
                ["type"] = "diamond",
                ["automatic"] = true,
                ["releaseDelaySeconds"] = 3f,
                ["cancelDelaySeconds"] = 120f,
                ["crossingPoint"] = Vector(crossing.Point),
                ["crossingAngleDegrees"] = crossing.AngleDegrees,
                ["approachLength"] = approachLength,
                ["releaseLength"] = releaseLength,
                ["routes"] = new JArray
                {
                    BuildDiamondRouteEntry("A", segmentA.id, routeA),
                    BuildDiamondRouteEntry("B", segmentB.id, routeB),
                },
                ["conflicts"] = new JArray
                {
                    new JArray("A", "B"),
                },
            };
            ExecuteTrainSignalEdit(
                "Build diamond interlocking",
                () =>
                {
                    foreach (var approach in approaches)
                        TrainSignalsArray.Add(approach.Entry);
                    TrainInterlockingsArray.Add(interlockingEntry);
                    _selectedTrainSignalId = approaches[0].SignalId;
                });
            var routeSegmentCount = approaches
                .SelectMany(item => item.SegmentIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var ambiguityCount = approaches.Sum(
                item => item.AmbiguousJunctions);
            return "Built diamond " + interlockingId
                   + " with four independently editable semaphore signals; "
                   + routeSegmentCount + " connected route segment(s) saved; "
                   + (ambiguityCount == 0
                       ? string.Empty
                       : ambiguityCount
                         + " turnout continuation(s) chose the best-aligned "
                         + "same-group/gauge path; ")
                   + approaches[0].SignalId + " selected";
        }

        internal void SelectTrainSignal(string id)
        {
            if (!_trainSignalMode || !_editModeActive)
                return;
            var entry = FindTrainSignal(id);
            _selectedTrainSignalId = entry == null
                ? string.Empty
                : ((string)entry["id"] ?? string.Empty).Trim();
            RefreshTrainSignalOverlayColors();
        }

        internal bool IsSelectedTrainSignal(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                   && string.Equals(
                       id,
                       _selectedTrainSignalId,
                       StringComparison.OrdinalIgnoreCase);
        }

        internal void ClearSelectedTrainSignal()
        {
            _selectedTrainSignalId = string.Empty;
            RefreshTrainSignalOverlayColors();
        }

        internal void MoveSelectedTrainSignal(
            Vector3 offset,
            bool localAxes)
        {
            ValidateVector(offset, "train signal movement");
            EditSelectedTrainSignal(
                "Move train signal",
                signal =>
                {
                    var applied = localAxes
                        ? Quaternion.Euler(signal.Rotation) * offset
                        : offset;
                    signal.Position += applied;
                });
        }

        internal void RotateSelectedTrainSignal(Vector3 offset)
        {
            ValidateVector(offset, "train signal rotation");
            EditSelectedTrainSignal(
                "Rotate train signal",
                signal => signal.Rotation += offset);
        }

        internal void FlipSelectedTrainSignal()
        {
            RotateSelectedTrainSignal(new Vector3(0f, 180f, 0f));
        }

        internal void SetSelectedTrainSignalTransform(
            Vector3 position,
            Vector3 rotation)
        {
            ValidateVector(position, "train signal position");
            ValidateVector(rotation, "train signal rotation");
            EditSelectedTrainSignal(
                "Set train signal transform",
                signal =>
                {
                    signal.Position = position;
                    signal.Rotation = rotation;
                });
        }

        internal string SnapSelectedTrainSignalToTrack(
            float lateralOffset,
            float verticalOffset,
            bool rightSide,
            bool lockToTrack)
        {
            ValidateSignalSnapOffsets(lateralOffset, verticalOffset);
            var segmentId = _selectedSegment?.id
                            ?? SelectedTrainSignal?.TrackSegmentId
                            ?? SelectedTrainSignal?.ProtectedSegmentId
                            ?? string.Empty;
            var snappedSegmentId = string.Empty;
            EditSelectedTrainSignal(
                lockToTrack
                    ? "Snap and lock train signal to track"
                    : "Snap train signal to track",
                signal =>
                {
                    var segment = SnapTrainSignalToTrack(
                        signal,
                        segmentId,
                        lateralOffset,
                        verticalOffset,
                        rightSide,
                        lockToTrack);
                    snappedSegmentId = segment.id;
                });
            return "Signal snapped to " + snappedSegmentId
                   + (lockToTrack ? " and locked" : string.Empty);
        }

        internal string SetSelectedTrainSignalTrackLocked(bool locked)
        {
            var segmentId = _selectedSegment?.id
                            ?? SelectedTrainSignal?.TrackSegmentId
                            ?? SelectedTrainSignal?.ProtectedSegmentId
                            ?? string.Empty;
            var attachedSegmentId = string.Empty;
            EditSelectedTrainSignal(
                locked
                    ? "Lock train signal to track"
                    : "Unlock train signal from track",
                signal =>
                {
                    if (!locked)
                    {
                        signal.TrackLocked = false;
                        signal.TrackSegmentId = string.Empty;
                        signal.TrackParameter = 0f;
                        signal.TrackLocalPosition = Vector3.zero;
                        signal.TrackLocalRotation = Vector3.zero;
                        return;
                    }
                    var segment = LockTrainSignalToTrack(
                        signal,
                        segmentId);
                    attachedSegmentId = segment.id;
                });
            return locked
                ? "Signal locked to " + attachedSegmentId
                : "Signal unlocked; its current world transform was kept";
        }

        internal void ConfigureSelectedTrainSignal(
            string id,
            int headCount,
            string initialAspect,
            string interlockingId,
            string protectedNodeId,
            string protectedSegmentId,
            string direction,
            bool enabled)
        {
            var normalizedId = NormalizeTrainSignalId(id);
            var existing = FindTrainSignal(normalizedId);
            if (existing != null
                && !string.Equals(
                    normalizedId,
                    _selectedTrainSignalId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Another train signal already uses id '"
                    + normalizedId + "'.");
            }
            EditSelectedTrainSignal(
                "Configure train signal",
                signal =>
                {
                    signal.Id = normalizedId;
                    signal.HeadCount = Mathf.Clamp(headCount, 1, 3);
                    signal.InitialAspect =
                        NormalizeSignalAspect(initialAspect);
                    signal.InterlockingId =
                        (interlockingId ?? string.Empty).Trim();
                    signal.ProtectedNodeId =
                        (protectedNodeId ?? string.Empty).Trim();
                    var normalizedProtectedSegment =
                        (protectedSegmentId ?? string.Empty).Trim();
                    if (!string.Equals(
                            signal.ProtectedSegmentId,
                            normalizedProtectedSegment,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        signal.ProtectedSegmentIds =
                            normalizedProtectedSegment.Length == 0
                                ? Array.Empty<string>()
                                : new[] { normalizedProtectedSegment };
                        signal.ApproachSegmentIds =
                            signal.ProtectedSegmentIds;
                    }
                    signal.ProtectedSegmentId = normalizedProtectedSegment;
                    signal.Direction =
                        NormalizeSignalDirection(direction);
                    signal.Enabled = enabled;
                });
        }

        internal string UseSelectedTrackForTrainSignal()
        {
            var selected = SelectedTrainSignal;
            if (selected == null)
                throw new InvalidOperationException(
                    "Select a train signal first.");
            if (_selectedNode == null && _selectedSegment == null)
            {
                throw new InvalidOperationException(
                    "Click a cyan node or yellow segment first.");
            }
            var nodeId = _selectedNode?.id ?? selected.ProtectedNodeId;
            var segmentId = _selectedSegment?.id
                            ?? (_selectedNode == null
                                ? selected.ProtectedSegmentId
                                : _graph.SegmentsConnectedTo(_selectedNode)
                                    .OrderBy(segment => segment.id,
                                        StringComparer.OrdinalIgnoreCase)
                                    .Select(segment => segment.id)
                                    .FirstOrDefault()
                                  ?? selected.ProtectedSegmentId);
            EditSelectedTrainSignal(
                "Bind train signal to track",
                signal =>
                {
                    signal.ProtectedNodeId = nodeId ?? string.Empty;
                    signal.ProtectedSegmentId = segmentId ?? string.Empty;
                    signal.ProtectedSegmentIds =
                        string.IsNullOrWhiteSpace(segmentId)
                            ? Array.Empty<string>()
                            : new[] { segmentId };
                    signal.ApproachSegmentIds =
                        signal.ProtectedSegmentIds;
                    LockTrainSignalToTrack(signal, segmentId);
                });
            return "Signal protects node " + (nodeId ?? "(none)")
                   + " / segment " + (segmentId ?? "(none)")
                   + " and is locked to that track";
        }

        internal string RecalculateSelectedTrainSignalRoute()
        {
            var entry = RequireSelectedTrainSignalEntry();
            var signal = ReadTrainSignal(entry);
            if (string.IsNullOrWhiteSpace(signal.InterlockingId)
                || string.IsNullOrWhiteSpace(signal.ApproachId))
            {
                throw new InvalidOperationException(
                    "This signal is not assigned to a generated diamond "
                    + "approach.");
            }
            var interlocking = FindTrainInterlocking(signal.InterlockingId);
            if (interlocking == null)
            {
                throw new InvalidOperationException(
                    "Interlocking '" + signal.InterlockingId
                    + "' was not found.");
            }
            var routes = (interlocking["routes"] as JArray
                          ?? new JArray()).OfType<JObject>().ToArray();
            JObject route = null;
            JObject approach = null;
            foreach (var candidateRoute in routes)
            {
                var candidate = (candidateRoute["approaches"] as JArray
                                 ?? new JArray()).OfType<JObject>()
                    .FirstOrDefault(item =>
                        string.Equals(
                            ((string)item["signalId"] ?? string.Empty).Trim(),
                            signal.Id,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            ((string)item["id"] ?? string.Empty).Trim(),
                            signal.ApproachId,
                            StringComparison.OrdinalIgnoreCase));
                if (candidate == null)
                    continue;
                route = candidateRoute;
                approach = candidate;
                break;
            }
            if (route == null || approach == null)
            {
                throw new InvalidOperationException(
                    "The signal is no longer referenced by its diamond "
                    + "route.");
            }
            var segmentIds = ReadSignalStringArray(
                approach["segmentIds"] as JArray,
                signal.ProtectedSegmentId).ToList();
            if (segmentIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "The diamond approach has no saved track chain.");
            }

            var closestIndex = -1;
            var closestDistanceSquared = float.MaxValue;
            for (var chainIndex = 0;
                 chainIndex < segmentIds.Count;
                 chainIndex++)
            {
                var segment = RequireSegmentById(segmentIds[chainIndex]);
                var samples = Mathf.Clamp(
                    Mathf.CeilToInt(Mathf.Max(1f, segment.GetLength()) / 0.5f),
                    32,
                    4096);
                for (var sample = 0; sample <= samples; sample++)
                {
                    var point = segment.Curve.GetPoint(
                        sample / (float)samples);
                    var distanceSquared =
                        (point - signal.Position).sqrMagnitude;
                    if (distanceSquared >= closestDistanceSquared)
                        continue;
                    closestDistanceSquared = distanceSquared;
                    closestIndex = chainIndex;
                }
            }
            var closestDistance = Mathf.Sqrt(closestDistanceSquared);
            if (closestIndex < 0 || closestDistance > 30f)
            {
                throw new InvalidOperationException(
                    "The mast is more than 30 m from its saved approach. "
                    + "Move it nearer the intended route before "
                    + "recalculating.");
            }

            var mastSegment = RequireSegmentById(segmentIds[closestIndex]);
            TrackNode outerNode = null;
            if (closestIndex + 1 < segmentIds.Count)
            {
                outerNode = SharedTrackNode(
                    mastSegment,
                    RequireSegmentById(segmentIds[closestIndex + 1]));
            }
            if (outerNode == null && closestIndex > 0)
            {
                var innerNode = SharedTrackNode(
                    mastSegment,
                    RequireSegmentById(segmentIds[closestIndex - 1]));
                outerNode = innerNode == mastSegment.a
                    ? mastSegment.b
                    : mastSegment.a;
            }
            if (outerNode == null)
            {
                var startSide = signal.ApproachId.EndsWith(
                    "1",
                    StringComparison.OrdinalIgnoreCase);
                outerNode = startSide ? mastSegment.a : mastSegment.b;
            }
            var protectedIds = segmentIds.Take(closestIndex + 1).ToArray();
            var nodeId = outerNode?.id ?? string.Empty;
            var direction = outerNode == mastSegment.a
                ? "forward"
                : "reverse";
            ExecuteTrainSignalEdit(
                "Recalculate diamond signal route",
                () =>
                {
                    signal.ProtectedNodeId = nodeId;
                    signal.ProtectedSegmentId = mastSegment.id;
                    signal.ProtectedSegmentIds = protectedIds;
                    signal.ApproachSegmentIds = segmentIds;
                    signal.Direction = direction;
                    WriteTrainSignal(entry, signal);
                    approach["nodeId"] = nodeId;
                    approach["segmentIds"] = new JArray(segmentIds);
                    approach["protectedSegmentIds"] =
                        new JArray(protectedIds);
                    var routeApproaches = (route["approaches"] as JArray
                                           ?? new JArray())
                        .OfType<JObject>().ToArray();
                    route["approachNodeIds"] = new JArray(
                        routeApproaches.Select(item =>
                            ((string)item["nodeId"] ?? string.Empty).Trim()));
                });
            return "Recalculated " + signal.ApproachId + " through "
                   + protectedIds.Length + " protected segment(s); mast is "
                   + closestDistance.ToString(
                       "0.0",
                       CultureInfo.InvariantCulture)
                   + " m from " + mastSegment.id;
        }

        internal bool SelectedTrainSignalInterlockingAutomatic
        {
            get
            {
                var signal = SelectedTrainSignal;
                var interlocking = signal == null
                    ? null
                    : FindTrainInterlocking(signal.InterlockingId);
                return interlocking != null
                       && ((bool?)interlocking["automatic"] ?? true);
            }
        }

        internal void SetSelectedTrainSignalInterlockingAutomatic(
            bool automatic)
        {
            var signal = SelectedTrainSignal;
            var interlocking = signal == null
                ? null
                : FindTrainInterlocking(signal.InterlockingId);
            if (interlocking == null)
            {
                throw new InvalidOperationException(
                    "Select a signal assigned to a diamond interlocking.");
            }
            ExecuteTrainSignalEdit(
                automatic
                    ? "Enable automatic diamond interlocking"
                    : "Disable automatic diamond interlocking",
                () => interlocking["automatic"] = automatic);
        }

        internal string RequestSelectedTrainSignalInterlockingRoute()
        {
            var signal = SelectedTrainSignal;
            if (signal == null
                || string.IsNullOrWhiteSpace(signal.InterlockingId)
                || string.IsNullOrWhiteSpace(signal.ApproachId))
            {
                throw new InvalidOperationException(
                    "Select a generated diamond approach signal first.");
            }
            var result = InvokeSignalRuntimeBool(
                "TryRequestInterlockingRoute",
                signal.InterlockingId,
                signal.ApproachId);
            if (!result)
            {
                throw new InvalidOperationException(
                    "Signal Runtime could not request " + signal.ApproachId
                    + ". Verify that the standalone runtime is installed "
                    + "and the signal file has reloaded.");
            }
            return "Requested " + signal.ApproachId + " on "
                   + signal.InterlockingId;
        }

        internal string ReleaseSelectedTrainSignalInterlocking()
        {
            var signal = SelectedTrainSignal;
            if (signal == null
                || string.IsNullOrWhiteSpace(signal.InterlockingId))
            {
                throw new InvalidOperationException(
                    "Select a signal assigned to a diamond interlocking.");
            }
            var result = InvokeSignalRuntimeBool(
                "TryReleaseInterlocking",
                signal.InterlockingId);
            if (!result)
            {
                throw new InvalidOperationException(
                    "Signal Runtime could not release "
                    + signal.InterlockingId + ".");
            }
            return "Release requested for " + signal.InterlockingId;
        }

        internal string SelectedTrainSignalInterlockingStatus()
        {
            var signal = SelectedTrainSignal;
            if (signal == null
                || string.IsNullOrWhiteSpace(signal.InterlockingId))
            {
                return "No interlocking assigned";
            }
            var type = ResolveSignalRuntimeMainType();
            var method = type?.GetMethod(
                "TryGetInterlocking",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return "Signal Runtime not installed";
            try
            {
                var arguments = new object[]
                {
                    signal.InterlockingId,
                    null,
                };
                if (!(bool)method.Invoke(null, arguments)
                    || arguments[1] == null)
                {
                    return "Interlocking waiting for runtime reload";
                }
                var status = arguments[1];
                var statusType = status.GetType();
                var phase = statusType.GetProperty("Phase")?
                                .GetValue(status, null)?.ToString()
                            ?? "Unknown";
                var active = statusType.GetProperty("ActiveApproachId")?
                                 .GetValue(status, null)?.ToString()
                             ?? string.Empty;
                var reason = statusType.GetProperty("LastTransitionReason")?
                                 .GetValue(status, null)?.ToString()
                             ?? string.Empty;
                return phase
                       + (string.IsNullOrWhiteSpace(active)
                           ? string.Empty
                           : " / active " + active)
                       + (string.IsNullOrWhiteSpace(reason)
                           ? string.Empty
                           : " / " + reason);
            }
            catch (Exception ex)
            {
                return "Runtime status unavailable: "
                       + (ex.InnerException?.Message ?? ex.Message);
            }
        }

        private static bool InvokeSignalRuntimeBool(
            string methodName,
            params object[] arguments)
        {
            var method = ResolveSignalRuntimeMainType()?.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return false;
            try
            {
                return method.Invoke(null, arguments) is bool result
                       && result;
            }
            catch
            {
                return false;
            }
        }

        internal void DeleteSelectedTrainSignal()
        {
            var entry = RequireSelectedTrainSignalEntry();
            ExecuteTrainSignalEdit(
                "Delete train signal",
                () =>
                {
                    RemoveSignalFromInterlockings(
                        ((string)entry["id"] ?? string.Empty).Trim());
                    entry.Remove();
                    _selectedTrainSignalId = string.Empty;
                });
        }

        internal void UndoTrainSignal()
        {
            if (_trainSignalUndo.Count == 0)
                return;
            _trainSignalRedo.Push(
                (JObject)_trainSignalsDocument.DeepClone());
            _trainSignalsDocument = _trainSignalUndo.Pop();
            if (FindTrainSignal(_selectedTrainSignalId) == null)
                _selectedTrainSignalId = string.Empty;
            SaveTrainSignals();
            RefreshTrainSignalOverlays();
        }

        internal void RedoTrainSignal()
        {
            if (_trainSignalRedo.Count == 0)
                return;
            _trainSignalUndo.Push(
                (JObject)_trainSignalsDocument.DeepClone());
            _trainSignalsDocument = _trainSignalRedo.Pop();
            if (FindTrainSignal(_selectedTrainSignalId) == null)
                _selectedTrainSignalId = string.Empty;
            SaveTrainSignals();
            RefreshTrainSignalOverlays();
        }

        internal void ShowSelectedTrainSignal()
        {
            var signal = SelectedTrainSignal;
            if (signal == null)
                throw new InvalidOperationException(
                    "Select a train signal first.");
            if (CameraSelector.shared == null)
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            CameraSelector.shared.ZoomToPoint(
                WorldTransformer.GameToWorld(signal.Position));
        }

        private void EditSelectedTrainSignal(
            string name,
            Action<TrainSignalInfo> mutation)
        {
            var entry = RequireSelectedTrainSignalEntry();
            var signal = ReadTrainSignal(entry);
            mutation(signal);
            if (signal.TrackLocked)
                RefreshTrainSignalTrackAttachment(signal);
            ExecuteTrainSignalEdit(
                name,
                () =>
                {
                    var oldId = ((string)entry["id"] ?? string.Empty).Trim();
                    WriteTrainSignal(entry, signal);
                    if (!string.Equals(
                            oldId,
                            signal.Id,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        RenameSignalInInterlockings(oldId, signal.Id);
                        _selectedTrainSignalId = signal.Id;
                    }
                });
        }

        private void ExecuteTrainSignalEdit(
            string name,
            Action mutation)
        {
            RequireSession();
            EnsureTrainSignalsDocument();
            var before = (JObject)_trainSignalsDocument.DeepClone();
            try
            {
                mutation();
                SaveTrainSignals();
                _trainSignalUndo.Push(before);
                while (_trainSignalUndo.Count > 30)
                {
                    var kept = _trainSignalUndo
                        .Take(30)
                        .Reverse()
                        .ToArray();
                    _trainSignalUndo.Clear();
                    foreach (var item in kept)
                        _trainSignalUndo.Push(item);
                }
                _trainSignalRedo.Clear();
                RefreshTrainSignalOverlays();
                _logger?.Log(name + " saved to " + _trainSignalsPath);
            }
            catch
            {
                _trainSignalsDocument = before;
                throw;
            }
        }

        private void SaveTrainSignals()
        {
            EnsureTrainSignalsDocument();
            EnsureSignalRuntimeRequirement();
            if (string.IsNullOrWhiteSpace(_trainSignalsBackupPath)
                && File.Exists(_trainSignalsPath))
            {
                _trainSignalsBackupPath = _trainSignalsPath
                    + ".tile-editor-backup-"
                    + DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss",
                        CultureInfo.InvariantCulture);
                File.Copy(
                    _trainSignalsPath,
                    _trainSignalsBackupPath,
                    false);
                TileEditorBackupRetention.PruneFor(_trainSignalsPath);
            }
            var directory = Path.GetDirectoryName(_trainSignalsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var temp = _trainSignalsPath + ".tile-editor.tmp";
            File.WriteAllText(
                temp,
                _trainSignalsDocument.ToString(Formatting.Indented));
            if (File.Exists(_trainSignalsPath))
            {
                try
                {
                    File.Replace(temp, _trainSignalsPath, null);
                }
                catch
                {
                    File.Delete(_trainSignalsPath);
                    File.Move(temp, _trainSignalsPath);
                }
            }
            else
            {
                File.Move(temp, _trainSignalsPath);
            }
            ReloadStandaloneSignalRuntime();
        }

        private void ResetTrainSignalSession()
        {
            DisposeTrainSignalOverlays();
            _selectedTrainSignalId = string.Empty;
            _trainSignalUndo.Clear();
            _trainSignalRedo.Clear();
            _trainSignalsBackupPath = string.Empty;
            _trainSignalsPath = string.IsNullOrWhiteSpace(_graphPath)
                ? string.Empty
                : Path.Combine(
                    Path.GetDirectoryName(_graphPath) ?? string.Empty,
                    "train-signals.json");
            _trainSignalsDocument = null;
            EnsureTrainSignalsDocument();
            if (_trainSignalMode && GraphOpen)
                RefreshTrainSignalOverlays();
        }

        private void EnsureTrainSignalsDocument()
        {
            if (_trainSignalsDocument != null)
                return;
            if (!string.IsNullOrWhiteSpace(_trainSignalsPath)
                && File.Exists(_trainSignalsPath))
            {
                try
                {
                    _trainSignalsDocument = JObject.Parse(
                        File.ReadAllText(_trainSignalsPath));
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not read train-signals.json; opening an empty "
                        + "signal document and leaving the invalid file "
                        + "untouched until an edit is made: " + ex.Message);
                    _trainSignalsDocument = null;
                }
            }
            if (_trainSignalsDocument == null)
            {
                _trainSignalsDocument = new JObject
                {
                    ["$schema"] =
                        "https://hrogers.dev/railroader/train-signals/v1",
                    ["formatVersion"] = 1,
                    ["signals"] = new JArray(),
                    ["interlockings"] = new JArray(),
                };
            }
            if (!(_trainSignalsDocument["signals"] is JArray))
                _trainSignalsDocument["signals"] = new JArray();
            if (!(_trainSignalsDocument["interlockings"] is JArray))
                _trainSignalsDocument["interlockings"] = new JArray();
        }

        private JArray TrainSignalsArray
        {
            get
            {
                EnsureTrainSignalsDocument();
                return (JArray)_trainSignalsDocument["signals"];
            }
        }

        private JArray TrainInterlockingsArray
        {
            get
            {
                EnsureTrainSignalsDocument();
                return (JArray)_trainSignalsDocument["interlockings"];
            }
        }

        private JObject FindTrainSignal(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            return TrainSignalsArray.OfType<JObject>().FirstOrDefault(
                entry => string.Equals(
                    ((string)entry["id"] ?? string.Empty).Trim(),
                    id.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        private JObject FindTrainInterlocking(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            return TrainInterlockingsArray.OfType<JObject>()
                .FirstOrDefault(entry => string.Equals(
                    ((string)entry["id"] ?? string.Empty).Trim(),
                    id.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        private JObject RequireSelectedTrainSignalEntry()
        {
            var entry = FindTrainSignal(_selectedTrainSignalId);
            if (entry == null)
                throw new InvalidOperationException(
                    "Select a train signal first.");
            return entry;
        }

        private string NextTrainSignalId(string requested)
        {
            var root = NormalizeTrainSignalId(requested);
            if (FindTrainSignal(root) == null)
                return root;
            for (var number = 2; number < 10000; number++)
            {
                var candidate = root + "-" + number.ToString(
                    CultureInfo.InvariantCulture);
                if (FindTrainSignal(candidate) == null)
                    return candidate;
            }
            return root + "-" + Guid.NewGuid()
                .ToString("N").Substring(0, 8);
        }

        private static string NormalizeTrainSignalId(string value)
        {
            var normalized = new string((value ?? string.Empty)
                .Trim()
                .Select(character =>
                    char.IsLetterOrDigit(character)
                    || character == '_'
                    || character == '-'
                    || character == ':'
                    || character == '.'
                        ? character
                        : '-')
                .ToArray())
                .Trim('-');
            return normalized.Length == 0
                ? "signal:new"
                : normalized;
        }

        private static string NormalizeSignalAspect(string value)
        {
            var normalized = (value ?? string.Empty)
                .Trim()
                .Replace("_", "-")
                .ToLowerInvariant();
            switch (normalized)
            {
                case "approach":
                case "clear":
                case "diverging-approach":
                case "diverging-clear":
                case "restricting":
                    return normalized;
                default:
                    return "stop";
            }
        }

        private static string NormalizeSignalDirection(string value)
        {
            return string.Equals(
                (value ?? string.Empty).Trim(),
                "reverse",
                StringComparison.OrdinalIgnoreCase)
                ? "reverse"
                : "forward";
        }

        private static void ValidateSignalSnapOffsets(
            float lateralOffset,
            float verticalOffset)
        {
            if (float.IsNaN(lateralOffset)
                || float.IsInfinity(lateralOffset)
                || lateralOffset < 0f
                || lateralOffset > 25f)
            {
                throw new InvalidOperationException(
                    "Signal side offset must be between 0 and 25 m.");
            }
            if (float.IsNaN(verticalOffset)
                || float.IsInfinity(verticalOffset)
                || verticalOffset < -10f
                || verticalOffset > 10f)
            {
                throw new InvalidOperationException(
                    "Signal vertical offset must be between -10 and 10 m.");
            }
        }

        private TrackSegment SnapTrainSignalToTrack(
            TrainSignalInfo signal,
            string preferredSegmentId,
            float lateralOffset,
            float verticalOffset,
            bool rightSide,
            bool lockToTrack)
        {
            ValidateSignalSnapOffsets(lateralOffset, verticalOffset);
            var segment = FindSignalAttachmentSegment(
                preferredSegmentId,
                signal.Position,
                35f);
            var parameter = ClosestCurveParameter(
                segment.Curve,
                signal.Position);
            var center = segment.Curve.GetPoint(parameter);
            var frame = SignalTrackFrame(segment, parameter);
            var side = rightSide ? 1f : -1f;
            signal.Position = center
                              + frame * Vector3.right
                              * (Mathf.Abs(lateralOffset) * side)
                              + Vector3.up * verticalOffset;
            var trackHeading = frame.eulerAngles.y;
            signal.Rotation = new Vector3(
                0f,
                trackHeading
                + (string.Equals(
                       signal.Direction,
                       "forward",
                       StringComparison.OrdinalIgnoreCase)
                    ? 180f
                    : 0f),
                0f);
            signal.ProtectedSegmentId = segment.id;
            signal.ProtectedSegmentIds = new[] { segment.id };
            signal.ApproachSegmentIds = new[] { segment.id };
            if (lockToTrack)
                AttachTrainSignalToSegment(signal, segment);
            else
            {
                signal.TrackLocked = false;
                signal.TrackSegmentId = string.Empty;
            }
            return segment;
        }

        private TrackSegment LockTrainSignalToTrack(
            TrainSignalInfo signal,
            string preferredSegmentId)
        {
            var segment = FindSignalAttachmentSegment(
                preferredSegmentId,
                signal.Position,
                35f);
            AttachTrainSignalToSegment(signal, segment);
            if (string.IsNullOrWhiteSpace(signal.ProtectedSegmentId))
            {
                signal.ProtectedSegmentId = segment.id;
                signal.ProtectedSegmentIds = new[] { segment.id };
                signal.ApproachSegmentIds = new[] { segment.id };
            }
            return segment;
        }

        private void AttachTrainSignalToSegment(
            TrainSignalInfo signal,
            TrackSegment segment)
        {
            var parameter = ClosestCurveParameter(
                segment.Curve,
                signal.Position);
            var frame = SignalTrackFrame(segment, parameter);
            var center = segment.Curve.GetPoint(parameter);
            signal.TrackLocked = true;
            signal.TrackSegmentId = segment.id;
            signal.TrackParameter = parameter;
            signal.TrackLocalPosition = Quaternion.Inverse(frame)
                                        * (signal.Position - center);
            signal.TrackLocalRotation = (
                Quaternion.Inverse(frame)
                * Quaternion.Euler(signal.Rotation)).eulerAngles;
        }

        private void RefreshTrainSignalTrackAttachment(
            TrainSignalInfo signal)
        {
            if (!signal.TrackLocked)
                return;
            var segment = _graph?.GetSegment(signal.TrackSegmentId);
            if (segment == null)
            {
                signal.TrackLocked = false;
                signal.TrackSegmentId = string.Empty;
                return;
            }
            AttachTrainSignalToSegment(signal, segment);
        }

        private void ResolveTrainSignalTrackAttachment(
            TrainSignalInfo signal)
        {
            if (!signal.TrackLocked || _graph == null)
                return;
            var segment = _graph.GetSegment(signal.TrackSegmentId);
            if (segment == null)
                return;
            signal.TrackParameter = Mathf.Clamp01(signal.TrackParameter);
            var frame = SignalTrackFrame(
                segment,
                signal.TrackParameter);
            signal.Position = segment.Curve.GetPoint(signal.TrackParameter)
                              + frame * signal.TrackLocalPosition;
            signal.Rotation = (
                frame
                * Quaternion.Euler(signal.TrackLocalRotation)).eulerAngles;
        }

        private TrackSegment FindSignalAttachmentSegment(
            string preferredSegmentId,
            Vector3 position,
            float maximumDistance)
        {
            if (_graph == null)
                throw new InvalidOperationException(
                    "Railroader's track graph is not ready.");
            var preferred = string.IsNullOrWhiteSpace(preferredSegmentId)
                ? null
                : _graph.GetSegment(preferredSegmentId.Trim());
            if (preferred != null)
                return preferred;
            TrackSegment best = null;
            var bestDistanceSquared = maximumDistance * maximumDistance;
            foreach (var segment in _graph.Segments)
            {
                if (segment == null
                    || !segment.BoundingBoxContains(
                        position,
                        maximumDistance))
                {
                    continue;
                }
                var parameter = ClosestCurveParameter(
                    segment.Curve,
                    position);
                var distanceSquared = (
                    segment.Curve.GetPoint(parameter) - position)
                    .sqrMagnitude;
                if (distanceSquared >= bestDistanceSquared)
                    continue;
                bestDistanceSquared = distanceSquared;
                best = segment;
            }
            if (best == null)
            {
                throw new InvalidOperationException(
                    "No track was found within "
                    + maximumDistance.ToString(
                        "0",
                        CultureInfo.InvariantCulture)
                    + " m. Click a yellow segment first or place the "
                    + "pointer closer to the track.");
            }
            return best;
        }

        private static Quaternion SignalTrackFrame(
            TrackSegment segment,
            float parameter)
        {
            var tangent = CurveHorizontalTangent(segment, parameter);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;
            tangent.y = 0f;
            tangent.Normalize();
            return Quaternion.LookRotation(tangent, Vector3.up);
        }

        private static bool Contains(string value, string query)
        {
            return (value ?? string.Empty).IndexOf(
                       query,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private TrainSignalInfo ReadTrainSignal(JObject entry)
        {
            if (entry == null)
                return null;
            var scale = entry["scale"] == null
                ? Vector3.one
                : ReadVector(entry["scale"]);
            var attachment = entry["trackAttachment"] as JObject;
            var signal = new TrainSignalInfo
            {
                Id = ((string)entry["id"] ?? string.Empty).Trim(),
                Enabled = (bool?)entry["enabled"] ?? true,
                Position = ReadVector(entry["position"]),
                Rotation = ReadVector(entry["rotation"]),
                Scale = scale,
                HeadCount = Mathf.Clamp(
                    (int?)entry["headCount"] ?? 1,
                    1,
                    3),
                InitialAspect = NormalizeSignalAspect(
                    (string)entry["initialAspect"]),
                InterlockingId =
                    ((string)entry["interlockingId"] ?? string.Empty).Trim(),
                ProtectedNodeId =
                    ((string)entry["protectedNodeId"] ?? string.Empty).Trim(),
                ProtectedSegmentId =
                    ((string)entry["protectedSegmentId"] ?? string.Empty).Trim(),
                ProtectedSegmentIds = ReadSignalStringArray(
                    entry["protectedSegmentIds"] as JArray,
                    ((string)entry["protectedSegmentId"]
                     ?? string.Empty).Trim()),
                ApproachSegmentIds = ReadSignalStringArray(
                    entry["approachSegmentIds"] as JArray,
                    ((string)entry["protectedSegmentId"]
                     ?? string.Empty).Trim()),
                Direction = NormalizeSignalDirection(
                    (string)entry["direction"]),
                ApproachId =
                    ((string)entry["approachId"] ?? string.Empty).Trim(),
                TrackLocked =
                    (bool?)attachment?["locked"] ?? false,
                TrackSegmentId =
                    ((string)attachment?["segmentId"]
                     ?? string.Empty).Trim(),
                TrackParameter = Mathf.Clamp01(
                    (float?)attachment?["parameter"] ?? 0f),
                TrackLocalPosition = attachment?["localPosition"] == null
                    ? Vector3.zero
                    : ReadVector(attachment["localPosition"]),
                TrackLocalRotation = attachment?["localRotation"] == null
                    ? Vector3.zero
                    : ReadVector(attachment["localRotation"]),
            };
            ResolveTrainSignalTrackAttachment(signal);
            return signal;
        }

        private static void WriteTrainSignal(
            JObject entry,
            TrainSignalInfo signal)
        {
            entry["id"] = signal.Id;
            entry["enabled"] = signal.Enabled;
            entry["position"] = Vector(signal.Position);
            entry["rotation"] = Vector(signal.Rotation);
            entry["scale"] = Vector(signal.Scale);
            entry["headCount"] = Mathf.Clamp(signal.HeadCount, 1, 3);
            entry["initialAspect"] = signal.InitialAspect;
            entry["interlockingId"] = signal.InterlockingId;
            entry["protectedNodeId"] = signal.ProtectedNodeId;
            entry["protectedSegmentId"] = signal.ProtectedSegmentId;
            entry["protectedSegmentIds"] =
                new JArray(signal.ProtectedSegmentIds
                    ?? Array.Empty<string>());
            entry["approachSegmentIds"] =
                new JArray(signal.ApproachSegmentIds
                    ?? Array.Empty<string>());
            entry["direction"] = signal.Direction;
            if (signal.TrackLocked
                && !string.IsNullOrWhiteSpace(signal.TrackSegmentId))
            {
                entry["trackAttachment"] = new JObject
                {
                    ["locked"] = true,
                    ["segmentId"] = signal.TrackSegmentId,
                    ["parameter"] = Mathf.Clamp01(signal.TrackParameter),
                    ["localPosition"] = Vector(signal.TrackLocalPosition),
                    ["localRotation"] = Vector(signal.TrackLocalRotation),
                };
            }
            else
            {
                entry.Property("trackAttachment")?.Remove();
            }
            if (string.IsNullOrWhiteSpace(signal.ApproachId))
                entry.Property("approachId")?.Remove();
            else
                entry["approachId"] = signal.ApproachId;
        }

        private static IReadOnlyList<string> ReadSignalStringArray(
            JArray array,
            string fallback)
        {
            var values = (array ?? new JArray())
                .Values<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
            return values.Length > 0 || string.IsNullOrWhiteSpace(fallback)
                ? values
                : new[] { fallback };
        }

        private DiamondApproach BuildDiamondApproach(
            string interlockingId,
            string approachId,
            string routeId,
            TrackSegment segment,
            float crossingParameter,
            bool startSide,
            float signalSetback,
            float lateralOffset,
            float verticalOffset,
            float approachLength,
            float releaseLength,
            int headCount)
        {
            var trace = TraceDiamondApproach(
                segment,
                crossingParameter,
                signalSetback,
                Mathf.Max(
                    signalSetback + approachLength,
                    releaseLength),
                startSide);
            var point = trace.SignalPoint;
            var towardDiamond = trace.TowardDiamond;
            towardDiamond.y = 0f;
            if (towardDiamond.sqrMagnitude < 0.0001f)
                throw new InvalidOperationException(
                    "Could not determine the track direction for "
                    + segment.id + ".");
            towardDiamond.Normalize();
            var right = new Vector3(
                towardDiamond.z,
                0f,
                -towardDiamond.x);
            point += right * lateralOffset;
            point.y += verticalOffset;
            // The front of Railroader's semaphore faces the approaching
            // train, opposite its travel direction into the diamond.
            var yaw = Mathf.Atan2(
                          towardDiamond.x,
                          towardDiamond.z)
                      * Mathf.Rad2Deg + 180f;
            var signalId = NextTrainSignalId(
                interlockingId + ":" + approachId);
            var signal = new TrainSignalInfo
            {
                Id = signalId,
                Enabled = true,
                Position = point,
                Rotation = new Vector3(0f, yaw, 0f),
                Scale = Vector3.one,
                HeadCount = Mathf.Clamp(headCount, 1, 3),
                InitialAspect = "stop",
                InterlockingId = interlockingId,
                ApproachId = approachId,
                ProtectedNodeId =
                    trace.ApproachNode?.id ?? string.Empty,
                ProtectedSegmentId = trace.SignalSegment.id,
                ProtectedSegmentIds = trace.ProtectedSegmentIds,
                ApproachSegmentIds = trace.SegmentIds,
                Direction = trace.TrainMovesForward
                    ? "forward"
                    : "reverse",
            };
            AttachTrainSignalToSegment(signal, trace.SignalSegment);
            var entry = new JObject();
            WriteTrainSignal(entry, signal);
            return new DiamondApproach
            {
                SignalId = signalId,
                RouteId = routeId,
                NodeId = trace.ApproachNode?.id ?? string.Empty,
                SegmentIds = trace.SegmentIds,
                ProtectedSegmentIds = trace.ProtectedSegmentIds,
                UsedMultipleSegments = trace.SegmentIds.Count > 1,
                AmbiguousJunctions = trace.AmbiguousJunctions,
                Entry = entry,
            };
        }

        private static JObject BuildDiamondRouteEntry(
            string routeId,
            string segmentId,
            IEnumerable<DiamondApproach> approaches)
        {
            var array = approaches.ToArray();
            var segmentIds = new[] { segmentId }
                .Concat(array.SelectMany(item => item.SegmentIds))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new JObject
            {
                ["id"] = routeId,
                ["segmentId"] = segmentId,
                ["segmentIds"] = new JArray(segmentIds),
                ["signalIds"] = new JArray(
                    array.Select(item => item.SignalId)),
                ["approachNodeIds"] = new JArray(
                    array.Select(item => item.NodeId)),
                ["approaches"] = new JArray(
                    array.Select(item => new JObject
                    {
                        ["id"] = (string)item.Entry["approachId"],
                        ["signalId"] = item.SignalId,
                        ["nodeId"] = item.NodeId,
                        ["segmentIds"] = new JArray(item.SegmentIds),
                        ["protectedSegmentIds"] =
                            new JArray(item.ProtectedSegmentIds),
                    })),
            };
        }

        private DiamondCrossing CalculateDiamondCrossing(
            string segmentAId,
            string segmentBId)
        {
            var segmentA = RequireSegmentById(segmentAId);
            var segmentB = RequireSegmentById(segmentBId);
            if (segmentA == segmentB)
                throw new InvalidOperationException(
                    "Railroad A and Railroad B must use different segments.");
            const int samples = 96;
            var pointsA = Enumerable.Range(0, samples + 1)
                .Select(index => segmentA.Curve.GetPoint(
                    index / (float)samples))
                .ToArray();
            var pointsB = Enumerable.Range(0, samples + 1)
                .Select(index => segmentB.Curve.GetPoint(
                    index / (float)samples))
                .ToArray();
            for (var indexA = 0; indexA < samples; indexA++)
            {
                for (var indexB = 0; indexB < samples; indexB++)
                {
                    if (!TryLineIntersectionXZ(
                            pointsA[indexA],
                            pointsA[indexA + 1],
                            pointsB[indexB],
                            pointsB[indexB + 1],
                            out var fractionA,
                            out var fractionB))
                    {
                        continue;
                    }
                    var parameterA =
                        (indexA + fractionA) / samples;
                    var parameterB =
                        (indexB + fractionB) / samples;
                    var pointA = Vector3.Lerp(
                        pointsA[indexA],
                        pointsA[indexA + 1],
                        fractionA);
                    var pointB = Vector3.Lerp(
                        pointsB[indexB],
                        pointsB[indexB + 1],
                        fractionB);
                    var tangentA = CurveHorizontalTangent(
                        segmentA,
                        parameterA);
                    var tangentB = CurveHorizontalTangent(
                        segmentB,
                        parameterB);
                    var angle = Vector3.Angle(tangentA, tangentB);
                    angle = Mathf.Min(angle, 180f - angle);
                    if (angle < 5f)
                        continue;
                    return new DiamondCrossing
                    {
                        ParameterA = parameterA,
                        ParameterB = parameterB,
                        Point = (pointA + pointB) * 0.5f,
                        VerticalGap = Mathf.Abs(pointA.y - pointB.y),
                        AngleDegrees = angle,
                    };
                }
            }
            throw new InvalidOperationException(
                "The selected track segments do not cross in plan view. "
                + "Choose the two complete segments that form the diamond.");
        }

        private TrackSegment RequireSegmentById(string id)
        {
            var segment = string.IsNullOrWhiteSpace(id)
                ? null
                : _graph?.GetSegment(id.Trim());
            if (segment == null)
                throw new InvalidOperationException(
                    "Track segment '" + (id ?? string.Empty)
                    + "' is not loaded.");
            return segment;
        }

        private DiamondApproachTrace TraceDiamondApproach(
            TrackSegment crossingSegment,
            float crossingParameter,
            float signalDistance,
            float coverageDistance,
            bool startSide)
        {
            var trace = new DiamondApproachTrace();
            var visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var segment = crossingSegment;
            var parameter = crossingParameter;
            var outwardEnd = startSide ? 0f : 1f;
            var previous = segment.Curve.GetPoint(parameter);
            var accumulated = 0f;
            var lastOutward = Vector3.zero;
            for (var chainIndex = 0; chainIndex < 128; chainIndex++)
            {
                if (segment == null || !visited.Add(segment.id))
                    break;
                trace.SegmentIds.Add(segment.id);
                var estimatedLength = Mathf.Max(1f, segment.GetLength());
                var fullSamples = Mathf.Clamp(
                    Mathf.CeilToInt(estimatedLength / 0.5f),
                    32,
                    4096);
                var sectionSamples = Mathf.Max(
                    1,
                    Mathf.CeilToInt(
                        Mathf.Abs(outwardEnd - parameter) * fullSamples));
                for (var sample = 1;
                     sample <= sectionSamples;
                     sample++)
                {
                    var nextParameter = Mathf.Lerp(
                        parameter,
                        outwardEnd,
                        sample / (float)sectionSamples);
                    var current = segment.Curve.GetPoint(nextParameter);
                    var section = Vector3.Distance(previous, current);
                    if (!trace.HasSignalPoint
                        && accumulated + section >= signalDistance
                        && section > 0.0001f)
                    {
                        var fraction =
                            (signalDistance - accumulated) / section;
                        trace.SignalPoint = Vector3.Lerp(
                            previous,
                            current,
                            fraction);
                        var outward = current - previous;
                        if (outward.sqrMagnitude < 0.0001f)
                        {
                            outward = CurveHorizontalTangent(
                                segment,
                                nextParameter);
                            if (outwardEnd < parameter)
                                outward = -outward;
                        }
                        trace.TowardDiamond = -outward.normalized;
                        trace.SignalSegment = segment;
                        trace.ProtectedSegmentIds =
                            trace.SegmentIds.ToList();
                        trace.TrainMovesForward = outwardEnd < parameter;
                        trace.ApproachNode = outwardEnd < 0.5f
                            ? segment.a
                            : segment.b;
                        trace.HasSignalPoint = true;
                    }
                    accumulated += section;
                    lastOutward = current - previous;
                    previous = current;
                    if (trace.HasSignalPoint
                        && accumulated >= coverageDistance)
                    {
                        trace.TracedDistance = accumulated;
                        return trace;
                    }
                }

                var node = outwardEnd < 0.5f ? segment.a : segment.b;
                if (node == null)
                    break;
                if (lastOutward.sqrMagnitude < 0.0001f)
                {
                    lastOutward = outwardEnd < 0.5f
                        ? -CurveHorizontalTangent(segment, 0f)
                        : CurveHorizontalTangent(segment, 1f);
                }
                var candidates = _graph.SegmentsConnectedTo(node)
                    .Where(candidate => candidate != null
                                        && candidate != segment
                                        && !visited.Contains(candidate.id))
                    .ToArray();
                if (candidates.Length == 0)
                    break;
                if (candidates.Length > 1)
                    trace.AmbiguousJunctions++;
                var next = ChooseDiamondContinuation(
                    segment,
                    node,
                    lastOutward,
                    candidates);
                if (next == null)
                    break;
                segment = next;
                parameter = segment.a == node ? 0f : 1f;
                outwardEnd = parameter < 0.5f ? 1f : 0f;
                previous = segment.Curve.GetPoint(parameter);
                lastOutward = Vector3.zero;
            }
            trace.TracedDistance = accumulated;
            if (!trace.HasSignalPoint)
            {
                throw new InvalidOperationException(
                    "The connected approach beginning at segment "
                    + crossingSegment.id + " has only "
                    + accumulated.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)
                    + " m available across "
                    + trace.SegmentIds.Count + " segment(s); reduce the "
                    + "signal setback or extend the track.");
            }
            return trace;
        }

        private TrackSegment ChooseDiamondContinuation(
            TrackSegment current,
            TrackNode sharedNode,
            Vector3 currentOutward,
            IEnumerable<TrackSegment> candidates)
        {
            currentOutward.y = 0f;
            if (currentOutward.sqrMagnitude < 0.0001f)
                currentOutward = Vector3.forward;
            currentOutward.Normalize();
            var currentGauge = GetSegmentGauge(current.id);
            return candidates
                .Select(candidate =>
                {
                    var fromStart = candidate.a == sharedNode;
                    var tangent = CurveHorizontalTangent(
                        candidate,
                        fromStart ? 0f : 1f);
                    if (!fromStart)
                        tangent = -tangent;
                    var score = Vector3.Dot(currentOutward, tangent);
                    if (!string.IsNullOrWhiteSpace(current.groupId)
                        && string.Equals(
                            current.groupId,
                            candidate.groupId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        score += 0.35f;
                    }
                    if (current.trackClass == candidate.trackClass)
                        score += 0.08f;
                    if (current.style == candidate.style)
                        score += 0.04f;
                    if (string.Equals(
                            currentGauge,
                            GetSegmentGauge(candidate.id),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        score += 0.15f;
                    }
                    return new
                    {
                        Segment = candidate,
                        Score = score,
                    };
                })
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Segment.id,
                    StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Segment)
                .FirstOrDefault();
        }

        private static Vector3 CurveHorizontalTangent(
            TrackSegment segment,
            float parameter)
        {
            var delta = 0.002f;
            var before = segment.Curve.GetPoint(
                Mathf.Clamp01(parameter - delta));
            var after = segment.Curve.GetPoint(
                Mathf.Clamp01(parameter + delta));
            var tangent = after - before;
            tangent.y = 0f;
            return tangent.sqrMagnitude < 0.0001f
                ? Vector3.forward
                : tangent.normalized;
        }

        private static TrackNode SharedTrackNode(
            TrackSegment first,
            TrackSegment second)
        {
            if (first == null || second == null)
                return null;
            if (first.a == second.a || first.a == second.b)
                return first.a;
            if (first.b == second.a || first.b == second.b)
                return first.b;
            return null;
        }

        private static bool TryLineIntersectionXZ(
            Vector3 a0,
            Vector3 a1,
            Vector3 b0,
            Vector3 b1,
            out float fractionA,
            out float fractionB)
        {
            var ax = a1.x - a0.x;
            var az = a1.z - a0.z;
            var bx = b1.x - b0.x;
            var bz = b1.z - b0.z;
            var denominator = ax * bz - az * bx;
            if (Mathf.Abs(denominator) < 0.000001f)
            {
                fractionA = 0f;
                fractionB = 0f;
                return false;
            }
            var dx = b0.x - a0.x;
            var dz = b0.z - a0.z;
            fractionA = (dx * bz - dz * bx) / denominator;
            fractionB = (dx * az - dz * ax) / denominator;
            return fractionA >= 0f && fractionA <= 1f
                   && fractionB >= 0f && fractionB <= 1f;
        }

        private void RemoveSignalFromInterlockings(string signalId)
        {
            if (string.IsNullOrWhiteSpace(signalId))
                return;
            foreach (var route in TrainInterlockingsArray
                         .OfType<JObject>()
                         .SelectMany(entry =>
                             (entry["routes"] as JArray
                              ?? new JArray()).OfType<JObject>()))
            {
                if (!(route["signalIds"] is JArray signalIds))
                    continue;
                foreach (var token in signalIds
                             .Where(token => string.Equals(
                                 (string)token,
                                 signalId,
                                 StringComparison.OrdinalIgnoreCase))
                             .ToArray())
                {
                    token.Remove();
                }
            }
        }

        private void RenameSignalInInterlockings(
            string oldId,
            string newId)
        {
            if (string.IsNullOrWhiteSpace(oldId)
                || string.IsNullOrWhiteSpace(newId))
            {
                return;
            }
            foreach (var signalId in TrainInterlockingsArray
                         .OfType<JObject>()
                         .SelectMany(entry =>
                             (entry["routes"] as JArray
                              ?? new JArray()).OfType<JObject>())
                         .Select(route => route["signalIds"] as JArray)
                         .Where(array => array != null)
                         .SelectMany(array => array)
                         .ToArray())
            {
                if (string.Equals(
                        (string)signalId,
                        oldId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    signalId.Replace(new JValue(newId));
                }
            }
        }

        private static string FormatVector(Vector3 value)
        {
            return value.x.ToString("0.0", CultureInfo.InvariantCulture)
                   + ", "
                   + value.y.ToString("0.0", CultureInfo.InvariantCulture)
                   + ", "
                   + value.z.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private sealed class DiamondCrossing
        {
            internal float ParameterA;
            internal float ParameterB;
            internal Vector3 Point;
            internal float VerticalGap;
            internal float AngleDegrees;
        }

        private sealed class DiamondApproach
        {
            internal string SignalId = string.Empty;
            internal string RouteId = string.Empty;
            internal string NodeId = string.Empty;
            internal List<string> SegmentIds = new List<string>();
            internal List<string> ProtectedSegmentIds = new List<string>();
            internal bool UsedMultipleSegments;
            internal int AmbiguousJunctions;
            internal JObject Entry;
        }

        private sealed class DiamondApproachTrace
        {
            internal bool HasSignalPoint;
            internal Vector3 SignalPoint;
            internal Vector3 TowardDiamond;
            internal TrackSegment SignalSegment;
            internal TrackNode ApproachNode;
            internal bool TrainMovesForward;
            internal List<string> SegmentIds = new List<string>();
            internal List<string> ProtectedSegmentIds = new List<string>();
            internal int AmbiguousJunctions;
            internal float TracedDistance;
        }

        private void RefreshTrainSignalOverlays()
        {
            DisposeTrainSignalOverlays();
            if (!_trainSignalMode || !_editModeActive || !GraphOpen)
                return;
            foreach (var signal in TrainSignalsArray
                         .OfType<JObject>()
                         .Select(ReadTrainSignal)
                         .Where(signal => signal != null))
            {
                var go = new GameObject(
                    "TileEditorTrainSignalOverlay-" + signal.Id);
                go.SetActive(false);
                go.transform.position = signal.Position;
                if (WorldTransformer.TryGetShared(out var transformer))
                    transformer.AddObjectToMove(go.transform);
                else
                    go.transform.position =
                        WorldTransformer.GameToWorld(signal.Position);
                go.transform.rotation = Quaternion.Euler(signal.Rotation);
                var overlay = go.AddComponent<TileEditorTrainSignalOverlay>();
                overlay.Initialize(this, signal.Id, signal.HeadCount);
                _trainSignalOverlays[signal.Id] = overlay;
                go.SetActive(true);
            }
            SetTrainSignalOverlaysVisible(true);
        }

        private void RefreshLockedTrainSignalOverlayTransforms()
        {
            if (!_trainSignalMode || !_editModeActive || !GraphOpen)
                return;
            foreach (var signal in TrainSignalsArray
                         .OfType<JObject>()
                         .Select(ReadTrainSignal)
                         .Where(item => item != null && item.TrackLocked))
            {
                if (!_trainSignalOverlays.TryGetValue(
                        signal.Id,
                        out var overlay)
                    || overlay == null)
                {
                    continue;
                }
                overlay.transform.position =
                    WorldTransformer.GameToWorld(signal.Position);
                overlay.transform.rotation =
                    Quaternion.Euler(signal.Rotation);
            }
        }

        private void SetTrainSignalOverlaysVisible(bool visible)
        {
            foreach (var overlay in _trainSignalOverlays.Values)
            {
                if (overlay != null)
                    overlay.SetOverlayVisible(visible);
            }
        }

        private void RefreshTrainSignalOverlayColors()
        {
            foreach (var overlay in _trainSignalOverlays.Values)
                overlay?.RefreshColor();
        }

        private void DisposeTrainSignalOverlays()
        {
            foreach (var overlay in _trainSignalOverlays.Values)
            {
                if (overlay == null)
                    continue;
                try
                {
                    if (WorldTransformer.TryGetShared(out var transformer))
                        transformer.RemoveObjectToMove(overlay.transform);
                }
                catch
                {
                }
                overlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(overlay.gameObject);
            }
            _trainSignalOverlays.Clear();
        }

        private static Type ResolveSignalRuntimeMainType()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "Hrogers.SignalRuntime.Main",
                    false))
                .FirstOrDefault(type => type != null);
        }

        private void ReloadStandaloneSignalRuntime()
        {
            try
            {
                ResolveSignalRuntimeMainType()?
                    .GetMethod(
                        "ReloadDefinitions",
                        BindingFlags.Public | BindingFlags.Static)?
                    .Invoke(null, null);
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not refresh Train Signal Runtime: "
                    + (ex.InnerException?.Message ?? ex.Message));
            }
        }
    }

    internal sealed class TileEditorTrainSignalOverlay
        : MonoBehaviour, IPickable
    {
        private TileEditorGraphSession _session;
        private string _signalId = string.Empty;
        private int _headCount;
        private LineRenderer _line;
        private BoxCollider _collider;

        public float MaxPickDistance => 600f;
        public int Priority => 30;
        public PickableActivationFilter ActivationFilter =>
            PickableActivationFilter.Any;

        public TooltipInfo TooltipInfo =>
            new TooltipInfo(
                "Train Signal " + _signalId,
                _headCount + "-head base-game semaphore\n"
                + "Click to select and edit");

        internal void Initialize(
            TileEditorGraphSession session,
            string signalId,
            int headCount)
        {
            _session = session;
            _signalId = signalId;
            _headCount = headCount;
            gameObject.layer = Layers.Clickable;
            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.startWidth = 0.10f;
            _line.endWidth = 0.10f;
            _line.useWorldSpace = false;
            _line.positionCount = 6;
            _line.SetPositions(new[]
            {
                new Vector3(0f, 0.05f, 0f),
                new Vector3(0f, 4.8f, 0f),
                new Vector3(-0.75f, 4.35f, 0f),
                new Vector3(0.75f, 4.35f, 0f),
                new Vector3(0f, 4.8f, 0f),
                new Vector3(0f, 0.05f, 0f),
            });
            _collider = gameObject.AddComponent<BoxCollider>();
            _collider.center = new Vector3(0f, 2.4f, 0f);
            _collider.size = new Vector3(1.8f, 5.2f, 1.8f);
            RefreshColor();
        }

        public void Activate(PickableActivateEvent evt)
        {
            if (!TileEditorCameraInput.EditorWorldInputBlocked
                && evt.Activation == PickableActivation.Primary)
            {
                _session?.SelectTrainSignal(_signalId);
            }
        }

        public void Deactivate()
        {
        }

        internal void SetOverlayVisible(bool visible)
        {
            enabled = visible;
            if (_line != null)
                _line.enabled = visible;
            if (_collider != null)
                _collider.enabled = visible;
        }

        internal void RefreshColor()
        {
            if (_line == null || _session == null)
                return;
            TileEditorOverlayVisuals.SetColor(
                _line,
                _session.IsSelectedTrainSignal(_signalId)
                    ? Color.magenta
                    : new Color(1f, 0.62f, 0.05f, 1f));
        }
    }
}
