using System;
using System.Collections.Generic;
using System.Linq;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private readonly List<string> _desktopSyncEvents =
            new List<string>();

        private bool DesktopGraphHasUnsavedChanges =>
            IsEditorOnline()
            && _state != null
            && (_state.graphDirty || _state.dirty);

        private void NotifyDesktopFilesSaved(
            string kind,
            IReadOnlyList<string> paths)
        {
            if (!IsEditorOnline() || paths == null)
                return;
            var saved = paths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (saved.Length == 0)
                return;
            var category = string.IsNullOrWhiteSpace(kind)
                ? "content"
                : kind.Trim().ToLowerInvariant();
            foreach (var path in saved)
            {
                _desktopSyncEvents.Add(
                    Guid.NewGuid().ToString("N")
                    + "\t"
                    + UnixMilliseconds()
                    + "\t"
                    + category
                    + "\t"
                    + path);
            }
            if (_desktopSyncEvents.Count > 256)
            {
                _desktopSyncEvents.RemoveRange(
                    0,
                    _desktopSyncEvents.Count - 256);
            }
            var payload = new List<string> { "batch" };
            payload.AddRange(_desktopSyncEvents);
            SendCommand(
                "files_saved_in_game",
                string.Join("\n", payload));
        }

        private string SyncNotificationSuffix(
            IReadOnlyList<string> paths)
        {
            return IsEditorOnline()
                   && paths != null
                   && paths.Any(
                       path => !string.IsNullOrWhiteSpace(path))
                ? "; desktop refresh sent"
                : string.Empty;
        }

        private void SaveGraphAndSyncDesktop()
        {
            if (DesktopGraphHasUnsavedChanges)
            {
                _lastPanelMessage =
                    "Save or undo desktop graph changes before saving in-game";
                return;
            }
            try
            {
                _mapEditor.Save();
                var paths = new[] { _mapEditor.GraphPath };
                NotifyDesktopFilesSaved("graph", paths);
                _lastPanelMessage =
                    "Saved Tile Editor graph layer"
                    + SyncNotificationSuffix(paths);
            }
            catch (Exception ex)
            {
                _lastPanelMessage = ex.Message;
                _logger?.Warning(
                    "In-game graph save failed: " + ex);
            }
        }

        private void SaveSplineysAndSyncDesktop()
        {
            if (DesktopGraphHasUnsavedChanges)
            {
                _lastPanelMessage =
                    "Save or undo desktop content before saving Splineys";
                return;
            }
            try
            {
                _mapEditor.SaveSplineys();
                NotifyDesktopFilesSaved(
                    "spliney",
                    _mapEditor.LastSavedSplinePaths);
                _lastPanelMessage =
                    "Saved road, river, and bridge splineys"
                    + SyncNotificationSuffix(
                        _mapEditor.LastSavedSplinePaths);
            }
            catch (Exception ex)
            {
                _lastPanelMessage = ex.Message;
                _logger?.Warning(
                    "In-game Spliney save failed: " + ex);
            }
        }

        private void SavePolesAndSyncDesktop()
        {
            if (DesktopGraphHasUnsavedChanges)
            {
                _lastPanelMessage =
                    "Save or undo desktop content before saving poles";
                return;
            }
            try
            {
                _mapEditor.SaveTelegraphPoles();
                NotifyDesktopFilesSaved(
                    "poles",
                    _mapEditor.LastSavedTelegraphPolePaths);
                _lastPanelMessage =
                    "Saved telegraph poles and wires"
                    + SyncNotificationSuffix(
                        _mapEditor.LastSavedTelegraphPolePaths);
            }
            catch (Exception ex)
            {
                _lastPanelMessage = ex.Message;
                _logger?.Warning(
                    "In-game pole save failed: " + ex);
            }
        }
    }
}
