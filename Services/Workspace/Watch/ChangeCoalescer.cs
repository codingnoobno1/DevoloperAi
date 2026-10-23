using System;
using System.Collections.Generic;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Workspace.Watch
{
    /// <summary>
    /// Collapses the burst of raw filesystem events an editor fires for a single save into one net
    /// change per path. Pure and thread-safe: <see cref="Record"/> accumulates, <see cref="Flush"/>
    /// atomically drains. Time-agnostic — the debounce timer lives in the watcher; the merge rules live
    /// here so they can be unit-tested without any clock.
    /// </summary>
    public sealed class ChangeCoalescer
    {
        private readonly object _gate = new();
        private Dictionary<string, FileChangeKind> _pending = new(StringComparer.OrdinalIgnoreCase);

        public void Record(string path, FileChangeKind kind)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            lock (_gate)
            {
                if (_pending.TryGetValue(path, out var existing))
                {
                    var merged = Merge(existing, kind);
                    if (merged is null) _pending.Remove(path);   // e.g. created-then-deleted nets nothing
                    else _pending[path] = merged.Value;
                }
                else
                {
                    _pending[path] = kind;
                }
            }
        }

        /// <summary>Atomically returns and clears the accumulated changes.</summary>
        public IReadOnlyList<(string Path, FileChangeKind Kind)> Flush()
        {
            Dictionary<string, FileChangeKind> drained;
            lock (_gate)
            {
                if (_pending.Count == 0) return Array.Empty<(string, FileChangeKind)>();
                drained = _pending;
                _pending = new Dictionary<string, FileChangeKind>(StringComparer.OrdinalIgnoreCase);
            }

            var list = new List<(string, FileChangeKind)>(drained.Count);
            foreach (var kv in drained) list.Add((kv.Key, kv.Value));
            return list;
        }

        public int PendingCount { get { lock (_gate) return _pending.Count; } }

        /// <summary>Net effect of an earlier change followed by a later one on the same path.
        /// Returns null when the two cancel out.</summary>
        private static FileChangeKind? Merge(FileChangeKind earlier, FileChangeKind later) => (earlier, later) switch
        {
            (FileChangeKind.Created, FileChangeKind.Deleted) => null,                       // never really existed
            (FileChangeKind.Created, FileChangeKind.Modified) => FileChangeKind.Created,     // still a create
            (FileChangeKind.Created, FileChangeKind.Created) => FileChangeKind.Created,
            (FileChangeKind.Deleted, FileChangeKind.Created) => FileChangeKind.Modified,     // recreated
            (FileChangeKind.Deleted, FileChangeKind.Modified) => FileChangeKind.Modified,
            (FileChangeKind.Modified, FileChangeKind.Deleted) => FileChangeKind.Deleted,
            (FileChangeKind.Modified, FileChangeKind.Created) => FileChangeKind.Modified,
            (_, _) => later                                                                  // last write wins
        };
    }
}
