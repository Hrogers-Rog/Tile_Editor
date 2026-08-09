using System;
using System.IO;
using System.Linq;

namespace Hrogers.TileEditorBridge
{
    internal static class TileEditorBackupRetention
    {
        internal const int MaximumBackups = 3;

        internal static void PruneFor(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;
            try
            {
                var fullPath = Path.GetFullPath(sourcePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory)
                    || !Directory.Exists(directory))
                {
                    return;
                }
                var prefix = Path.GetFileName(fullPath)
                             + ".tile-editor-backup-";
                foreach (var stale in Directory.GetFiles(directory)
                             .Where(path => Path.GetFileName(path)
                                 .StartsWith(
                                     prefix,
                                     StringComparison.OrdinalIgnoreCase))
                             .OrderByDescending(File.GetLastWriteTimeUtc)
                             .Skip(MaximumBackups))
                {
                    File.Delete(stale);
                }
            }
            catch (IOException)
            {
                // A backup may still be open during an atomic save. It can
                // be pruned on the next save instead.
            }
            catch (UnauthorizedAccessException)
            {
                // Saving the content is more important than retention.
            }
        }
    }
}
