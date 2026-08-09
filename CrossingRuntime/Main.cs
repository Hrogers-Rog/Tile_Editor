using System;
using System.IO;
using UnityModManagerNet;

namespace Hrogers.CrossingRuntime
{
    public static class Main
    {
        private static UnityModManager.ModEntry _entry;
        private static CrossingRegistry _registry;
        private static string _modsDirectory = string.Empty;

        internal static string ModsDirectory => _modsDirectory;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            _entry = entry;
            _modsDirectory = string.IsNullOrWhiteSpace(entry.Path)
                ? string.Empty
                : Directory.GetParent(
                    entry.Path.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar))?.FullName
                  ?? string.Empty;
            _registry = new CrossingRegistry(entry.Logger);
            entry.OnUpdate = OnUpdate;
            entry.OnToggle = OnToggle;
            entry.OnUnload = OnUnload;
            ReloadDefinitions();
            entry.Logger.Log(
                "Grade Crossing Runtime loaded. Portable map crossings "
                + "apply to every native Auto Engineer train, including "
                + "player-owned equipment in Waypoint mode.");
            return true;
        }

        public static void ReloadDefinitions()
        {
            _registry?.Reload(ModsDirectory);
        }

        private static void OnUpdate(
            UnityModManager.ModEntry entry,
            float deltaTime)
        {
            try
            {
                _registry?.Tick(ModsDirectory);
            }
            catch (Exception ex)
            {
                entry.Logger.Error(
                    "Grade crossing runtime update failed: " + ex);
            }
        }

        private static bool OnToggle(
            UnityModManager.ModEntry entry,
            bool enabled)
        {
            _registry?.SetEnabled(enabled);
            return true;
        }

        private static bool OnUnload(UnityModManager.ModEntry entry)
        {
            _registry?.Dispose();
            _registry = null;
            _entry = null;
            _modsDirectory = string.Empty;
            return true;
        }
    }
}
