using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Helpers;
using Newtonsoft.Json.Linq;
using SimpleGraph.Runtime;
using TelegraphPoles;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        private const string CustomPoleFileName =
            "tile-editor-telegraph-poles.json";

        private sealed class CustomTelegraphPole
        {
            internal string Key;
            internal string FilePath;
            internal JObject Document;
            internal JObject Entry;
            internal int RuntimeNodeId;
        }

        private readonly Dictionary<string, JObject>
            _customTelegraphPoleDocuments =
                new Dictionary<string, JObject>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CustomTelegraphPole>
            _customTelegraphPolesByKey =
                new Dictionary<string, CustomTelegraphPole>(
                    StringComparer.Ordinal);
        private readonly Dictionary<int, CustomTelegraphPole>
            _customTelegraphPolesByNode =
                new Dictionary<int, CustomTelegraphPole>();
        private TelegraphPoleManager _customPolesAppliedManager;
        private bool _customPoleDocumentsScanned;
        private bool _applyingCustomPoles;
        private bool _customPoleLoadWarningLogged;

        internal int CustomTelegraphPoleCount =>
            _customTelegraphPolesByNode.Count;

        internal void RefreshTelegraphPoleMode()
        {
            RefreshPersistentTelegraphPoles();
            if (GraphOpen)
                RefreshTelegraphPoleOverlays();
        }

        internal void RefreshPersistentTelegraphPoles()
        {
            if (_applyingCustomPoles)
                return;
            EnsureTelegraphPoleManager();
            if (_telegraphPoleManager == null
                || _telegraphPoleGraph == null)
            {
                return;
            }
            EnsureCustomPoleDocumentsScanned();
            if (_customPolesAppliedManager == _telegraphPoleManager
                && _customPolesAppliedManager != null)
            {
                return;
            }

            _customPolesAppliedManager = _telegraphPoleManager;
            _customTelegraphPolesByKey.Clear();
            _customTelegraphPolesByNode.Clear();
            try
            {
                _applyingCustomPoles = true;
                RunTelegraphGraphBatch(ApplyCustomPoleDocuments);
                _customPoleLoadWarningLogged = false;
            }
            catch (Exception ex)
            {
                if (!_customPoleLoadWarningLogged)
                {
                    _logger?.Warning(
                        "Could not restore Tile Editor telegraph poles: "
                        + ex.Message);
                    _customPoleLoadWarningLogged = true;
                }
            }
            finally
            {
                _applyingCustomPoles = false;
            }
        }

        internal string CreateTelegraphPoleAtCamera(
            bool standalone,
            float maximumConnectionDistance)
        {
            RequireSession();
            RefreshPersistentTelegraphPoles();
            if (CameraSelector.shared == null)
            {
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            }
            maximumConnectionDistance = Mathf.Max(
                1f,
                maximumConnectionDistance);

            var worldPosition =
                CameraSelector.shared.CurrentCameraGroundPosition;
            return CreateTelegraphPoleAtPosition(
                worldPosition,
                standalone,
                maximumConnectionDistance);
        }

        internal string CreateTelegraphPoleAtPosition(
            Vector3 worldPosition,
            bool standalone,
            float maximumConnectionDistance)
        {
            RequireSession();
            RequireGraphEditOwnership();
            RefreshPersistentTelegraphPoles();
            maximumConnectionDistance = Mathf.Max(
                1f,
                maximumConnectionDistance);
            var gamePosition =
                WorldTransformer.WorldToGame(worldPosition);
            var source = standalone
                ? null
                : SelectedOrNearestTelegraphNode(
                    worldPosition,
                    maximumConnectionDistance);
            var styleSource = source
                              ?? NearestTelegraphNode(
                                  worldPosition,
                                  float.PositiveInfinity);
            var rotation = source == null
                ? new Vector3(
                    0f,
                    Camera.main == null
                        ? 0f
                        : Camera.main.transform.eulerAngles.y,
                    0f)
                : HeadingFromTo(
                    TelegraphNodePositionToGame(source),
                    gamePosition);
            var scale = styleSource?.scale ?? Vector3.one;
            var tag = styleSource?.tag ?? 0;
            var document = EnsureOwningCustomPoleDocument(
                out var filePath);
            var key = NextCustomPoleKey();
            var entry = new JObject
            {
                ["key"] = key,
                ["nodeId"] = -1,
                ["position"] = PoleVector(gamePosition),
                ["rotation"] = PoleVector(rotation),
                ["scale"] = PoleVector(scale),
                ["tag"] = tag,
            };
            CustomTelegraphPole custom = null;
            Node node = null;
            RunTelegraphGraphBatch(
                () =>
                {
                    node = _telegraphPoleGraph.CreateNode(
                        worldPosition,
                        rotation,
                        scale,
                        tag);
                    entry["nodeId"] = node.id;
                    CustomPoleArray(document).Add(entry);
                    custom = RegisterRuntimeCustomPole(
                        key,
                        filePath,
                        document,
                        entry,
                        node.id);
                    if (source != null)
                    {
                        _telegraphPoleGraph.AddEdge(
                            source.id,
                            node.id);
                        CustomWireArray(document).Add(
                            new JObject
                            {
                                ["a"] = PoleReferenceForNode(source.id),
                                ["b"] = "c:" + key,
                            });
                    }
                });

            _dirtyTelegraphPoleFiles.Add(filePath);
            _selectedTelegraphPoleId = node.id;
            _telegraphPoleOverlaySignature = int.MinValue;
            RefreshTelegraphPoleOverlays();
            return source == null
                ? "Added standalone pole " + node.id
                : "Added pole " + node.id + " connected to "
                  + source.id;
        }

        internal void RotateSelectedTelegraphPole(
            Vector3 rotationOffset)
        {
            ValidateVector(
                rotationOffset,
                "telegraph pole rotation");
            if (rotationOffset.sqrMagnitude < 0.00000001f)
                return;
            RequireGraphEditOwnership();
            var node = RequireTelegraphPoleNode();
            var rotation = node.eulerAngles;
            rotation.x = NormalizeDegrees(
                rotation.x + rotationOffset.x);
            rotation.y = NormalizeDegrees(
                rotation.y + rotationOffset.y);
            rotation.z = NormalizeDegrees(
                rotation.z + rotationOffset.z);
            if (TryGetCustomTelegraphPole(node.id, out var custom))
            {
                ExecuteCustomTelegraphPoleTransform(
                    custom,
                    node,
                    node.position,
                    rotation);
                return;
            }
            ExecuteBaseTelegraphPoleRotation(node, rotation);
        }

        internal void SetSelectedTelegraphPoleRotation(
            Vector3 rotation)
        {
            ValidateVector(rotation, "telegraph pole rotation");
            RequireGraphEditOwnership();
            var node = RequireTelegraphPoleNode();
            rotation = new Vector3(
                NormalizeDegrees(rotation.x),
                NormalizeDegrees(rotation.y),
                NormalizeDegrees(rotation.z));
            if (TryGetCustomTelegraphPole(node.id, out var custom))
            {
                ExecuteCustomTelegraphPoleTransform(
                    custom,
                    node,
                    node.position,
                    rotation);
                return;
            }
            ExecuteBaseTelegraphPoleRotation(node, rotation);
        }

        internal void ConnectTelegraphPoles(
            int firstPoleId,
            int secondPoleId)
        {
            RequireSession();
            RequireGraphEditOwnership();
            RefreshPersistentTelegraphPoles();
            if (firstPoleId == secondPoleId)
            {
                throw new InvalidOperationException(
                    "Choose two different poles.");
            }
            var first = _telegraphPoleGraph.NodeForId(firstPoleId);
            var second = _telegraphPoleGraph.NodeForId(secondPoleId);
            if (first == null || second == null)
            {
                throw new InvalidOperationException(
                    "One of the selected poles is no longer available.");
            }
            if (TryGetTelegraphEdge(
                    firstPoleId,
                    secondPoleId,
                    out _))
            {
                throw new InvalidOperationException(
                    "Those poles already have a wire connection.");
            }
            var document = EnsureOwningCustomPoleDocument(
                out var filePath);
            RunTelegraphGraphBatch(
                () => _telegraphPoleGraph.AddEdge(
                    firstPoleId,
                    secondPoleId));
            CustomWireArray(document).Add(
                new JObject
                {
                    ["a"] = PoleReferenceForNode(firstPoleId),
                    ["b"] = PoleReferenceForNode(secondPoleId),
                });
            _dirtyTelegraphPoleFiles.Add(filePath);
            _telegraphPoleOverlaySignature = int.MinValue;
            RefreshTelegraphPoleOverlays();
        }

        internal void DisconnectTelegraphPoles(
            int firstPoleId,
            int secondPoleId)
        {
            RequireSession();
            RequireGraphEditOwnership();
            RefreshPersistentTelegraphPoles();
            if (!TryFindCustomWire(
                    firstPoleId,
                    secondPoleId,
                    out var document,
                    out var filePath,
                    out var wire))
            {
                throw new InvalidOperationException(
                    "That is an original map wire. This panel only removes "
                    + "connections created by the Tile Editor.");
            }
            if (TryGetTelegraphEdge(
                    firstPoleId,
                    secondPoleId,
                    out var edgeId))
            {
                RunTelegraphGraphBatch(
                    () => _telegraphPoleGraph.RemoveEdge(edgeId));
            }
            CustomWireArray(document).Remove(wire);
            _dirtyTelegraphPoleFiles.Add(filePath);
            _telegraphPoleOverlaySignature = int.MinValue;
            RefreshTelegraphPoleOverlays();
        }

        internal bool IsCustomTelegraphWire(
            int firstPoleId,
            int secondPoleId)
        {
            return TryFindCustomWire(
                firstPoleId,
                secondPoleId,
                out _,
                out _,
                out _);
        }

        internal void DeleteSelectedCustomTelegraphPole()
        {
            RequireGraphEditOwnership();
            var node = RequireTelegraphPoleNode();
            if (!TryGetCustomTelegraphPole(
                    node.id,
                    out var custom))
            {
                throw new InvalidOperationException(
                    "Original map poles cannot be deleted here. Only poles "
                    + "created by the Tile Editor can be removed.");
            }

            var customReference = "c:" + custom.Key;
            foreach (var pair in _customTelegraphPoleDocuments.ToArray())
            {
                var wires = CustomWireArray(pair.Value);
                var removed = false;
                foreach (var wire in wires.OfType<JObject>().ToArray())
                {
                    if (!string.Equals(
                            (string)wire["a"],
                            customReference,
                            StringComparison.Ordinal)
                        && !string.Equals(
                            (string)wire["b"],
                            customReference,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    wires.Remove(wire);
                    removed = true;
                }
                if (removed)
                    _dirtyTelegraphPoleFiles.Add(pair.Key);
            }

            CustomPoleArray(custom.Document).Remove(custom.Entry);
            _dirtyTelegraphPoleFiles.Add(custom.FilePath);
            RunTelegraphGraphBatch(
                () => _telegraphPoleGraph.RemoveNode(node.id));
            _customTelegraphPolesByNode.Remove(node.id);
            _customTelegraphPolesByKey.Remove(custom.Key);
            _selectedTelegraphPoleId = -1;
            _telegraphPoleOverlaySignature = int.MinValue;
            RefreshTelegraphPoleOverlays();
        }

        private void ExecuteCustomTelegraphPoleTransform(
            CustomTelegraphPole custom,
            Node node,
            Vector3 targetLocalPosition,
            Vector3 targetRotation)
        {
            if ((targetLocalPosition - node.position).sqrMagnitude
                    < 0.00000001f
                && (targetRotation - node.eulerAngles).sqrMagnitude
                    < 0.00000001f)
            {
                return;
            }
            var edit = new TelegraphPoleEdit
            {
                IsCustom = true,
                CustomPole = custom,
                PoleId = node.id,
                BeforeNodePosition = node.position,
                AfterNodePosition = targetLocalPosition,
                BeforeNodeRotation = node.eulerAngles,
                AfterNodeRotation = targetRotation,
            };
            node.position = targetLocalPosition;
            node.eulerAngles = targetRotation;
            WriteCustomPoleTransform(custom, node);
            RebuildLiveTelegraphPoles(node.id);
            _telegraphPoleUndo.Push(edit);
            _telegraphPoleRedo.Clear();
            _dirtyTelegraphPoleFiles.Add(custom.FilePath);
        }

        private void RestoreCustomTelegraphPoleEdit(
            TelegraphPoleEdit edit,
            bool after)
        {
            RefreshPersistentTelegraphPoles();
            var node = _telegraphPoleGraph?.NodeForId(edit.PoleId);
            if (node == null || edit.CustomPole == null)
                return;
            node.position = after
                ? edit.AfterNodePosition
                : edit.BeforeNodePosition;
            node.eulerAngles = after
                ? edit.AfterNodeRotation
                : edit.BeforeNodeRotation;
            WriteCustomPoleTransform(edit.CustomPole, node);
            RebuildLiveTelegraphPoles(node.id);
            _selectedTelegraphPoleId = node.id;
            _dirtyTelegraphPoleFiles.Add(
                edit.CustomPole.FilePath);
        }

        private void ExecuteBaseTelegraphPoleRotation(
            Node node,
            Vector3 targetRotation)
        {
            if ((targetRotation - node.eulerAngles).sqrMagnitude
                < 0.00000001f)
            {
                return;
            }
            var document = EnsureOwningCustomPoleDocument(
                out var filePath);
            var existed = TryReadBasePoleRotationOverride(
                document,
                node.id,
                out _);
            var edit = new TelegraphPoleEdit
            {
                IsBaseRotationOverride = true,
                BaseRotationDocument = document,
                BaseRotationFilePath = filePath,
                PoleId = node.id,
                BeforeExists = existed,
                AfterExists = true,
                BeforeNodePosition = node.position,
                AfterNodePosition = node.position,
                BeforeNodeRotation = node.eulerAngles,
                AfterNodeRotation = targetRotation,
            };
            WriteBasePoleRotationOverride(
                document,
                node.id,
                true,
                targetRotation);
            node.eulerAngles = targetRotation;
            RebuildLiveTelegraphPoles(node.id);
            _telegraphPoleUndo.Push(edit);
            _telegraphPoleRedo.Clear();
            _dirtyTelegraphPoleFiles.Add(filePath);
        }

        private void RestoreBaseTelegraphPoleRotation(
            TelegraphPoleEdit edit,
            bool after)
        {
            var exists = after
                ? edit.AfterExists
                : edit.BeforeExists;
            var rotation = after
                ? edit.AfterNodeRotation
                : edit.BeforeNodeRotation;
            WriteBasePoleRotationOverride(
                edit.BaseRotationDocument,
                edit.PoleId,
                exists,
                rotation);
            EnsureTelegraphPoleManager();
            var node = _telegraphPoleGraph?.NodeForId(edit.PoleId);
            if (node != null)
            {
                node.eulerAngles = rotation;
                RebuildLiveTelegraphPoles(node.id);
            }
            _selectedTelegraphPoleId = edit.PoleId;
            _dirtyTelegraphPoleFiles.Add(
                edit.BaseRotationFilePath);
        }

        private void WriteCustomPoleTransform(
            CustomTelegraphPole custom,
            Node node)
        {
            custom.Entry["nodeId"] = node.id;
            custom.Entry["position"] = PoleVector(
                TelegraphNodePositionToGame(node));
            custom.Entry["rotation"] = PoleVector(node.eulerAngles);
            custom.Entry["scale"] = PoleVector(node.scale);
            custom.Entry["tag"] = node.tag;
        }

        private bool TryGetCustomTelegraphPole(
            int nodeId,
            out CustomTelegraphPole custom)
        {
            RefreshPersistentTelegraphPoles();
            return _customTelegraphPolesByNode.TryGetValue(
                nodeId,
                out custom);
        }

        private Node SelectedOrNearestTelegraphNode(
            Vector3 worldPosition,
            float maximumDistance)
        {
            if (_selectedTelegraphPoleId >= 0)
            {
                var selected = _telegraphPoleGraph.NodeForId(
                    _selectedTelegraphPoleId);
                if (selected != null)
                    return selected;
            }
            return NearestTelegraphNode(
                worldPosition,
                maximumDistance);
        }

        private Node NearestTelegraphNode(
            Vector3 worldPosition,
            float maximumDistance)
        {
            Node nearest = null;
            var best = float.IsPositiveInfinity(maximumDistance)
                ? float.PositiveInfinity
                : maximumDistance * maximumDistance;
            foreach (var node in _telegraphPoleGraph.Nodes)
            {
                var distance = (
                    _telegraphPoleGraph.WorldPositionForNode(node)
                    - worldPosition).sqrMagnitude;
                if (distance >= best)
                    continue;
                best = distance;
                nearest = node;
            }
            return nearest;
        }

        private void EnsureCustomPoleDocumentsScanned()
        {
            if (_customPoleDocumentsScanned)
                return;
            _customPoleDocumentsScanned = true;
            var modsDirectory = Path.Combine(_gameRoot, "Mods");
            if (!Directory.Exists(modsDirectory))
                return;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    modsDirectory,
                    CustomPoleFileName,
                    SearchOption.AllDirectories).ToArray();
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not scan saved Tile Editor poles: "
                    + ex.Message);
                return;
            }
            foreach (var file in files)
            {
                try
                {
                    var path = Path.GetFullPath(file);
                    var document = JObject.Parse(
                        File.ReadAllText(path));
                    RegisterCustomPoleDocument(path, document);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not read custom pole file "
                        + Path.GetFileName(file) + ": " + ex.Message);
                }
            }
        }

        private JObject EnsureOwningCustomPoleDocument(
            out string filePath)
        {
            EnsureCustomPoleDocumentsScanned();
            var modDirectory = FindOwningModDirectory();
            if (string.IsNullOrWhiteSpace(modDirectory))
            {
                throw new InvalidOperationException(
                    "Open a graph JSON from a mod with Definition.json "
                    + "before adding or connecting poles.");
            }
            filePath = Path.GetFullPath(
                Path.Combine(modDirectory, CustomPoleFileName));
            if (_customTelegraphPoleDocuments.TryGetValue(
                    filePath,
                    out var existing))
            {
                _telegraphPoleDocuments[filePath] = existing;
                return existing;
            }
            var document = new JObject
            {
                ["formatVersion"] = 2,
                ["description"] =
                    "Telegraph pole edits created by Hrogers Tile Editor",
                ["poles"] = new JArray(),
                ["wires"] = new JArray(),
                ["basePoleOverrides"] = new JArray(),
            };
            RegisterCustomPoleDocument(filePath, document);
            return document;
        }

        private void RegisterCustomPoleDocument(
            string filePath,
            JObject document)
        {
            document["formatVersion"] =
                Math.Max(
                    (int?)document["formatVersion"] ?? 1,
                    2);
            CustomPoleArray(document);
            CustomWireArray(document);
            BasePoleOverrideArray(document);
            _customTelegraphPoleDocuments[filePath] = document;
            _telegraphPoleDocuments[filePath] = document;
        }

        private void ApplyCustomPoleDocuments()
        {
            foreach (var pair in _customTelegraphPoleDocuments
                         .OrderBy(pair => pair.Key))
            {
                foreach (var entry in BasePoleOverrideArray(
                                 pair.Value)
                             .OfType<JObject>())
                {
                    var nodeId = (int?)entry["nodeId"] ?? -1;
                    var node = _telegraphPoleGraph.NodeForId(nodeId);
                    if (node == null)
                        continue;
                    node.eulerAngles = ReadPoleVector(
                        entry["rotation"],
                        node.eulerAngles);
                }
            }

            foreach (var pair in _customTelegraphPoleDocuments
                         .OrderBy(pair => pair.Key))
            {
                foreach (var entry in CustomPoleArray(pair.Value)
                             .OfType<JObject>())
                {
                    var key = ((string)entry["key"] ?? string.Empty)
                        .Trim();
                    if (key.Length == 0
                        || _customTelegraphPolesByKey.ContainsKey(key))
                    {
                        continue;
                    }
                    var position = ReadPoleVector(
                        entry["position"],
                        Vector3.zero);
                    var rotation = ReadPoleVector(
                        entry["rotation"],
                        Vector3.zero);
                    var scale = ReadPoleVector(
                        entry["scale"],
                        Vector3.one);
                    var tag = (int?)entry["tag"] ?? 0;
                    var node = _telegraphPoleGraph.CreateNode(
                        WorldTransformer.GameToWorld(position),
                        rotation,
                        scale,
                        tag);
                    entry["nodeId"] = node.id;
                    RegisterRuntimeCustomPole(
                        key,
                        pair.Key,
                        pair.Value,
                        entry,
                        node.id);
                }
            }

            foreach (var pair in _customTelegraphPoleDocuments
                         .OrderBy(pair => pair.Key))
            {
                foreach (var wire in CustomWireArray(pair.Value)
                             .OfType<JObject>())
                {
                    if (!TryResolvePoleReference(
                            (string)wire["a"],
                            out var first)
                        || !TryResolvePoleReference(
                            (string)wire["b"],
                            out var second)
                        || first == second
                        || TryGetTelegraphEdge(
                            first,
                            second,
                            out _))
                    {
                        continue;
                    }
                    _telegraphPoleGraph.AddEdge(first, second);
                }
            }
        }

        private CustomTelegraphPole RegisterRuntimeCustomPole(
            string key,
            string filePath,
            JObject document,
            JObject entry,
            int nodeId)
        {
            var custom = new CustomTelegraphPole
            {
                Key = key,
                FilePath = filePath,
                Document = document,
                Entry = entry,
                RuntimeNodeId = nodeId,
            };
            _customTelegraphPolesByKey[key] = custom;
            _customTelegraphPolesByNode[nodeId] = custom;
            return custom;
        }

        private string PoleReferenceForNode(int nodeId)
        {
            return _customTelegraphPolesByNode.TryGetValue(
                nodeId,
                out var custom)
                ? "c:" + custom.Key
                : "n:" + nodeId.ToString(
                    CultureInfo.InvariantCulture);
        }

        private bool TryResolvePoleReference(
            string reference,
            out int nodeId)
        {
            nodeId = -1;
            if (string.IsNullOrWhiteSpace(reference))
                return false;
            if (reference.StartsWith(
                    "c:",
                    StringComparison.Ordinal))
            {
                return _customTelegraphPolesByKey.TryGetValue(
                           reference.Substring(2),
                           out var custom)
                       && (nodeId = custom.RuntimeNodeId) >= 0;
            }
            return reference.StartsWith(
                       "n:",
                       StringComparison.Ordinal)
                   && int.TryParse(
                       reference.Substring(2),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out nodeId)
                   && _telegraphPoleGraph.NodeForId(nodeId) != null;
        }

        private bool TryFindCustomWire(
            int firstPoleId,
            int secondPoleId,
            out JObject document,
            out string filePath,
            out JObject wire)
        {
            foreach (var pair in _customTelegraphPoleDocuments)
            {
                foreach (var candidate in CustomWireArray(pair.Value)
                             .OfType<JObject>())
                {
                    if (!TryResolvePoleReference(
                            (string)candidate["a"],
                            out var a)
                        || !TryResolvePoleReference(
                            (string)candidate["b"],
                            out var b)
                        || !((a == firstPoleId
                              && b == secondPoleId)
                             || (a == secondPoleId
                                 && b == firstPoleId)))
                    {
                        continue;
                    }
                    document = pair.Value;
                    filePath = pair.Key;
                    wire = candidate;
                    return true;
                }
            }
            document = null;
            filePath = null;
            wire = null;
            return false;
        }

        private bool TryGetTelegraphEdge(
            int firstPoleId,
            int secondPoleId,
            out int edgeId)
        {
            var edge = _telegraphPoleGraph.Edges.FirstOrDefault(
                candidate =>
                    (candidate.idA == firstPoleId
                     && candidate.idB == secondPoleId)
                    || (candidate.idA == secondPoleId
                        && candidate.idB == firstPoleId));
            edgeId = edge?.id ?? -1;
            return edge != null;
        }

        private void RunTelegraphGraphBatch(Action action)
        {
            var manager = _telegraphPoleManager;
            var wasEnabled = manager != null && manager.enabled;
            if (wasEnabled)
                manager.enabled = false;
            try
            {
                action();
            }
            finally
            {
                if (wasEnabled && manager != null)
                    manager.enabled = true;
            }
        }

        private static JArray CustomPoleArray(JObject document)
        {
            if (document["poles"] is JArray poles)
                return poles;
            poles = new JArray();
            document["poles"] = poles;
            return poles;
        }

        private static JArray CustomWireArray(JObject document)
        {
            if (document["wires"] is JArray wires)
                return wires;
            wires = new JArray();
            document["wires"] = wires;
            return wires;
        }

        private static JArray BasePoleOverrideArray(
            JObject document)
        {
            if (document["basePoleOverrides"] is JArray overrides)
                return overrides;
            overrides = new JArray();
            document["basePoleOverrides"] = overrides;
            return overrides;
        }

        private static JObject FindBasePoleRotationOverride(
            JObject document,
            int poleId)
        {
            return BasePoleOverrideArray(document)
                .OfType<JObject>()
                .FirstOrDefault(entry =>
                    (int?)entry["nodeId"] == poleId);
        }

        private static bool TryReadBasePoleRotationOverride(
            JObject document,
            int poleId,
            out Vector3 rotation)
        {
            rotation = Vector3.zero;
            var entry = FindBasePoleRotationOverride(
                document,
                poleId);
            if (entry == null)
                return false;
            rotation = ReadPoleVector(
                entry["rotation"],
                Vector3.zero);
            return true;
        }

        private static void WriteBasePoleRotationOverride(
            JObject document,
            int poleId,
            bool exists,
            Vector3 rotation)
        {
            var overrides = BasePoleOverrideArray(document);
            var entry = FindBasePoleRotationOverride(
                document,
                poleId);
            if (!exists)
            {
                if (entry != null)
                    overrides.Remove(entry);
                return;
            }
            if (entry == null)
            {
                entry = new JObject
                {
                    ["nodeId"] = poleId,
                };
                overrides.Add(entry);
            }
            entry["rotation"] = PoleVector(rotation);
        }

        private static JArray PoleVector(Vector3 value)
        {
            return new JArray(value.x, value.y, value.z);
        }

        private static Vector3 ReadPoleVector(
            JToken token,
            Vector3 fallback)
        {
            if (!(token is JArray values))
                return fallback;
            return new Vector3(
                (float?)values.ElementAtOrDefault(0) ?? fallback.x,
                (float?)values.ElementAtOrDefault(1) ?? fallback.y,
                (float?)values.ElementAtOrDefault(2) ?? fallback.z);
        }

        private static Vector3 HeadingFromTo(
            Vector3 start,
            Vector3 end)
        {
            var delta = end - start;
            delta.y = 0f;
            return delta.sqrMagnitude < 0.00001f
                ? Vector3.zero
                : new Vector3(
                    0f,
                    Mathf.Atan2(delta.x, delta.z)
                    * Mathf.Rad2Deg,
                    0f);
        }

        private string NextCustomPoleKey()
        {
            string key;
            do
            {
                key = "TEP_"
                      + Guid.NewGuid().ToString("N").Substring(0, 10);
            } while (_customTelegraphPolesByKey.ContainsKey(key));
            return key;
        }

        private static float NormalizeDegrees(float value)
        {
            value %= 360f;
            return value < 0f ? value + 360f : value;
        }
    }
}
