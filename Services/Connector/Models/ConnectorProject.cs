using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>
    /// Runtime twin of connector.md's design doc — the persisted contract for one connected
    /// project, written as <c>connector.json</c> in the project root. Frontend extraction hasn't
    /// landed yet, so <see cref="Frontend"/> and <see cref="Mappings"/> stay empty/null until that
    /// phase exists; only the backend section is populated today.
    /// </summary>
    public sealed class ConnectorProject
    {
        public int Version { get; set; } = 1;

        public FrontendConfig? Frontend { get; set; }

        public BackendConfig? Backend { get; set; }

        public List<ConnectionMappingEntry> Mappings { get; set; } = new();
    }

    public sealed class FrontendConfig
    {
        public string Path { get; set; } = "";
        public string Stack { get; set; } = "";
        public string? RunCommand { get; set; }
        public string? ApiClient { get; set; }
        public string? BaseUrlSymbol { get; set; }
        public string? PreviewUrl { get; set; }
    }

    public sealed class BackendConfig
    {
        public string Path { get; set; } = "";
        public string Stack { get; set; } = "";
        public string? RunCommand { get; set; }
        public int? Port { get; set; }
        public string? SwaggerPath { get; set; }
    }

    public sealed class ConnectionMappingEntry
    {
        /// <summary>"GET /api/users" — the frontend side (populated once the extractor lands).</summary>
        public string Call { get; set; } = "";

        /// <summary>"GET /api/users" — the matched backend route, null when unmatched.</summary>
        public string? Route { get; set; }

        /// <summary>Matched | ShapeMismatch | MethodMismatch | Missing.</summary>
        public string State { get; set; } = "Unknown";
    }
}
