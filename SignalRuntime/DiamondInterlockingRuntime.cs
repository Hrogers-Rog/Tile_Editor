using System;
using System.Collections.Generic;
using System.Linq;
using Model;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.SignalRuntime
{
    internal sealed class DiamondInterlockingRuntime
    {
        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private readonly Dictionary<string, RuntimeState> _states =
            new Dictionary<string, RuntimeState>(
                StringComparer.OrdinalIgnoreCase);

        internal DiamondInterlockingRuntime(
            UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
        }

        internal void Reset(
            IEnumerable<PlacedDiamondInterlocking> interlockings)
        {
            _states.Clear();
            foreach (var interlocking in interlockings
                         ?? Array.Empty<PlacedDiamondInterlocking>())
            {
                interlocking.Phase = "Stop";
                interlocking.ActiveApproachId = string.Empty;
                interlocking.RequestingApproachIds = Array.Empty<string>();
                interlocking.OccupiedSegmentIds = Array.Empty<string>();
                interlocking.LastTransitionReason =
                    "Interlocking runtime reset";
            }
        }

        internal void Tick(
            IReadOnlyList<PlacedDiamondInterlocking> interlockings,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            var occupancy = ReadOccupancy();
            foreach (var interlocking in interlockings)
            {
                if (!_states.TryGetValue(interlocking.Id, out var state))
                {
                    state = new RuntimeState();
                    _states[interlocking.Id] = state;
                    Transition(
                        interlocking,
                        state,
                        "Stop",
                        "Fail-safe Stop until an approach requests the diamond");
                }
                UpdateInterlocking(
                    interlocking,
                    state,
                    signals,
                    occupancy);
            }
        }

        internal bool TryRequest(
            string interlockingId,
            string approachId,
            IReadOnlyList<PlacedDiamondInterlocking> interlockings,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            var interlocking = FindInterlocking(
                interlockingId,
                interlockings);
            var normalizedApproach = (approachId ?? string.Empty).Trim();
            if (interlocking == null
                || !ApproachSignals(interlocking, signals)
                    .Select(signal => signal.ApproachId)
                    .Contains(
                    normalizedApproach,
                    StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            var state = StateFor(interlocking.Id);
            state.ManualRequestApproachId = normalizedApproach;
            if (!state.WaitingSince.ContainsKey(
                    state.ManualRequestApproachId))
            {
                state.WaitingSince[state.ManualRequestApproachId] =
                    Time.unscaledTime;
            }
            return true;
        }

        internal bool TryRelease(
            string interlockingId,
            IReadOnlyList<PlacedDiamondInterlocking> interlockings)
        {
            var interlocking = FindInterlocking(
                interlockingId,
                interlockings);
            if (interlocking == null)
                return false;
            var state = StateFor(interlocking.Id);
            state.ReleaseRequested = true;
            state.ManualRequestApproachId = string.Empty;
            return true;
        }

        internal bool TrySetAutomatic(
            string interlockingId,
            bool automatic,
            IReadOnlyList<PlacedDiamondInterlocking> interlockings)
        {
            var interlocking = FindInterlocking(
                interlockingId,
                interlockings);
            if (interlocking == null)
                return false;
            interlocking.Automatic = automatic;
            if (!automatic)
                StateFor(interlocking.Id).WaitingSince.Clear();
            return true;
        }

        private void UpdateInterlocking(
            PlacedDiamondInterlocking interlocking,
            RuntimeState state,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals,
            OccupancySnapshot occupancy)
        {
            var occupiedSegments = occupancy.SegmentIds;
            var approaches = ApproachSignals(interlocking, signals).ToArray();
            var centralSegments = new HashSet<string>(
                interlocking.Routes
                    .Select(route => route.SegmentId)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            var diamondRouteSegments = new HashSet<string>(
                interlocking.Routes
                    .SelectMany(route => route.SegmentIds)
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.OrdinalIgnoreCase);
            var diamondOccupied = IsDiamondOccupied(
                interlocking,
                diamondRouteSegments,
                occupancy.Locations);
            interlocking.OccupiedSegmentIds = occupiedSegments
                .Where(id => centralSegments.Contains(id)
                             || interlocking.Routes.Any(route =>
                                 route.SegmentIds.Contains(
                                     id,
                                     StringComparer.OrdinalIgnoreCase)))
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var automaticRequests = interlocking.Automatic
                ? approaches.Where(approach =>
                        IsOccupied(
                            OuterApproachSegments(approach),
                            occupiedSegments))
                    .Select(approach => approach.ApproachId)
                    .ToArray()
                : Array.Empty<string>();
            var requests = automaticRequests
                .Concat(string.IsNullOrWhiteSpace(
                        state.ManualRequestApproachId)
                    ? Array.Empty<string>()
                    : new[] { state.ManualRequestApproachId })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            interlocking.RequestingApproachIds = requests;
            UpdateWaitingTimes(state, requests);

            if (string.IsNullOrWhiteSpace(state.ActiveApproachId))
            {
                if (state.ReleaseRequested)
                {
                    state.ReleaseRequested = false;
                    state.ManualRequestApproachId = string.Empty;
                }
                StopAll(approaches);
                interlocking.ActiveApproachId = string.Empty;
                if (diamondOccupied)
                {
                    Transition(
                        interlocking,
                        state,
                        "Occupied",
                        "Diamond occupied with no route lined; all signals Stop");
                    return;
                }
                var candidate = requests
                    .Select(id => approaches.FirstOrDefault(signal =>
                        string.Equals(
                            signal.ApproachId,
                            id,
                            StringComparison.OrdinalIgnoreCase)))
                    .Where(signal => signal != null)
                    .Where(signal => CanGrant(signal, occupiedSegments))
                    .OrderBy(signal => state.WaitingSince.TryGetValue(
                            signal.ApproachId,
                            out var since)
                        ? since
                        : Time.unscaledTime)
                    .ThenBy(signal => signal.ApproachId,
                        StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (candidate == null)
                {
                    Transition(
                        interlocking,
                        state,
                        requests.Length == 0 ? "Stop" : "Waiting",
                        requests.Length == 0
                            ? "No approach request; all signals Stop"
                            : "Requested route is occupied; waiting at Stop");
                    return;
                }
                state.ActiveApproachId = candidate.ApproachId;
                state.GrantedAt = Time.unscaledTime;
                state.EnteredProtectedBlock = false;
                state.EnteredDiamond = false;
                state.ClearSince = -1f;
                state.ReleaseRequested = false;
                interlocking.ActiveApproachId = candidate.ApproachId;
                candidate.SetAspect("clear");
                Transition(
                    interlocking,
                    state,
                    "Route Lined",
                    "Granted " + candidate.ApproachId
                    + "; all conflicting signals held at Stop");
                return;
            }

            var active = approaches.FirstOrDefault(signal => string.Equals(
                signal.ApproachId,
                state.ActiveApproachId,
                StringComparison.OrdinalIgnoreCase));
            if (active == null)
            {
                StopAll(approaches);
                Release(
                    interlocking,
                    state,
                    "Active approach definition disappeared");
                return;
            }
            interlocking.ActiveApproachId = active.ApproachId;
            var activeRoute = RouteForSignal(interlocking, active.Id);
            var activeCentral = new HashSet<string>(
                activeRoute == null
                    ? Array.Empty<string>()
                    : activeRoute.SegmentIds,
                StringComparer.OrdinalIgnoreCase);
            var activeDiamondOccupied = IsDiamondOccupied(
                interlocking,
                activeCentral,
                occupancy.Locations);
            var innerProtected = InnerProtectedSegments(active);
            if (IsOccupied(innerProtected, occupiedSegments))
                state.EnteredProtectedBlock = true;
            if (activeDiamondOccupied)
                state.EnteredDiamond = true;

            StopAllExcept(approaches, active.Id);
            if (state.EnteredProtectedBlock)
                active.SetAspect("stop");
            else
                active.SetAspect("clear");

            if (state.ReleaseRequested)
            {
                if (diamondOccupied)
                {
                    Transition(
                        interlocking,
                        state,
                        "Occupied",
                        "Manual release refused while the diamond is occupied");
                    return;
                }
                StopAll(approaches);
                Release(interlocking, state, "Manually released");
                return;
            }

            if (state.EnteredDiamond)
            {
                if (diamondOccupied)
                {
                    state.ClearSince = -1f;
                    Transition(
                        interlocking,
                        state,
                        "Occupied",
                        active.ApproachId
                        + " is traversing the diamond; route remains locked");
                    return;
                }
                if (state.ClearSince < 0f)
                    state.ClearSince = Time.unscaledTime;
                var remaining = Mathf.Max(
                    0f,
                    interlocking.ReleaseDelaySeconds
                    - (Time.unscaledTime - state.ClearSince));
                if (remaining > 0f)
                {
                    Transition(
                        interlocking,
                        state,
                        "Releasing",
                        "Diamond clear; release timer "
                        + remaining.ToString("0.0") + " s");
                    return;
                }
                StopAll(approaches);
                Release(
                    interlocking,
                    state,
                    active.ApproachId + " cleared the diamond");
                return;
            }

            var activeStillRequested = requests.Contains(
                active.ApproachId,
                StringComparer.OrdinalIgnoreCase);
            if (!state.EnteredProtectedBlock
                && !activeStillRequested
                && Time.unscaledTime - state.GrantedAt
                >= interlocking.CancelDelaySeconds)
            {
                StopAll(approaches);
                Release(
                    interlocking,
                    state,
                    "Unused route timed out before a train entered");
                return;
            }
            Transition(
                interlocking,
                state,
                state.EnteredProtectedBlock ? "Approach Locked" : "Route Lined",
                state.EnteredProtectedBlock
                    ? active.ApproachId
                      + " entered the protected block; signal returned to Stop"
                    : active.ApproachId + " remains cleared into the diamond");
        }

        private static bool CanGrant(
            PlacedTrainSignal signal,
            ISet<string> occupiedSegments)
        {
            return !IsOccupied(
                InnerProtectedSegments(signal),
                occupiedSegments);
        }

        private static IEnumerable<PlacedTrainSignal> ApproachSignals(
            PlacedDiamondInterlocking interlocking,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            var ids = new HashSet<string>(
                interlocking.Routes.SelectMany(route => route.SignalIds),
                StringComparer.OrdinalIgnoreCase);
            return ids.Select(id => signals.TryGetValue(id, out var signal)
                    ? signal
                    : null)
                .Where(signal => signal != null
                                 && !string.IsNullOrWhiteSpace(
                                     signal.ApproachId));
        }

        private static PlacedInterlockingRoute RouteForSignal(
            PlacedDiamondInterlocking interlocking,
            string signalId)
        {
            return interlocking.Routes.FirstOrDefault(route =>
                route.SignalIds.Contains(
                    signalId,
                    StringComparer.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> InnerProtectedSegments(
            PlacedTrainSignal signal)
        {
            var protectedIds = signal.ProtectedSegmentIds
                               ?? Array.Empty<string>();
            return protectedIds.Take(Math.Max(0, protectedIds.Count - 1));
        }

        private static IEnumerable<string> OuterApproachSegments(
            PlacedTrainSignal signal)
        {
            var protectedIds = signal.ProtectedSegmentIds
                               ?? Array.Empty<string>();
            var approachIds = signal.ApproachSegmentIds
                              ?? Array.Empty<string>();
            return approachIds.Skip(Math.Max(0, protectedIds.Count - 1));
        }

        private static OccupancySnapshot ReadOccupancy()
        {
            var result = new OccupancySnapshot();
            var controller = TrainController.Shared;
            if (controller == null)
                return result;
            foreach (Car car in controller.Cars)
            {
                if (car == null || car.IsInBardo)
                    continue;
                try
                {
                    AddLocation(result, car.WheelBoundsF);
                    AddLocation(result, car.WheelBoundsR);
                    AddLocation(result, car.LocationF);
                    AddLocation(result, car.LocationR);
                }
                catch
                {
                    // A car may be between restore/remove states for one tick.
                }
            }
            return result;
        }

        private static void AddLocation(
            OccupancySnapshot result,
            Track.Location location)
        {
            var id = location.segment?.id;
            if (string.IsNullOrWhiteSpace(id) || !location.IsValid)
                return;
            result.SegmentIds.Add(id);
            result.Locations.Add(location);
        }

        private static bool IsDiamondOccupied(
            PlacedDiamondInterlocking interlocking,
            ISet<string> routeSegmentIds,
            IEnumerable<Track.Location> locations)
        {
            var radius = Mathf.Max(5f, interlocking.ReleaseLength);
            var radiusSquared = radius * radius;
            return locations.Any(location =>
            {
                var segmentId = location.segment?.id;
                if (string.IsNullOrWhiteSpace(segmentId)
                    || !routeSegmentIds.Contains(segmentId))
                {
                    return false;
                }
                var position = location.GetPosition();
                var offset = position - interlocking.CrossingPoint;
                offset.y = 0f;
                return offset.sqrMagnitude <= radiusSquared;
            });
        }

        private static bool IsOccupied(
            IEnumerable<string> segmentIds,
            ISet<string> occupiedSegments)
        {
            return segmentIds.Any(occupiedSegments.Contains);
        }

        private static void StopAll(
            IEnumerable<PlacedTrainSignal> approaches)
        {
            foreach (var signal in approaches)
                signal.SetAspect("stop");
        }

        private static void StopAllExcept(
            IEnumerable<PlacedTrainSignal> approaches,
            string signalId)
        {
            foreach (var signal in approaches)
            {
                if (!string.Equals(
                        signal.Id,
                        signalId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    signal.SetAspect("stop");
                }
            }
        }

        private void Release(
            PlacedDiamondInterlocking interlocking,
            RuntimeState state,
            string reason)
        {
            state.ActiveApproachId = string.Empty;
            state.ManualRequestApproachId = string.Empty;
            state.ReleaseRequested = false;
            state.EnteredProtectedBlock = false;
            state.EnteredDiamond = false;
            state.ClearSince = -1f;
            interlocking.ActiveApproachId = string.Empty;
            Transition(interlocking, state, "Stop", reason);
        }

        private void Transition(
            PlacedDiamondInterlocking interlocking,
            RuntimeState state,
            string phase,
            string reason)
        {
            var changed = !string.Equals(
                state.LastLoggedPhase,
                phase,
                StringComparison.Ordinal);
            interlocking.Phase = phase;
            interlocking.LastTransitionReason = reason;
            state.LastLoggedPhase = phase;
            state.LastLoggedReason = reason;
            if (changed)
            {
                _logger?.Log(
                    "Diamond " + interlocking.Id + ": " + phase
                    + " - " + reason);
            }
        }

        private RuntimeState StateFor(string id)
        {
            if (!_states.TryGetValue(id, out var state))
            {
                state = new RuntimeState();
                _states[id] = state;
            }
            return state;
        }

        private static PlacedDiamondInterlocking FindInterlocking(
            string id,
            IEnumerable<PlacedDiamondInterlocking> interlockings)
        {
            return interlockings.FirstOrDefault(interlocking =>
                string.Equals(
                    interlocking.Id,
                    (id ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        private static void UpdateWaitingTimes(
            RuntimeState state,
            IEnumerable<string> requests)
        {
            var current = new HashSet<string>(
                requests,
                StringComparer.OrdinalIgnoreCase);
            foreach (var id in state.WaitingSince.Keys
                         .Where(id => !current.Contains(id))
                         .ToArray())
            {
                state.WaitingSince.Remove(id);
            }
            foreach (var id in current)
            {
                if (!state.WaitingSince.ContainsKey(id))
                    state.WaitingSince[id] = Time.unscaledTime;
            }
        }

        private sealed class RuntimeState
        {
            internal string ActiveApproachId = string.Empty;
            internal string ManualRequestApproachId = string.Empty;
            internal bool ReleaseRequested;
            internal bool EnteredProtectedBlock;
            internal bool EnteredDiamond;
            internal float GrantedAt;
            internal float ClearSince = -1f;
            internal string LastLoggedPhase = string.Empty;
            internal string LastLoggedReason = string.Empty;
            internal readonly Dictionary<string, float> WaitingSince =
                new Dictionary<string, float>(
                    StringComparer.OrdinalIgnoreCase);
        }

        private sealed class OccupancySnapshot
        {
            internal readonly HashSet<string> SegmentIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<Track.Location> Locations =
                new List<Track.Location>();
        }
    }
}
