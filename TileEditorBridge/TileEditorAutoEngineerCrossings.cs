using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        private const string PortableCrossingFileName =
            "grade-crossings.json";
        private string _portableCrossingPath = string.Empty;
        private readonly HashSet<string> _autoEngineerCrossingNodes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal bool AutoEngineerCrossingsAvailable =>
            GraphOpen
            && !string.IsNullOrWhiteSpace(_portableCrossingPath);

        internal bool CrossingRuntimeLoaded =>
            FindCrossingRuntimeReloadMethod() != null;

        internal bool SelectedNodeIsAutoEngineerCrossing =>
            _selectedNode != null
            && _autoEngineerCrossingNodes.Contains(_selectedNode.id);

        private void RefreshAutoEngineerCrossings()
        {
            _autoEngineerCrossingNodes.Clear();
            var graphDirectory = string.IsNullOrWhiteSpace(_graphPath)
                ? string.Empty
                : Path.GetDirectoryName(_graphPath);
            _portableCrossingPath =
                string.IsNullOrWhiteSpace(graphDirectory)
                    ? string.Empty
                    : Path.Combine(
                        graphDirectory,
                        PortableCrossingFileName);
            if (!File.Exists(_portableCrossingPath))
                return;
            try
            {
                var document = JObject.Parse(
                    File.ReadAllText(_portableCrossingPath));
                if (document["crossings"] is JArray crossings)
                {
                    foreach (var entry in crossings.OfType<JObject>())
                    {
                        var nodeId = (string)entry["nodeId"];
                        if (!string.IsNullOrWhiteSpace(nodeId)
                            && ((bool?)entry["enabled"] ?? true))
                        {
                            _autoEngineerCrossingNodes.Add(nodeId);
                        }
                    }
                }
            }
            catch
            {
                // The toggle reports the parse error when editing is tried.
            }
        }

        internal void ToggleSelectedAutoEngineerCrossing()
        {
            var node = RequireNode();
            if (string.IsNullOrWhiteSpace(_portableCrossingPath))
            {
                throw new InvalidOperationException(
                    "Open an editable map graph before adding a crossing.");
            }
            if (!_graph.SegmentsConnectedTo(node).Any())
                throw new InvalidOperationException(
                    "The crossing marker must be placed on a track node.");

            JObject document;
            if (File.Exists(_portableCrossingPath))
            {
                document = JObject.Parse(
                    File.ReadAllText(_portableCrossingPath));
            }
            else
            {
                document = new JObject
                {
                    ["version"] = 1,
                };
            }
            var crossings = document["crossings"] as JArray;
            if (crossings == null)
            {
                crossings = new JArray();
                document["crossings"] = crossings;
            }
            var existing = crossings.OfType<JObject>().FirstOrDefault(
                entry => string.Equals(
                    (string)entry["nodeId"],
                    node.id,
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Remove();
                _autoEngineerCrossingNodes.Remove(node.id);
            }
            else
            {
                var idRoot = "tile-editor-" + CrossingIdPart(node.id);
                var id = idRoot;
                var suffix = 2;
                while (crossings.OfType<JObject>().Any(entry =>
                           string.Equals(
                               (string)entry["id"],
                               id,
                               StringComparison.OrdinalIgnoreCase)))
                {
                    id = idRoot + "-" + suffix.ToString(
                        CultureInfo.InvariantCulture);
                    suffix++;
                }
                crossings.Add(new JObject
                {
                    ["id"] = id,
                    ["enabled"] = true,
                    ["nodeId"] = node.id,
                });
                _autoEngineerCrossingNodes.Add(node.id);
            }

            if (File.Exists(_portableCrossingPath))
            {
                var backup = _portableCrossingPath
                             + ".tile-editor-backup-"
                             + DateTime.Now.ToString(
                                 "yyyyMMdd-HHmmss-fff",
                                 CultureInfo.InvariantCulture);
                File.Copy(_portableCrossingPath, backup, false);
                TileEditorBackupRetention.PruneFor(
                    _portableCrossingPath);
            }
            var temporary = _portableCrossingPath
                            + ".tile-editor.tmp";
            File.WriteAllText(
                temporary,
                document.ToString(Formatting.Indented));
            if (File.Exists(_portableCrossingPath))
            {
                try
                {
                    File.Replace(
                        temporary,
                        _portableCrossingPath,
                        null);
                }
                catch
                {
                    File.Delete(_portableCrossingPath);
                    File.Move(temporary, _portableCrossingPath);
                }
            }
            else
            {
                File.Move(temporary, _portableCrossingPath);
            }
            ReloadPortableCrossingRuntime();
        }

        private static string CrossingIdPart(string value)
        {
            var chars = (value ?? string.Empty)
                .ToLowerInvariant()
                .Select(character =>
                    char.IsLetterOrDigit(character)
                        ? character
                        : '-')
                .ToArray();
            var result = new string(chars).Trim('-');
            return string.IsNullOrWhiteSpace(result)
                ? "crossing"
                : result;
        }

        private static MethodInfo FindCrossingRuntimeReloadMethod()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    "Hrogers.CrossingRuntime",
                    StringComparison.OrdinalIgnoreCase));
            return assembly?.GetType(
                    "Hrogers.CrossingRuntime.Main",
                    false)
                ?.GetMethod(
                    "ReloadDefinitions",
                    BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic);
        }

        private static void ReloadPortableCrossingRuntime()
        {
            FindCrossingRuntimeReloadMethod()?.Invoke(null, null);
        }
    }
}
