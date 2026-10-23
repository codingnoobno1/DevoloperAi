using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>One field in a persisted route shape (request or response). Mirrors
    /// <see cref="Models.ApiField"/> but is decoupled from it so a snapshot on disk stays stable even
    /// if the live model changes.</summary>
    public sealed class FieldSnapshot
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "any";
        public bool Required { get; set; }
    }

    /// <summary>One backend route captured for drift comparison.</summary>
    public sealed class RouteSnapshot
    {
        public string Method { get; set; } = "GET";
        public string PathTemplate { get; set; } = "";

        /// <summary>Stable identity = "{METHOD} {PathTemplate}".</summary>
        public string Id => $"{Method.ToUpperInvariant()} {PathTemplate}";

        public List<FieldSnapshot> Request { get; set; } = new();
        public List<FieldSnapshot> Response { get; set; } = new();
    }

    /// <summary>
    /// A point-in-time capture of a backend's API surface — the baseline the Contract Engine diffs
    /// against to detect breaking drift (e.g. a response field the frontend still reads was removed).
    /// Serialized to <c>&lt;projectRoot&gt;/.syncro/contract.baseline.json</c>.
    /// </summary>
    public sealed class ContractSnapshot
    {
        public string BackendPath { get; set; } = "";
        public string Stack { get; set; } = "Unknown";

        /// <summary>UTC time the snapshot was captured (set by the engine, not the differ).</summary>
        public DateTimeOffset? CapturedAtUtc { get; set; }

        public List<RouteSnapshot> Routes { get; set; } = new();
    }

    /// <summary>The kind of change one route/field underwent between two snapshots.</summary>
    public enum ContractChangeKind
    {
        RouteAdded,
        RouteRemoved,
        MethodChanged,
        RequestFieldAdded,
        RequestFieldRemoved,
        RequestFieldTypeChanged,
        RequestFieldNowRequired,
        ResponseFieldAdded,
        ResponseFieldRemoved,
        ResponseFieldTypeChanged
    }

    /// <summary>A single classified change in the contract between baseline and current.</summary>
    public sealed class ContractChange
    {
        public ContractChangeKind Kind { get; set; }

        /// <summary>Route id, or "{route id} → {field}" for field-level changes.</summary>
        public string Target { get; set; } = "";

        public string Detail { get; set; } = "";

        /// <summary>True when this change can break an existing consumer even though both sides may
        /// still compile (e.g. a removed response field, a changed type, a newly-required request field).</summary>
        public bool Breaking { get; set; }
    }

    /// <summary>The result of diffing a baseline snapshot against the current backend surface.</summary>
    public sealed class ContractDriftReport
    {
        public List<ContractChange> Changes { get; set; } = new();

        public int Total => Changes.Count;
        public int BreakingCount => Changes.Count(c => c.Breaking);
        public bool HasBreakingChanges => BreakingCount > 0;
    }
}
