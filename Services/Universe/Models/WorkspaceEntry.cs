using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Universe.Models
{
    /// <summary>
    /// A workspace registered in the Universe — the unit above which cross-workspace relationships
    /// are held. Persisted independently of any project's <c>.syncro_db</c> so it survives across
    /// sessions and spans folders (universe.md §1).
    /// </summary>
    public sealed class WorkspaceEntry
    {
        /// <summary>Stable id derived from the normalized path. LiteDB uses this as _id.</summary>
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        public string Path { get; set; } = "";

        /// <summary>Optional organization grouping ("Universe" root).</summary>
        public string? OrgId { get; set; }

        /// <summary>Detected stacks/frameworks (populated during indexing).</summary>
        public List<string> Stacks { get; set; } = new();

        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastIndexedAt { get; set; }
    }
}
