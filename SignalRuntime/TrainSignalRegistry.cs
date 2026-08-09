using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Helpers;
using Newtonsoft.Json.Linq;
using Track;
using Track.Signals;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace Hrogers.SignalRuntime
{
    internal sealed class TrainSignalRegistry : IDisposable
    {
        private const string DefinitionFileName = "train-signals.json";
        private const string DefaultSingleSource =
            "CTC/BR-EL-GI-WH Module/BR-E/Signal BR-E Main";
        private const string DefaultMultiSource =
            "CTC/BR-EL-GI-WH Module/BR-E/Signal BR-E Enter";

        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private readonly List<SignalDefinition> _definitions =
            new List<SignalDefinition>();
        private readonly Dictionary<string, PlacedTrainSignal> _signals =
            new Dictionary<string, PlacedTrainSignal>(
                StringComparer.OrdinalIgnoreCase);
        private readonly List<PlacedDiamondInterlocking> _interlockings =
            new List<PlacedDiamondInterlocking>();
        private readonly DiamondInterlockingRuntime _interlockingRuntime;
        private readonly PortableCtcRuntime _ctcRuntime;
        private readonly TrainOrderRuntime _trainOrderRuntime;
        private Graph _graph;
        private WorldTransformer _transformer;
        private GameObject _root;
        private bool _enabled = true;
        private float _nextTickAt;
        private float _nextFileCheckAt;
        private string _fileSignature = string.Empty;
        private string[] _definitionFiles = Array.Empty<string>();

        internal TrainSignalRegistry(
            UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
            _interlockingRuntime = new DiamondInterlockingRuntime(logger);
            _ctcRuntime = new PortableCtcRuntime(logger);
            _trainOrderRuntime = new TrainOrderRuntime(logger);
        }

        internal IReadOnlyList<PlacedTrainSignal> Signals =>
            _signals.Values
                .OrderBy(signal => signal.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        internal IReadOnlyList<PlacedDiamondInterlocking> Interlockings =>
            _interlockings
                .OrderBy(interlocking => interlocking.Id,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        internal IReadOnlyList<PlacedCtcControlPoint> CtcControlPoints =>
            _ctcRuntime.ControlPoints;

        internal IReadOnlyList<PlacedCtcBlock> CtcBlocks =>
            _ctcRuntime.Blocks;

        internal IReadOnlyList<PlacedTrainOrder> TrainOrders =>
            _trainOrderRuntime.Orders;

        internal void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled)
            {
                _ctcRuntime.ResetRuntime();
                _trainOrderRuntime.ResetRuntime();
                RemoveSignals();
            }
            else
                _nextTickAt = 0f;
        }

        internal void Reload(string modsDirectory)
        {
            _definitions.Clear();
            _interlockings.Clear();
            var files = DiscoverDefinitionFiles(modsDirectory).ToArray();
            _definitionFiles = files;
            _fileSignature = FileSignature(files);
            var knownIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var knownInterlockingIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                LoadFile(file, knownIds, knownInterlockingIds);
            _ctcRuntime.Reload(modsDirectory);
            _trainOrderRuntime.ReloadDefinitions(
                _ctcRuntime.TrainOrders,
                _ctcRuntime.Blocks);
            _interlockingRuntime.Reset(_interlockings);
            RemoveSignals();
            EnsureSignals();
            _logger?.Log(
                "Loaded " + _definitions.Count
                + " portable train signal definition(s) from "
                + files.Length + " map file(s)." );
        }

        internal void Tick(string modsDirectory)
        {
            if (!_enabled || Time.unscaledTime < _nextTickAt)
                return;
            _nextTickAt = Time.unscaledTime + 0.5f;

            if (Time.unscaledTime >= _nextFileCheckAt)
            {
                _nextFileCheckAt = Time.unscaledTime + 2f;
                // New files are announced through Main.ReloadDefinitions by the
                // editor. Between reloads, checking the known paths avoids a full
                // recursive walk of every installed mod twice per second.
                var files = _definitionFiles.Where(File.Exists).ToArray();
                var signature = FileSignature(files);
                if (!string.Equals(
                        signature,
                        _fileSignature,
                        StringComparison.Ordinal))
                {
                    Reload(modsDirectory);
                    return;
                }
            }

            var graph = Graph.Shared;
            if (graph == null || !graph.HasPopulatedCollections
                || !WorldTransformer.TryGetShared(out var transformer))
            {
                if (_signals.Count > 0)
                    RemoveSignals();
                _ctcRuntime.ResetRuntime();
                _trainOrderRuntime.ResetRuntime();
                _graph = null;
                _transformer = null;
                return;
            }
            if (_graph != graph || _transformer != transformer)
            {
                RemoveSignals();
                _graph = graph;
                _transformer = transformer;
            }
            EnsureSignals();
            RefreshAttachedSignalTransforms();
            _interlockingRuntime.Tick(_interlockings, _signals);
            _ctcRuntime.Tick(modsDirectory, _graph, _signals);
            _trainOrderRuntime.Tick(
                _graph,
                _ctcRuntime.TrainOrders,
                _ctcRuntime.Blocks);
        }

        internal bool TryTrainOrderAction(
            string action,
            string orderId,
            string trainCrewId)
        {
            return _trainOrderRuntime.TryRequest(
                action,
                orderId,
                trainCrewId);
        }

        internal bool TryGetTrainOrder(
            string orderId,
            out PlacedTrainOrder order)
        {
            return _trainOrderRuntime.TryGetOrder(orderId, out order);
        }

        internal bool TrySetAspect(string signalId, string aspect)
        {
            return _signals.TryGetValue(
                       (signalId ?? string.Empty).Trim(),
                       out var signal)
                   && signal.SetAspect(aspect);
        }

        internal bool TryGetSignal(
            string signalId,
            out PlacedTrainSignal signal)
        {
            return _signals.TryGetValue(
                (signalId ?? string.Empty).Trim(),
                out signal);
        }

        internal bool TryRequestInterlockingRoute(
            string interlockingId,
            string approachId)
        {
            return _interlockingRuntime.TryRequest(
                interlockingId,
                approachId,
                _interlockings,
                _signals);
        }

        internal bool TryReleaseInterlocking(string interlockingId)
        {
            return _interlockingRuntime.TryRelease(
                interlockingId,
                _interlockings);
        }

        internal bool TrySetInterlockingAutomatic(
            string interlockingId,
            bool automatic)
        {
            return _interlockingRuntime.TrySetAutomatic(
                interlockingId,
                automatic,
                _interlockings);
        }

        internal bool TryGetInterlocking(
            string interlockingId,
            out PlacedDiamondInterlocking interlocking)
        {
            interlocking = _interlockings.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    (interlockingId ?? string.Empty).Trim(),
                    StringComparison.OrdinalIgnoreCase));
            return interlocking != null;
        }

        internal bool TrySetCtcSwitch(
            string controlPointId,
            bool thrown)
        {
            return _ctcRuntime.TrySetSwitch(
                controlPointId,
                thrown,
                _graph);
        }

        internal bool TryLineCtcRoute(
            string controlPointId,
            string routeId)
        {
            return _ctcRuntime.TryLineRoute(
                controlPointId,
                routeId,
                _graph,
                _signals);
        }

        internal bool TryCancelCtcRoute(string controlPointId)
        {
            return _ctcRuntime.TryCancelRoute(controlPointId);
        }

        internal bool TryGetCtcControlPoint(
            string controlPointId,
            out PlacedCtcControlPoint controlPoint)
        {
            return _ctcRuntime.TryGetControlPoint(
                controlPointId,
                out controlPoint);
        }

        internal static bool TryParseAspect(
            string value,
            out SignalAspect aspect)
        {
            switch ((value ?? string.Empty)
                    .Trim()
                    .Replace("-", string.Empty)
                    .Replace("_", string.Empty)
                    .ToLowerInvariant())
            {
                case "approach":
                case "yellow":
                    aspect = SignalAspect.Approach;
                    return true;
                case "clear":
                case "green":
                    aspect = SignalAspect.Clear;
                    return true;
                case "divergingapproach":
                    aspect = SignalAspect.DivergingApproach;
                    return true;
                case "divergingclear":
                    aspect = SignalAspect.DivergingClear;
                    return true;
                case "restricting":
                    aspect = SignalAspect.Restricting;
                    return true;
                case "stop":
                case "red":
                    aspect = SignalAspect.Stop;
                    return true;
                default:
                    aspect = SignalAspect.Stop;
                    return false;
            }
        }

        private void EnsureSignals()
        {
            if (!_enabled
                || Graph.Shared == null
                || !Graph.Shared.HasPopulatedCollections
                || !WorldTransformer.TryGetShared(out var transformer))
            {
                return;
            }
            _graph = Graph.Shared;
            _transformer = transformer;
            EnsureRoot();
            foreach (var definition in _definitions.Where(
                         definition => definition.Enabled))
            {
                if (!_signals.ContainsKey(definition.Id))
                    TryCreateSignal(definition);
            }
        }

        private void TryCreateSignal(SignalDefinition definition)
        {
            ResolveSignalTrackAttachment(definition);
            var sourcePath = string.IsNullOrWhiteSpace(
                definition.SourcePath)
                ? definition.HeadCount > 1
                    ? DefaultMultiSource
                    : DefaultSingleSource
                : definition.SourcePath;
            var source = FindSceneObjectByPath(sourcePath);
            if (source == null)
                return;

            GameObject clone = null;
            var sourceWasActive = source.activeSelf;
            try
            {
                source.SetActive(false);
                clone = UnityEngine.Object.Instantiate(source, _root.transform);
                clone.name = "Hrogers Train Signal [" + definition.Id + "]";
                foreach (var signal in clone
                             .GetComponentsInChildren<CTCSignal>(true))
                {
                    UnityEngine.Object.DestroyImmediate(signal);
                }
                foreach (var pickable in clone
                             .GetComponentsInChildren<CTCSignalPickable>(true))
                {
                    UnityEngine.Object.DestroyImmediate(pickable);
                }

                clone.transform.position = definition.Position;
                _transformer.AddObjectToMove(clone.transform);
                clone.transform.rotation =
                    Quaternion.Euler(definition.Rotation);
                clone.transform.localScale = definition.Scale;
                clone.SetActive(true);

                var controller = clone
                    .GetComponentInChildren<CTCSignalModelController>(true);
                if (controller == null)
                    throw new InvalidOperationException(
                        "Base semaphore source has no model controller.");
                controller.Configure(definition.HeadCount);
                var placed = new PlacedTrainSignal
                {
                    Id = definition.Id,
                    InterlockingId = definition.InterlockingId,
                    ProtectedNodeId = definition.ProtectedNodeId,
                    ProtectedSegmentId = definition.ProtectedSegmentId,
                    ProtectedSegmentIds = definition.ProtectedSegmentIds,
                    ApproachSegmentIds = definition.ApproachSegmentIds,
                    Direction = definition.Direction,
                    ApproachId = definition.ApproachId,
                    TrackLocked = definition.TrackLocked,
                    TrackSegmentId = definition.TrackSegmentId,
                    TrackParameter = definition.TrackParameter,
                    HeadCount = definition.HeadCount,
                    GameObject = clone,
                    ModelController = controller,
                };
                _signals[definition.Id] = placed;
                placed.SetAspect(definition.InitialAspect);
            }
            catch (Exception ex)
            {
                if (clone != null)
                {
                    try
                    {
                        _transformer?.RemoveObjectToMove(clone.transform);
                    }
                    catch
                    {
                    }
                    UnityEngine.Object.Destroy(clone);
                }
                _logger?.Warning(
                    "Could not create train signal '" + definition.Id
                    + "' from '" + sourcePath + "': " + ex.Message);
            }
            finally
            {
                if (source != null && sourceWasActive)
                    source.SetActive(true);
            }
        }

        private void EnsureRoot()
        {
            if (_root != null)
                return;
            _root = new GameObject("Hrogers Train Signals");
            UnityEngine.Object.DontDestroyOnLoad(_root);
        }

        private void RefreshAttachedSignalTransforms()
        {
            if (_graph == null)
                return;
            foreach (var definition in _definitions.Where(
                         item => item.TrackLocked))
            {
                if (!ResolveSignalTrackAttachment(definition)
                    || !_signals.TryGetValue(
                        definition.Id,
                        out var placed)
                    || placed?.GameObject == null)
                {
                    continue;
                }
                placed.GameObject.transform.position =
                    WorldTransformer.GameToWorld(definition.Position);
                placed.GameObject.transform.rotation =
                    Quaternion.Euler(definition.Rotation);
                placed.TrackLocked = true;
                placed.TrackSegmentId = definition.TrackSegmentId;
                placed.TrackParameter = definition.TrackParameter;
            }
        }

        private bool ResolveSignalTrackAttachment(
            SignalDefinition definition)
        {
            if (!definition.TrackLocked
                || _graph == null
                || string.IsNullOrWhiteSpace(
                    definition.TrackSegmentId))
            {
                return false;
            }
            var segment = _graph.GetSegment(definition.TrackSegmentId);
            if (segment == null)
                return false;
            definition.TrackParameter = Mathf.Clamp01(
                definition.TrackParameter);
            var frame = SignalTrackFrame(
                segment,
                definition.TrackParameter);
            definition.Position = segment.Curve.GetPoint(
                                      definition.TrackParameter)
                                  + frame
                                  * definition.TrackLocalPosition;
            definition.Rotation = (
                frame
                * Quaternion.Euler(
                    definition.TrackLocalRotation)).eulerAngles;
            return true;
        }

        private static Quaternion SignalTrackFrame(
            TrackSegment segment,
            float parameter)
        {
            const float delta = 0.0025f;
            var before = segment.Curve.GetPoint(
                Mathf.Clamp01(parameter - delta));
            var after = segment.Curve.GetPoint(
                Mathf.Clamp01(parameter + delta));
            var tangent = after - before;
            tangent.y = 0f;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.forward;
            tangent.Normalize();
            return Quaternion.LookRotation(tangent, Vector3.up);
        }

        private void LoadFile(
            string path,
            ISet<string> knownIds,
            ISet<string> knownInterlockingIds)
        {
            try
            {
                var document = JObject.Parse(File.ReadAllText(path));
                if (!(document["signals"] is JArray signals))
                    return;
                foreach (var entry in signals.OfType<JObject>())
                {
                    var id = ((string)entry["id"] ?? string.Empty).Trim();
                    if (id.Length == 0)
                        continue;
                    if (!knownIds.Add(id))
                    {
                        _logger?.Warning(
                            "Duplicate portable train signal id '" + id
                            + "' ignored in " + path + ".");
                        continue;
                    }
                    var attachment = entry["trackAttachment"] as JObject;
                    _definitions.Add(new SignalDefinition
                    {
                        Id = id,
                        Enabled = (bool?)entry["enabled"] ?? true,
                        Position = ReadVector(
                            entry["position"] as JObject,
                            Vector3.zero),
                        Rotation = ReadVector(
                            entry["rotation"] as JObject,
                            Vector3.zero),
                        Scale = ReadVector(
                            entry["scale"] as JObject,
                            Vector3.one),
                        HeadCount = Mathf.Clamp(
                            (int?)entry["headCount"] ?? 1,
                            1,
                            3),
                        InitialAspect =
                            ((string)entry["initialAspect"] ?? "stop").Trim(),
                        InterlockingId =
                            ((string)entry["interlockingId"] ?? string.Empty).Trim(),
                        ProtectedNodeId =
                            ((string)entry["protectedNodeId"] ?? string.Empty).Trim(),
                        ProtectedSegmentId =
                            ((string)entry["protectedSegmentId"] ?? string.Empty).Trim(),
                        ProtectedSegmentIds = ReadStringArrayWithFallback(
                            entry["protectedSegmentIds"] as JArray,
                            ((string)entry["protectedSegmentId"]
                             ?? string.Empty).Trim()),
                        ApproachSegmentIds = ReadStringArrayWithFallback(
                            entry["approachSegmentIds"] as JArray,
                            ((string)entry["protectedSegmentId"]
                             ?? string.Empty).Trim()),
                        Direction =
                            ((string)entry["direction"] ?? "forward").Trim(),
                        ApproachId =
                            ((string)entry["approachId"] ?? string.Empty).Trim(),
                        SourcePath =
                            ((string)entry["sourcePath"] ?? string.Empty).Trim(),
                        TrackLocked =
                            (bool?)attachment?["locked"] ?? false,
                        TrackSegmentId =
                            ((string)attachment?["segmentId"]
                             ?? string.Empty).Trim(),
                        TrackParameter = Mathf.Clamp01(
                            (float?)attachment?["parameter"] ?? 0f),
                        TrackLocalPosition =
                            ReadVector(
                                attachment?["localPosition"] as JObject,
                                Vector3.zero),
                        TrackLocalRotation =
                            ReadVector(
                                attachment?["localRotation"] as JObject,
                                Vector3.zero),
                    });
                }
                if (document["interlockings"] is JArray interlockings)
                {
                    foreach (var entry in interlockings.OfType<JObject>())
                    {
                        if (!string.Equals(
                                (string)entry["type"],
                                "diamond",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        var id = ((string)entry["id"]
                                  ?? string.Empty).Trim();
                        if (id.Length == 0
                            || !knownInterlockingIds.Add(id))
                        {
                            continue;
                        }
                        var routes = (entry["routes"] as JArray
                                      ?? new JArray())
                            .OfType<JObject>()
                            .Select(route => new PlacedInterlockingRoute
                            {
                                Id = ((string)route["id"]
                                      ?? string.Empty).Trim(),
                                SegmentId = ((string)route["segmentId"]
                                             ?? string.Empty).Trim(),
                                SegmentIds = ReadStringArrayWithFallback(
                                    route["segmentIds"] as JArray,
                                    ((string)route["segmentId"]
                                     ?? string.Empty).Trim()),
                                SignalIds = ReadStringArray(
                                    route["signalIds"] as JArray),
                                ApproachNodeIds = ReadStringArray(
                                    route["approachNodeIds"] as JArray),
                            })
                            .ToArray();
                        _interlockings.Add(
                            new PlacedDiamondInterlocking
                            {
                                Id = id,
                                Automatic =
                                    (bool?)entry["automatic"] ?? true,
                                ReleaseDelaySeconds = Mathf.Clamp(
                                    ReadFloat(
                                        entry["releaseDelaySeconds"],
                                        3f),
                                    0f,
                                    60f),
                                CancelDelaySeconds = Mathf.Clamp(
                                    ReadFloat(
                                        entry["cancelDelaySeconds"],
                                        120f),
                                    5f,
                                    3600f),
                                CrossingPoint = ReadVector(
                                    entry["crossingPoint"] as JObject,
                                    Vector3.zero),
                                CrossingAngleDegrees =
                                    ReadFloat(
                                        entry["crossingAngleDegrees"],
                                        0f),
                                ApproachLength = ReadFloat(
                                    entry["approachLength"],
                                    120f),
                                ReleaseLength = ReadFloat(
                                    entry["releaseLength"],
                                    60f),
                                Routes = routes,
                            });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Error(
                    "Could not load " + path + ": " + ex.Message);
            }
        }

        private static IEnumerable<string> DiscoverDefinitionFiles(
            string modsDirectory)
        {
            if (string.IsNullOrWhiteSpace(modsDirectory)
                || !Directory.Exists(modsDirectory))
            {
                yield break;
            }
            foreach (var modDirectory in Directory.GetDirectories(
                         modsDirectory))
            {
                foreach (var file in SafeFiles(modDirectory))
                    yield return file;
            }
        }

        private static IEnumerable<string> SafeFiles(string modDirectory)
        {
            var pending = new Stack<string>();
            pending.Push(modDirectory);
            while (pending.Count > 0)
            {
                var directory = pending.Pop();
                if (IsIgnoredDirectory(directory, modDirectory))
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

        private static bool IsIgnoredDirectory(
            string path,
            string modRoot)
        {
            if (string.Equals(path, modRoot,
                    StringComparison.OrdinalIgnoreCase))
                return false;
            var name = Path.GetFileName(path) ?? string.Empty;
            return name.StartsWith("backup",
                       StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("_", StringComparison.Ordinal)
                   || string.Equals(name, "Cache",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, ".venv",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "__pycache__",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string FileSignature(IEnumerable<string> paths)
        {
            return string.Join(
                "|",
                paths.OrderBy(path => path,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(path =>
                    {
                        var file = new FileInfo(path);
                        return file.FullName + ":"
                               + file.Length + ":"
                               + file.LastWriteTimeUtc.Ticks;
                    }));
        }

        private static GameObject FindSceneObjectByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var pieces = path.Split(
                new[] { '/' },
                2,
                StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0)
                return null;
            for (var sceneIndex = 0;
                 sceneIndex < SceneManager.sceneCount;
                 sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                var root = scene.GetRootGameObjects().FirstOrDefault(
                    candidate => candidate != null
                                 && string.Equals(
                                     candidate.name,
                                     pieces[0],
                                     StringComparison.Ordinal));
                if (root == null)
                    continue;
                return pieces.Length == 1
                    ? root
                    : root.transform.Find(pieces[1])?.gameObject;
            }
            return null;
        }

        private static Vector3 ReadVector(
            JObject value,
            Vector3 fallback)
        {
            if (value == null)
                return fallback;
            return new Vector3(
                ReadFloat(value["x"], fallback.x),
                ReadFloat(value["y"], fallback.y),
                ReadFloat(value["z"], fallback.z));
        }

        private static float ReadFloat(JToken value, float fallback)
        {
            if (value == null)
                return fallback;
            return float.TryParse(
                value.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : fallback;
        }

        private static IReadOnlyList<string> ReadStringArray(JArray array)
        {
            return array == null
                ? Array.Empty<string>()
                : array.Values<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray();
        }

        private static IReadOnlyList<string> ReadStringArrayWithFallback(
            JArray array,
            string fallback)
        {
            var values = ReadStringArray(array);
            if (values.Count > 0 || string.IsNullOrWhiteSpace(fallback))
                return values;
            return new[] { fallback };
        }

        private void RemoveSignals()
        {
            foreach (var signal in _signals.Values)
            {
                if (signal?.GameObject == null)
                    continue;
                try
                {
                    _transformer?.RemoveObjectToMove(
                        signal.GameObject.transform);
                }
                catch
                {
                }
                signal.GameObject.SetActive(false);
                UnityEngine.Object.Destroy(signal.GameObject);
            }
            _signals.Clear();
            _interlockingRuntime.Reset(_interlockings);
        }

        public void Dispose()
        {
            _enabled = false;
            _ctcRuntime.Dispose();
            _trainOrderRuntime.Dispose();
            RemoveSignals();
            _definitions.Clear();
            _interlockings.Clear();
            _interlockingRuntime.Reset(_interlockings);
            if (_root != null)
                UnityEngine.Object.Destroy(_root);
            _root = null;
            _graph = null;
            _transformer = null;
        }

        private sealed class SignalDefinition
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
            internal string SourcePath = string.Empty;
            internal bool TrackLocked;
            internal string TrackSegmentId = string.Empty;
            internal float TrackParameter;
            internal Vector3 TrackLocalPosition;
            internal Vector3 TrackLocalRotation;
        }
    }
}
