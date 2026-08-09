using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal static class TileEditorTrackOverrides
    {
        private const string FileName = "tile-editor-track-overrides.json";
        private static readonly HashSet<string> DisabledBumpers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _path = string.Empty;
        private static JObject _document;

        internal static void LoadForGraph(string graphPath)
        {
            DisabledBumpers.Clear();
            _document = null;
            _path = string.Empty;
            if (string.IsNullOrWhiteSpace(graphPath))
                return;
            var directory = Path.GetDirectoryName(
                Path.GetFullPath(graphPath));
            if (string.IsNullOrWhiteSpace(directory))
                return;
            _path = Path.Combine(directory, FileName);
            if (File.Exists(_path))
            {
                try
                {
                    _document = JObject.Parse(File.ReadAllText(_path));
                }
                catch
                {
                    _document = new JObject();
                }
            }
            else
            {
                _document = new JObject();
            }
            if (_document["disabledBumpers"] is JArray disabled)
            {
                foreach (var id in disabled.Values<string>()
                             .Where(value =>
                                 !string.IsNullOrWhiteSpace(value)))
                {
                    DisabledBumpers.Add(id.Trim());
                }
            }
        }

        internal static bool BumperEnabled(string nodeId)
        {
            return string.IsNullOrWhiteSpace(nodeId)
                   || !DisabledBumpers.Contains(nodeId);
        }

        internal static void SetBumperEnabled(
            string nodeId,
            bool enabled)
        {
            if (string.IsNullOrWhiteSpace(nodeId)
                || string.IsNullOrWhiteSpace(_path))
            {
                throw new InvalidOperationException(
                    "Open an editable graph before changing a track bumper.");
            }
            if (enabled)
                DisabledBumpers.Remove(nodeId);
            else
                DisabledBumpers.Add(nodeId);
            _document ??= new JObject();
            _document["version"] = 1;
            _document["disabledBumpers"] = new JArray(
                DisabledBumpers.OrderBy(
                    value => value,
                    StringComparer.OrdinalIgnoreCase));
            AtomicWrite(_path, _document.ToString(Formatting.Indented));
        }

        private static void AtomicWrite(string path, string contents)
        {
            var temporary = path + ".tile-editor.tmp";
            File.WriteAllText(temporary, contents);
            if (File.Exists(path))
            {
                var backup = path + ".tile-editor-backup-"
                             + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, backup, false);
                TileEditorBackupRetention.PruneFor(path);
                try
                {
                    File.Replace(temporary, path, null);
                    return;
                }
                catch
                {
                    File.Delete(path);
                }
            }
            File.Move(temporary, path);
        }

        internal static GameObject EmptyGeneratedObject(
            TrackNode node,
            string kind)
        {
            var result = new GameObject(
                "tile-editor-disabled-" + kind + "-"
                + (node?.id ?? "unknown"));
            result.SetActive(true);
            return result;
        }
    }

    [HarmonyPatch(
        typeof(TrackObjectBuilder),
        nameof(TrackObjectBuilder.CreateBumperObject))]
    internal static class TileEditorBumperObjectPatch
    {
        private static bool Prefix(
            TrackNode node,
            ref GameObject __result)
        {
            if (TileEditorTrackOverrides.BumperEnabled(node?.id))
                return true;
            __result = TileEditorTrackOverrides.EmptyGeneratedObject(
                node,
                "bumper");
            return false;
        }
    }

    [HarmonyPatch(
        typeof(TrackObjectBuilder),
        nameof(TrackObjectBuilder.CreateBumperMasks))]
    internal static class TileEditorBumperMaskPatch
    {
        private static bool Prefix(
            TrackNode node,
            ref GameObject __result)
        {
            if (TileEditorTrackOverrides.BumperEnabled(node?.id))
                return true;
            __result = TileEditorTrackOverrides.EmptyGeneratedObject(
                node,
                "bumper-mask");
            return false;
        }
    }
}
