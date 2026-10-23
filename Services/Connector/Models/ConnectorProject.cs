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
        public int Version { get; set; } = 2;

        public FrontendConfig? Frontend { get; set; }

        public BackendConfig? Backend { get; set; }

        public List<ConnectionMappingEntry> Mappings { get; set; } = new();

        /// <summary>Phase P9 (connector.json v2): N-project workspace graph nodes (Frontend, Backend, FullStack, SharedLib).</summary>
        public List<ConnectorProjectNode> Projects { get; set; } = new();

        /// <summary>Phase P9 (connector.json v2): Explicit (Consumer -> Producer) links with optional BaseUrlSymbol disambiguation.</summary>
        public List<ConnectorLinkEntry> Links { get; set; } = new();
    }

    public sealed class ConnectorProjectNode
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public string Role { get; set; } = "Backend";      // Frontend | Backend | FullStack | SharedLib
        public string Stack { get; set; } = "";
        public int? Port { get; set; }
    }

    public sealed class ConnectorLinkEntry
    {
        public string Consumer { get; set; } = "";
        public string Producer { get; set; } = "";
        public string? BaseUrlSymbol { get; set; }
        public int MatchedRoutes { get; set; }
        public int MissingRoutes { get; set; }
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
