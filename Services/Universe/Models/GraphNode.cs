using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Universe.Models
{
    /// <summary>The kind of thing a <see cref="GraphNode"/> represents in the Universe graph.</summary>
    public enum NodeKind
    {
        Workspace,
        File,
        Symbol,     // class / function / interface / type
        Endpoint,   // an HTTP route a service exposes
        ApiCall,    // an HTTP call a client makes
        Service,
        Package,    // an npm / pip / nuget dependency
        Process,    // a running stack (runtime graph)
        EnvVar,
        Migration,
        Hindsight,  // a recorded decision / architecture-impact memory

        // ── runtime graph (runtime.md R1+) ──
        Container,      // a docker container
        TestRun,        // a test execution result
        Metric,         // a sampled cpu/mem/latency reading
        SchemaObject,   // a db collection / table / schema element
        GitState        // branch / dirty / ahead-behind for a workspace
    }

    /// <summary>
    /// One node in the persistent cross-workspace graph (universe.md §3). Deliberately shallow and
    /// LiteDB-mappable: the <c>Id</c> property is the document id (set deterministically by callers
    /// so upserts are idempotent), never auto-generated.
    /// </summary>
    public sealed class GraphNode
    {
        /// <summary>Deterministic id, e.g. <c>sym:{workspace}:{path}:{name}</c>. LiteDB uses this as _id.</summary>
        public string Id { get; set; } = "";

        public NodeKind Kind { get; set; }

        /// <summary>The workspace this node belongs to (empty for cross-cutting nodes).</summary>
        public string WorkspaceId { get; set; } = "";

        /// <summary>Absolute file path, when the node maps to something on disk.</summary>
        public string? Path { get; set; }

        public string Name { get; set; } = "";

        public string? Language { get; set; }

        public string? Signature { get; set; }

        /// <summary>Content hash of the source the node was derived from — enables hash-gated re-index.</summary>
        public string? Hash { get; set; }

        public Dictionary<string, string> Meta { get; set; } = new();

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
