using System;

namespace Syncro.Desktop.Services.Universe.Models
{
    /// <summary>The relationship a <see cref="GraphEdge"/> represents.</summary>
    public enum EdgeKind
    {
        Imports,    // file → file / symbol → symbol
        Calls,      // symbol → symbol
        Exposes,    // service → endpoint
        Consumes,   // apiCall → endpoint (THE cross-workspace edge)
        DependsOn,  // workspace → workspace
        Shares,     // workspace → package / dto / auth
        RunsOn,     // process → port
        Tests,      // test symbol → symbol under test
        Contains,   // workspace → file, file → symbol

        // ── runtime graph (runtime.md R1+) ──
        Affects,    // dependency/process → the consumers impacted if it fails
        Covers      // testRun → symbol it exercises
    }

    /// <summary>
    /// One directed, typed relationship in the Universe graph. <c>Id</c> is a deterministic
    /// <c>from|kind|to</c> string so re-deriving the same edge upserts in place rather than
    /// duplicating. Confidence lets heuristic (cross-language / suffix-matched) edges be scored
    /// rather than treated as authoritative — see universe.md §11.
    /// </summary>
    public sealed class GraphEdge
    {
        public string Id { get; set; } = "";

        public string FromId { get; set; } = "";

        public string ToId { get; set; } = "";

        public EdgeKind Kind { get; set; }

        public double Confidence { get; set; } = 1.0;

        /// <summary>What derived this edge, e.g. "ast", "connector", "wiring".</summary>
        public string? Source { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public static string MakeId(string fromId, EdgeKind kind, string toId) => $"{fromId}|{kind}|{toId}";
    }
}
