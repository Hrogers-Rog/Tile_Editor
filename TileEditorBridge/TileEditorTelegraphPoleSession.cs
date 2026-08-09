using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleGraph.Runtime;
using TelegraphPoles;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class TelegraphPoleInfo
        {
            internal int Id;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal Vector3 Offset;
            internal string FileName = string.Empty;
            internal bool IsCustom;
            internal int[] ConnectedPoleIds = Array.Empty<int>();
        }

        private sealed class TelegraphPoleSource
        {
            internal string Id;
            internal string FilePath;
            internal JObject Document;
            internal JObject Entry;
        }

        private sealed class TelegraphPoleEdit
        {
            internal TelegraphPoleSource Source;
            internal CustomTelegraphPole CustomPole;
            internal int PoleId;
            internal bool IsCustom;
            internal bool IsBaseRotationOverride;
            internal bool BeforeExists;
            internal bool AfterExists;
            internal JObject BaseRotationDocument;
            internal string BaseRotationFilePath;
            internal Vector3 BeforeOffset;
            internal Vector3 AfterOffset;
            internal Vector3 BeforeNodePosition;
            internal Vector3 AfterNodePosition;
            internal Vector3 BeforeNodeRotation;
            internal Vector3 AfterNodeRotation;
        }

        private readonly List<TelegraphPoleSource> _telegraphPoleSources =
            new List<TelegraphPoleSource>();
        private readonly Dictionary<string, JObject> _telegraphPoleDocuments =
            new Dictionary<string, JObject>(
                StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirtyTelegraphPoleFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _lastSavedTelegraphPolePaths =
            new List<string>();
        private readonly Dictionary<string, string> _telegraphPoleBackups =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Stack<TelegraphPoleEdit> _telegraphPoleUndo =
            new Stack<TelegraphPoleEdit>();
        private readonly Stack<TelegraphPoleEdit> _telegraphPoleRedo =
            new Stack<TelegraphPoleEdit>();
        private TelegraphPoleManager _telegraphPoleManager;
        private SimpleGraph.Runtime.SimpleGraph _telegraphPoleGraph;
        private int _selectedTelegraphPoleId = -1;
        private int _telegraphPoleOverlaySignature;
        private int _liveTelegraphPoleCount;
        private bool _telegraphPoleSourcesDiscovered;

        internal bool TelegraphPoleDirty =>
            _dirtyTelegraphPoleFiles.Count > 0;
        internal bool CanUndoTelegraphPole =>
            _telegraphPoleUndo.Count > 0;
        internal bool CanRedoTelegraphPole =>
            _telegraphPoleRedo.Count > 0;
        internal int LiveTelegraphPoleCount =>
            _liveTelegraphPoleCount;
        internal IReadOnlyList<string> LastSavedTelegraphPolePaths =>
            _lastSavedTelegraphPolePaths;

        internal TelegraphPoleInfo SelectedTelegraphPole
        {
            get
            {
                if (_selectedTelegraphPoleId < 0)
                    return null;
                EnsureTelegraphPoleManager();
                var node = _telegraphPoleGraph?.NodeForId(
                    _selectedTelegraphPoleId);
                if (node == null)
                    return null;
                var source = FindTelegraphPoleSource(
                    _selectedTelegraphPoleId);
                TryReadPoleOffset(
                    source,
                    _selectedTelegraphPoleId,
                    out var offset);
                var isCustom = TryGetCustomTelegraphPole(
                    _selectedTelegraphPoleId,
                    out var customPole);
                return new TelegraphPoleInfo
                {
                    Id = _selectedTelegraphPoleId,
                    Position = TelegraphNodePositionToGame(node),
                    Rotation = node.eulerAngles,
                    Offset = offset,
                    FileName = isCustom
                        ? Path.GetFileName(customPole.FilePath)
                        : source == null
                            ? Path.GetFileName(_graphPath)
                            : Path.GetFileName(source.FilePath),
                    IsCustom = isCustom,
                    ConnectedPoleIds = _telegraphPoleGraph
                        .EnumerateEdgesFromTo(node)
                        .Select(edge => edge.Other(node)?.id ?? -1)
                        .Where(id => id >= 0)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray(),
                };
            }
        }

        internal void SelectTelegraphPole(int poleId)
        {
            if (!_poleMode || !_editModeActive || poleId < 0)
                return;
            EnsureTelegraphPoleManager();
            if (_telegraphPoleGraph?.NodeForId(poleId) == null)
                return;
            var previous = _selectedTelegraphPoleId;
            _selectedTelegraphPoleId = poleId;
            _selectedScenery = null;
            _selectedSceneryId = string.Empty;
            RefreshTelegraphPoleOverlayColor(previous);
            RefreshTelegraphPoleOverlayColor(poleId);
            RefreshSceneryOverlayColors();
        }

        internal bool IsSelectedTelegraphPole(int poleId)
        {
            return _selectedTelegraphPoleId == poleId;
        }

        internal void ClearSelectedTelegraphPole()
        {
            var previous = _selectedTelegraphPoleId;
            _selectedTelegraphPoleId = -1;
            RefreshTelegraphPoleOverlayColor(previous);
        }

        internal void ShowSelectedTelegraphPole()
        {
            var node = RequireTelegraphPoleNode();
            if (CameraSelector.shared == null)
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            CameraSelector.shared.ZoomToPoint(
                _telegraphPoleGraph.WorldPositionForNode(node));
        }

        internal void MoveSelectedTelegraphPole(
            Vector3 gameOffset,
            bool localAxes)
        {
            ValidateVector(gameOffset, "telegraph pole movement");
            var node = RequireTelegraphPoleNode();
            var graphOffset = _telegraphPoleGraph.transform
                .InverseTransformVector(gameOffset);
            var localOffset = localAxes
                ? Quaternion.Euler(
                    0f,
                    node.eulerAngles.y,
                    0f) * graphOffset
                : graphOffset;
            ExecuteTelegraphPoleEdit(
                node,
                localOffset);
        }

        internal void SetSelectedTelegraphPolePosition(
            Vector3 gamePosition)
        {
            ValidateVector(gamePosition, "telegraph pole position");
            var node = RequireTelegraphPoleNode();
            var world = WorldTransformer.GameToWorld(gamePosition);
            var targetLocal = _telegraphPoleGraph.transform
                .InverseTransformPoint(world);
            ExecuteTelegraphPoleEdit(
                node,
                targetLocal - node.position);
        }

        internal void ResetSelectedTelegraphPoleOffset()
        {
            var node = RequireTelegraphPoleNode();
            if (TryGetCustomTelegraphPole(node.id, out _))
                return;
            var source = FindTelegraphPoleSource(node.id);
            if (!TryReadPoleOffset(source, node.id, out var offset)
                || offset.sqrMagnitude < 0.000001f)
            {
                return;
            }
            ExecuteTelegraphPoleEdit(node, -offset);
        }

        internal void UndoTelegraphPole()
        {
            if (_telegraphPoleUndo.Count == 0)
                return;
            var edit = _telegraphPoleUndo.Pop();
            RestoreTelegraphPoleEdit(edit, false);
            _telegraphPoleRedo.Push(edit);
        }

        internal void RedoTelegraphPole()
        {
            if (_telegraphPoleRedo.Count == 0)
                return;
            var edit = _telegraphPoleRedo.Pop();
            RestoreTelegraphPoleEdit(edit, true);
            _telegraphPoleUndo.Push(edit);
        }

        internal void SaveTelegraphPoles()
        {
            _lastSavedTelegraphPolePaths.Clear();
            foreach (var path in _dirtyTelegraphPoleFiles.ToArray())
            {
                var document = string.Equals(
                    path,
                    _graphPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? _document
                    : _telegraphPoleDocuments.TryGetValue(
                        path,
                        out var found)
                        ? found
                        : _customTelegraphPoleDocuments.TryGetValue(
                            path,
                            out var customDocument)
                            ? customDocument
                            : null;
                if (document == null)
                    continue;
                if (!_telegraphPoleBackups.ContainsKey(path)
                    && File.Exists(path))
                {
                    var backup = path + ".tile-editor-backup-"
                                 + DateTime.Now.ToString(
                                     "yyyyMMdd-HHmmss",
                                     CultureInfo.InvariantCulture);
                    File.Copy(path, backup, false);
                    TileEditorBackupRetention.PruneFor(path);
                    _telegraphPoleBackups[path] = backup;
                }
                var temp = path + ".tile-editor.tmp";
                File.WriteAllText(
                    temp,
                    document.ToString(Formatting.Indented));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
                _lastSavedTelegraphPolePaths.Add(path);
            }
            _dirtyTelegraphPoleFiles.Clear();
        }

        private void ExecuteTelegraphPoleEdit(
            Node node,
            Vector3 localOffset)
        {
            if (localOffset.sqrMagnitude < 0.00000001f)
                return;
            RequireGraphEditOwnership();
            if (TryGetCustomTelegraphPole(node.id, out var customPole))
            {
                ExecuteCustomTelegraphPoleTransform(
                    customPole,
                    node,
                    node.position + localOffset,
                    node.eulerAngles);
                return;
            }
            EnsureTelegraphPoleSources();
            var source = FindTelegraphPoleSource(node.id)
                         ?? EnsureTelegraphPoleSource();
            var existed = TryReadPoleOffset(
                source,
                node.id,
                out var previousOffset);
            var edit = new TelegraphPoleEdit
            {
                Source = source,
                PoleId = node.id,
                BeforeExists = existed,
                AfterExists = true,
                BeforeOffset = previousOffset,
                AfterOffset = previousOffset + localOffset,
                BeforeNodePosition = node.position,
                AfterNodePosition = node.position + localOffset,
                BeforeNodeRotation = node.eulerAngles,
                AfterNodeRotation = node.eulerAngles,
            };
            WritePoleOffset(
                source,
                node.id,
                true,
                edit.AfterOffset);
            node.position = edit.AfterNodePosition;
            RebuildLiveTelegraphPoles(node.id);
            _telegraphPoleUndo.Push(edit);
            _telegraphPoleRedo.Clear();
            _dirtyTelegraphPoleFiles.Add(source.FilePath);
        }

        private void RestoreTelegraphPoleEdit(
            TelegraphPoleEdit edit,
            bool after)
        {
            if (edit.IsCustom)
            {
                RestoreCustomTelegraphPoleEdit(edit, after);
                return;
            }
            if (edit.IsBaseRotationOverride)
            {
                RestoreBaseTelegraphPoleRotation(edit, after);
                return;
            }
            SyncTelegraphPoleSourceDocument(edit.Source);
            var exists = after
                ? edit.AfterExists
                : edit.BeforeExists;
            var offset = after
                ? edit.AfterOffset
                : edit.BeforeOffset;
            var nodePosition = after
                ? edit.AfterNodePosition
                : edit.BeforeNodePosition;
            WritePoleOffset(
                edit.Source,
                edit.PoleId,
                exists,
                offset);
            EnsureTelegraphPoleManager();
            var node = _telegraphPoleGraph?.NodeForId(edit.PoleId);
            if (node != null)
            {
                node.position = nodePosition;
                node.eulerAngles = after
                    ? edit.AfterNodeRotation
                    : edit.BeforeNodeRotation;
                RebuildLiveTelegraphPoles(node.id);
            }
            _selectedTelegraphPoleId = edit.PoleId;
            _dirtyTelegraphPoleFiles.Add(edit.Source.FilePath);
        }

        private Node RequireTelegraphPoleNode()
        {
            RequireSession();
            EnsureTelegraphPoleManager();
            if (_selectedTelegraphPoleId < 0
                || _telegraphPoleGraph == null)
            {
                throw new InvalidOperationException(
                    "Click an amber telegraph pole marker first.");
            }
            var node = _telegraphPoleGraph.NodeForId(
                _selectedTelegraphPoleId);
            if (node == null)
                throw new InvalidOperationException(
                    "That telegraph pole is no longer available.");
            return node;
        }

        private Vector3 TelegraphNodePositionToGame(Node node)
        {
            return WorldTransformer.WorldToGame(
                _telegraphPoleGraph.WorldPositionForNode(node));
        }

        private void RebuildLiveTelegraphPoles(int poleId)
        {
            _telegraphPoleGraph.NotifyDidChangeNodes(
                new[] { poleId });
            var rebuild = typeof(TelegraphPoleManager).GetMethod(
                "Rebuild",
                BindingFlags.Instance | BindingFlags.NonPublic);
            rebuild?.Invoke(_telegraphPoleManager, null);
            _telegraphPoleOverlaySignature = int.MinValue;
            RefreshTelegraphPoleOverlays();
        }

        private void EnsureTelegraphPoleManager()
        {
            if (_telegraphPoleManager != null
                && _telegraphPoleGraph != null)
            {
                return;
            }
            _telegraphPoleManager =
                UnityEngine.Object.FindObjectOfType<
                    TelegraphPoleManager>();
            _telegraphPoleGraph = _telegraphPoleManager == null
                ? null
                : _telegraphPoleManager.GetComponent<
                    SimpleGraph.Runtime.SimpleGraph>();
        }

        private void EnsureTelegraphPoleSources()
        {
            if (_telegraphPoleSourcesDiscovered)
                return;
            _telegraphPoleSourcesDiscovered = true;
            var modDirectory = FindOwningModDirectory();
            if (string.IsNullOrWhiteSpace(modDirectory))
                return;
            var definitionPath = Path.Combine(
                modDirectory,
                "Definition.json");
            if (!File.Exists(definitionPath))
                return;
            try
            {
                var definition = JObject.Parse(
                    File.ReadAllText(definitionPath));
                var mixintos =
                    definition["mixintos"]?["game-graph"] as JArray;
                if (mixintos == null)
                    return;
                foreach (var token in mixintos)
                {
                    var relative = ParseFileMixinto((string)token);
                    if (string.IsNullOrWhiteSpace(relative))
                        continue;
                    var path = Path.GetFullPath(Path.Combine(
                        modDirectory,
                        relative));
                    if (!File.Exists(path))
                        continue;
                    JObject document;
                    if (string.Equals(
                            path,
                            _graphPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        document = _document;
                    }
                    else
                    {
                        try
                        {
                            document = JObject.Parse(
                                File.ReadAllText(path));
                        }
                        catch
                        {
                            continue;
                        }
                    }
                    _telegraphPoleDocuments[path] = document;
                    if (!(document["splineys"] is JObject splineys))
                        continue;
                    foreach (var property in splineys.Properties())
                    {
                        if (!(property.Value is JObject entry))
                            continue;
                        var handler =
                            (string)entry["handler"] ?? string.Empty;
                        if (handler.IndexOf(
                                "TelegraphPoleMover",
                                StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        _telegraphPoleSources.Add(
                            new TelegraphPoleSource
                            {
                                Id = property.Name,
                                FilePath = path,
                                Document = document,
                                Entry = entry,
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not discover telegraph pole movers: "
                    + ex.Message);
            }
        }

        private TelegraphPoleSource EnsureTelegraphPoleSource()
        {
            if (_telegraphPoleSources.Count > 0)
                return _telegraphPoleSources[0];
            var splineys = EnsureSplineysObject(_document);
            var id = "Tile Editor Telegraph Pole Moves";
            var suffix = 1;
            while (splineys[id] != null)
            {
                id = "Tile Editor Telegraph Pole Moves "
                     + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }
            var entry = new JObject
            {
                ["handler"] =
                    "AlinasMapMod.TelegraphPoles.TelegraphPoleMover",
                ["polesToMove"] = new JArray(),
                ["poleMovement"] = new JArray(),
            };
            splineys[id] = entry;
            var source = new TelegraphPoleSource
            {
                Id = id,
                FilePath = _graphPath,
                Document = _document,
                Entry = entry,
            };
            _telegraphPoleSources.Add(source);
            _telegraphPoleDocuments[_graphPath] = _document;
            return source;
        }

        private TelegraphPoleSource FindTelegraphPoleSource(
            int poleId)
        {
            EnsureTelegraphPoleSources();
            return _telegraphPoleSources.FirstOrDefault(
                source => PoleIndex(source, poleId) >= 0);
        }

        private void SyncTelegraphPoleSourceDocument(
            TelegraphPoleSource source)
        {
            if (source == null
                || !string.Equals(
                    source.FilePath,
                    _graphPath,
                    StringComparison.OrdinalIgnoreCase)
                || ReferenceEquals(source.Document, _document))
            {
                return;
            }
            source.Document = _document;
            var current = _document["splineys"]?[source.Id]
                as JObject;
            if (current != null)
                source.Entry = current;
            _telegraphPoleDocuments[source.FilePath] = _document;
        }

        private static int PoleIndex(
            TelegraphPoleSource source,
            int poleId)
        {
            if (!(ReadEntryValue(
                    source?.Entry,
                    "polesToMove") is JArray poles))
            {
                return -1;
            }
            for (var index = 0; index < poles.Count; index++)
            {
                if ((int?)poles[index] == poleId)
                    return index;
            }
            return -1;
        }

        private static bool TryReadPoleOffset(
            TelegraphPoleSource source,
            int poleId,
            out Vector3 offset)
        {
            offset = Vector3.zero;
            var index = PoleIndex(source, poleId);
            if (index < 0
                || !(ReadEntryValue(
                    source.Entry,
                    "poleMovement") is JArray movements)
                || index >= movements.Count
                || !(movements[index] is JArray values))
            {
                return false;
            }
            offset = new Vector3(
                (float?)values.ElementAtOrDefault(0) ?? 0f,
                (float?)values.ElementAtOrDefault(1) ?? 0f,
                (float?)values.ElementAtOrDefault(2) ?? 0f);
            return true;
        }

        private static void WritePoleOffset(
            TelegraphPoleSource source,
            int poleId,
            bool exists,
            Vector3 offset)
        {
            var poles = EnsureEntryArray(
                source.Entry,
                "polesToMove");
            var movements = EnsureEntryArray(
                source.Entry,
                "poleMovement");
            var index = PoleIndex(source, poleId);
            if (!exists)
            {
                if (index >= 0)
                {
                    poles.RemoveAt(index);
                    if (index < movements.Count)
                        movements.RemoveAt(index);
                }
                return;
            }
            var value = new JArray(offset.x, offset.y, offset.z);
            if (index < 0)
            {
                poles.Add(poleId);
                movements.Add(value);
            }
            else
            {
                while (movements.Count <= index)
                    movements.Add(new JArray(0f, 0f, 0f));
                movements[index] = value;
            }
        }

        private static JToken ReadEntryValue(
            JObject entry,
            string name)
        {
            return entry?.Properties().FirstOrDefault(
                property => string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private static JArray EnsureEntryArray(
            JObject entry,
            string name)
        {
            var property = entry.Properties().FirstOrDefault(
                candidate => string.Equals(
                    candidate.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (property?.Value is JArray existing)
                return existing;
            var result = new JArray();
            if (property == null)
                entry[name] = result;
            else
                property.Value = result;
            return result;
        }

        private void RefreshTelegraphPoleOverlays()
        {
            if (!GraphOpen || !_poleMode)
                return;
            EnsureTelegraphPoleManager();
            EnsureTelegraphPoleSources();
            var poles = FindLiveTelegraphPoles();
            _liveTelegraphPoleCount = poles.Count;
            var signature = TelegraphPoleSignature(poles);
            if (signature == _telegraphPoleOverlaySignature)
                return;
            _telegraphPoleOverlaySignature = signature;
            foreach (var item in poles)
            {
                var overlay = item.Pole.GetComponentInChildren<
                    TileEditorTelegraphPoleOverlay>(true);
                if (overlay == null)
                {
                    var go = new GameObject(
                        "TileEditorTelegraphPoleOverlay");
                    go.transform.SetParent(item.Pole.transform, false);
                    overlay = go.AddComponent<
                        TileEditorTelegraphPoleOverlay>();
                }
                overlay.Initialize(this, item.Pole, item.Id);
                overlay.SetOverlayVisible(
                    _editModeActive && _poleMode);
            }
        }

        private void RefreshTelegraphPoleOverlayColor(int poleId)
        {
            if (poleId < 0)
                return;
            foreach (var overlay in UnityEngine.Object
                         .FindObjectsOfType<
                             TileEditorTelegraphPoleOverlay>())
            {
                if (overlay.PoleId == poleId)
                    overlay.RefreshColor();
            }
        }

        private void SetTelegraphPoleOverlaysVisible(bool visible)
        {
            foreach (var overlay in UnityEngine.Object
                         .FindObjectsOfType<
                             TileEditorTelegraphPoleOverlay>())
            {
                overlay?.SetOverlayVisible(visible);
            }
        }

        private void DisposeTelegraphPoleSession()
        {
            foreach (var overlay in Resources
                         .FindObjectsOfTypeAll<
                             TileEditorTelegraphPoleOverlay>())
            {
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
            _telegraphPoleManager = null;
            _telegraphPoleGraph = null;
            _selectedTelegraphPoleId = -1;
            _telegraphPoleOverlaySignature = 0;
            _liveTelegraphPoleCount = 0;
            _telegraphPoleSources.Clear();
            _telegraphPoleDocuments.Clear();
            _dirtyTelegraphPoleFiles.Clear();
            _telegraphPoleBackups.Clear();
            _telegraphPoleUndo.Clear();
            _telegraphPoleRedo.Clear();
            _telegraphPoleSourcesDiscovered = false;
        }

        private List<LiveTelegraphPole> FindLiveTelegraphPoles()
        {
            var result = new List<LiveTelegraphPole>();
            if (_telegraphPoleManager == null
                || _telegraphPoleGraph == null)
            {
                return result;
            }
            foreach (var node in _telegraphPoleGraph.Nodes)
            {
                if (!_telegraphPoleManager.TryGetPole(
                        node.id,
                        out var pole)
                    || pole == null
                    || !pole.gameObject.scene.IsValid()
                    || !pole.gameObject.activeInHierarchy)
                {
                    continue;
                }
                result.Add(new LiveTelegraphPole
                {
                    Id = node.id,
                    Pole = pole,
                });
            }
            return result;
        }

        private static int TelegraphPoleSignature(
            IEnumerable<LiveTelegraphPole> poles)
        {
            unchecked
            {
                var count = 0;
                var sum = 0;
                var xor = 0;
                foreach (var item in poles)
                {
                    var instance = item.Pole.GetInstanceID();
                    count++;
                    sum += instance;
                    xor ^= instance;
                }
                return (count * 397) ^ sum ^ (xor * 31);
            }
        }

        private sealed class LiveTelegraphPole
        {
            internal int Id;
            internal TelegraphPole Pole;
        }
    }

    internal sealed class TileEditorTelegraphPoleOverlay
        : MonoBehaviour, IPickable
    {
        private TileEditorGraphSession _session;
        private TelegraphPole _pole;
        private int _poleId;
        private LineRenderer _line;
        private BoxCollider _collider;

        internal int PoleId => _poleId;

        public float MaxPickDistance => 600f;
        public int Priority => 19;
        public PickableActivationFilter ActivationFilter =>
            PickableActivationFilter.Any;

        public TooltipInfo TooltipInfo => _pole == null
            ? TooltipInfo.Empty
            : new TooltipInfo(
                "Tile Editor Telegraph Pole " + _poleId,
                "Click to move this numbered pole.");

        internal void Initialize(
            TileEditorGraphSession session,
            TelegraphPole pole,
            int poleId)
        {
            _session = session;
            _pole = pole;
            _poleId = poleId;
            BuildVisual();
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

        internal void RefreshColor()
        {
            if (_line == null || _session == null)
                return;
            TileEditorOverlayVisuals.SetColor(
                _line,
                _session.IsSelectedTelegraphPole(_poleId)
                    ? Color.magenta
                    : new Color(1f, 0.68f, 0.12f));
        }

        public void Activate(PickableActivateEvent evt)
        {
            if (!TileEditorCameraInput.EditorWorldInputBlocked)
                _session?.SelectTelegraphPole(_poleId);
        }

        public void Deactivate()
        {
        }

        private void BuildVisual()
        {
            if (_pole == null)
                return;
            gameObject.layer = Layers.Clickable;
            transform.localPosition = Vector3.up * 0.6f;
            transform.localRotation = Quaternion.identity;
            _line = GetComponent<LineRenderer>()
                    ?? gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.startWidth = 0.10f;
            _line.endWidth = 0.10f;
            _line.positionCount = 5;
            _line.SetPositions(new[]
            {
                new Vector3(0f, 0f, 0.65f),
                new Vector3(0.65f, 0f, 0f),
                new Vector3(0f, 0f, -0.65f),
                new Vector3(-0.65f, 0f, 0f),
                new Vector3(0f, 0f, 0.65f),
            });
            _collider = GetComponent<BoxCollider>()
                        ?? gameObject.AddComponent<BoxCollider>();
            _collider.center = new Vector3(0f, 4.5f, 0f);
            _collider.size = new Vector3(1.6f, 10f, 1.6f);
            RefreshColor();
        }

    }
}
