using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AST.Models;

public class AstReport
{
    public string ReportName { get; set; } = "Syncro Codebase Assessment";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<AstProjectMap> ProjectMaps { get; set; } = new();
    
    // Aggregated cross-project stats
    public List<PortInfo> OpenPorts { get; set; } = new();
    public List<AstEndpoint> SharedEndpoints { get; set; } = new();
    public List<AstDependencyInfo> SharedDependencies { get; set; } = new();
}
