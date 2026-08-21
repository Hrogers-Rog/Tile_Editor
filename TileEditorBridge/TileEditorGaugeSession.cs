using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        private const string StandardGauge = "Standard";
        private static readonly string[] SupportedTrackGauges =
        {
            StandardGauge,
            "Narrow",
            "DualGauge",
            "DualGauge_L",
            "DualGauge_R",
            "DualGauge_T",
        };

        private readonly Dictionary<string, string> _segmentGauges =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingFuseSegmentDefinitions =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        private bool _narrowGaugeFullSyncPending;
        private float _narrowGaugeFullSyncAt;
        private string _newTrackGauge = StandardGauge;

        internal string NewTrackGauge
        {
            get => _newTrackGauge;
            set => _newTrackGauge = NormalizeTrackGauge(value);
        }

        internal string GetSegmentGaugeDisplay(TrackSegment segment)
        {
            return segment == null
                ? StandardGauge
                : GetSegmentGauge(segment.id);
        }

        internal bool GaugeRequiresNarrowGaugeRuntime(string gauge)
        {
            var normalized = NormalizeTrackGauge(gauge);
            return string.Equals(
                       normalized,
                       "Narrow",
                       StringComparison.OrdinalIgnoreCase)
                   || normalized.StartsWith(
                       "DualGauge",
                       StringComparison.OrdinalIgnoreCase);
        }

        internal bool NarrowGaugeRuntimeReady
        {
            get
            {
                var managerType = FindLoadedType(
                    "NarrowGaugeMod.NarrowGaugeManager");
                return managerType != null
                       && FindLoadedType(
                           "FUSE.Runtime.API.TrackAPI") != null
                       && FindLoadedType(
                           "FUSE.Authoring.Data.FuseSegment") != null
                       && Resources.FindObjectsOfTypeAll(
                               managerType)
                           .Length > 0;
            }
        }

        internal string DescribeGaugeRuntime(string gauge)
        {
            if (!GaugeRequiresNarrowGaugeRuntime(gauge))
                return string.Empty;
            if (NarrowGaugeRuntimeReady)
            {
                return "FUSE Narrow Gauge is live. Gauge visuals will "
                       + "synchronize after the track rebuild.";
            }
            if (FindLoadedType("FUSE.Runtime.API.TrackAPI") != null)
            {
                return "Gauge is saved, but FUSE Narrow Gauge is not active. "
                       + "Enable/install FUSE Narrow Gauge and restart "
                       + "Railroader to render 3-foot or dual-gauge rails.";
            }
            return "Gauge is saved as metadata. FUSE and FUSE Narrow Gauge "
                   + "are not loaded; install/enable both and restart "
                   + "Railroader to render 3-foot or dual-gauge rails.";
        }

        internal string SynchronizeNarrowGaugeRuntime()
        {
            if (!NarrowGaugeRuntimeReady)
                return DescribeGaugeRuntime("Narrow");
            PublishFuseSegmentDefinitions(
                _graph?.Segments
                    .Where(segment =>
                        segment != null
                        && !IsGeneratedNarrowGaugeId(segment.id))
                    .Select(segment => segment.id)
                ?? Array.Empty<string>());
            RefreshNarrowGaugeMetadata();
            RequestNarrowGaugeSynchronization();
            ScheduleTrackOverlayRepair(
                Array.Empty<string>(),
                Array.Empty<string>(),
                true);
            return "FUSE Narrow Gauge synchronization requested. "
                   + "The visible rails may take a moment to rebuild.";
        }

        internal Color GetSegmentOverlayColor(TrackSegment segment)
        {
            var gauge = GetSegmentGaugeDisplay(segment);
            if (string.Equals(
                    gauge,
                    "Narrow",
                    StringComparison.OrdinalIgnoreCase))
            {
                return new Color(1f, 0.48f, 0.08f, 1f);
            }
            if (gauge.StartsWith(
                    "DualGauge",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(
                    gauge,
                    "DualGauge_T",
                    StringComparison.OrdinalIgnoreCase)
                    ? new Color(0.96f, 0.25f, 0.82f, 1f)
                    : new Color(0.43f, 0.72f, 1f, 1f);
            }
            return Color.yellow;
        }

        internal bool IsDualGaugeTransition(string gauge)
        {
            return string.Equals(
                NormalizeTrackGauge(gauge),
                "DualGauge_T",
                StringComparison.OrdinalIgnoreCase);
        }

        internal string DescribeSelectedDualGaugeTransition()
        {
            if (_selectedSegment == null
                || !IsDualGaugeTransition(
                    GetSegmentGauge(_selectedSegment.id)))
            {
                return string.Empty;
            }

            var aGauge = GetTransitionNeighborGauge(
                _selectedSegment,
                _selectedSegment.a,
                out var aCount);
            var bGauge = GetTransitionNeighborGauge(
                _selectedSegment,
                _selectedSegment.b,
                out var bCount);
            if (aCount != 1 || bCount != 1)
            {
                return "Transition needs exactly one non-transition "
                       + "dual-gauge segment at each end. Found "
                       + aCount + " at A and " + bCount + " at B.";
            }

            var explicitA = IsExplicitDualGaugeSide(aGauge);
            var explicitB = IsExplicitDualGaugeSide(bGauge);
            if (explicitA && explicitB)
            {
                if (string.Equals(
                        aGauge,
                        bGauge,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "Transition warning: both neighboring segments "
                           + "use " + ShortGaugeName(aGauge)
                           + ". Set one side to DUAL L and the other to "
                           + "DUAL R.";
                }
                return "Transition ready: "
                       + ShortGaugeName(aGauge)
                       + " \u2194 "
                       + ShortGaugeName(bGauge)
                       + ".";
            }

            return "Transition connected, but its neighbors use automatic "
                   + "dual gauge. For a predictable rail crossover, set one "
                   + "neighbor to DUAL L and the other to DUAL R.";
        }

        private string GetTransitionNeighborGauge(
            TrackSegment transition,
            TrackNode node,
            out int count)
        {
            count = 0;
            if (_graph == null || transition == null || node == null)
                return string.Empty;
            var neighbors = _graph.SegmentsConnectedTo(node)
                .Where(segment =>
                    segment != null
                    && segment != transition
                    && !IsGeneratedNarrowGaugeId(segment.id))
                .Select(segment => new
                {
                    Segment = segment,
                    Gauge = GetSegmentGauge(segment.id),
                })
                .Where(item =>
                    item.Gauge.StartsWith(
                        "DualGauge",
                        StringComparison.OrdinalIgnoreCase)
                    && !IsDualGaugeTransition(item.Gauge))
                .ToArray();
            count = neighbors.Length;
            return count == 1
                ? neighbors[0].Gauge
                : string.Empty;
        }

        private static bool IsExplicitDualGaugeSide(string gauge)
        {
            return string.Equals(
                       gauge,
                       "DualGauge_L",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       gauge,
                       "DualGauge_R",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ShortGaugeName(string gauge)
        {
            if (string.Equals(
                    gauge,
                    "DualGauge_L",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "DUAL L";
            }
            if (string.Equals(
                    gauge,
                    "DualGauge_R",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "DUAL R";
            }
            return "DUAL";
        }

        private void ResetGaugeSession()
        {
            _segmentGauges.Clear();
            _pendingFuseSegmentDefinitions.Clear();
            _narrowGaugeFullSyncPending = false;
        }

        private string GetSegmentGauge(string segmentId)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
                return StandardGauge;
            if (_segmentGauges.TryGetValue(
                    segmentId,
                    out var cached))
            {
                return cached;
            }
            var token = _document?["tracks"]?["segments"]?[segmentId];
            var raw = token?["gauge"]?.Value<string>()
                      ?? token?["Gauge"]?.Value<string>()
                      ?? string.Empty;
            var gauge = string.IsNullOrWhiteSpace(raw)
                ? StandardGauge
                : NormalizeTrackGauge(raw);
            _segmentGauges[segmentId] = gauge;
            return gauge;
        }

        private static string NormalizeTrackGauge(string gauge)
        {
            gauge = (gauge ?? string.Empty).Trim();
            if (gauge.Length == 0
                || gauge.Equals(
                    "standard",
                    StringComparison.OrdinalIgnoreCase)
                || gauge.Equals(
                    "std",
                    StringComparison.OrdinalIgnoreCase))
            {
                return StandardGauge;
            }
            if (gauge.Equals(
                    "3ft",
                    StringComparison.OrdinalIgnoreCase)
                || gauge.Equals(
                    "3 ft",
                    StringComparison.OrdinalIgnoreCase)
                || gauge.Equals(
                    "threefoot",
                    StringComparison.OrdinalIgnoreCase)
                || gauge.Equals(
                    "three foot",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Narrow";
            }
            if (gauge.Equals(
                    "dual",
                    StringComparison.OrdinalIgnoreCase)
                || gauge.Equals(
                    "mixed",
                    StringComparison.OrdinalIgnoreCase)
                || gauge.Equals(
                    "mixedgauge",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "DualGauge";
            }
            var canonical = SupportedTrackGauges.FirstOrDefault(
                candidate => candidate.Equals(
                    gauge,
                    StringComparison.OrdinalIgnoreCase));
            if (canonical != null)
                return canonical;
            // Preserve companion-mod values added after this editor build.
            // The UI only authors the canonical values above, but merely
            // selecting or moving newer custom-gauge track must never fail.
            return gauge;
        }

        private List<TrackSegment> CollectThroughChain(
            TrackSegment start)
        {
            var result = new List<TrackSegment>();
            if (start == null || _graph == null)
                return result;
            var visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<TrackSegment>();
            pending.Enqueue(start);
            while (pending.Count > 0)
            {
                var segment = pending.Dequeue();
                if (segment == null
                    || !visited.Add(segment.id))
                {
                    continue;
                }
                result.Add(segment);
                foreach (var node in new[] { segment.a, segment.b })
                {
                    if (node == null)
                        continue;
                    var connected = _graph.SegmentsConnectedTo(node)
                        .Where(candidate => candidate != null)
                        .ToArray();
                    if (connected.Length != 2)
                        continue;
                    var next = connected.FirstOrDefault(
                        candidate => !visited.Contains(candidate.id));
                    if (next != null)
                        pending.Enqueue(next);
                }
            }
            return result;
        }

        private static bool IsGeneratedNarrowGaugeId(string id)
        {
            return !string.IsNullOrWhiteSpace(id)
                   && (id.StartsWith(
                           "fuse-ng:",
                           StringComparison.OrdinalIgnoreCase)
                       || id.StartsWith(
                           "tile-editor-ng:",
                           StringComparison.OrdinalIgnoreCase));
        }

        private void QueueFuseSegmentDefinition(string segmentId)
        {
            if (!string.IsNullOrWhiteSpace(segmentId)
                && !IsGeneratedNarrowGaugeId(segmentId))
            {
                _pendingFuseSegmentDefinitions.Add(segmentId);
            }
        }

        private void FlushQueuedFuseSegmentDefinitions(
            IEnumerable<string> affectedSegmentIds)
        {
            var affected = (affectedSegmentIds
                            ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var queued = _pendingFuseSegmentDefinitions.ToArray();
            _pendingFuseSegmentDefinitions.Clear();
            if (queued.Length > 0)
                PublishFuseSegmentDefinitions(queued);
            if (affected.Length > 0)
                RefreshNarrowGaugeMetadata();
        }

        private void PublishFuseSegmentDefinitions(
            IEnumerable<string> segmentIds)
        {
            if (segmentIds == null || _graph == null)
                return;
            var segments = segmentIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => _graph.GetSegment(id))
                .Where(segment =>
                    segment != null
                    && !IsGeneratedNarrowGaugeId(segment.id))
                .ToArray();
            if (segments.Length == 0)
                return;

            var apiType = FindLoadedType(
                "FUSE.Runtime.API.TrackAPI");
            var definitionType = FindLoadedType(
                "FUSE.Authoring.Data.FuseSegment");
            if (apiType == null || definitionType == null)
                return;
            var getDefinition = apiType.GetMethod(
                "GetSegmentDefinition",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            var update = apiType.GetMethod(
                "UpdateSegment",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), definitionType },
                null);
            var begin = apiType.GetMethod(
                "BeginBatch",
                BindingFlags.Public | BindingFlags.Static);
            var end = apiType.GetMethod(
                "EndBatch",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            var consumePending = apiType.GetMethod(
                "ConsumePendingRebuildRequest",
                BindingFlags.Public | BindingFlags.Static);
            var isBatching = apiType.GetProperty(
                "IsBatching",
                BindingFlags.Public | BindingFlags.Static);
            if (getDefinition == null
                || update == null
                || begin == null
                || end == null)
            {
                return;
            }

            var outerBatch = isBatching != null
                             && (bool)isBatching.GetValue(
                                 null,
                                 null);
            var began = false;
            try
            {
                begin.Invoke(null, null);
                began = true;
                foreach (var segment in segments)
                {
                    try
                    {
                        PublishFuseSegmentDefinitionInBatch(
                            segment,
                            GetSegmentGauge(segment.id),
                            definitionType,
                            getDefinition,
                            update);
                    }
                    catch (TargetInvocationException ex)
                    {
                        _logger?.Warning(
                            "Could not publish live gauge for segment "
                            + segment.id + ": "
                            + (ex.InnerException?.Message
                               ?? ex.Message));
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(
                            "Could not publish live gauge for segment "
                            + segment.id + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                if (began)
                    end.Invoke(null, new object[] { false });
            }

            // UpdateSegment requests a FUSE rebuild. The editor immediately
            // performs a targeted endpoint rebuild, so consume that request
            // when this method owned the outer batch. Leaving it pending
            // makes the next unrelated FUSE batch rebuild the entire map.
            if (!outerBatch && consumePending != null)
                consumePending.Invoke(null, null);
        }

        private void PublishFuseSegmentDefinitionInBatch(
            TrackSegment segment,
            string gauge,
            Type definitionType,
            MethodInfo getDefinition,
            MethodInfo update)
        {
            var definition = getDefinition.Invoke(
                                 null,
                                 new object[] { segment.id })
                             ?? Activator.CreateInstance(
                                 definitionType);
            SetPublicProperty(
                definition,
                "StartNodeId",
                segment.a?.id);
            SetPublicProperty(
                definition,
                "EndNodeId",
                segment.b?.id);
            SetPublicProperty(
                definition,
                "Style",
                segment.style.ToString());
            SetPublicProperty(
                definition,
                "TrackClass",
                segment.trackClass == TrackClass.Mainline
                    ? "main"
                    : segment.trackClass.ToString());
            SetPublicProperty(
                definition,
                "SpeedLimit",
                segment.speedLimit);
            SetPublicProperty(
                definition,
                "Priority",
                segment.priority);
            SetPublicProperty(
                definition,
                "GroupId",
                segment.groupId);
            SetPublicProperty(
                definition,
                "Gauge",
                NormalizeTrackGauge(gauge));
            update.Invoke(
                null,
                new[] { (object)segment.id, definition });
        }

        private void RefreshNarrowGaugeMetadata()
        {
            var managerType = FindLoadedType(
                "NarrowGaugeMod.NarrowGaugeManager");
            var refresh = managerType?.GetMethod(
                "RefreshGaugeMetadata",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Graph) },
                null);
            try
            {
                refresh?.Invoke(null, new object[] { _graph });
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not refresh narrow-gauge metadata: "
                    + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private void ScheduleNarrowGaugeSynchronizationForEdit(
            IEnumerable<string> affectedNodeIds,
            IEnumerable<string> affectedSegmentIds)
        {
            if (!NarrowGaugeRuntimeReady || _graph == null)
                return;
            var segmentIds = (affectedSegmentIds
                              ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var affectedNodes = new HashSet<string>(
                affectedNodeIds
                ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var segmentId in segmentIds)
            {
                var segment = _graph.GetSegment(segmentId);
                if (segment?.a != null)
                    affectedNodes.Add(segment.a.id);
                if (segment?.b != null)
                    affectedNodes.Add(segment.b.id);
            }
            var hasDualGauge = segmentIds.Any(id =>
                    GetSegmentGauge(id).StartsWith(
                        "DualGauge",
                        StringComparison.OrdinalIgnoreCase))
                || affectedNodes
                .Select(id => _graph.GetNode(id))
                .Where(node => node != null)
                .Any(node =>
                    _graph.SegmentsConnectedTo(node).Any(segment =>
                        GetSegmentGauge(segment.id).StartsWith(
                            "DualGauge",
                            StringComparison.OrdinalIgnoreCase)));
            // Pure 3-foot track and turnouts are handled by the targeted
            // endpoint rebuild. Only dual-gauge edits need the expensive
            // ghost-rail and shared-rail topology synchronizer.
            if (!hasDualGauge)
                return;
            _narrowGaugeFullSyncPending = true;
            _narrowGaugeFullSyncAt =
                Time.unscaledTime + 0.65f;
        }

        private void FlushPendingNarrowGaugeSynchronization()
        {
            if (!_narrowGaugeFullSyncPending
                || (_deferredTrackRebuilds && TrackRebuildPending)
                || Time.unscaledTime < _narrowGaugeFullSyncAt)
            {
                return;
            }
            _narrowGaugeFullSyncPending = false;
            RequestNarrowGaugeSynchronization();
        }

        private void RequestNarrowGaugeSynchronization()
        {
            _narrowGaugeFullSyncPending = false;
            var managerType = FindLoadedType(
                "NarrowGaugeMod.NarrowGaugeManager");
            var requestSync = managerType?.GetMethod(
                "RequestSynchronization",
                BindingFlags.Public | BindingFlags.Static);
            try
            {
                requestSync?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not request narrow-gauge synchronization: "
                    + (ex.InnerException?.Message ?? ex.Message));
            }
        }
    }
}
