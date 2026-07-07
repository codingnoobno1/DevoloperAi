using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Syncro.Desktop.Services.Universe.Models;

namespace Syncro.Desktop.Services.Universe.Store
{
    /// <summary>
    /// The persistent Universe graph (universe.md §3), backed by LiteDB — an embedded, indexed
    /// document DB (already a project dependency, used by RepairDatabaseService). Lives at
    /// <c>%LocalAppData%/SyncroDesktop/universe.db</c>, OUTSIDE any workspace, so it spans folders
    /// and survives across sessions. Registered as a DI singleton; holds one connection for the app
    /// lifetime.
    ///
    /// Everything here is deliberately synchronous and cheap: LiteDB queries hit indexes, and the
    /// reverse index on <c>ToId</c> gives O(log n) "who depends on X" — the primitive the impact
    /// engine and auto-mode levels (§5) are built on.
    /// </summary>
    public sealed class UniverseGraphStore : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<GraphNode> _nodes;
        private readonly ILiteCollection<GraphEdge> _edges;
        private readonly ILiteCollection<WorkspaceEntry> _workspaces;

        public UniverseGraphStore(string? dbPath = null)
        {
            var path = dbPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SyncroDesktop", "universe.db");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            _db = new LiteDatabase(path);
            _nodes = _db.GetCollection<GraphNode>("nodes");
            _edges = _db.GetCollection<GraphEdge>("edges");
            _workspaces = _db.GetCollection<WorkspaceEntry>("workspaces");

            _nodes.EnsureIndex(x => x.Kind);
            _nodes.EnsureIndex(x => x.WorkspaceId);
            _nodes.EnsureIndex(x => x.Path);
            _edges.EnsureIndex(x => x.FromId);
            _edges.EnsureIndex(x => x.ToId);
            _edges.EnsureIndex(x => x.Kind);
        }

        // ── nodes ────────────────────────────────────────────────────────────────────────
        public void UpsertNode(GraphNode node)
        {
            node.UpdatedAt = DateTime.UtcNow;
            _nodes.Upsert(node);
        }

        public void UpsertNodes(IEnumerable<GraphNode> nodes)
        {
            foreach (var n in nodes) n.UpdatedAt = DateTime.UtcNow;
            _nodes.Upsert(nodes);
        }

        public GraphNode? GetNode(string id) => _nodes.FindById(id);

        public IReadOnlyList<GraphNode> NodesInWorkspace(string workspaceId) =>
            _nodes.Find(n => n.WorkspaceId == workspaceId).ToList();

        public IReadOnlyList<GraphNode> NodesByKind(NodeKind kind) =>
            _nodes.Find(n => n.Kind == kind).ToList();

        public IReadOnlyList<GraphNode> NodesByKindInWorkspace(string workspaceId, NodeKind kind) =>
            _nodes.Find(n => n.WorkspaceId == workspaceId && n.Kind == kind).ToList();

        /// <summary>Remove all nodes derived from one file (used on incremental re-index, §4).</summary>
        public int DeleteNodesForPath(string workspaceId, string path) =>
            _nodes.DeleteMany(n => n.WorkspaceId == workspaceId && n.Path == path);

        // ── edges ────────────────────────────────────────────────────────────────────────
        public void UpsertEdge(GraphEdge edge)
        {
            if (string.IsNullOrEmpty(edge.Id))
                edge.Id = GraphEdge.MakeId(edge.FromId, edge.Kind, edge.ToId);
            edge.UpdatedAt = DateTime.UtcNow;
            _edges.Upsert(edge);
        }

        public void UpsertEdges(IEnumerable<GraphEdge> edges)
        {
            foreach (var e in edges) UpsertEdge(e);
        }

        /// <summary>Edges leaving a node — "what does X depend on / call".</summary>
        public IReadOnlyList<GraphEdge> OutEdges(string fromId) =>
            _edges.Find(e => e.FromId == fromId).ToList();

        /// <summary>Edges entering a node — "who depends on / calls X" (reverse index).</summary>
        public IReadOnlyList<GraphEdge> InEdges(string toId) =>
            _edges.Find(e => e.ToId == toId).ToList();

        public IReadOnlyList<GraphEdge> EdgesByKind(EdgeKind kind) =>
            _edges.Find(e => e.Kind == kind).ToList();

        public int DeleteEdgesFrom(string fromId) => _edges.DeleteMany(e => e.FromId == fromId);

        // ── workspace registry ────────────────────────────────────────────────────────────
        public void UpsertWorkspace(WorkspaceEntry entry) => _workspaces.Upsert(entry);

        public WorkspaceEntry? GetWorkspace(string id) => _workspaces.FindById(id);

        public IReadOnlyList<WorkspaceEntry> Workspaces() => _workspaces.FindAll().ToList();

        // ── stats ────────────────────────────────────────────────────────────────────────
        public (int nodes, int edges, int workspaces) Stats() =>
            (_nodes.Count(), _edges.Count(), _workspaces.Count());

        public void Dispose() => _db?.Dispose();
    }
}
