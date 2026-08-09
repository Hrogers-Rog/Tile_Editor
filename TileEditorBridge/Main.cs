using System;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.TileEditorBridge
{
    public static class Main
    {
        private static UnityModManager.ModEntry.ModLogger _logger;
        private static TileEditorBridgePanel _panel;
        private static Harmony _harmony;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            _logger = modEntry.Logger;
            try
            {
                _harmony = new Harmony(modEntry.Info.Id);
                _harmony.PatchAll(typeof(Main).Assembly);
            }
            catch (Exception ex)
            {
                _harmony = null;
                _logger.Error(
                    "Tile Editor mouse-camera lock could not be installed: "
                    + ex);
            }
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGui;
            modEntry.OnUnload = OnUnload;
            _logger.Log(
                "Tile Editor Bridge v" + SuiteVersion.Value
                + " loaded. Press F9 for the in-game Geo editor.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool enabled)
        {
            try
            {
                if (enabled)
                {
                    EnsurePanel();
                    _panel.SetRuntimeEnabled(true);
                }
                else if (_panel != null)
                {
                    _panel.SetRuntimeEnabled(false);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Error("Tile Editor Bridge toggle failed: " + ex);
                return false;
            }
        }

        private static void OnGui(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Press F9 in game to show or hide Tile Editor edit mode.");
            GUILayout.Label(
                "Choose any installed RailLoader game-graph in the F9 panel; "
                + "the desktop Tile Editor is optional.");
            if (GUILayout.Button("Show panel", GUILayout.Width(140f)))
            {
                EnsurePanel();
                _panel.Show();
            }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (_panel != null)
            {
                UnityEngine.Object.Destroy(_panel.gameObject);
                _panel = null;
            }
            TileEditorCameraInput.SetMouseCameraLocked(false);
            TileEditorCameraInput.EditorInputActive = false;
            _harmony?.UnpatchAll(modEntry.Info.Id);
            _harmony = null;
            return true;
        }

        private static void EnsurePanel()
        {
            if (_panel != null)
                return;
            var go = new GameObject("Hrogers.TileEditorBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _panel = go.AddComponent<TileEditorBridgePanel>();
            _panel.Initialize(_logger);
        }
    }
}
