using System;
using System.IO;
using System.Timers;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Workspace.Watch
{
    /// <summary>
    /// <see cref="IWorkspaceWatcher"/> backed by <see cref="FileSystemWatcher"/>. Raw OS events are
    /// filtered (<see cref="WorkspacePathFilter"/>) and coalesced (<see cref="ChangeCoalescer"/>); a
    /// debounce timer flushes the net changes as <see cref="FileChanged"/> events. The
    /// filter→coalesce→publish pipeline (<see cref="OnRawChange"/> / <see cref="FlushPending"/>) is
    /// public so it can be pumped deterministically in tests without a real filesystem or clock.
    /// </summary>
    public sealed class FileSystemWorkspaceWatcher : IWorkspaceWatcher, IDisposable
    {
        private readonly IEventBus _bus;
        private readonly ChangeCoalescer _coalescer = new();
        private readonly double _debounceMs;

        private FileSystemWatcher? _fsw;
        private System.Timers.Timer? _timer;
        private string? _root;

        public FileSystemWorkspaceWatcher(IEventBus bus, double debounceMs = 400)
        {
            _bus = bus;
            _debounceMs = debounceMs;
            _bus.Subscribe<WorkspaceOpened>(e => Start(e.WorkspacePath));
            _bus.Subscribe<WorkspaceClosed>(_ => Stop());
        }

        public bool IsWatching => _fsw != null;

        public void Start(string root)
        {
            Stop();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
            _root = root;

            _fsw = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _fsw.Created += (_, e) => OnRawChange(e.FullPath, FileChangeKind.Created);
            _fsw.Changed += (_, e) => OnRawChange(e.FullPath, FileChangeKind.Modified);
            _fsw.Deleted += (_, e) => OnRawChange(e.FullPath, FileChangeKind.Deleted);
            // A rename is a delete of the old path + a create of the new one.
            _fsw.Renamed += (_, e) =>
            {
                OnRawChange(e.OldFullPath, FileChangeKind.Deleted);
                OnRawChange(e.FullPath, FileChangeKind.Created);
            };
            _fsw.Error += (_, _) => { /* buffer overflow etc. — swallow; next change re-syncs */ };
            _fsw.EnableRaisingEvents = true;

            _timer = new System.Timers.Timer(_debounceMs) { AutoReset = true };
            _timer.Elapsed += (_, _) => FlushPending();
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;

            if (_fsw != null)
            {
                _fsw.EnableRaisingEvents = false;
                _fsw.Dispose();
                _fsw = null;
            }
            _root = null;
        }

        /// <summary>Filter + record one raw change. Public for deterministic tests (pump directly).</summary>
        public void OnRawChange(string fullPath, FileChangeKind kind)
        {
            if (_root == null || string.IsNullOrWhiteSpace(fullPath)) return;
            string rel;
            try { rel = Path.GetRelativePath(_root, fullPath); }
            catch { rel = fullPath; }
            if (!WorkspacePathFilter.ShouldWatch(rel)) return;
            _coalescer.Record(fullPath, kind);
        }

        /// <summary>Publish the coalesced changes as <see cref="FileChanged"/> events. Public for tests.</summary>
        public void FlushPending()
        {
            if (_root == null) return;
            foreach (var (path, kind) in _coalescer.Flush())
                _bus.Publish(new FileChanged(_root, path, kind));
        }

        public void Dispose() => Stop();
    }
}
