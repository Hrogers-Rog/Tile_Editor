using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Game.Messages;
using Game.State;
using Model;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.SignalRuntime
{
    internal sealed class PortableCtcRuntime : IDisposable
    {
        private const string DefinitionFileName = "ctc-system.json";
        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private readonly List<PlacedCtcControlPoint> _controlPoints =
            new List<PlacedCtcControlPoint>();
        private readonly List<PlacedCtcBlock> _blocks =
            new List<PlacedCtcBlock>();
        private readonly List<PlacedTrainOrder> _trainOrders =
            new List<PlacedTrainOrder>();
        private readonly Dictionary<string, RouteState> _routeStates =
            new Dictionary<string, RouteState>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, NodeClaim> _nodeClaims =
            new Dictionary<string, NodeClaim>(
                StringComparer.OrdinalIgnoreCase);
        private readonly CtcMultiplayerSync _multiplayerSync;
        private Graph _claimedGraph;
        private string _fileSignature = string.Empty;
        private float _nextFileCheckAt;
        private string[] _definitionFiles = Array.Empty<string>();

        internal PortableCtcRuntime(
            UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
            _multiplayerSync = new CtcMultiplayerSync(logger);
        }

        internal IReadOnlyList<PlacedCtcControlPoint> ControlPoints =>
            _controlPoints;

        internal IReadOnlyList<PlacedCtcBlock> Blocks => _blocks;
        internal IReadOnlyList<PlacedTrainOrder> TrainOrders => _trainOrders;

        internal void Reload(string modsDirectory)
        {
            ReleaseNodeClaims();
            _controlPoints.Clear();
            _blocks.Clear();
            _trainOrders.Clear();
            _routeStates.Clear();
            var files = DiscoverDefinitionFiles(modsDirectory).ToArray();
            _definitionFiles = files;
            _fileSignature = FileSignature(files);
            var controlPointIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var blockIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var orderIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                LoadFile(file, controlPointIds, blockIds, orderIds);
            _logger?.Log(
                "Loaded portable signaling system: "
                + _controlPoints.Count + " CTC control point(s), "
                + _blocks.Count + " ABS/CTC block(s), and "
                + _trainOrders.Count + " train order(s) from "
                + files.Length + " map file(s).");
        }

        internal void Tick(
            string modsDirectory,
            Graph graph,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            if (Time.unscaledTime >= _nextFileCheckAt)
            {
                _nextFileCheckAt = Time.unscaledTime + 2f;
                // The editor calls the owning Signal Runtime reload after it
                // creates a new sidecar. Polling only known files keeps ordinary
                // gameplay off the recursive Mods-directory scan path.
                var files = _definitionFiles.Where(File.Exists).ToArray();
                var signature = FileSignature(files);
                if (!string.Equals(
                        signature,
                        _fileSignature,
                        StringComparison.Ordinal))
                {
                    Reload(modsDirectory);
                }
            }
            if (graph == null || !graph.HasPopulatedCollections)
            {
                ReleaseNodeClaims();
                return;
            }
            EnsureNodeClaims(graph);
            var occupiedSegments = ReadOccupiedSegments();
            foreach (var block in _blocks)
            {
                block.IsOccupied = block.SegmentIds.Any(
                    occupiedSegments.Contains);
            }
            UpdateAbsSignals(signals);
            if (StateManager.IsHost)
            {
                _multiplayerSync.TickHost(
                    this,
                    graph,
                    signals,
                    _controlPoints);
                UpdateCtcControlPoints(graph, signals);
                _multiplayerSync.PublishIfChanged(_controlPoints);
            }
            else
            {
                _multiplayerSync.ApplyClientState(
                    _controlPoints,
                    signals);
            }
        }

        internal bool TrySetSwitch(
            string controlPointId,
            bool thrown,
            Graph graph)
        {
            var cp = FindControlPoint(controlPointId);
            if (cp == null)
                return false;
            if (!StateManager.IsHost)
            {
                return _multiplayerSync.TryRequest(
                    "switch",
                    cp.Id,
                    string.Empty,
                    thrown);
            }
            var controller = TrainController.Shared;
            if (graph == null || controller == null)
                return false;
            if (StateFor(cp.Id).ActiveRouteId.Length > 0)
                return false;
            var assignment = cp.Switches.FirstOrDefault();
            var node = assignment == null
                ? null
                : graph.GetNode(assignment.NodeId);
            if (node == null
                || !controller.CanSetSwitch(
                    node,
                    thrown,
                    out var _))
            {
                return false;
            }
            ApplySwitch(node, thrown);
            return true;
        }

        internal bool TryLineRoute(
            string controlPointId,
            string routeId,
            Graph graph,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            var cp = FindControlPoint(controlPointId);
            if (cp == null)
                return false;
            if (!StateManager.IsHost)
            {
                return _multiplayerSync.TryRequest(
                    "line",
                    cp.Id,
                    routeId,
                    false);
            }
            var controller = TrainController.Shared;
            var route = cp?.Routes.FirstOrDefault(item => string.Equals(
                item.Id,
                (routeId ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase));
            if (route == null || graph == null
                || controller == null
                || string.IsNullOrWhiteSpace(route.EntrySignalId)
                || !signals.ContainsKey(route.EntrySignalId)
                || route.BlockIds.Count == 0)
            {
                return false;
            }
            var protectedBlocks = BlocksFor(route).ToArray();
            if (protectedBlocks.Length != route.BlockIds
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count()
                || route.SwitchSettings.Count == 0)
            {
                return false;
            }
            var state = StateFor(cp.Id);
            if (state.ActiveRouteId.Length > 0)
                return string.Equals(
                    state.ActiveRouteId,
                    route.Id,
                    StringComparison.OrdinalIgnoreCase);
            if (protectedBlocks.Any(block => block.IsOccupied))
                return false;
            var routeBlockIds = new HashSet<string>(
                route.BlockIds,
                StringComparer.OrdinalIgnoreCase);
            foreach (var other in _controlPoints.Where(item => item != cp))
            {
                var otherState = StateFor(other.Id);
                var otherRoute = other.Routes.FirstOrDefault(item =>
                    string.Equals(
                        item.Id,
                        otherState.ActiveRouteId,
                        StringComparison.OrdinalIgnoreCase));
                if (otherRoute != null
                    && otherRoute.BlockIds.Any(routeBlockIds.Contains))
                {
                    return false;
                }
            }
            var switchChanges = new List<Tuple<TrackNode, bool>>();
            foreach (var setting in route.SwitchSettings)
            {
                var node = graph.GetNode(setting.NodeId);
                if (node == null
                    || !controller.CanSetSwitch(
                        node,
                        setting.Thrown,
                        out var _))
                {
                    return false;
                }
                switchChanges.Add(Tuple.Create(node, setting.Thrown));
            }
            foreach (var change in switchChanges)
                ApplySwitch(change.Item1, change.Item2);
            state.ActiveRouteId = route.Id;
            state.EnteredRoute = false;
            state.ClearSince = -1f;
            cp.ActiveRouteId = route.Id;
            cp.Phase = "Route Lined";
            cp.LastReason = route.Label + " route accepted; switches locked";
            StopControlPointSignals(cp, signals);
            signals[route.EntrySignalId].SetAspect("clear");
            _logger?.Log(
                "CTC " + cp.Id + ": lined " + route.Id
                + " with entry signal " + route.EntrySignalId + ".");
            return true;
        }

        internal bool TryCancelRoute(string controlPointId)
        {
            var cp = FindControlPoint(controlPointId);
            if (cp == null)
                return false;
            if (!StateManager.IsHost)
            {
                return _multiplayerSync.TryRequest(
                    "cancel",
                    cp.Id,
                    string.Empty,
                    false);
            }
            var state = StateFor(cp.Id);
            if (state.ActiveRouteId.Length == 0)
                return true;
            var route = cp.Routes.FirstOrDefault(item => string.Equals(
                item.Id,
                state.ActiveRouteId,
                StringComparison.OrdinalIgnoreCase));
            if (route != null && BlocksFor(route).Any(block => block.IsOccupied))
                return false;
            ReleaseRoute(cp, state, "Dispatcher cancelled clear route");
            return true;
        }

        internal bool TryGetControlPoint(
            string id,
            out PlacedCtcControlPoint controlPoint)
        {
            controlPoint = FindControlPoint(id);
            return controlPoint != null;
        }

        internal void ResetRuntime()
        {
            ReleaseNodeClaims();
            foreach (var cp in _controlPoints)
            {
                cp.Phase = "Stop";
                cp.ActiveRouteId = string.Empty;
                cp.LastReason = "Runtime reset";
            }
            _routeStates.Clear();
        }

        public void Dispose()
        {
            ResetRuntime();
            _multiplayerSync.Dispose();
        }

        private void UpdateAbsSignals(
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            foreach (var block in _blocks)
            {
                if (string.Equals(
                        block.Mode,
                        "manual",
                        StringComparison.OrdinalIgnoreCase))
                {
                    SetSignal(signals, block.SignalAId, "stop");
                    SetSignal(signals, block.SignalBId, "stop");
                    continue;
                }
                var aspectA = AbsAspect(block, block.NextFromAId);
                var aspectB = AbsAspect(block, block.NextFromBId);
                SetSignal(signals, block.SignalAId, aspectA);
                SetSignal(signals, block.SignalBId, aspectB);
            }
        }

        private string AbsAspect(PlacedCtcBlock block, string nextBlockId)
        {
            if (block.IsOccupied)
                return "stop";
            if (!string.IsNullOrWhiteSpace(nextBlockId))
            {
                var next = _blocks.FirstOrDefault(item => string.Equals(
                    item.Id,
                    nextBlockId,
                    StringComparison.OrdinalIgnoreCase));
                if (next == null || next.IsOccupied)
                    return "approach";
            }
            return "clear";
        }

        private void UpdateCtcControlPoints(
            Graph graph,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            foreach (var cp in _controlPoints)
            {
                foreach (var assignment in cp.Switches)
                {
                    var node = graph.GetNode(assignment.NodeId);
                    assignment.IsThrown = node != null && node.isThrown;
                    assignment.Locked = StateFor(cp.Id).ActiveRouteId.Length > 0;
                }
                StopControlPointSignals(cp, signals);
                var state = StateFor(cp.Id);
                if (state.ActiveRouteId.Length == 0)
                {
                    cp.ActiveRouteId = string.Empty;
                    cp.Phase = "Stop";
                    if (string.IsNullOrWhiteSpace(cp.LastReason))
                        cp.LastReason = "No route lined";
                    continue;
                }
                var route = cp.Routes.FirstOrDefault(item => string.Equals(
                    item.Id,
                    state.ActiveRouteId,
                    StringComparison.OrdinalIgnoreCase));
                if (route == null)
                {
                    ReleaseRoute(cp, state, "Route definition disappeared");
                    continue;
                }
                var correspondence = route.SwitchSettings.All(setting =>
                {
                    var node = graph.GetNode(setting.NodeId);
                    return node != null && node.isThrown == setting.Thrown;
                });
                if (!correspondence)
                {
                    cp.Phase = "Stop / Switch Out of Correspondence";
                    cp.LastReason = "A route switch is not in its coded position";
                    continue;
                }
                var occupied = BlocksFor(route).Any(block => block.IsOccupied);
                if (occupied)
                {
                    state.EnteredRoute = true;
                    state.ClearSince = -1f;
                    cp.Phase = "Occupied / Route Locked";
                    cp.LastReason = route.Label
                                    + " movement occupies its protected block";
                    continue;
                }
                if (state.EnteredRoute)
                {
                    if (state.ClearSince < 0f)
                        state.ClearSince = Time.unscaledTime;
                    if (Time.unscaledTime - state.ClearSince >= 3f)
                    {
                        ReleaseRoute(
                            cp,
                            state,
                            route.Label + " movement cleared the route");
                        continue;
                    }
                    cp.Phase = "Releasing";
                    cp.LastReason = "Block clear; time release running";
                    continue;
                }
                if (signals.TryGetValue(route.EntrySignalId, out var signal))
                    signal.SetAspect("clear");
                cp.Phase = "Route Lined";
                cp.ActiveRouteId = route.Id;
                cp.LastReason = route.Label + " route clear";
            }
        }

        private void ReleaseRoute(
            PlacedCtcControlPoint cp,
            RouteState state,
            string reason)
        {
            state.ActiveRouteId = string.Empty;
            state.EnteredRoute = false;
            state.ClearSince = -1f;
            cp.ActiveRouteId = string.Empty;
            cp.Phase = "Stop";
            cp.LastReason = reason;
            _logger?.Log("CTC " + cp.Id + ": " + reason + ".");
        }

        private IEnumerable<PlacedCtcBlock> BlocksFor(PlacedCtcRoute route)
        {
            var ids = new HashSet<string>(
                route.BlockIds,
                StringComparer.OrdinalIgnoreCase);
            return _blocks.Where(block => ids.Contains(block.Id));
        }

        private static void StopControlPointSignals(
            PlacedCtcControlPoint cp,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            foreach (var signalId in cp.Routes
                         .Select(route => route.EntrySignalId)
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                SetSignal(signals, signalId, "stop");
            }
        }

        private static void SetSignal(
            IReadOnlyDictionary<string, PlacedTrainSignal> signals,
            string id,
            string aspect)
        {
            if (!string.IsNullOrWhiteSpace(id)
                && signals.TryGetValue(id.Trim(), out var signal))
            {
                signal.SetAspect(aspect);
            }
        }

        private void EnsureNodeClaims(Graph graph)
        {
            if (_claimedGraph == graph && _nodeClaims.Count > 0)
                return;
            ReleaseNodeClaims();
            _claimedGraph = graph;
            foreach (var switchId in _controlPoints
                         .SelectMany(cp => cp.Switches)
                         .Select(item => item.NodeId)
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var node = graph.GetNode(switchId);
                if (node == null)
                    continue;
                _nodeClaims[switchId] = new NodeClaim
                {
                    Node = node,
                    WasCtc = node.IsCTCSwitch,
                    WasUnlocked = node.IsCTCSwitchUnlocked,
                };
                node.IsCTCSwitch = true;
                node.IsCTCSwitchUnlocked = false;
            }
        }

        private void ReleaseNodeClaims()
        {
            foreach (var claim in _nodeClaims.Values)
            {
                if (claim.Node == null)
                    continue;
                claim.Node.IsCTCSwitch = claim.WasCtc;
                claim.Node.IsCTCSwitchUnlocked = claim.WasUnlocked;
            }
            _nodeClaims.Clear();
            _claimedGraph = null;
        }

        private static void ApplySwitch(TrackNode node, bool thrown)
        {
            if (node.isThrown == thrown)
                return;
            StateManager.ApplyLocal(new SetSwitch(
                node.id,
                thrown,
                StateManager.Now,
                "Hrogers Portable CTC"));
        }

        private PlacedCtcControlPoint FindControlPoint(string id)
        {
            return _controlPoints.FirstOrDefault(item => string.Equals(
                item.Id,
                (id ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase));
        }

        private RouteState StateFor(string id)
        {
            if (!_routeStates.TryGetValue(id, out var state))
            {
                state = new RouteState();
                _routeStates[id] = state;
            }
            return state;
        }

        private void LoadFile(
            string path,
            ISet<string> knownControlPointIds,
            ISet<string> knownBlockIds,
            ISet<string> knownOrderIds)
        {
            try
            {
                var document = JObject.Parse(File.ReadAllText(path));
                foreach (var entry in (document["blocks"] as JArray
                                      ?? new JArray()).OfType<JObject>())
                {
                    var id = Text(entry["id"]);
                    if (id.Length == 0 || !knownBlockIds.Add(id))
                        continue;
                    _blocks.Add(new PlacedCtcBlock
                    {
                        Id = id,
                        Name = Text(entry["name"], id),
                        Mode = Text(entry["mode"], "abs").ToLowerInvariant(),
                        SegmentIds = Strings(entry["segmentIds"] as JArray),
                        SignalAId = Text(entry["signals"]?["a"]),
                        SignalBId = Text(entry["signals"]?["b"]),
                        NextFromAId = Text(entry["nextBlocks"]?["fromA"]),
                        NextFromBId = Text(entry["nextBlocks"]?["fromB"]),
                    });
                }
                foreach (var entry in (document["controlPoints"] as JArray
                                      ?? new JArray()).OfType<JObject>())
                {
                    var id = Text(entry["id"]);
                    if (id.Length == 0 || !knownControlPointIds.Add(id))
                        continue;
                    var switches = (entry["switches"] as JArray
                                    ?? new JArray()).OfType<JObject>()
                        .Select(item => new PlacedCtcSwitch
                        {
                            NodeId = Text(item["nodeId"]),
                            NormalLabel = Text(item["normalLabel"], "Main"),
                            ReverseLabel = Text(
                                item["reverseLabel"], "Diverging"),
                        }).Where(item => item.NodeId.Length > 0).ToArray();
                    var routes = (entry["routes"] as JArray
                                  ?? new JArray()).OfType<JObject>()
                        .Select(route => new PlacedCtcRoute
                        {
                            Id = Text(route["id"]),
                            Label = Text(route["label"]),
                            EntrySignalId = Text(route["entrySignalId"]),
                            BlockIds = Strings(route["blockIds"] as JArray),
                            SwitchSettings = (route["switchSettings"] as JArray
                                              ?? new JArray())
                                .OfType<JObject>()
                                .Select(setting =>
                                    new PlacedCtcSwitchSetting
                                    {
                                        NodeId = Text(setting["nodeId"]),
                                        Thrown =
                                            (bool?)setting["thrown"] ?? false,
                                    }).Where(setting =>
                                    setting.NodeId.Length > 0).ToArray(),
                        }).Where(route => route.Id.Length > 0).ToArray();
                    _controlPoints.Add(new PlacedCtcControlPoint
                    {
                        Id = id,
                        Name = Text(entry["name"], id),
                        BoardX = Number(entry["board"]?["x"]),
                        BoardY = Number(entry["board"]?["y"]),
                        Switches = switches,
                        Routes = routes,
                    });
                }
                foreach (var entry in (document["trainOrders"] as JArray
                                      ?? new JArray()).OfType<JObject>())
                {
                    var id = Text(entry["id"]);
                    if (id.Length == 0 || !knownOrderIds.Add(id))
                        continue;
                    var type = Text(entry["type"], "Form 19");
                    _trainOrders.Add(new PlacedTrainOrder
                    {
                        Id = id,
                        Number = (int?)entry["number"] ?? 0,
                        Type = type,
                        TrainId = Text(entry["trainId"]),
                        Crew = Text(entry["crew"]),
                        AssignedCrewId = Text(entry["crewId"]),
                        From = Text(entry["limits"]?["from"]),
                        To = Text(entry["limits"]?["to"]),
                        MeetAt = Text(entry["meetAt"]),
                        Text = Text(entry["text"]),
                        Status = Text(entry["status"], "Draft"),
                        Priority = (int?)entry["priority"] ?? 0,
                        Effective = Text(entry["effective"]),
                        Expires = Text(entry["expires"]),
                        RequiresAcknowledgement =
                            (bool?)entry["requiresAcknowledgement"] ?? true,
                        EnforceAuthority =
                            (bool?)entry["authority"]?["enforce"] ?? true,
                        MaxSpeedMph =
                            (int?)entry["authority"]?["maxSpeedMph"] ?? 0,
                        AuthorityBlockIds = Strings(
                            entry["authority"]?["blockIds"] as JArray),
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    "Could not load portable CTC file " + path + ": "
                    + ex.Message);
            }
        }

        private static HashSet<string> ReadOccupiedSegments()
        {
            var result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var controller = TrainController.Shared;
            if (controller == null)
                return result;
            foreach (Car car in controller.Cars)
            {
                if (car == null || car.IsInBardo)
                    continue;
                try
                {
                    Add(result, car.WheelBoundsF);
                    Add(result, car.WheelBoundsR);
                    Add(result, car.LocationF);
                    Add(result, car.LocationR);
                }
                catch
                {
                    // Cars can briefly be between restore/remove states.
                }
            }
            return result;
        }

        private static void Add(ISet<string> result, Location location)
        {
            var id = location.segment?.id;
            if (!string.IsNullOrWhiteSpace(id) && location.IsValid)
                result.Add(id);
        }

        private static IEnumerable<string> DiscoverDefinitionFiles(
            string modsDirectory)
        {
            if (string.IsNullOrWhiteSpace(modsDirectory)
                || !Directory.Exists(modsDirectory))
            {
                yield break;
            }
            foreach (var root in Directory.GetDirectories(modsDirectory))
            {
                var pending = new Stack<string>();
                pending.Push(root);
                while (pending.Count > 0)
                {
                    var directory = pending.Pop();
                    if (Ignored(directory, root))
                        continue;
                    var file = Path.Combine(directory, DefinitionFileName);
                    if (File.Exists(file))
                        yield return file;
                    string[] children;
                    try
                    {
                        children = Directory.GetDirectories(directory);
                    }
                    catch
                    {
                        continue;
                    }
                    foreach (var child in children)
                        pending.Push(child);
                }
            }
        }

        private static bool Ignored(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
                return false;
            var name = Path.GetFileName(path) ?? string.Empty;
            return name.StartsWith("backup", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("_", StringComparison.Ordinal)
                   || string.Equals(name, "Cache",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, ".venv",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "__pycache__",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string FileSignature(IEnumerable<string> files) =>
            string.Join(
                "|",
                files.OrderBy(path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(path =>
                    {
                        var file = new FileInfo(path);
                        return file.FullName + ":" + file.Length + ":"
                               + file.LastWriteTimeUtc.Ticks;
                    }));

        private static IReadOnlyList<string> Strings(JArray array) =>
            (array ?? new JArray()).Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static string Text(JToken token, string fallback = "")
        {
            var value = token?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static float Number(JToken token)
        {
            return token != null && float.TryParse(
                token.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0f;
        }

        private sealed class RouteState
        {
            internal string ActiveRouteId = string.Empty;
            internal bool EnteredRoute;
            internal float ClearSince = -1f;
        }

        private sealed class NodeClaim
        {
            internal TrackNode Node;
            internal bool WasCtc;
            internal bool WasUnlocked;
        }
    }
}
