using System;
using System.Collections.Generic;
using System.Linq;
using Game;
using Game.AccessControl;
using Game.State;
using KeyValue.Runtime;
using Network;
using Network.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.SignalRuntime
{
    internal sealed class CtcMultiplayerSync : IDisposable
    {
        private const string PropertyObjectId =
            "hrogers.signal-runtime.ctc-dispatch.v1";
        private const string CatalogKey = "catalog";
        private const string RevisionKey = "revision";
        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private GameObject _root;
        private KeyValueObject _properties;
        private StateManager _registeredStateManager;
        private string _lastCatalog = string.Empty;
        private int _revision;

        internal CtcMultiplayerSync(
            UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
        }

        internal bool TryRequest(
            string action,
            string controlPointId,
            string routeId,
            bool thrown)
        {
            EnsureRegistered();
            if (_properties == null || StateManager.Shared == null)
                return false;
            var playerId = PlayersManager.PlayerId.String;
            if (string.IsNullOrWhiteSpace(playerId))
                return false;
            var request = new JObject
            {
                ["nonce"] = Guid.NewGuid().ToString("N"),
                ["action"] = (action ?? string.Empty).Trim(),
                ["controlPointId"] =
                    (controlPointId ?? string.Empty).Trim(),
                ["routeId"] = (routeId ?? string.Empty).Trim(),
                ["thrown"] = thrown,
            };
            _properties[playerId] =
                Value.String(request.ToString(Formatting.None));
            return true;
        }

        internal void TickHost(
            PortableCtcRuntime runtime,
            Graph graph,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals,
            IReadOnlyList<PlacedCtcControlPoint> controlPoints)
        {
            EnsureRegistered();
            if (_properties == null)
                return;
            if (StateManager.IsHost)
            {
                ProcessRequests(runtime, graph, signals);
                PublishIfChanged(controlPoints);
            }
        }

        internal void ApplyClientState(
            IReadOnlyList<PlacedCtcControlPoint> controlPoints,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            EnsureRegistered();
            if (_properties == null || StateManager.IsHost)
                return;
            var catalog = _properties[CatalogKey].StringValue;
            if (string.IsNullOrWhiteSpace(catalog))
                return;
            try
            {
                var states = JArray.Parse(catalog).OfType<JObject>()
                    .ToDictionary(
                        state => Text(state["id"]),
                        StringComparer.OrdinalIgnoreCase);
                foreach (var cp in controlPoints)
                {
                    StopSignals(cp, signals);
                    if (!states.TryGetValue(cp.Id, out var state))
                    {
                        cp.ActiveRouteId = string.Empty;
                        cp.Phase = "Stop";
                        cp.LastReason = "Waiting for host CTC state";
                        continue;
                    }
                    cp.ActiveRouteId = Text(state["activeRouteId"]);
                    cp.Phase = Text(state["phase"], "Stop");
                    cp.LastReason = Text(state["lastReason"]);
                    if (!string.Equals(
                            cp.Phase,
                            "Route Lined",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var route = cp.Routes.FirstOrDefault(item =>
                        string.Equals(
                            item.Id,
                            cp.ActiveRouteId,
                            StringComparison.OrdinalIgnoreCase));
                    if (route != null
                        && signals.TryGetValue(
                            route.EntrySignalId,
                            out var signal))
                    {
                        signal.SetAspect("clear");
                    }
                }
                _lastCatalog = catalog;
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not apply synchronized CTC state: "
                    + ex.Message);
            }
        }

        internal void PublishIfChanged(
            IReadOnlyList<PlacedCtcControlPoint> controlPoints)
        {
            if (!StateManager.IsHost || _properties == null)
                return;
            var catalog = new JArray(controlPoints.Select(cp => new JObject
            {
                ["id"] = cp.Id,
                ["activeRouteId"] = cp.ActiveRouteId,
                ["phase"] = cp.Phase,
                ["lastReason"] = cp.LastReason,
            })).ToString(Formatting.None);
            if (string.Equals(
                    catalog,
                    _lastCatalog,
                    StringComparison.Ordinal))
            {
                return;
            }
            _lastCatalog = catalog;
            _properties[CatalogKey] = Value.String(catalog);
            _properties[RevisionKey] = Value.Int(++_revision);
        }

        public void Dispose()
        {
            Unregister();
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _properties = null;
        }

        private void EnsureRegistered()
        {
            var stateManager = StateManager.Shared;
            if (stateManager == null)
            {
                if (_registeredStateManager != null)
                    Unregister();
                return;
            }
            if (_registeredStateManager == stateManager
                && _properties != null)
            {
                return;
            }
            Unregister();
            if (_root == null)
            {
                _root = new GameObject("Hrogers CTC Multiplayer State")
                {
                    hideFlags = HideFlags.DontSave,
                };
                _properties = _root.AddComponent<KeyValueObject>();
            }
            _registeredStateManager = stateManager;
            stateManager.RegisterPropertyObject(
                PropertyObjectId,
                _properties,
                new CtcPropertyAccess());
            _lastCatalog = string.Empty;
        }

        private void Unregister()
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

        private void ProcessRequests(
            PortableCtcRuntime runtime,
            Graph graph,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            foreach (var key in _properties.Keys.Where(key =>
                         !string.Equals(key, CatalogKey,
                             StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(key, RevisionKey,
                             StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                var raw = _properties[key].StringValue;
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                var playerId = new PlayerId(key);
                try
                {
                    if (!HasDispatcherAccess(playerId))
                    {
                        Reject(playerId, "Dispatcher access is required.");
                        continue;
                    }
                    var request = JObject.Parse(raw);
                    var action = Text(request["action"])
                        .ToLowerInvariant();
                    var cp = Text(request["controlPointId"]);
                    var accepted = action switch
                    {
                        "switch" => runtime.TrySetSwitch(
                            cp,
                            (bool?)request["thrown"] ?? false,
                            graph),
                        "line" => runtime.TryLineRoute(
                            cp,
                            Text(request["routeId"]),
                            graph,
                            signals),
                        "cancel" => runtime.TryCancelRoute(cp),
                        _ => false,
                    };
                    if (!accepted)
                    {
                        Reject(
                            playerId,
                            "The host refused the CTC " + action
                            + " command for " + cp + ".");
                    }
                }
                catch (Exception ex)
                {
                    Reject(playerId, "Invalid CTC command: " + ex.Message);
                }
                finally
                {
                    _properties[key] = Value.Null();
                }
            }
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

        private static void StopSignals(
            PlacedCtcControlPoint cp,
            IReadOnlyDictionary<string, PlacedTrainSignal> signals)
        {
            foreach (var signalId in cp.Routes
                         .Select(route => route.EntrySignalId)
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (signals.TryGetValue(signalId, out var signal))
                    signal.SetAspect("stop");
            }
        }

        private static string Text(JToken token, string fallback = "")
        {
            var value = token?.ToString()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private sealed class CtcPropertyAccess :
            IPropertyAccessControlDelegate
        {
            public AuthorizationRequirementInfo
                AuthorizationRequirementForPropertyWrite(string key)
            {
                return string.Equals(key, CatalogKey,
                           StringComparison.OrdinalIgnoreCase)
                       || string.Equals(key, RevisionKey,
                           StringComparison.OrdinalIgnoreCase)
                    ? new AuthorizationRequirementInfo(
                        AuthorizationRequirement.HostOnly)
                    : new AuthorizationRequirementInfo(
                        AuthorizationRequirement.PlayerIdKey);
            }
        }
    }
}
