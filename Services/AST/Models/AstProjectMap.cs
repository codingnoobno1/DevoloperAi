using System.Collections.Generic;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Models;

public class AstProjectMap
{
    public string ProjectPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Language { get; set; } = ""; // e.g. CSharp, TypeScript, Python
    public string Framework { get; set; } = ""; // e.g. ASP.NET, Next.js, Express, FastAPI
    
    // Core structural nodes (classes, methods, namespaces)
    public List<AstNode> Nodes { get; set; } = new();
    
    // Scanned artifacts
    public List<AstEndpoint> Endpoints { get; set; } = new();
    public List<AstDtoModel> Dtos { get; set; } = new();
    public List<AstDependencyInfo> Dependencies { get; set; } = new();
    public List<PortInfo> Ports { get; set; } = new();
    public List<SwaggerSpec> SwaggerSpecs { get; set; } = new();
    
    // Config files found: relativePath -> absolutePath
    public Dictionary<string, string> ConfigFiles { get; set; } = new();
    
    // Errors logged during the ingestion pipeline
    public List<string> Errors { get; set; } = new();
    
    // Graph outputs (serialized)
    public string DependencyGraphDot { get; set; } = "";
    public string CallGraphDot { get; set; } = "";
    public string DagFlowchartMermaid { get; set; } = "";
}
