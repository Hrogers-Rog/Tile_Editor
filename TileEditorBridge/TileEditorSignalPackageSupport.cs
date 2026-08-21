using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        private const string SignalRuntimePackageId = "AITraffic";

        private void EnsureSignalRuntimeRequirement()
        {
            var folder = Path.GetDirectoryName(_graphPath);
            if (string.IsNullOrWhiteSpace(folder))
                return;

            var infoPath = Path.Combine(folder, "Info.json");
            var definitionPath = Path.Combine(folder, "Definition.json");
            if (File.Exists(infoPath))
            {
                EnsureUmmSignalRuntimeRequirement(infoPath);
                return;
            }
            if (File.Exists(definitionPath))
                EnsureLegacySignalRuntimeRequirement(definitionPath);
        }

        private void EnsureUmmSignalRuntimeRequirement(string path)
        {
            var document = JObject.Parse(File.ReadAllText(path));
            var changed = AddManifestId(
                document,
                "Requirements",
                SignalRuntimePackageId,
                true);
            changed |= AddManifestId(
                document,
                "LoadAfter",
                SignalRuntimePackageId,
                false);
            if (!changed)
                return;
            WritePackageManifestAtomically(path, document);
            _logger?.Log(
                "Added Railroad Operations (AITraffic) to Info.json because "
                + "this map authors portable signals/CTC.");
        }

        private void EnsureLegacySignalRuntimeRequirement(string path)
        {
            var document = JObject.Parse(File.ReadAllText(path));
            var changed = AddManifestId(
                document,
                "requires",
                SignalRuntimePackageId,
                false);
            changed |= AddManifestId(
                document,
                "loadAfter",
                SignalRuntimePackageId,
                false);
            if (!changed)
                return;
            WritePackageManifestAtomically(path, document);
            _logger?.Log(
                "Added Railroad Operations (AITraffic) to Definition.json "
                + "because this map authors portable signals/CTC.");
        }

        private static bool AddManifestId(
            JObject document,
            string propertyName,
            string id,
            bool objectForm)
        {
            var token = document[propertyName];
            var values = token as JArray;
            if (values == null)
            {
                values = token == null || token.Type == JTokenType.Null
                    ? new JArray()
                    : new JArray(token.DeepClone());
                document[propertyName] = values;
            }
            if (values.Any(value => string.Equals(
                    ManifestId(value),
                    id,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            values.Add(objectForm
                ? (JToken)new JObject { ["Id"] = id }
                : new JValue(id));
            return true;
        }

        private static string ManifestId(JToken token)
        {
            if (token == null)
                return string.Empty;
            if (token.Type == JTokenType.String)
                return ((string)token ?? string.Empty).Trim();
            var entry = token as JObject;
            return ((string)entry?["Id"]
                    ?? (string)entry?["id"]
                    ?? string.Empty).Trim();
        }

        private static void WritePackageManifestAtomically(
            string path,
            JObject document)
        {
            var temp = path + ".tile-editor.tmp";
            File.WriteAllText(temp, document.ToString(Formatting.Indented));
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }
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
    }
}
