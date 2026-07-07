using System;
using System.Collections.Generic;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Universe.Runtime;

namespace Syncro.Desktop.Services.Connector.ProjectDomain
{
    /// <summary>One workspace folder inside the Universe Connector fleet.</summary>
    public sealed class ConnectorWorkspaceNode
    {
        public Guid Id { get; } = Guid.NewGuid();

        public string Path { get; set; } = "";
        public string Label { get; set; } = "";

        public DomainKind Domain { get; set; } = DomainKind.Unknown;
        public string Stack { get; set; } = "Unknown";

        /// <summary>Wave order: 0=DB/infra, 1=backend/AI service, 2=frontend/mobile.</summary>
        public int Wave { get; set; } = 1;

        public int Port { get; set; }
        public string RunCommand { get; set; } = "";

        // Git
        public string GitBranch { get; set; } = "";
        public string GitRemote { get; set; } = "";
        public bool IsGitDirty { get; set; }

        // Async state flags
        public bool IsDetecting { get; set; }
        public bool IsResolvingRoutes { get; set; }

        // Backend API surface (resolved on demand)
        public List<BackendRoute> Routes { get; set; } = new();

        // Live run status (polled from StackRunManager.Snapshot())
        public RunStatus? RunState { get; set; }
    }
}
