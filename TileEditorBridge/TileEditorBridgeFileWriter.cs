using System;
using System.IO;
using System.Threading;

namespace Hrogers.TileEditorBridge
{
    /// <summary>
    /// Keeps periodic bridge heartbeat disk I/O off Unity's main thread. New
    /// snapshots replace an older pending snapshot, so a slow disk cannot build
    /// an unbounded writer queue.
    /// </summary>
    internal sealed class TileEditorBridgeFileWriter : IDisposable
    {
        private readonly object _gate = new object();
        private readonly string _path;
        private string _pendingContents;
        private string _lastError;
        private bool _workerRunning;
        private bool _disposed;

        internal TileEditorBridgeFileWriter(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        internal void QueueLatest(string contents)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _pendingContents = contents ?? string.Empty;
                if (_workerRunning)
                    return;
                _workerRunning = true;
                ThreadPool.QueueUserWorkItem(_ => Drain());
            }
        }

        internal string TakeLastError()
        {
            lock (_gate)
            {
                var error = _lastError;
                _lastError = null;
                return error;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _pendingContents = null;
            }
        }

        private void Drain()
        {
            while (true)
            {
                string contents;
                lock (_gate)
                {
                    if (_disposed || _pendingContents == null)
                    {
                        _workerRunning = false;
                        return;
                    }
                    contents = _pendingContents;
                    _pendingContents = null;
                }

                try
                {
                    AtomicWrite(_path, contents);
                }
                catch (Exception ex)
                {
                    lock (_gate)
                        _lastError = ex.GetBaseException().Message;
                }
            }
        }

        private static void AtomicWrite(string path, string contents)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents);
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }
            try
            {
                File.Replace(tempPath, path, null);
            }
            catch
            {
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }
    }
}
