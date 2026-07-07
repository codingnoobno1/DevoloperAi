using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>Result of matching one frontend call against the backend's routes.</summary>
    public enum MatchState
    {
        /// <summary>Call maps to a route with the same method + path (and compatible shape).</summary>
        Matched,

        /// <summary>Method + path match, but the request/response fields differ.</summary>
        ShapeMismatch,

        /// <summary>A route with the same path exists, but under a different HTTP method.</summary>
        MethodMismatch,

        /// <summary>No backend route matches this call — the backend is missing it.</summary>
        Missing
    }

    /// <summary>One edge in the connection graph: a frontend call and the backend route it maps to.</summary>
    public sealed class ConnectionEdge
    {
        public ApiCallSite Call { get; set; } = new();

        /// <summary>The matched backend route, or null when <see cref="State"/> is <see cref="MatchState.Missing"/>.</summary>
        public BackendRoute? Route { get; set; }

        public MatchState State { get; set; }

        /// <summary>Human-readable notes: why it's a mismatch, or that it matched on a path suffix.</summary>
        public List<string> Diffs { get; set; } = new();
    }

    /// <summary>The full frontend↔backend picture: every call as an edge, plus routes nothing calls.</summary>
    public sealed class ConnectionReport
    {
        public List<ConnectionEdge> Edges { get; set; } = new();

        /// <summary>Backend routes no frontend call reaches (dead endpoints / not-yet-consumed).</summary>
        public List<BackendRoute> OrphanRoutes { get; set; } = new();

        public int Matched { get; set; }
        public int ShapeMismatch { get; set; }
        public int MethodMismatch { get; set; }
        public int Missing { get; set; }
    }
}
