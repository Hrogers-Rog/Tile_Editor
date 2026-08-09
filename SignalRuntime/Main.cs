using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HarmonyLib;
using Track.Signals;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.SignalRuntime
{
    public static class Main
    {
        private static UnityModManager.ModEntry _entry;
        private static TrainSignalRegistry _registry;
        private static Harmony _harmony;
        private static string _modsDirectory = string.Empty;
        private static long _nextSlowUpdateLogAt;

        private const double SlowUpdateThresholdMilliseconds = 20d;
        private const int SlowUpdateLogCooldownSeconds = 5;

        internal static string ModsDirectory => _modsDirectory;

        public static IReadOnlyList<PlacedTrainSignal> Signals =>
            _registry?.Signals ?? Array.Empty<PlacedTrainSignal>();

        public static IReadOnlyList<PlacedDiamondInterlocking>
            Interlockings =>
                _registry?.Interlockings
                ?? Array.Empty<PlacedDiamondInterlocking>();

        public static IReadOnlyList<PlacedCtcControlPoint> CtcControlPoints =>
            _registry?.CtcControlPoints
            ?? Array.Empty<PlacedCtcControlPoint>();

        public static IReadOnlyList<PlacedCtcBlock> CtcBlocks =>
            _registry?.CtcBlocks ?? Array.Empty<PlacedCtcBlock>();

        public static IReadOnlyList<PlacedTrainOrder> TrainOrders =>
            _registry?.TrainOrders ?? Array.Empty<PlacedTrainOrder>();

        public static bool TryIssueTrainOrder(string orderId)
        {
            return _registry != null
                   && _registry.TryTrainOrderAction(
                       "issue", orderId, string.Empty);
        }

        public static bool TryDeliverTrainOrder(
            string orderId,
            string trainCrewId)
        {
            return _registry != null
                   && _registry.TryTrainOrderAction(
                       "deliver", orderId, trainCrewId);
        }

        public static bool TryAcknowledgeTrainOrder(string orderId)
        {
            return _registry != null
                   && _registry.TryTrainOrderAction(
                       "acknowledge", orderId, string.Empty);
        }

        public static bool TryFulfillTrainOrder(string orderId)
        {
            return _registry != null
                   && _registry.TryTrainOrderAction(
                       "fulfill", orderId, string.Empty);
        }

        public static bool TryCancelTrainOrder(string orderId)
        {
            return _registry != null
                   && _registry.TryTrainOrderAction(
                       "cancel", orderId, string.Empty);
        }

        public static bool TryGetTrainOrder(
            string orderId,
            out PlacedTrainOrder order)
        {
            order = null;
            return _registry != null
                   && _registry.TryGetTrainOrder(orderId, out order);
        }

        public static bool Load(UnityModManager.ModEntry entry)
        {
            _entry = entry;
            _modsDirectory = string.IsNullOrWhiteSpace(entry.Path)
                ? string.Empty
                : Directory.GetParent(
                    entry.Path.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar))?.FullName
                  ?? string.Empty;
            try
            {
                _harmony = new Harmony(entry.Info.Id);
                _harmony.PatchAll(typeof(Main).Assembly);
            }
            catch (Exception ex)
            {
                _harmony = null;
                entry.Logger.Error(
                    "Could not attach the signal desk to Railroader's "
                    + "Company > Operations window: " + ex);
            }
            _registry = new TrainSignalRegistry(entry.Logger);
            entry.OnUpdate = OnUpdate;
            entry.OnToggle = OnToggle;
            entry.OnUnload = OnUnload;
            ReloadDefinitions();
            entry.Logger.Log(
                "Train Signal Runtime loaded. Base-game semaphore assets "
                + "are loaded from portable train-signals.json files; "
                + "Tile Editor is not required during gameplay.");
            return true;
        }

        public static void ReloadDefinitions()
        {
            _registry?.Reload(ModsDirectory);
        }

        public static bool TrySetAspect(string signalId, string aspect)
        {
            return _registry != null
                   && _registry.TrySetAspect(signalId, aspect);
        }

        public static bool TryGetSignal(
            string signalId,
            out PlacedTrainSignal signal)
        {
            signal = null;
            return _registry != null
                   && _registry.TryGetSignal(signalId, out signal);
        }

        public static bool TryRequestInterlockingRoute(
            string interlockingId,
            string approachId)
        {
            return _registry != null
                   && _registry.TryRequestInterlockingRoute(
                       interlockingId,
                       approachId);
        }

        public static bool TryReleaseInterlocking(string interlockingId)
        {
            return _registry != null
                   && _registry.TryReleaseInterlocking(interlockingId);
        }

        public static bool TrySetInterlockingAutomatic(
            string interlockingId,
            bool automatic)
        {
            return _registry != null
                   && _registry.TrySetInterlockingAutomatic(
                       interlockingId,
                       automatic);
        }

        public static bool TryGetInterlocking(
            string interlockingId,
            out PlacedDiamondInterlocking interlocking)
        {
            interlocking = null;
            return _registry != null
                   && _registry.TryGetInterlocking(
                       interlockingId,
                       out interlocking);
        }

        public static bool TrySetCtcSwitch(
            string controlPointId,
            bool thrown)
        {
            return _registry != null
                   && _registry.TrySetCtcSwitch(controlPointId, thrown);
        }

        public static bool TryLineCtcRoute(
            string controlPointId,
            string routeId)
        {
            return _registry != null
                   && _registry.TryLineCtcRoute(controlPointId, routeId);
        }

        public static bool TryCancelCtcRoute(string controlPointId)
        {
            return _registry != null
                   && _registry.TryCancelCtcRoute(controlPointId);
        }

        public static bool TryGetCtcControlPoint(
            string controlPointId,
            out PlacedCtcControlPoint controlPoint)
        {
            controlPoint = null;
            return _registry != null
                   && _registry.TryGetCtcControlPoint(
                       controlPointId,
                       out controlPoint);
        }

        private static void OnUpdate(
            UnityModManager.ModEntry entry,
            float deltaTime)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                _registry?.Tick(ModsDirectory);
            }
            catch (Exception ex)
            {
                entry.Logger.Error(
                    "Train signal runtime update failed: " + ex);
            }
            finally
            {
                var finishedAt = Stopwatch.GetTimestamp();
                var elapsedMilliseconds =
                    (finishedAt - startedAt) * 1000d / Stopwatch.Frequency;
                if (elapsedMilliseconds >= SlowUpdateThresholdMilliseconds
                    && finishedAt >= _nextSlowUpdateLogAt)
                {
                    _nextSlowUpdateLogAt = finishedAt
                                           + Stopwatch.Frequency
                                           * SlowUpdateLogCooldownSeconds;
                    entry.Logger.Warning(
                        "Slow Signal Runtime update: "
                        + elapsedMilliseconds.ToString("0.0") + " ms.");
                }
            }
        }

        private static bool OnToggle(
            UnityModManager.ModEntry entry,
            bool enabled)
        {
            _registry?.SetEnabled(enabled);
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            _registry?.Dispose();
            _registry = null;
            _harmony?.UnpatchAll(entry.Info.Id);
            _harmony = null;
            _entry = null;
            _modsDirectory = string.Empty;
            return true;
        }
    }

    public sealed class PlacedTrainSignal
    {
        internal CTCSignalModelController ModelController;

        public string Id { get; internal set; } = string.Empty;
        public string InterlockingId { get; internal set; } = string.Empty;
        public string ProtectedNodeId { get; internal set; } = string.Empty;
        public string ProtectedSegmentId { get; internal set; } = string.Empty;
        public IReadOnlyList<string> ProtectedSegmentIds
            { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> ApproachSegmentIds
            { get; internal set; } = Array.Empty<string>();
        public string Direction { get; internal set; } = "forward";
        public string ApproachId { get; internal set; } = string.Empty;
        public bool TrackLocked { get; internal set; }
        public string TrackSegmentId { get; internal set; } = string.Empty;
        public float TrackParameter { get; internal set; }
        public int HeadCount { get; internal set; } = 1;
        public GameObject GameObject { get; internal set; }
        public string CurrentAspect { get; internal set; } = string.Empty;

        public bool SetAspect(string aspect)
        {
            if (ModelController == null
                || !TrainSignalRegistry.TryParseAspect(
                    aspect,
                    out var parsed))
            {
                return false;
            }
            var normalized = parsed.ToString();
            if (string.Equals(
                    CurrentAspect,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            ModelController.DisplayAspect(parsed, Id);
            CurrentAspect = normalized;
            return true;
        }
    }

    public sealed class PlacedDiamondInterlocking
    {
        public string Id { get; internal set; } = string.Empty;
        public Vector3 CrossingPoint { get; internal set; }
        public float CrossingAngleDegrees { get; internal set; }
        public float ApproachLength { get; internal set; }
        public float ReleaseLength { get; internal set; }
        public bool Automatic { get; internal set; } = true;
        public float ReleaseDelaySeconds { get; internal set; } = 3f;
        public float CancelDelaySeconds { get; internal set; } = 120f;
        public string Phase { get; internal set; } = "Stop";
        public string ActiveApproachId { get; internal set; } = string.Empty;
        public IReadOnlyList<string> RequestingApproachIds
            { get; internal set; } = Array.Empty<string>();
        public IReadOnlyList<string> OccupiedSegmentIds
            { get; internal set; } = Array.Empty<string>();
        public string LastTransitionReason { get; internal set; } =
            "Waiting for graph";
        public IReadOnlyList<PlacedInterlockingRoute> Routes
            { get; internal set; } = Array.Empty<PlacedInterlockingRoute>();
    }

    public sealed class PlacedInterlockingRoute
    {
        public string Id { get; internal set; } = string.Empty;
        public string SegmentId { get; internal set; } = string.Empty;
        public IReadOnlyList<string> SegmentIds { get; internal set; } =
            Array.Empty<string>();
        public IReadOnlyList<string> SignalIds { get; internal set; } =
            Array.Empty<string>();
        public IReadOnlyList<string> ApproachNodeIds { get; internal set; } =
            Array.Empty<string>();
    }

    public sealed class PlacedCtcControlPoint
    {
        public string Id { get; internal set; } = string.Empty;
        public string Name { get; internal set; } = string.Empty;
        public float BoardX { get; internal set; }
        public float BoardY { get; internal set; }
        public IReadOnlyList<PlacedCtcSwitch> Switches
            { get; internal set; } = Array.Empty<PlacedCtcSwitch>();
        public IReadOnlyList<PlacedCtcRoute> Routes
            { get; internal set; } = Array.Empty<PlacedCtcRoute>();
        public string Phase { get; internal set; } = "Stop";
        public string ActiveRouteId { get; internal set; } = string.Empty;
        public string LastReason { get; internal set; } = "No route lined";
    }

    public sealed class PlacedCtcSwitch
    {
        public string NodeId { get; internal set; } = string.Empty;
        public string NormalLabel { get; internal set; } = "Main";
        public string ReverseLabel { get; internal set; } = "Diverging";
        public bool IsThrown { get; internal set; }
        public bool Locked { get; internal set; }
    }

    public sealed class PlacedCtcRoute
    {
        public string Id { get; internal set; } = string.Empty;
        public string Label { get; internal set; } = string.Empty;
        public string EntrySignalId { get; internal set; } = string.Empty;
        public IReadOnlyList<string> BlockIds { get; internal set; } =
            Array.Empty<string>();
        public IReadOnlyList<PlacedCtcSwitchSetting> SwitchSettings
            { get; internal set; } = Array.Empty<PlacedCtcSwitchSetting>();
    }

    public sealed class PlacedCtcSwitchSetting
    {
        public string NodeId { get; internal set; } = string.Empty;
        public bool Thrown { get; internal set; }
    }

    public sealed class PlacedCtcBlock
    {
        public string Id { get; internal set; } = string.Empty;
        public string Name { get; internal set; } = string.Empty;
        public string Mode { get; internal set; } = "abs";
        public IReadOnlyList<string> SegmentIds { get; internal set; } =
            Array.Empty<string>();
        public string SignalAId { get; internal set; } = string.Empty;
        public string SignalBId { get; internal set; } = string.Empty;
        public string NextFromAId { get; internal set; } = string.Empty;
        public string NextFromBId { get; internal set; } = string.Empty;
        public bool IsOccupied { get; internal set; }
    }

    public sealed class PlacedTrainOrder
    {
        public string Id { get; internal set; } = string.Empty;
        public int Number { get; internal set; }
        public string Type { get; internal set; } = string.Empty;
        public string TrainId { get; internal set; } = string.Empty;
        public string Crew { get; internal set; } = string.Empty;
        public string From { get; internal set; } = string.Empty;
        public string To { get; internal set; } = string.Empty;
        public string MeetAt { get; internal set; } = string.Empty;
        public string Text { get; internal set; } = string.Empty;
        public string Status { get; internal set; } = "Draft";
        public int Priority { get; internal set; }
        public string Effective { get; internal set; } = string.Empty;
        public string Expires { get; internal set; } = string.Empty;
        public bool RequiresAcknowledgement { get; internal set; }
        public bool EnforceAuthority { get; internal set; } = true;
        public int MaxSpeedMph { get; internal set; }
        public IReadOnlyList<string> AuthorityBlockIds
            { get; internal set; } = Array.Empty<string>();
        public string AssignedCrewId { get; internal set; } = string.Empty;
        public string DeliveredAt { get; internal set; } = string.Empty;
        public string DeliveredBy { get; internal set; } = string.Empty;
        public string AcknowledgedAt { get; internal set; } = string.Empty;
        public string AcknowledgedBy { get; internal set; } = string.Empty;
        public string LastUpdatedAt { get; internal set; } = string.Empty;
        public string LastReason { get; internal set; } = string.Empty;
        public bool IsAuthorityEffective =>
            string.Equals(
                Status,
                "Acknowledged",
                StringComparison.OrdinalIgnoreCase)
            && EnforceAuthority;
    }
}
