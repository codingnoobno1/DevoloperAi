using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Syncro.Desktop.Services.Universe.Models;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe
{
    /// <summary>
    /// The Workspace Registry (universe.md §1) — the entry point to the Universe. Registers folders
    /// as workspaces, gives each a stable path-derived id, and mirrors each as a <c>Workspace</c>
    /// node in the graph so cross-workspace edges have real endpoints to point at.
    /// </summary>
    public sealed class UniverseRegistry
    {
        private readonly UniverseGraphStore _store;

        public UniverseRegistry(UniverseGraphStore store)
        {
            _store = store;
        }

        /// <summary>Register (or update) a folder as a workspace. Idempotent per path.</summary>
        public WorkspaceEntry Register(string path, string? name = null, string? orgId = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Workspace path is required.", nameof(path));

            var id = MakeWorkspaceId(path);
            var entry = _store.GetWorkspace(id) ?? new WorkspaceEntry { Id = id, RegisteredAt = DateTime.UtcNow };

            entry.Path = path;
            entry.Name = name ?? SafeFolderName(path);
            entry.OrgId = orgId ?? entry.OrgId;
            _store.UpsertWorkspace(entry);

            _store.UpsertNode(new GraphNode
            {
                Id = $"ws:{id}",
                Kind = NodeKind.Workspace,
                WorkspaceId = id,
                Path = path,
                Name = entry.Name
            });

            return entry;
        }

        public IReadOnlyList<WorkspaceEntry> All() => _store.Workspaces();

        public WorkspaceEntry? Get(string id) => _store.GetWorkspace(id);

        public WorkspaceEntry? GetByPath(string path)
        {
            var id = MakeWorkspaceId(path);
            return _store.GetWorkspace(id);
        }

        public void MarkIndexed(string id, IEnumerable<string>? stacks = null)
        {
            var entry = _store.GetWorkspace(id);
            if (entry == null) return;
            entry.LastIndexedAt = DateTime.UtcNow;
            if (stacks != null) entry.Stacks = stacks.Distinct().ToList();
            _store.UpsertWorkspace(entry);
        }

        /// <summary>Stable id from the normalized full path (case-insensitive on Windows).</summary>
        public static string MakeWorkspaceId(string path)
        {
            string normalized;
            try { normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { normalized = path; }
            normalized = normalized.ToLowerInvariant();

            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();  // 12-char stable id
        }

        private static string SafeFolderName(string path)
        {
            try
            {
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.IsNullOrWhiteSpace(name) ? path : name;
            }
            catch { return path; }
        }
    }
}
