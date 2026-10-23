using System.Collections.Generic;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Connector.ProjectDomain;

namespace Syncro.Desktop.Services.Connector.Scan
{
    /// <summary>Per-project result of a multi-project scan — real, resolver-derived counts.</summary>
    public sealed class ProjectScan
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public DomainKind Domain { get; set; }
        public string Stack { get; set; } = "";

        public string Role =>
            Routes > 0 && Calls > 0 ? "fullstack" :
            Routes > 0 ? "backend" :
            Calls > 0 ? "frontend" :
            Domain switch
            {
                DomainKind.Backend or DomainKind.AIService => "backend",
                DomainKind.Frontend or DomainKind.Mobile => "frontend",
                DomainKind.Database => "database",
                _ => "unknown"
            };

        public int Routes { get; set; }
        public int Calls { get; set; }
        public string RouteSource { get; set; } = "";   // Swagger | RouteScan | none

        public BackendContract? Backend { get; set; }
        public FrontendContract? Frontend { get; set; }
        public DataLayer Data { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>One cross-project connection: a frontend call resolved (or not) to a backend route.</summary>
    public sealed class CrossEdge
    {
        public string FromProject { get; set; } = "";
        public string Method { get; set; } = "";
        public string CallPath { get; set; } = "";
        public string? ToProject { get; set; }
        public string? RoutePath { get; set; }
        public string State { get; set; } = "";   // Matched | MethodMismatch | ShapeMismatch | Missing
    }

    /// <summary>The full multi-project scan report for one root folder.</summary>
    public sealed class MultiProjectScanReport
    {
        public string Root { get; set; } = "";
        public List<ProjectScan> Projects { get; set; } = new();
        public List<CrossEdge> Connections { get; set; } = new();

        public int ProjectCount => Projects.Count;
        public int TotalRoutes { get; set; }
        public int TotalCalls { get; set; }
        public int Matched { get; set; }
        public int Missing { get; set; }
        public int OrphanRoutes { get; set; }
        public long ElapsedMs { get; set; }
    }
}
