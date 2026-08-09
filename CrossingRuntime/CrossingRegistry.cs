using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.CrossingRuntime
{
    internal sealed class CrossingRegistry : IDisposable
    {
        private const string DefinitionFileName = "grade-crossings.json";
        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private readonly List<CrossingDefinition> _definitions =
            new List<CrossingDefinition>();
        private readonly Dictionary<string, TrackMarker> _markers =
            new Dictionary<string, TrackMarker>(
                StringComparer.OrdinalIgnoreCase);
        private Graph _graph;
        private bool _enabled = true;
        private float _nextTickAt;
        private float _nextFileCheckAt;
        private string _fileSignature = string.Empty;
        private string[] _definitionFiles = Array.Empty<string>();

        internal CrossingRegistry(
            UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
        }

        internal void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (!enabled)
                RemoveMarkers();
            else
                _nextTickAt = 0f;
        }

        internal void Reload(string modsDirectory)
        {
            _definitions.Clear();
            var files = DiscoverDefinitionFiles(modsDirectory).ToArray();
            _definitionFiles = files;
            _fileSignature = FileSignature(files);
            foreach (var file in files)
                LoadFile(file);
            RebuildMarkers();
            _logger?.Log(
                "Loaded " + _definitions.Count
                + " portable grade crossing definition(s) from "
                + files.Length + " map file(s).");
        }

        internal void Tick(string modsDirectory)
        {
            if (!_enabled || Time.unscaledTime < _nextTickAt)
                return;
            _nextTickAt = Time.unscaledTime + 0.5f;

            if (Time.unscaledTime >= _nextFileCheckAt)
            {
                _nextFileCheckAt = Time.unscaledTime + 2f;
                // Installed mod roots do not change during a running game. Poll the
                // files discovered at load instead of walking every mod directory;
                // Tile Editor explicitly calls ReloadDefinitions after creating a
                // new portable file.
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
            if (graph == null || !graph.HasPopulatedCollections)
            {
                if (_markers.Count > 0)
                    RemoveMarkers();
                _graph = null;
                return;
            }
            if (_graph != graph || _markers.Count == 0)
                RebuildMarkers();
        }

        private void RebuildMarkers()
        {
            RemoveMarkers();
            if (!_enabled)
                return;
            var graph = Graph.Shared;
            if (graph == null || !graph.HasPopulatedCollections)
                return;
            _graph = graph;
            foreach (var definition in _definitions.Where(
                         definition => definition.Enabled))
            {
                RegisterDefinition(graph, definition);
            }
        }

        private void RegisterDefinition(
            Graph graph,
            CrossingDefinition definition)
        {
            var node = graph.GetNode(definition.NodeId);
            if (node == null)
            {
                _logger?.Warning(
                    "Crossing '" + definition.Id + "' from "
                    + definition.SourceName + " could not find node '"
                    + definition.NodeId + "'.");
                return;
            }
            var connected = graph.SegmentsConnectedTo(node)
                .Where(segment => segment != null)
                .OrderBy(segment => segment.id,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
            var requested = definition.SegmentIds.Count > 0
                ? new HashSet<string>(
                    definition.SegmentIds,
                    StringComparer.OrdinalIgnoreCase)
                : null;
            var segments = requested == null
                ? connected
                : connected.Where(segment =>
                    requested.Contains(segment.id)).ToList();
            if (segments.Count == 0)
            {
                _logger?.Warning(
                    "Crossing '" + definition.Id
                    + "' has no usable segment at node '"
                    + definition.NodeId + "'.");
                return;
            }

            // Register the same physical crossing on each selected segment
            // touching the node. Native Auto Engineer searches markers along
            // its actual route, so this makes approaches from either side—and
            // player-owned Waypoint locomotives—see the crossing reliably.
            foreach (var segment in segments)
            {
                var key = definition.SourceKey + "|" + definition.Id
                          + "|" + segment.id;
                var markerObject = new GameObject(
                    "Grade Crossing - " + definition.Id + " - "
                    + segment.id);
                markerObject.SetActive(false);
                markerObject.transform.SetParent(graph.transform, false);
                var marker = markerObject.AddComponent<TrackMarker>();
                marker.id = "hrogers-crossing-"
                            + StableIdPart(definition.SourceName)
                            + "-" + StableIdPart(definition.Id)
                            + "-" + StableIdPart(segment.id);
                marker.type = TrackMarkerType.Crossing;
                marker.Location = new Location(
                    segment,
                    0f,
                    segment.EndForNode(node));
                _markers[key] = marker;
                markerObject.SetActive(true);
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                var document = JObject.Parse(File.ReadAllText(path));
                var crossings = document["crossings"] as JArray;
                if (crossings == null)
                    return;
                var sourceName = new DirectoryInfo(
                    Path.GetDirectoryName(path) ?? string.Empty).Name;
                foreach (var entry in crossings.OfType<JObject>())
                {
                    var id = ((string)entry["id"] ?? string.Empty).Trim();
                    var nodeId = ((string)entry["nodeId"]
                                  ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(id)
                        || string.IsNullOrWhiteSpace(nodeId))
                    {
                        continue;
                    }
                    var segmentIds = new List<string>();
                    var legacySegment = ((string)entry["segmentId"]
                                         ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(legacySegment))
                        segmentIds.Add(legacySegment);
                    if (entry["segmentIds"] is JArray array)
                    {
                        segmentIds.AddRange(array.Values<string>().Where(
                            value => !string.IsNullOrWhiteSpace(value)));
                    }
                    _definitions.Add(new CrossingDefinition
                    {
                        Id = id,
                        NodeId = nodeId,
                        Enabled = (bool?)entry["enabled"] ?? true,
                        SegmentIds = segmentIds.Distinct(
                            StringComparer.OrdinalIgnoreCase).ToList(),
                        SourceKey = Path.GetFullPath(path),
                        SourceName = sourceName,
                    });
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
                var rootFile = Path.Combine(
                    modDirectory,
                    DefinitionFileName);
                if (File.Exists(rootFile))
                    yield return rootFile;
                foreach (var child in SafeDirectories(modDirectory))
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith("backup", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("_", StringComparison.Ordinal)
                        || string.Equals(name, "Cache",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, ".venv",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var nested = Path.Combine(child, DefinitionFileName);
                    if (File.Exists(nested))
                        yield return nested;
                }
            }
        }

        private static IEnumerable<string> SafeDirectories(string path)
        {
            try
            {
                return Directory.GetDirectories(path);
            }
            catch
            {
                return Array.Empty<string>();
            }
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

        private static string StableIdPart(string value)
        {
            var result = new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Take(32)
                .ToArray());
            return string.IsNullOrWhiteSpace(result) ? "item" : result;
        }

        private void RemoveMarkers()
        {
            foreach (var marker in _markers.Values)
            {
                if (marker == null)
                    continue;
                marker.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(marker.gameObject);
            }
            _markers.Clear();
        }

        public void Dispose()
        {
            _enabled = false;
            RemoveMarkers();
            _definitions.Clear();
            _graph = null;
        }

        private sealed class CrossingDefinition
        {
            internal string Id = string.Empty;
            internal string NodeId = string.Empty;
            internal bool Enabled = true;
            internal List<string> SegmentIds = new List<string>();
            internal string SourceKey = string.Empty;
            internal string SourceName = string.Empty;
        }
    }
}
