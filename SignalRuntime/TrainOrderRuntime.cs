using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game;
using Game.AccessControl;
using Game.Messages;
using Game.Notices;
using Game.State;
using KeyValue.Runtime;
using Model;
using Model.AI;
using Network;
using Network.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.SignalRuntime
{
    internal sealed class TrainOrderRuntime : IDisposable
    {
        private const string PropertyObjectId =
            "hrogers.signal-runtime.train-orders.v1";
        private const string CatalogKey = "catalog";
        private const string RevisionKey = "revision";
        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private readonly List<PlacedTrainOrder> _definitionOrders =
            new List<PlacedTrainOrder>();
        private readonly List<PlacedCtcBlock> _definitionBlocks =
            new List<PlacedCtcBlock>();
        private readonly List<PlacedTrainOrder> _orders =
            new List<PlacedTrainOrder>();
        private readonly Dictionary<string, ManualStopClaim> _stopClaims =
            new Dictionary<string, ManualStopClaim>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastTrainNotice =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        private GameObject _root;
        private KeyValueObject _properties;
        private StateManager _registeredStateManager;
        private TrainOrderOverlay _overlay;
        private string _definitionSignature = string.Empty;
        private string _lastCatalog = string.Empty;
        private int _revision;

        internal TrainOrderRuntime(
            UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
        }

        internal IReadOnlyList<PlacedTrainOrder> Orders => _orders
            .OrderByDescending(order => order.Priority)
            .ThenByDescending(order => order.Number)
            .ThenBy(order => order.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        internal void ReloadDefinitions(
            IReadOnlyList<PlacedTrainOrder> orders,
            IReadOnlyList<PlacedCtcBlock> blocks)
        {
            _definitionOrders.Clear();
            _definitionOrders.AddRange((orders ?? Array.Empty<PlacedTrainOrder>())
                .Select(CloneOrder));
            _definitionBlocks.Clear();
            _definitionBlocks.AddRange((blocks ?? Array.Empty<PlacedCtcBlock>())
                .Select(CloneBlock));
            _definitionSignature = DefinitionSignature(
                _definitionOrders,
                _definitionBlocks);
            if (StateManager.IsHost)
            {
                MergeDefinitions();
                Publish("Definitions reloaded");
            }
        }

        internal void Tick(
            Graph graph,
            IReadOnlyList<PlacedTrainOrder> orders,
            IReadOnlyList<PlacedCtcBlock> blocks)
        {
            var signature = DefinitionSignature(orders, blocks);
            if (!string.Equals(
                    signature,
                    _definitionSignature,
                    StringComparison.Ordinal))
            {
                ReloadDefinitions(orders, blocks);
            }
            EnsureSynchronization();
            if (_properties == null)
                return;
            ReadCatalogIfChanged();
            if (!StateManager.IsHost)
                return;
            ProcessRequests();
            EnforceMovementAuthorities(graph);
        }

        internal bool TryRequest(
            string action,
            string orderId,
            string trainCrewId)
        {
            EnsureSynchronization();
            if (_properties == null || StateManager.Shared == null)
                return false;
            var normalizedAction = (action ?? string.Empty)
                .Trim().ToLowerInvariant();
            if (!(new[]
                {
                    "issue", "deliver", "acknowledge", "fulfill", "cancel",
                }).Contains(normalizedAction))
            {
                return false;
            }
            if (FindOrder(orderId) == null)
                return false;
            var playerId = PlayersManager.PlayerId.String;
            if (string.IsNullOrWhiteSpace(playerId))
                return false;
            var request = new JObject
            {
                ["nonce"] = Guid.NewGuid().ToString("N"),
                ["action"] = normalizedAction,
                ["orderId"] = (orderId ?? string.Empty).Trim(),
                ["trainCrewId"] = (trainCrewId ?? string.Empty).Trim(),
            };
            // PlayerIdKey authorization requires the property key to be the
            // exact authenticated multiplayer player id.
            _properties[playerId] =
                Value.String(request.ToString(Formatting.None));
            return true;
        }

        internal bool TryGetOrder(
            string orderId,
            out PlacedTrainOrder order)
        {
            order = FindOrder(orderId);
            return order != null;
        }

        internal void ResetRuntime()
        {
            ReleaseAllStopClaims();
            _lastTrainNotice.Clear();
        }

        public void Dispose()
        {
            ResetRuntime();
            UnregisterPropertyObject();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _properties = null;
            _overlay = null;
        }

        private void EnsureSynchronization()
        {
            var stateManager = StateManager.Shared;
            if (stateManager == null)
            {
                if (_registeredStateManager != null)
                    UnregisterPropertyObject();
                return;
            }
            if (_registeredStateManager == stateManager
                && _properties != null)
            {
                return;
            }
            UnregisterPropertyObject();
            if (_root == null)
            {
                _root = new GameObject("Hrogers Train Order Runtime")
                {
                    hideFlags = HideFlags.DontSave,
                };
                _properties = _root.AddComponent<KeyValueObject>();
                _overlay = _root.AddComponent<TrainOrderOverlay>();
                _overlay.Configure(this);
            }
            _registeredStateManager = stateManager;
            stateManager.RegisterPropertyObject(
                PropertyObjectId,
                _properties,
                new TrainOrderPropertyAccess());
            _lastCatalog = string.Empty;
            var catalog = _properties[CatalogKey].StringValue;
            if (!string.IsNullOrWhiteSpace(catalog))
            {
                ConsumeCatalog(catalog);
            }
            else if (StateManager.IsHost)
            {
                MergeDefinitions();
                Publish("Host initialized train-order desk");
            }
        }

        private void UnregisterPropertyObject()
        {
            if (_registeredStateManager != null
                && !StateManager.IsUnloading)
            {
                try
                {
                    _registeredStateManager.UnregisterPropertyObject(
                        PropertyObjectId);
                }
                catch
                {
                }
            }
            if (_properties != null)
            {
                _properties.ResetData(
                    new Dictionary<string, Value>(),
                    SetValueOrigin.Remote);
            }
            _registeredStateManager = null;
            _lastCatalog = string.Empty;
        }

        private void ProcessRequests()
        {
            var keys = _properties.Keys
                .Where(key => !string.Equals(
                                  key,
                                  CatalogKey,
                                  StringComparison.OrdinalIgnoreCase)
                              && !string.Equals(
                                  key,
                                  RevisionKey,
                                  StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var key in keys)
            {
                var raw = _properties[key].StringValue;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var playerIdText = key;
                try
                {
                    var request = JObject.Parse(raw);
                    HandleRequest(
                        new PlayerId(playerIdText),
                        Text(request["action"]),
                        Text(request["orderId"]),
                        Text(request["trainCrewId"]));
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Rejected malformed train-order request from "
                        + playerIdText + ": " + ex.Message);
                }
                finally
                {
                    _properties[key] = Value.Null();
                }
            }
        }

        private void HandleRequest(
            PlayerId playerId,
            string action,
            string orderId,
            string trainCrewId)
        {
            var order = FindOrder(orderId);
            if (order == null)
            {
                Reject(playerId, "Train order was not found.");
                return;
            }
            var player = StateManager.Shared.PlayersManager.PlayerForId(
                playerId);
            var playerName = player?.Name ?? playerId.String;
            if (string.Equals(
                    action,
                    "acknowledge",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!CanAcknowledge(playerId, order, out var reason))
                {
                    Reject(playerId, reason);
                    return;
                }
                order.Status = "Acknowledged";
                order.AcknowledgedBy = playerName;
                order.AcknowledgedAt = Timestamp();
                order.LastUpdatedAt = order.AcknowledgedAt;
                order.LastReason = "Repeated and acknowledged by "
                                   + playerName;
                Publish(order.LastReason);
                Multiplayer.Broadcast(
                    "Train order No. " + order.Number
                    + " acknowledged by " + playerName + ".");
                return;
            }
            if (!HasDispatcherAccess(playerId))
            {
                Reject(
                    playerId,
                    "Dispatcher access is required for that train-order "
                    + "action.");
                return;
            }
            switch ((action ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "issue":
                    if (IsTerminal(order.Status))
                    {
                        Reject(playerId, "A closed order cannot be issued.");
                        return;
                    }
                    order.Status = "Issued";
                    order.LastReason = "Issued by " + playerName;
                    break;
                case "deliver":
                    if (!StateManager.Shared.PlayersManager.TrainCrewForId(
                            trainCrewId,
                            out var crew))
                    {
                        Reject(playerId, "Select a valid train crew.");
                        return;
                    }
                    if (IsTerminal(order.Status))
                    {
                        Reject(playerId, "A closed order cannot be delivered.");
                        return;
                    }
                    order.AssignedCrewId = crew.Id;
                    order.Status = "Delivered";
                    order.DeliveredBy = playerName;
                    order.DeliveredAt = Timestamp();
                    order.AcknowledgedBy = string.Empty;
                    order.AcknowledgedAt = string.Empty;
                    order.LastReason = "Delivered to " + crew.Name
                                       + " by " + playerName;
                    NotifyCrew(
                        crew,
                        "Train order No. " + order.Number + " ("
                        + order.Type + ") delivered. Press F8 to read, "
                        + "repeat, and acknowledge it.");
                    break;
                case "fulfill":
                    order.Status = "Fulfilled";
                    order.LastReason = "Marked fulfilled by " + playerName;
                    break;
                case "cancel":
                    order.Status = "Cancelled";
                    order.LastReason = "Cancelled by " + playerName;
                    break;
                default:
                    Reject(playerId, "Unknown train-order action.");
                    return;
            }
            order.LastUpdatedAt = Timestamp();
            Publish(order.LastReason);
            Multiplayer.Broadcast(
                "Train order No. " + order.Number + ": "
                + order.LastReason + ".");
        }

        private bool CanAcknowledge(
            PlayerId playerId,
            PlacedTrainOrder order,
            out string reason)
        {
            reason = string.Empty;
            if (!string.Equals(
                    order.Status,
                    "Delivered",
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Only a delivered order can be acknowledged.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(order.AssignedCrewId)
                || !StateManager.Shared.PlayersManager.TrainCrewForId(
                    order.AssignedCrewId,
                    out var crew)
                || !crew.MemberPlayerIds.Contains(playerId))
            {
                reason = "Only a member of the assigned train crew may "
                         + "acknowledge this order.";
                return false;
            }
            return true;
        }

        private static bool HasDispatcherAccess(PlayerId playerId)
        {
            if (StateManager.IsHost && playerId == PlayersManager.PlayerId)
                return true;
            return StateManager.Shared.PlayersManager.TryGetAccessLevel(
                       playerId,
                       out var level)
                   && level >= AccessLevel.Dispatcher;
        }

        private static void Reject(PlayerId playerId, string message)
        {
            var player = StateManager.Shared?.PlayersManager?.PlayerForId(
                playerId);
            if (player != null)
                Multiplayer.SendError(player, message, AlertLevel.Error);
        }

        private static void NotifyCrew(TrainCrew crew, string message)
        {
            foreach (var playerId in crew.MemberPlayerIds)
            {
                var player = StateManager.Shared.PlayersManager.PlayerForId(
                    playerId);
                if (player != null)
                    Multiplayer.SendError(player, message, AlertLevel.Info);
            }
        }

        private void EnforceMovementAuthorities(Graph graph)
        {
            if (graph == null || TrainController.Shared == null)
            {
                ReleaseAllStopClaims();
                return;
            }
            var locomotives = TrainController.Shared.Cars
                .OfType<BaseLocomotive>()
                .Where(locomotive => !locomotive.IsInBardo)
                .ToArray();
            var claimedIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var locomotive in locomotives)
            {
                var crewId = (locomotive.trainCrewId ?? string.Empty).Trim();
                if (crewId.Length == 0)
                    continue;
                var crewOrders = _orders.Where(order =>
                        order.EnforceAuthority
                        && string.Equals(
                            order.AssignedCrewId,
                            crewId,
                            StringComparison.OrdinalIgnoreCase)
                        && !IsTerminal(order.Status))
                    .ToArray();
                if (crewOrders.Length == 0)
                    continue;
                var pending = crewOrders.FirstOrDefault(order =>
                    string.Equals(
                        order.Status,
                        "Delivered",
                        StringComparison.OrdinalIgnoreCase));
                if (pending != null)
                {
                    ApplyRestriction(
                        locomotive,
                        0f,
                        "Train order No. " + pending.Number
                        + " requires crew acknowledgement",
                        claimedIds);
                    continue;
                }
                var active = crewOrders.Where(order =>
                        string.Equals(
                            order.Status,
                            "Acknowledged",
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (active.Length == 0)
                    continue;
                var hold = active.FirstOrDefault(order => string.Equals(
                    order.Type,
                    "Hold Order",
                    StringComparison.OrdinalIgnoreCase));
                if (hold != null)
                {
                    ApplyRestriction(
                        locomotive,
                        0f,
                        "Hold order No. " + hold.Number,
                        claimedIds);
                    continue;
                }
                var allowed = ResolveAllowedSegments(active, graph);
                if (allowed.Count == 0)
                {
                    ApplyRestriction(
                        locomotive,
                        0f,
                        "No valid track blocks are assigned to the active "
                        + "movement authority",
                        claimedIds);
                    continue;
                }
                var distance = DistanceToAuthorityLimit(
                    locomotive,
                    graph,
                    allowed);
                if (distance.HasValue)
                {
                    ApplyRestriction(
                        locomotive,
                        distance.Value,
                        distance.Value <= 0.5f
                            ? "Movement authority limit reached"
                            : "Movement authority limit ahead",
                        claimedIds);
                }
                var maximum = active.Where(order => order.MaxSpeedMph > 0)
                    .Select(order => order.MaxSpeedMph)
                    .DefaultIfEmpty(0)
                    .Min();
                if (maximum > 0
                    && locomotive.VelocityMphAbs > maximum + 1f)
                {
                    ApplySpeedRestriction(locomotive, maximum);
                }
            }
            foreach (var id in _stopClaims.Keys
                         .Where(id => !claimedIds.Contains(id)).ToArray())
            {
                ReleaseStopClaim(id);
            }
        }

        private HashSet<string> ResolveAllowedSegments(
            IEnumerable<PlacedTrainOrder> active,
            Graph graph)
        {
            var allowed = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var blocks = _definitionBlocks.ToDictionary(
                block => block.Id,
                StringComparer.OrdinalIgnoreCase);
            foreach (var order in active)
            {
                var ids = order.AuthorityBlockIds.Count > 0
                    ? order.AuthorityBlockIds
                    : ResolveLimitBlocks(order.From, order.To, blocks);
                foreach (var id in ids)
                {
                    if (blocks.TryGetValue(id, out var block))
                    {
                        foreach (var segmentId in block.SegmentIds)
                            allowed.Add(segmentId);
                    }
                    else if (graph.GetSegment(id) != null)
                    {
                        allowed.Add(id);
                    }
                }
            }
            return allowed;
        }

        private static IReadOnlyList<string> ResolveLimitBlocks(
            string from,
            string to,
            IReadOnlyDictionary<string, PlacedCtcBlock> blocks)
        {
            var start = (from ?? string.Empty).Trim();
            var end = (to ?? string.Empty).Trim();
            if (start.Length == 0 && end.Length == 0)
                return Array.Empty<string>();
            if (start.Length > 0 && end.Length == 0)
                return new[] { start };
            if (start.Length == 0)
                return new[] { end };
            if (string.Equals(start, end, StringComparison.OrdinalIgnoreCase))
                return new[] { start };
            if (!blocks.ContainsKey(start) || !blocks.ContainsKey(end))
                return new[] { start, end };
            var adjacency = blocks.Values.ToDictionary(
                block => block.Id,
                block => new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            foreach (var block in blocks.Values)
            {
                foreach (var neighbor in new[]
                         {
                             block.NextFromAId,
                             block.NextFromBId,
                         }.Where(id => !string.IsNullOrWhiteSpace(id)
                                      && blocks.ContainsKey(id)))
                {
                    adjacency[block.Id].Add(neighbor);
                    adjacency[neighbor].Add(block.Id);
                }
            }
            var queue = new Queue<string>();
            var previous = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            queue.Enqueue(start);
            previous[start] = null;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (string.Equals(
                        current,
                        end,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                foreach (var neighbor in adjacency[current])
                {
                    if (previous.ContainsKey(neighbor))
                        continue;
                    previous[neighbor] = current;
                    queue.Enqueue(neighbor);
                }
            }
            if (!previous.ContainsKey(end))
                return new[] { start, end };
            var path = new List<string>();
            for (var cursor = end; cursor != null; cursor = previous[cursor])
                path.Add(cursor);
            path.Reverse();
            return path;
        }

        private static float? DistanceToAuthorityLimit(
            BaseLocomotive locomotive,
            Graph graph,
            ISet<string> allowed)
        {
            var forward = DirectionIsForward(locomotive);
            var cursor = forward
                ? locomotive.LocationF
                : locomotive.LocationR.Flipped();
            if (!cursor.IsValid)
                return 0f;
            if (!allowed.Contains(cursor.segment.id))
                return 0f;
            const float step = 10f;
            const float maximum = 3000f;
            var travelled = 0f;
            try
            {
                while (travelled < maximum)
                {
                    var next = graph.LocationByMoving(
                        cursor,
                        step,
                        checkSwitchAgainstMovement: true,
                        stopAtEndOfTrack: true);
                    var delta = graph.GetDistanceBetweenClose(cursor, next);
                    if (delta <= 0.01f)
                        return null;
                    travelled += Mathf.Abs(delta);
                    if (!allowed.Contains(next.segment.id))
                        return Mathf.Max(0f, travelled - step);
                    cursor = next;
                }
            }
            catch
            {
                // Existing switch, signal, and end-of-track protection is
                // already more restrictive than this authority calculation.
            }
            return null;
        }

        private static bool DirectionIsForward(BaseLocomotive locomotive)
        {
            if (locomotive.velocity > 0.05f)
                return true;
            if (locomotive.velocity < -0.05f)
                return false;
            var orders = Model.AI.Orders.FromPropertyValue(
                locomotive.KeyValueObject["aiOrders"]);
            return !orders.HasValue || orders.Value.Forward;
        }

        private void ApplyRestriction(
            BaseLocomotive locomotive,
            float distance,
            string reason,
            ISet<string> claimedIds)
        {
            claimedIds.Add(locomotive.id);
            if (!_stopClaims.TryGetValue(locomotive.id, out var claim))
            {
                claim = new ManualStopClaim
                {
                    Locomotive = locomotive,
                    Original = locomotive.KeyValueObject[
                        "aiManualStopDistance"],
                };
                _stopClaims[locomotive.id] = claim;
            }
            claim.Locomotive = locomotive;
            var target = Mathf.Max(0f, distance);
            var current = locomotive.KeyValueObject[
                "aiManualStopDistance"];
            if (current.IsNull
                || Mathf.Abs(current.FloatValue - target) > 1f)
            {
                locomotive.KeyValueObject["aiManualStopDistance"] =
                    Value.Float(target);
            }
            claim.LastApplied = target;
            var velocity = Mathf.Abs(locomotive.velocity);
            var conservativeStoppingDistance =
                25f + velocity * velocity / (2f * 0.3f);
            var autoOrders = Model.AI.Orders.FromPropertyValue(
                locomotive.KeyValueObject["aiOrders"]);
            var autoEngineerEnabled = autoOrders.HasValue
                                      && autoOrders.Value.Enabled;
            if (target <= 0.5f
                || (!autoEngineerEnabled
                    && target <= conservativeStoppingDistance))
            {
                HoldLocomotive(locomotive);
            }
            NoticeRestriction(locomotive.trainCrewId, reason);
        }

        private static void ApplySpeedRestriction(
            BaseLocomotive locomotive,
            int maximumMph)
        {
            locomotive.KeyValueObject[
                PropertyChange.KeyForControl(
                    PropertyChange.Control.Throttle)] = Value.Float(0f);
            locomotive.KeyValueObject[
                PropertyChange.KeyForControl(
                    PropertyChange.Control.TrainBrake)] = Value.Float(0.35f);
            locomotive.PostNotice(
                "train-order-speed",
                "Train-order speed limit " + maximumMph + " mph");
        }

        private static void HoldLocomotive(BaseLocomotive locomotive)
        {
            locomotive.KeyValueObject[
                PropertyChange.KeyForControl(
                    PropertyChange.Control.Throttle)] = Value.Float(0f);
            locomotive.KeyValueObject[
                PropertyChange.KeyForControl(
                    PropertyChange.Control.TrainBrake)] = Value.Float(1f);
            locomotive.KeyValueObject[
                PropertyChange.KeyForControl(
                    PropertyChange.Control.LocomotiveBrake)] = Value.Float(1f);
        }

        private void NoticeRestriction(string crewId, string reason)
        {
            if (string.IsNullOrWhiteSpace(crewId)
                || (_lastTrainNotice.TryGetValue(crewId, out var previous)
                    && string.Equals(
                        previous,
                        reason,
                        StringComparison.Ordinal)))
            {
                return;
            }
            _lastTrainNotice[crewId] = reason;
            if (StateManager.Shared.PlayersManager.TrainCrewForId(
                    crewId,
                    out var crew))
            {
                NotifyCrew(crew, reason + ".");
            }
        }

        private void ReleaseAllStopClaims()
        {
            foreach (var id in _stopClaims.Keys.ToArray())
                ReleaseStopClaim(id);
        }

        private void ReleaseStopClaim(string locomotiveId)
        {
            if (!_stopClaims.TryGetValue(locomotiveId, out var claim))
                return;
            _stopClaims.Remove(locomotiveId);
            if (claim.Locomotive != null
                && claim.Locomotive.KeyValueObject != null)
            {
                claim.Locomotive.KeyValueObject[
                    "aiManualStopDistance"] = claim.Original;
                claim.Locomotive.PostNotice(
                    "train-order-speed",
                    null);
            }
        }

        private void MergeDefinitions()
        {
            var existing = _orders.ToDictionary(
                order => order.Id,
                StringComparer.OrdinalIgnoreCase);
            _orders.Clear();
            foreach (var definition in _definitionOrders)
            {
                if (!existing.TryGetValue(definition.Id, out var order))
                {
                    order = CloneOrder(definition);
                }
                else
                {
                    CopyStaticDefinition(definition, order);
                }
                _orders.Add(order);
            }
        }

        private void Publish(string reason)
        {
            if (!StateManager.IsHost || _properties == null)
                return;
            var json = SerializeCatalog();
            _lastCatalog = json;
            _properties[CatalogKey] = Value.String(json);
            _properties[RevisionKey] = Value.Int(++_revision);
            _logger?.Log("Train-order desk: " + reason + ".");
        }

        private void ReadCatalogIfChanged()
        {
            var catalog = _properties[CatalogKey].StringValue;
            if (string.IsNullOrWhiteSpace(catalog)
                || string.Equals(
                    catalog,
                    _lastCatalog,
                    StringComparison.Ordinal))
            {
                return;
            }
            ConsumeCatalog(catalog);
        }

        private void ConsumeCatalog(string json)
        {
            try
            {
                var array = JArray.Parse(json);
                _orders.Clear();
                _orders.AddRange(array.OfType<JObject>()
                    .Select(ReadRuntimeOrder)
                    .Where(order => order != null));
                _lastCatalog = json;
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    "Could not read synchronized train orders: "
                    + ex.Message);
            }
        }

        private string SerializeCatalog()
        {
            return new JArray(_orders.Select(order => new JObject
            {
                ["id"] = order.Id,
                ["number"] = order.Number,
                ["type"] = order.Type,
                ["trainId"] = order.TrainId,
                ["crew"] = order.Crew,
                ["from"] = order.From,
                ["to"] = order.To,
                ["meetAt"] = order.MeetAt,
                ["text"] = order.Text,
                ["status"] = order.Status,
                ["priority"] = order.Priority,
                ["effective"] = order.Effective,
                ["expires"] = order.Expires,
                ["requiresAcknowledgement"] =
                    order.RequiresAcknowledgement,
                ["enforceAuthority"] = order.EnforceAuthority,
                ["maxSpeedMph"] = order.MaxSpeedMph,
                ["authorityBlockIds"] =
                    new JArray(order.AuthorityBlockIds),
                ["assignedCrewId"] = order.AssignedCrewId,
                ["deliveredAt"] = order.DeliveredAt,
                ["deliveredBy"] = order.DeliveredBy,
                ["acknowledgedAt"] = order.AcknowledgedAt,
                ["acknowledgedBy"] = order.AcknowledgedBy,
                ["lastUpdatedAt"] = order.LastUpdatedAt,
                ["lastReason"] = order.LastReason,
            })).ToString(Formatting.None);
        }

        private static PlacedTrainOrder ReadRuntimeOrder(JObject entry)
        {
            var id = Text(entry["id"]);
            if (id.Length == 0)
                return null;
            return new PlacedTrainOrder
            {
                Id = id,
                Number = (int?)entry["number"] ?? 0,
                Type = Text(entry["type"]),
                TrainId = Text(entry["trainId"]),
                Crew = Text(entry["crew"]),
                From = Text(entry["from"]),
                To = Text(entry["to"]),
                MeetAt = Text(entry["meetAt"]),
                Text = Text(entry["text"]),
                Status = Text(entry["status"], "Draft"),
                Priority = (int?)entry["priority"] ?? 0,
                Effective = Text(entry["effective"]),
                Expires = Text(entry["expires"]),
                RequiresAcknowledgement =
                    (bool?)entry["requiresAcknowledgement"] ?? true,
                EnforceAuthority =
                    (bool?)entry["enforceAuthority"] ?? true,
                MaxSpeedMph = (int?)entry["maxSpeedMph"] ?? 0,
                AuthorityBlockIds = Strings(
                    entry["authorityBlockIds"] as JArray),
                AssignedCrewId = Text(entry["assignedCrewId"]),
                DeliveredAt = Text(entry["deliveredAt"]),
                DeliveredBy = Text(entry["deliveredBy"]),
                AcknowledgedAt = Text(entry["acknowledgedAt"]),
                AcknowledgedBy = Text(entry["acknowledgedBy"]),
                LastUpdatedAt = Text(entry["lastUpdatedAt"]),
                LastReason = Text(entry["lastReason"]),
            };
        }

        private static PlacedTrainOrder CloneOrder(PlacedTrainOrder source)
        {
            var clone = new PlacedTrainOrder();
            CopyStaticDefinition(source, clone);
            clone.Status = source.Status;
            clone.AssignedCrewId = source.AssignedCrewId;
            clone.DeliveredAt = source.DeliveredAt;
            clone.DeliveredBy = source.DeliveredBy;
            clone.AcknowledgedAt = source.AcknowledgedAt;
            clone.AcknowledgedBy = source.AcknowledgedBy;
            clone.LastUpdatedAt = source.LastUpdatedAt;
            clone.LastReason = source.LastReason;
            return clone;
        }

        private static void CopyStaticDefinition(
            PlacedTrainOrder source,
            PlacedTrainOrder target)
        {
            target.Id = source.Id;
            target.Number = source.Number;
            target.Type = source.Type;
            target.TrainId = source.TrainId;
            target.Crew = source.Crew;
            target.From = source.From;
            target.To = source.To;
            target.MeetAt = source.MeetAt;
            target.Text = source.Text;
            target.Priority = source.Priority;
            target.Effective = source.Effective;
            target.Expires = source.Expires;
            target.RequiresAcknowledgement = source.RequiresAcknowledgement;
            target.EnforceAuthority = source.EnforceAuthority;
            target.MaxSpeedMph = source.MaxSpeedMph;
            target.AuthorityBlockIds = source.AuthorityBlockIds.ToArray();
            if (string.IsNullOrWhiteSpace(target.AssignedCrewId))
                target.AssignedCrewId = source.AssignedCrewId;
            if (string.IsNullOrWhiteSpace(target.Status))
                target.Status = source.Status;
        }

        private static PlacedCtcBlock CloneBlock(PlacedCtcBlock source) =>
            new PlacedCtcBlock
            {
                Id = source.Id,
                Name = source.Name,
                Mode = source.Mode,
                SegmentIds = source.SegmentIds.ToArray(),
                SignalAId = source.SignalAId,
                SignalBId = source.SignalBId,
                NextFromAId = source.NextFromAId,
                NextFromBId = source.NextFromBId,
            };

        private PlacedTrainOrder FindOrder(string id) =>
            _orders.FirstOrDefault(order => string.Equals(
                order.Id,
                (id ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase));

        private static string DefinitionSignature(
            IEnumerable<PlacedTrainOrder> orders,
            IEnumerable<PlacedCtcBlock> blocks)
        {
            return string.Join("|", (orders ?? Array.Empty<PlacedTrainOrder>())
                       .Select(order => order.Id + ":" + order.Number + ":"
                                        + order.Type + ":" + order.TrainId
                                        + ":" + order.Crew + ":" + order.From
                                        + ":" + order.To + ":" + order.MeetAt
                                        + ":" + order.Text + ":" + order.Status
                                        + ":" + order.Priority + ":"
                                        + order.Effective + ":" + order.Expires
                                        + ":" + order.EnforceAuthority + ":"
                                        + order.MaxSpeedMph + ":"
                                        + string.Join(",", order.AuthorityBlockIds)))
                   + "//"
                   + string.Join("|", (blocks ?? Array.Empty<PlacedCtcBlock>())
                       .Select(block => block.Id + ":"
                                        + string.Join(",", block.SegmentIds)
                                        + ":" + block.NextFromAId + ":"
                                        + block.NextFromBId));
        }

        private static bool IsTerminal(string status) =>
            string.Equals(status, "Fulfilled",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Cancelled",
                StringComparison.OrdinalIgnoreCase);

        private static string Timestamp() =>
            DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        private static string Text(JToken token, string fallback = "")
        {
            var value = token?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static IReadOnlyList<string> Strings(JArray values) =>
            (values ?? new JArray()).Values<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private sealed class ManualStopClaim
        {
            internal BaseLocomotive Locomotive;
            internal Value Original;
            internal float LastApplied;
        }

        private sealed class TrainOrderPropertyAccess :
            IPropertyAccessControlDelegate
        {
            public AuthorizationRequirementInfo
                AuthorizationRequirementForPropertyWrite(string key)
            {
                return string.Equals(
                           key,
                           CatalogKey,
                           StringComparison.OrdinalIgnoreCase)
                       || string.Equals(
                           key,
                           RevisionKey,
                           StringComparison.OrdinalIgnoreCase)
                    ? new AuthorizationRequirementInfo(
                        AuthorizationRequirement.HostOnly)
                    : new AuthorizationRequirementInfo(
                        AuthorizationRequirement.PlayerIdKey);
            }
        }
    }

    internal sealed class TrainOrderOverlay : MonoBehaviour
    {
        private TrainOrderRuntime _runtime;
        private Rect _window = new Rect(90f, 80f, 520f, 540f);
        private Vector2 _scroll;
        private bool _visible;
        private CursorLockMode _oldLock;
        private bool _oldVisible;

        internal void Configure(TrainOrderRuntime runtime)
        {
            _runtime = runtime;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
                SetVisible(!_visible);
        }

        private void OnDisable()
        {
            if (_visible)
                RestoreCursor();
        }

        private void OnGUI()
        {
            if (!_visible || _runtime == null)
                return;
            _window = GUI.Window(
                19001950,
                _window,
                DrawWindow,
                "TRAIN ORDERS - F8");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label(
                "Dispatcher delivery / crew repeat and acknowledgement");
            var crew = StateManager.Shared?.PlayersManager?.MyTrainCrew;
            if (crew == null)
            {
                GUILayout.Label(
                    "Join a train crew to receive and acknowledge orders.");
            }
            else
            {
                GUILayout.Label("Train crew: " + crew.Name);
                _scroll = GUILayout.BeginScrollView(_scroll);
                var orders = _runtime.Orders.Where(order => string.Equals(
                        order.AssignedCrewId,
                        crew.Id,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (orders.Length == 0)
                    GUILayout.Label("No orders have been delivered.");
                foreach (var order in orders)
                    DrawOrder(order);
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("CLOSE"))
                SetVisible(false);
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 28f));
        }

        private void DrawOrder(PlacedTrainOrder order)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(
                "No. " + order.Number + " - " + order.Type
                + " - " + order.Status);
            GUILayout.Label(
                "Authority: " + order.From + " to " + order.To);
            if (!string.IsNullOrWhiteSpace(order.MeetAt))
                GUILayout.Label("Meet at: " + order.MeetAt);
            if (order.AuthorityBlockIds.Count > 0)
            {
                GUILayout.Label(
                    "Blocks: " + string.Join(", ",
                        order.AuthorityBlockIds));
            }
            GUILayout.TextArea(order.Text ?? string.Empty);
            if (string.Equals(
                    order.Status,
                    "Delivered",
                    StringComparison.OrdinalIgnoreCase)
                && GUILayout.Button(
                    order.Type == "Form 31"
                        ? "SIGN / REPEAT / ACKNOWLEDGE"
                        : "REPEAT / ACKNOWLEDGE"))
            {
                _runtime.TryRequest(
                    "acknowledge",
                    order.Id,
                    string.Empty);
            }
            if (!string.IsNullOrWhiteSpace(order.AcknowledgedBy))
            {
                GUILayout.Label(
                    "Acknowledged by " + order.AcknowledgedBy
                    + " at " + order.AcknowledgedAt);
            }
            GUILayout.EndVertical();
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible)
                return;
            _visible = visible;
            if (visible)
            {
                _oldLock = Cursor.lockState;
                _oldVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = _oldLock;
            Cursor.visible = _oldVisible;
        }
    }
}
