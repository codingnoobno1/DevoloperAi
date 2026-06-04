# Syncro AST Module — Full Implementation Plan

## Overview

The **AST (Abstract Syntax Tree) Module** is a new first-class service in `Services/AST/`.
It scans any project on disk — C#, Next.js, Node.js, Python — and builds a rich graph of
the codebase: endpoints, DTOs, dependencies, ports, Swagger specs. It then generates a PDF
report with visual graphs and stores everything in a per-project cache.

**Slot in the architecture:**

```
UI (Blazor)
  └── Components/Pages/AST/          ← 4 new .razor pages
        │
Services/AST/AstService.cs           ← DI singleton, registered in MauiProgram.cs
  ├── AstEngine                      ← orchestrates all sub-systems
  ├── Parsers/*                      ← language-specific AST walkers
  ├── Graph/*                        ← dependency + call + DAG graphs
  ├── Scanners/*                     ← port, API, Swagger, route, config
  ├── Analyzers/*                    ← project, DTO, dependency, complexity
  ├── RAG/*                          ← AST knowledge indexer for AI
  ├── Reporters/*                    ← PDF, JSON, Markdown output
  └── Storage/*                      ← persist & cache results

Services/SyncroCLI/Commands/AstCommand.cs   ← `syncro ast` CLI command
```

---

## NuGet Packages to Add

Add to `Syncro.Desktop.csproj`:

```xml
<!-- C# AST (Roslyn) -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.11.0" />
<PackageReference Include="Buildalyzer" Version="7.1.0" />
<PackageReference Include="Buildalyzer.WorkspaceLoader" Version="7.1.0" />

<!-- Graph data structures & algorithms -->
<PackageReference Include="QuikGraph" Version="2.5.0" />
<PackageReference Include="QuikGraph.Serialization" Version="2.5.0" />

<!-- OpenAPI / Swagger parsing -->
<PackageReference Include="Microsoft.OpenApi.Readers" Version="1.6.22" />
<PackageReference Include="Swashbuckle.AspNetCore.SwaggerGen" Version="6.9.0" />

<!-- YAML parsing (docker-compose, pyproject.toml, etc.) -->
<PackageReference Include="YamlDotNet" Version="16.3.0" />

<!-- PDF generation -->
<PackageReference Include="QuestPDF" Version="2025.1.3" />

<!-- Chart/graph rendering to image (for PDF embed) -->
<PackageReference Include="OxyPlot.Core" Version="2.2.0" />
<PackageReference Include="OxyPlot.SkiaSharp" Version="2.2.0" />

<!-- JSON (already have Newtonsoft, also use System.Text.Json) -->
<!-- Already referenced: Newtonsoft.Json 13.0.3 -->
```

---

## File Map (43 C# Files + 4 Razor Pages)

```
Services/AST/
├── AstService.cs                          [1]  Main DI singleton
│
├── Core/
│   ├── IAstParser.cs                      [2]  Parser contract
│   ├── IAstNode.cs                        [3]  Node contract
│   ├── AstNode.cs                         [4]  Base node (language-agnostic)
│   ├── AstContext.cs                      [5]  Per-scan context carrier
│   ├── AstRegistry.cs                     [6]  Auto-registers parsers by extension
│   └── AstEngine.cs                       [7]  Orchestrates full project scan
│
├── Parsers/
│   ├── CSharpAstParser.cs                 [8]  Roslyn SyntaxTree walker
│   ├── TypeScriptAstParser.cs             [9]  Regex + structure parse for .ts/.tsx
│   ├── JavaScriptAstParser.cs             [10] Node.js .js/.mjs parse
│   ├── PythonAstParser.cs                 [11] Python subprocess + ast.dump parse
│   └── ConfigFileParser.cs                [12] JSON / YAML / TOML / .env configs
│
├── Graph/
│   ├── IAstGraph.cs                       [13] Graph contract
│   ├── AstGraph.cs                        [14] QuikGraph-backed impl
│   ├── DependencyGraph.cs                 [15] Module → module edges
│   ├── CallGraph.cs                       [16] Method → method call edges
│   ├── DagBuilder.cs                      [17] Topological sort, cycle detection
│   └── GraphExporter.cs                   [18] DOT / JSON / adjacency matrix
│
├── Scanners/
│   ├── PortScanner.cs                     [19] TCP connect scan (common dev ports)
│   ├── ApiEndpointScanner.cs              [20] Detect REST routes in code
│   ├── SwaggerScanner.cs                  [21] Parse swagger.json / openapi.yaml
│   ├── RouteScanner.cs                    [22] Next.js pages/, Express router, Flask
│   └── ConfigScanner.cs                   [23] Detect .env, appsettings, docker-compose
│
├── Analyzers/
│   ├── ProjectAnalyzer.cs                 [24] Entry: detect language, framework, root
│   ├── DtoAnalyzer.cs                     [25] Find / generate DTO classes
│   ├── DependencyAnalyzer.cs              [26] NuGet / npm / pip dependency graph
│   ├── ComplexityAnalyzer.cs              [27] Cyclomatic complexity per method
│   └── FrameworkDetector.cs               [28] Detect Next.js, Express, FastAPI, ASP.NET
│
├── Models/
│   ├── AstProjectMap.cs                   [29] Root model: all results for one project
│   ├── AstEndpoint.cs                     [30] HTTP endpoint (method, path, params, DTOs)
│   ├── AstDtoModel.cs                     [31] DTO class (properties, annotations)
│   ├── AstDependencyInfo.cs               [32] Package (name, version, type, transitive)
│   ├── PortInfo.cs                        [33] Port scan result (port, state, service)
│   ├── SwaggerSpec.cs                     [34] Parsed OpenAPI spec
│   ├── AstReport.cs                       [35] Aggregated report (multi-project)
│   └── AstNodeType.cs                     [36] Enum: Class, Method, Interface, Route...
│
├── RAG/
│   ├── AstRagIndexer.cs                   [37] Chunk AST → embeddings → local index
│   └── AstRagQuery.cs                     [38] Query index → context for AIClient
│
├── Reporters/
│   ├── IAstReporter.cs                    [39] Reporter contract
│   ├── PdfReporter.cs                     [40] QuestPDF report with OxyPlot graphs
│   └── JsonReporter.cs                    [41] Machine-readable JSON export
│
└── Storage/
    ├── AstStorageService.cs               [42] Save/load AstProjectMap to disk
    └── AstCacheManager.cs                 [43] Invalidate by file hash, TTL

Services/SyncroCLI/Commands/
└── AstCommand.cs                          [44] `syncro ast <path> [--pdf] [--json]`

Components/Pages/AST/
├── AstDashboard.razor                     [45] Project picker + scan trigger
├── ProjectMapView.razor                   [46] Interactive graph (MudBlazor tree)
├── EndpointExplorer.razor                 [47] Endpoint table + Swagger viewer
└── DtoPlanner.razor                       [48] DTO class list + generator
```

---

## File-by-File Specification

---

### [1] `Services/AST/AstService.cs`

**Purpose:** Top-level singleton registered in DI. Entry point for all AST operations.

```csharp
namespace Syncro.Desktop.Services.AST;

public class AstService
{
    private readonly AstEngine _engine;
    private readonly AstStorageService _storage;
    private readonly AstCacheManager _cache;

    public AstService(AstEngine engine, AstStorageService storage, AstCacheManager cache) { }

    // Scan a single project path, return full map
    public Task<AstProjectMap> ScanProjectAsync(string projectPath, CancellationToken ct = default);

    // Scan multiple projects in parallel, return merged report
    public Task<AstReport> ScanAllProjectsAsync(IEnumerable<string> paths, CancellationToken ct = default);

    // Generate PDF report for a map, save to outputPath
    public Task<string> GeneratePdfAsync(AstProjectMap map, string outputPath);

    // Export as JSON
    public Task<string> ExportJsonAsync(AstProjectMap map, string outputPath);

    // Get cached map if available
    public Task<AstProjectMap?> GetCachedMapAsync(string projectPath);
}
```

---

### [2] `Services/AST/Core/IAstParser.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Core;

public interface IAstParser
{
    // File extensions this parser handles e.g. [".cs"] or [".ts", ".tsx"]
    IReadOnlyList<string> SupportedExtensions { get; }

    // Parse a single file, return all nodes (classes, methods, routes, etc.)
    Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);

    // Parse entire project folder
    Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
}
```

---

### [3] `Services/AST/Core/IAstNode.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Core;

public interface IAstNode
{
    string Id { get; }           // Unique: "filePath::ClassName::MethodName"
    AstNodeType Type { get; }
    string Name { get; }
    string FilePath { get; }
    int LineNumber { get; }
    IReadOnlyList<string> Children { get; }  // Child node IDs
    IDictionary<string, object> Metadata { get; }  // Annotations, return types, params
}
```

---

### [4] `Services/AST/Core/AstNode.cs`

**Purpose:** Concrete, serializable node. Language-agnostic.

```csharp
namespace Syncro.Desktop.Services.AST.Core;

public class AstNode : IAstNode
{
    public string Id { get; init; } = "";
    public AstNodeType Type { get; init; }
    public string Name { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string? Namespace { get; init; }
    public string? ReturnType { get; init; }
    public List<string> Parameters { get; init; } = new();
    public List<string> Children { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
    public List<string> HttpMethods { get; init; } = new();   // GET, POST…
    public string? Route { get; init; }                        // "/api/users"
    public string? Summary { get; init; }                      // XML doc or docstring
}
```

---

### [5] `Services/AST/Core/AstContext.cs`

**Purpose:** Carries scan configuration and accumulated state across parsers.

```csharp
namespace Syncro.Desktop.Services.AST.Core;

public class AstContext
{
    public string RootPath { get; init; } = "";
    public string ProjectLanguage { get; set; } = "";      // "csharp" | "typescript" | "python" | "javascript"
    public string Framework { get; set; } = "";             // "aspnet" | "nextjs" | "express" | "fastapi"
    public List<string> IgnorePatterns { get; init; } = new() { "bin", "obj", "node_modules", ".git", "__pycache__" };
    public bool ScanPorts { get; init; } = true;
    public bool ParseSwagger { get; init; } = true;
    public CancellationToken CancellationToken { get; init; }
    public List<string> Errors { get; } = new();
    public Dictionary<string, List<AstNode>> FileNodes { get; } = new();  // filePath → nodes
}
```

---

### [6] `Services/AST/Core/AstRegistry.cs`

**Purpose:** Auto-discovers and registers all `IAstParser` implementations by extension.

```csharp
namespace Syncro.Desktop.Services.AST.Core;

public class AstRegistry
{
    private readonly Dictionary<string, IAstParser> _parsers = new();

    public AstRegistry(IEnumerable<IAstParser> parsers)
    {
        foreach (var p in parsers)
            foreach (var ext in p.SupportedExtensions)
                _parsers[ext.ToLower()] = p;
    }

    // Returns null if no parser registered for this extension
    public IAstParser? GetParser(string fileExtension);

    // List all registered extensions
    public IReadOnlyList<string> RegisteredExtensions { get; }
}
```

---

### [7] `Services/AST/Core/AstEngine.cs`

**Purpose:** Main orchestrator. Walks a project directory, dispatches to parsers,
builds graph, runs scanners and analyzers, returns `AstProjectMap`.

```csharp
namespace Syncro.Desktop.Services.AST.Core;

public class AstEngine
{
    // Constructor injects: registry, graph builder, scanners, analyzers, framework detector
    
    public async Task<AstProjectMap> RunAsync(string projectPath, AstContext ctx)
    {
        // 1. Detect framework → sets ctx.Framework, ctx.ProjectLanguage
        // 2. Walk files matching supported extensions (respect IgnorePatterns)
        // 3. Parse each file → collect AstNodes
        // 4. Build DependencyGraph + CallGraph
        // 5. Build DAG via DagBuilder
        // 6. Run PortScanner if ctx.ScanPorts
        // 7. Run SwaggerScanner + ApiEndpointScanner + RouteScanner
        // 8. Run DtoAnalyzer → extract/infer DTOs
        // 9. Run DependencyAnalyzer → build package tree
        // 10. Run ComplexityAnalyzer
        // 11. Assemble AstProjectMap and return
    }
}
```

---

### [8] `Services/AST/Parsers/CSharpAstParser.cs`

**Purpose:** Uses **Roslyn** (`Microsoft.CodeAnalysis.CSharp`) to walk `.cs` files.
Extracts: namespaces, classes, interfaces, methods, properties, attributes (for routes),
XML doc comments.

```csharp
namespace Syncro.Desktop.Services.AST.Parsers;

public class CSharpAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => [".cs"];

    // Uses CSharpSyntaxTree.ParseText()
    // Walks: NamespaceDeclarationSyntax, ClassDeclarationSyntax,
    //        MethodDeclarationSyntax, PropertyDeclarationSyntax
    // Reads [HttpGet], [HttpPost], [Route] attributes → sets AstNode.Route, .HttpMethods
    // Reads /// <summary> XML docs → sets AstNode.Summary
    public Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);

    // Uses Buildalyzer to load full .csproj workspace, then parses all .cs files
    public Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
}
```

**Key Roslyn types used:**
- `CSharpSyntaxTree.ParseText(sourceCode)`
- `SyntaxWalker` subclass visiting class/method/property/attribute nodes
- `SemanticModel` for resolving types (loaded via `AdhocWorkspace`)

---

### [9] `Services/AST/Parsers/TypeScriptAstParser.cs`

**Purpose:** Parses `.ts` / `.tsx` files for Next.js projects.
Since adding a full TS compiler is heavy, uses a hybrid approach:
regex pattern matching + structural analysis of known Next.js patterns.

```csharp
namespace Syncro.Desktop.Services.AST.Parsers;

public class TypeScriptAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => [".ts", ".tsx"];

    // Detects:
    //   - export default function Page() → route node (from file path)
    //   - export async function GET/POST/PUT/DELETE() → API route handler
    //   - interface/type declarations → DTO nodes
    //   - import statements → dependency edges
    //   - Next.js app/ directory convention → automatic route derivation
    public Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);
    public Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
    
    // Derives route from file path:
    //   app/api/users/route.ts → /api/users
    //   app/dashboard/page.tsx → /dashboard
    private string DeriveNextJsRoute(string filePath, string rootPath);
}
```

---

### [10] `Services/AST/Parsers/JavaScriptAstParser.cs`

**Purpose:** Parses Node.js `.js` / `.mjs` files.
Detects Express routes (`app.get`, `router.post`), `require()` / `import` statements,
exported functions.

```csharp
namespace Syncro.Desktop.Services.AST.Parsers;

public class JavaScriptAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => [".js", ".mjs", ".cjs"];

    // Regex patterns:
    //   app\.(get|post|put|delete|patch)\s*\(\s*['"`]([^'"`]+)['"`]  → Express routes
    //   router\.(get|post|put|delete)\s*\(\s*['"`]([^'"`]+)['"`]     → Express router
    //   require\s*\(\s*['"`]([^'"`]+)['"`]\s*\)                      → dependency
    //   export\s+(default\s+)?(async\s+)?function\s+(\w+)            → exported fn
    public Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);
    public Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
}
```

---

### [11] `Services/AST/Parsers/PythonAstParser.cs`

**Purpose:** Parses Python `.py` files. Uses Python's own `ast` module via subprocess
to dump AST as JSON, then deserializes it. Falls back to regex if Python is unavailable.

```csharp
namespace Syncro.Desktop.Services.AST.Parsers;

public class PythonAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => [".py"];

    // Primary: runs `python -c "import ast,json,sys; ..."` to dump AST JSON
    // Parses: FunctionDef, AsyncFunctionDef, ClassDef, Import, ImportFrom
    // For FastAPI: @app.get/@app.post decorators → route nodes
    // For Flask: @app.route decorator → route nodes
    // Fallback: regex-based parse (def/class/import detection)
    public Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);
    public Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
    
    private bool IsPythonAvailable();
    private Task<string> RunPythonAstDump(string filePath);
}
```

---

### [12] `Services/AST/Parsers/ConfigFileParser.cs`

**Purpose:** Parses configuration/manifest files:
`package.json`, `pyproject.toml`, `requirements.txt`, `appsettings.json`,
`docker-compose.yaml`, `.env`, `Cargo.toml`, `go.mod`, `pubspec.yaml`.

```csharp
namespace Syncro.Desktop.Services.AST.Parsers;

public class ConfigFileParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => 
        [".json", ".yaml", ".yml", ".toml", ".txt", ".env"];

    // Extracts:
    //   package.json → name, version, dependencies, scripts, main entry
    //   pyproject.toml / requirements.txt → Python dependencies + versions
    //   appsettings.json → connection strings, port config (Kestrel)
    //   docker-compose.yaml → service ports, volumes, environment vars
    //   .env → key-value pairs (no secret values, just keys)
    //   go.mod → module name, Go version, require block
    //   pubspec.yaml → Flutter package name + dependencies
    public Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);
    public Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
}
```

---

### [13] `Services/AST/Graph/IAstGraph.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Graph;

public interface IAstGraph
{
    void AddNode(AstNode node);
    void AddEdge(string fromId, string toId, string edgeType);
    IReadOnlyList<AstNode> GetNeighbors(string nodeId);
    IReadOnlyList<(string From, string To, string EdgeType)> GetEdges();
    bool HasCycle();
    IReadOnlyList<string> TopologicalOrder();  // DAG only
    string ExportDot();    // Graphviz DOT format
    string ExportJson();   // Adjacency list JSON
}
```

---

### [14] `Services/AST/Graph/AstGraph.cs`

**Purpose:** QuikGraph-backed directed graph implementation.

```csharp
namespace Syncro.Desktop.Services.AST.Graph;

using QuikGraph;
using QuikGraph.Algorithms;

public class AstGraph : IAstGraph
{
    // Uses BidirectionalGraph<string, TaggedEdge<string, string>> from QuikGraph
    // Node IDs are string (AstNode.Id)
    // TaggedEdge.Tag = edge type: "calls", "imports", "inherits", "implements", "uses"
    
    private readonly BidirectionalGraph<string, TaggedEdge<string, string>> _graph = new();
    private readonly Dictionary<string, AstNode> _nodes = new();

    public void AddNode(AstNode node) { }
    public void AddEdge(string fromId, string toId, string edgeType) { }
    public bool HasCycle() => !_graph.IsDirectedAcyclicGraph();
    public IReadOnlyList<string> TopologicalOrder() { /* Kahn's algorithm */ }
    public string ExportDot() { /* Graphviz DOT serialization */ }
    public string ExportJson() { /* adjacency list JSON */ }
}
```

---

### [15] `Services/AST/Graph/DependencyGraph.cs`

**Purpose:** Specialized graph for module/package dependencies.
Nodes = modules/packages. Edges = "depends on".

```csharp
namespace Syncro.Desktop.Services.AST.Graph;

public class DependencyGraph
{
    private readonly AstGraph _graph;

    // Build from list of AstNodes (import/require/using statements)
    public void BuildFromNodes(IEnumerable<AstNode> nodes);

    // Returns all direct dependencies of a module
    public IReadOnlyList<string> GetDirectDeps(string moduleId);

    // Returns full transitive closure
    public IReadOnlyList<string> GetAllTransitiveDeps(string moduleId);

    // Detect circular dependencies
    public IReadOnlyList<IReadOnlyList<string>> FindCircularDeps();

    // Generate mermaid-compatible output for PDF
    public string ToMermaidGraph();
}
```

---

### [16] `Services/AST/Graph/CallGraph.cs`

**Purpose:** Tracks method → method call relationships.

```csharp
namespace Syncro.Desktop.Services.AST.Graph;

public class CallGraph
{
    // Build from C# Roslyn InvocationExpressionSyntax nodes
    // Build from Python function call detection
    // Build from JS/TS function call detection
    
    public void AddCall(string callerNodeId, string calleeNodeId);
    public IReadOnlyList<string> GetCallers(string methodId);
    public IReadOnlyList<string> GetCallees(string methodId);
    
    // Find hot-spots: methods called by many callers
    public IReadOnlyList<(string MethodId, int CallerCount)> GetHotspots(int topN = 10);
    
    // Generate call tree from entry point
    public string ToMermaidCallTree(string entryMethodId, int maxDepth = 5);
}
```

---

### [17] `Services/AST/Graph/DagBuilder.cs`

**Purpose:** Converts any AstGraph into a true DAG (removes or flags cycles).
Used for pipeline/execution order analysis.

```csharp
namespace Syncro.Desktop.Services.AST.Graph;

public class DagBuilder
{
    // Takes an AstGraph, returns a new DAG (cycle-free version)
    public AstGraph BuildDag(AstGraph source, out List<(string, string)> removedEdges);

    // Topological sort → execution order for project modules
    public IReadOnlyList<string> GetExecutionOrder(AstGraph dag);

    // Layer assignment: nodes grouped by their topological level
    public Dictionary<int, List<string>> GetLayers(AstGraph dag);

    // Visualize layers as ASCII or mermaid
    public string ToMermaidFlowchart(AstGraph dag);
}
```

---

### [18] `Services/AST/Graph/GraphExporter.cs`

**Purpose:** Exports graphs to multiple formats.

```csharp
namespace Syncro.Desktop.Services.AST.Graph;

public class GraphExporter
{
    // Graphviz DOT → can be rendered with `dot -Tpng` externally
    public string ToDot(AstGraph graph, string graphName = "AstGraph");

    // Adjacency list JSON (for web/JS graph renderers)
    public string ToJson(AstGraph graph);

    // Mermaid diagram syntax (for embedding in Markdown/PDF)
    public string ToMermaid(AstGraph graph);

    // D3.js force-graph format { nodes: [], links: [] }
    public string ToD3Json(AstGraph graph);

    // OxyPlot-compatible data points for bar/pie charts
    public OxyPlot.PlotModel ToOxyPlotModel(AstGraph graph, string title);
}
```

---

### [19] `Services/AST/Scanners/PortScanner.cs`

**Purpose:** TCP connect scan for common developer ports (3000, 3001, 4000, 5000,
5001, 8000, 8080, 8443, 9000, etc.). Also reads config files to find declared ports.

```csharp
namespace Syncro.Desktop.Services.AST.Scanners;

public class PortScanner
{
    // Common dev ports to check
    private static readonly int[] DevPorts = 
    [
        3000, 3001, 3002, 4000, 5000, 5001, 5173,  // Vite/React/Next
        8000, 8080, 8443, 8888,                       // Django/Node/misc
        9000, 9229,                                    // Node debug
        1433, 3306, 5432, 6379, 27017,               // DBs
        7071, 7272                                     // Azure Functions, Dapr
    ];

    // TCP connect scan (non-root, no raw sockets)
    public Task<List<PortInfo>> ScanAsync(string host = "localhost", int timeoutMs = 300);

    // Read declared port from config files
    public Task<List<PortInfo>> ExtractDeclaredPortsAsync(string projectPath);

    // Match scanned open ports to known services
    private string GuessService(int port);
}
```

---

### [20] `Services/AST/Scanners/ApiEndpointScanner.cs`

**Purpose:** Scans parsed AST nodes to extract REST API endpoints.
Works across all languages by looking at nodes where `Type == Route`.

```csharp
namespace Syncro.Desktop.Services.AST.Scanners;

public class ApiEndpointScanner
{
    // Extract AstEndpoint list from AstNodes
    public List<AstEndpoint> ExtractEndpoints(IEnumerable<AstNode> nodes);

    // Resolve route parameters: {id} → param type from method signature
    public void ResolveRouteParams(List<AstEndpoint> endpoints, IEnumerable<AstNode> allNodes);

    // Group endpoints by controller/router file
    public Dictionary<string, List<AstEndpoint>> GroupByController(List<AstEndpoint> endpoints);

    // Generate OpenAPI-compatible path items from endpoints
    public string ToOpenApiYaml(List<AstEndpoint> endpoints, string title, string version = "1.0.0");
}
```

---

### [21] `Services/AST/Scanners/SwaggerScanner.cs`

**Purpose:** Reads existing `swagger.json` or `openapi.yaml` files.
Uses `Microsoft.OpenApi.Readers`.

```csharp
namespace Syncro.Desktop.Services.AST.Scanners;

using Microsoft.OpenApi.Readers;

public class SwaggerScanner
{
    // Find swagger/openapi files in project tree
    public IEnumerable<string> FindSwaggerFiles(string projectPath);

    // Parse using Microsoft.OpenApi.Readers.OpenApiStreamReader
    public Task<SwaggerSpec> ParseAsync(string filePath);

    // Merge multiple swagger specs (multi-service projects)
    public SwaggerSpec MergeSpecs(IEnumerable<SwaggerSpec> specs);

    // Convert parsed spec into AstEndpoint list
    public List<AstEndpoint> ToAstEndpoints(SwaggerSpec spec);
}
```

---

### [22] `Services/AST/Scanners/RouteScanner.cs`

**Purpose:** Framework-specific route discovery beyond what the language parser handles.

```csharp
namespace Syncro.Desktop.Services.AST.Scanners;

public class RouteScanner
{
    // Next.js App Router (app/ directory)
    public List<AstEndpoint> ScanNextJsAppDir(string rootPath);

    // Next.js Pages Router (pages/api/)
    public List<AstEndpoint> ScanNextJsPagesDir(string rootPath);

    // Express: scan index.js/app.js for router imports → follow chains
    public List<AstEndpoint> ScanExpressRoutes(string rootPath, List<AstNode> jsNodes);

    // Flask/FastAPI: scan for decorator patterns + blueprint registrations
    public List<AstEndpoint> ScanPythonRoutes(string rootPath, List<AstNode> pyNodes);

    // ASP.NET Core: combine Attribute routes + conventional routing (Startup.cs)
    public List<AstEndpoint> ScanAspNetRoutes(string rootPath, List<AstNode> csNodes);

    // Deduplicate and normalize (lowercase, strip trailing slash)
    public List<AstEndpoint> Normalize(List<AstEndpoint> endpoints);
}
```

---

### [23] `Services/AST/Scanners/ConfigScanner.cs`

**Purpose:** Extracts configuration metadata: connection strings, port declarations,
environment variable keys, build scripts, entry points.

```csharp
namespace Syncro.Desktop.Services.AST.Scanners;

public class ConfigScanner
{
    // Find and classify config files in project
    public Dictionary<string, string> FindConfigFiles(string projectPath);
    // Key: "package.json" | "appsettings.json" | "docker-compose.yaml" | ...
    // Value: absolute file path

    // Extract declared ports from appsettings.json (Kestrel endpoints)
    public List<int> ExtractAspNetPorts(string appsettingsPath);

    // Extract port from docker-compose ports: ["3000:3000"]
    public List<(int Host, int Container)> ExtractDockerPorts(string composePath);

    // Extract npm scripts (start, build, dev) → reveals port via --port flags
    public Dictionary<string, string> ExtractNpmScripts(string packageJsonPath);

    // List .env keys (no values) for documentation
    public List<string> ExtractEnvKeys(string envFilePath);
}
```

---

### [24] `Services/AST/Analyzers/ProjectAnalyzer.cs`

**Purpose:** Entry-point analyzer. Detects what kind of project this is and
sets up the `AstContext` accordingly before other analyzers run.

```csharp
namespace Syncro.Desktop.Services.AST.Analyzers;

public class ProjectAnalyzer
{
    // Analyze project root → populate AstContext fields
    public Task<AstContext> AnalyzeAsync(string rootPath);

    // Detect primary language by file count + config files
    private string DetectLanguage(string rootPath);

    // Project name from package.json / .csproj / pyproject.toml
    public string? ExtractProjectName(string rootPath);

    // Project version
    public string? ExtractVersion(string rootPath);

    // Author / organization
    public string? ExtractAuthor(string rootPath);

    // Collect all source files respecting ignore patterns
    public IReadOnlyList<string> CollectSourceFiles(string rootPath, AstContext ctx);
}
```

---

### [25] `Services/AST/Analyzers/DtoAnalyzer.cs`

**Purpose:** Finds existing DTOs and infers missing ones from endpoint signatures.

```csharp
namespace Syncro.Desktop.Services.AST.Analyzers;

public class DtoAnalyzer
{
    // Extract existing DTO classes from parsed C# nodes
    // (classes with only properties, no methods, in Models/ or Dto/ folder)
    public List<AstDtoModel> ExtractCSharpDtos(IEnumerable<AstNode> nodes);

    // Extract TypeScript interfaces → DTOs
    public List<AstDtoModel> ExtractTypeScriptDtos(IEnumerable<AstNode> nodes);

    // Extract Python dataclasses / Pydantic BaseModel subclasses
    public List<AstDtoModel> ExtractPythonDtos(IEnumerable<AstNode> nodes);

    // Infer missing DTOs from endpoint return types + parameters
    public List<AstDtoModel> InferMissingDtos(List<AstEndpoint> endpoints, List<AstDtoModel> existing);

    // Generate C# DTO class source code from AstDtoModel
    public string GenerateCSharpDto(AstDtoModel dto);

    // Generate TypeScript interface source code
    public string GenerateTypeScriptInterface(AstDtoModel dto);
}
```

---

### [26] `Services/AST/Analyzers/DependencyAnalyzer.cs`

**Purpose:** Builds a complete dependency tree for NuGet, npm, pip, go.mod, etc.

```csharp
namespace Syncro.Desktop.Services.AST.Analyzers;

public class DependencyAnalyzer
{
    // NuGet: parse .csproj PackageReference elements
    public List<AstDependencyInfo> AnalyzeNuGet(string csprojPath);

    // npm/yarn: parse package.json dependencies + devDependencies
    public List<AstDependencyInfo> AnalyzeNpm(string packageJsonPath);

    // pip: parse requirements.txt or pyproject.toml [tool.poetry.dependencies]
    public List<AstDependencyInfo> AnalyzePip(string requirementsPath);

    // Go: parse go.mod require block
    public List<AstDependencyInfo> AnalyzeGoMod(string goModPath);

    // Detect dependency conflicts (same package different versions)
    public List<(string Package, string[] Versions)> FindConflicts(List<AstDependencyInfo> deps);

    // Build DependencyGraph from dep list
    public DependencyGraph BuildGraph(List<AstDependencyInfo> deps);
}
```

---

### [27] `Services/AST/Analyzers/ComplexityAnalyzer.cs`

**Purpose:** Calculates cyclomatic complexity for each method/function.

```csharp
namespace Syncro.Desktop.Services.AST.Analyzers;

public class ComplexityAnalyzer
{
    // Cyclomatic complexity from C# Roslyn SyntaxTree
    // Counts: if, else if, for, foreach, while, do, case, catch, ??= , &&, ||
    public int CalculateCSharpComplexity(Microsoft.CodeAnalysis.SyntaxNode methodNode);

    // Complexity from regex-counted branch keywords for JS/TS/Python
    public int EstimateComplexity(string methodSource, string language);

    // Analyze all methods in project, return top complex ones
    public List<(AstNode Method, int Complexity)> AnalyzeAll(IEnumerable<AstNode> nodes);

    // Flag methods above threshold (default: >10)
    public List<AstNode> GetHighComplexityMethods(IEnumerable<AstNode> nodes, int threshold = 10);
}
```

---

### [28] `Services/AST/Analyzers/FrameworkDetector.cs`

**Purpose:** Fingerprints the framework from config files + file structure.

```csharp
namespace Syncro.Desktop.Services.AST.Analyzers;

public class FrameworkDetector
{
    // Returns detected framework string
    public string Detect(string rootPath);

    // C# frameworks
    private bool IsAspNetCore(string rootPath);     // .csproj has Microsoft.AspNetCore.*
    private bool IsMaui(string rootPath);           // .csproj has Microsoft.Maui
    private bool IsBlazor(string rootPath);         // Has .razor files
    private bool IsConsoleApp(string rootPath);     // OutputType = Exe, no web deps

    // JS/TS frameworks
    private bool IsNextJs(string rootPath);         // next in package.json deps
    private bool IsExpress(string rootPath);        // express in package.json deps
    private bool IsVite(string rootPath);           // vite in package.json devDeps
    private bool IsReact(string rootPath);          // react in deps (not next)

    // Python frameworks
    private bool IsFastApi(string rootPath);        // fastapi in requirements.txt
    private bool IsFlask(string rootPath);          // flask in requirements.txt
    private bool IsDjango(string rootPath);         // django in requirements.txt

    // Other
    private bool IsGoApp(string rootPath);          // go.mod exists
    private bool IsFlutterApp(string rootPath);     // pubspec.yaml with flutter:
}
```

---

### [29] `Services/AST/Models/AstProjectMap.cs`

**Purpose:** Root model for one scanned project. Serialized to JSON for storage.

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class AstProjectMap
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ProjectPath { get; init; } = "";
    public string ProjectName { get; set; } = "";
    public string Language { get; set; } = "";
    public string Framework { get; set; } = "";
    public string? Version { get; set; }
    public DateTime ScannedAt { get; init; } = DateTime.UtcNow;

    public List<AstNode> AllNodes { get; set; } = new();
    public List<AstEndpoint> Endpoints { get; set; } = new();
    public List<AstDtoModel> Dtos { get; set; } = new();
    public List<AstDependencyInfo> Dependencies { get; set; } = new();
    public List<PortInfo> Ports { get; set; } = new();
    public SwaggerSpec? SwaggerSpec { get; set; }
    public List<string> ConfigFiles { get; set; } = new();
    public List<(string Method, int Complexity)> ComplexityHotspots { get; set; } = new();

    // Serialized graph (adjacency JSON)
    public string? DependencyGraphJson { get; set; }
    public string? CallGraphJson { get; set; }
    public string? DagJson { get; set; }

    public int TotalFiles { get; set; }
    public int TotalLinesOfCode { get; set; }
    public List<string> ScanErrors { get; set; } = new();
}
```

---

### [30] `Services/AST/Models/AstEndpoint.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class AstEndpoint
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Route { get; set; } = "";           // "/api/users/{id}"
    public List<string> HttpMethods { get; set; } = new();  // ["GET", "POST"]
    public string SourceFile { get; set; } = "";
    public int LineNumber { get; set; }
    public string? HandlerName { get; set; }          // Method/function name
    public string? ControllerName { get; set; }
    public List<AstEndpointParam> Parameters { get; set; } = new();
    public string? RequestBodyType { get; set; }      // DTO type name
    public string? ResponseType { get; set; }
    public string? Summary { get; set; }              // From XML doc or JSDoc
    public bool RequiresAuth { get; set; }            // Has [Authorize] or auth middleware
    public string Framework { get; set; } = "";       // "aspnet" | "nextjs" | "express" ...
}

public class AstEndpointParam
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Source { get; set; } = "";  // "path" | "query" | "body" | "header"
    public bool Required { get; set; }
}
```

---

### [31] `Services/AST/Models/AstDtoModel.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class AstDtoModel
{
    public string Name { get; set; } = "";
    public string? Namespace { get; set; }
    public string Language { get; set; } = "";  // "csharp" | "typescript" | "python"
    public string SourceFile { get; set; } = "";
    public bool IsInferred { get; set; }         // True if generated, not found in source
    public List<AstDtoProperty> Properties { get; set; } = new();
    public List<string> UsedInEndpoints { get; set; } = new();  // Endpoint IDs
}

public class AstDtoProperty
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Required { get; set; }
    public bool IsNullable { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> Annotations { get; set; } = new();  // [Required], [MaxLength] etc.
}
```

---

### [32] `Services/AST/Models/AstDependencyInfo.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class AstDependencyInfo
{
    public string Name { get; set; } = "";
    public string? Version { get; set; }
    public string PackageManager { get; set; } = "";  // "nuget" | "npm" | "pip" | "go" | "cargo"
    public bool IsDevDependency { get; set; }
    public bool IsTransitive { get; set; }
    public List<string> DependsOn { get; set; } = new();  // Transitive deps of this package
}
```

---

### [33] `Services/AST/Models/PortInfo.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class PortInfo
{
    public int Port { get; set; }
    public string State { get; set; } = "";   // "open" | "closed" | "declared"
    public string? Service { get; set; }       // "HTTP" | "PostgreSQL" | "Redis" | ...
    public string Source { get; set; } = "";   // "scan" | "appsettings" | "docker-compose" | "package.json"
    public string? ProcessName { get; set; }
    public string? Url { get; set; }           // "http://localhost:3000"
}
```

---

### [34] `Services/AST/Models/SwaggerSpec.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class SwaggerSpec
{
    public string Title { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Servers { get; set; } = new();
    public List<SwaggerPath> Paths { get; set; } = new();
    public List<SwaggerSchema> Schemas { get; set; } = new();  // Components/schemas
    public List<string> SecuritySchemes { get; set; } = new();
    public string SourceFile { get; set; } = "";
}

public class SwaggerPath
{
    public string Route { get; set; } = "";
    public List<SwaggerOperation> Operations { get; set; } = new();
}

public class SwaggerOperation
{
    public string Method { get; set; } = "";
    public string? Summary { get; set; }
    public string? OperationId { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? RequestBodyRef { get; set; }
    public Dictionary<string, string> Responses { get; set; } = new();
}

public class SwaggerSchema
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public Dictionary<string, string> Properties { get; set; } = new();
}
```

---

### [35] `Services/AST/Models/AstReport.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public class AstReport
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public List<AstProjectMap> Projects { get; set; } = new();

    // Aggregated stats
    public int TotalProjects => Projects.Count;
    public int TotalEndpoints => Projects.Sum(p => p.Endpoints.Count);
    public int TotalDtos => Projects.Sum(p => p.Dtos.Count);
    public int TotalFiles => Projects.Sum(p => p.TotalFiles);
    public int TotalLoc => Projects.Sum(p => p.TotalLinesOfCode);

    // Cross-project: shared dependency packages
    public List<string> SharedDependencies { get; set; } = new();

    // All open ports across all scanned projects
    public List<PortInfo> AllOpenPorts { get; set; } = new();

    // PDF output path (set after PDF generation)
    public string? PdfOutputPath { get; set; }
}
```

---

### [36] `Services/AST/Models/AstNodeType.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Models;

public enum AstNodeType
{
    Unknown,
    Namespace,
    Class,
    Interface,
    Enum,
    Method,
    Property,
    Field,
    Constructor,
    Route,          // HTTP endpoint handler
    Import,         // import / using / require
    Decorator,      // Python/TS decorators
    Config,         // Config file entry
    Dependency,     // NuGet / npm / pip package
    Module,         // File/module-level node
    TypeAlias,      // TypeScript type alias
    DataClass,      // Python dataclass
}
```

---

### [37] `Services/AST/RAG/AstRagIndexer.cs`

**Purpose:** Chunks AST data into searchable text and builds a local in-memory
vector index (using cosine similarity with TF-IDF or embedding via AIClient).
Feeds the existing `AIClient` with project-specific context.

```csharp
namespace Syncro.Desktop.Services.AST.RAG;

public class AstRagIndexer
{
    private readonly AIClient _ai;
    private readonly List<RagChunk> _index = new();

    // Chunk a full AstProjectMap into retrievable fragments
    public Task IndexAsync(AstProjectMap map);

    // Chunk strategies:
    //   - One chunk per class (name + properties + methods)
    //   - One chunk per endpoint (route + params + response)
    //   - One chunk per DTO
    //   - One chunk per file summary
    private List<RagChunk> ChunkProjectMap(AstProjectMap map);

    // TF-IDF similarity search (no external embedding API needed)
    public List<RagChunk> Search(string query, int topK = 5);

    // Save index to disk
    public Task SaveIndexAsync(string path);
    public Task LoadIndexAsync(string path);
}

public class RagChunk
{
    public string Id { get; set; } = "";
    public string Content { get; set; } = "";  // human-readable chunk text
    public string ProjectPath { get; set; } = "";
    public AstNodeType NodeType { get; set; }
    public Dictionary<string, double> TfIdfVector { get; set; } = new();
}
```

---

### [38] `Services/AST/RAG/AstRagQuery.cs`

**Purpose:** Query the RAG index and build context for `AIClient`.

```csharp
namespace Syncro.Desktop.Services.AST.RAG;

public class AstRagQuery
{
    private readonly AstRagIndexer _indexer;
    private readonly AIClient _ai;

    // Retrieve relevant chunks + inject as context into AIClient prompt
    public Task<string> AskAsync(string question, string workspacePath, string model = "gemini-2.5-flash");

    // Build a context-rich system prompt from retrieved chunks
    private string BuildContextPrompt(string question, List<RagChunk> chunks);

    // Summarize a project using RAG context
    public Task<string> SummarizeProjectAsync(AstProjectMap map);

    // Generate API documentation from endpoints + DTOs
    public Task<string> GenerateApiDocsAsync(List<AstEndpoint> endpoints, List<AstDtoModel> dtos);
}
```

---

### [39] `Services/AST/Reporters/IAstReporter.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Reporters;

public interface IAstReporter
{
    string Format { get; }  // "pdf" | "json" | "markdown"
    Task<string> GenerateAsync(AstReport report, string outputDirectory);
    Task<string> GenerateForProjectAsync(AstProjectMap map, string outputDirectory);
}
```

---

### [40] `Services/AST/Reporters/PdfReporter.cs`

**Purpose:** Generates a structured PDF report using **QuestPDF**.
Embeds OxyPlot charts as images: dependency pie chart, endpoint bar chart,
complexity chart, DAG layer diagram.

```csharp
namespace Syncro.Desktop.Services.AST.Reporters;

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using OxyPlot;

public class PdfReporter : IAstReporter
{
    public string Format => "pdf";

    // Generate multi-project report PDF
    public Task<string> GenerateAsync(AstReport report, string outputDirectory);

    // Generate single project PDF
    public Task<string> GenerateForProjectAsync(AstProjectMap map, string outputDirectory);

    // PDF sections:
    //   1. Cover page: project name, scan date, stats summary
    //   2. Executive summary: key numbers, framework, language
    //   3. Endpoint catalog: table of all routes (method, path, auth, DTO)
    //   4. DTO reference: property tables for each DTO
    //   5. Dependency tree: OxyPlot pie chart of package managers
    //   6. Graph section: DAG diagram (mermaid rendered via Graphviz subprocess)
    //   7. Port scan results: table
    //   8. Complexity hotspots: bar chart (top 10 complex methods)
    //   9. Swagger spec summary (if found)
    //  10. Error/warning log

    private byte[] RenderOxyPlotToBytes(PlotModel model, int width = 600, int height = 400);
    private void AddEndpointTable(IContainer container, List<AstEndpoint> endpoints);
    private void AddDtoTable(IContainer container, List<AstDtoModel> dtos);
    private void AddDependencyChart(IContainer container, List<AstDependencyInfo> deps);
    private void AddComplexityChart(IContainer container, List<(string, int)> hotspots);
}
```

---

### [41] `Services/AST/Reporters/JsonReporter.cs`

**Purpose:** Exports `AstReport` / `AstProjectMap` as pretty-printed JSON.

```csharp
namespace Syncro.Desktop.Services.AST.Reporters;

public class JsonReporter : IAstReporter
{
    public string Format => "json";

    public Task<string> GenerateAsync(AstReport report, string outputDirectory);
    public Task<string> GenerateForProjectAsync(AstProjectMap map, string outputDirectory);

    // Include graph JSON, endpoint list, DTO list, port list
    // Uses Newtonsoft.Json with indentation and enum-as-string settings
    private JsonSerializerSettings Settings => new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() }
    };
}
```

---

### [42] `Services/AST/Storage/AstStorageService.cs`

**Purpose:** Persists `AstProjectMap` to disk alongside the project metadata.
Stored in `{projectPath}/.projectmeta/ast_map.json`.

```csharp
namespace Syncro.Desktop.Services.AST.Storage;

public class AstStorageService
{
    private const string AstFileName = "ast_map.json";
    private const string MetaFolder = ".projectmeta";

    // Save map alongside project
    public Task SaveAsync(AstProjectMap map);

    // Load from project path
    public Task<AstProjectMap?> LoadAsync(string projectPath);

    // List all cached maps under a root folder
    public Task<List<AstProjectMap>> LoadAllAsync(string rootFolder);

    // Delete cached map
    public Task DeleteAsync(string projectPath);

    private string GetAstFilePath(string projectPath) =>
        Path.Combine(projectPath, MetaFolder, AstFileName);
}
```

---

### [43] `Services/AST/Storage/AstCacheManager.cs`

**Purpose:** Decides when a cached `AstProjectMap` is stale and needs re-scanning.

```csharp
namespace Syncro.Desktop.Services.AST.Storage;

public class AstCacheManager
{
    private readonly AstStorageService _storage;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);

    // Returns cached map if valid, null if stale or missing
    public Task<AstProjectMap?> GetValidCacheAsync(string projectPath, TimeSpan? ttl = null);

    // Hash of all source files: if hash changed, cache is stale
    public Task<string> ComputeProjectHashAsync(string projectPath);

    // Invalidate on demand
    public Task InvalidateAsync(string projectPath);

    // Is the map still fresh?
    public bool IsFresh(AstProjectMap map, TimeSpan? ttl = null) =>
        DateTime.UtcNow - map.ScannedAt < (ttl ?? DefaultTtl);
}
```

---

### [44] `Services/SyncroCLI/Commands/AstCommand.cs`

**Purpose:** Adds `syncro ast` command to the CLI engine.

```csharp
namespace Syncro.Desktop.Services.SyncroCLI.Commands;

public class AstCommand : ICliCommand
{
    private readonly AstService _ast;

    public string Name => "ast";
    public string Description => "Scan a project and generate AST graph, endpoint map, and PDF report";

    // Usage: syncro ast <path> [--pdf] [--json] [--ports] [--all]
    public async Task Execute(CommandContext ctx)
    {
        var path = ctx.Args.FirstOrDefault() ?? Directory.GetCurrentDirectory();
        var genPdf = ctx.Args.Contains("--pdf");
        var genJson = ctx.Args.Contains("--json");
        var all = ctx.Args.Contains("--all");

        // Scan project(s)
        // Stream progress to SyncroCLIService output event
        // Print summary table to terminal
        // Optionally generate PDF / JSON
    }

    // Help text
    public string GetHelp() => """
        Usage: syncro ast <path> [options]
          <path>    Project root to scan (defaults to current dir)
          --pdf     Generate PDF report in .projectmeta/
          --json    Export JSON report in .projectmeta/
          --ports   Run port scanner
          --all     Scan all known projects in Syncro cache
        """;
}
```

---

## Razor Pages (4 files)

### `Components/Pages/AST/AstDashboard.razor`

- Project path input (or pick from ProjectService cache)
- "Scan" button → calls `AstService.ScanProjectAsync()`
- Progress indicator during scan
- Shows summary cards: Endpoints count, DTOs count, Files, LOC, Open ports
- "Generate PDF" and "Export JSON" buttons
- Links to sub-pages

### `Components/Pages/AST/ProjectMapView.razor`

- Renders `DependencyGraph` as interactive tree (MudBlazor `MudTreeView`)
- Renders `CallGraph` hotspots as `MudDataGrid`
- DAG layers visualized as `MudTimeline`
- Toggle between: dependency graph / call graph / DAG view

### `Components/Pages/AST/EndpointExplorer.razor`

- `MudDataGrid` of all endpoints (route, method, controller, auth, DTO)
- Filter by: HTTP method, framework, auth required
- Click row → shows full endpoint detail with parameters
- "Copy as curl" button per endpoint
- Embedded Swagger UI (iframe to parsed OpenAPI yaml if available)

### `Components/Pages/AST/DtoPlanner.razor`

- List of discovered DTOs (MudDataGrid)
- Highlight inferred (generated) vs source DTOs
- Click DTO → shows property table with types and annotations
- "Generate DTO" button for inferred ones → shows generated C#/TS code in MudDialog
- "Export all DTOs" → downloads as .cs or .ts file

---

## DI Registration (additions to MauiProgram.cs)

```csharp
// AST parsers (all IAstParser implementations)
builder.Services.AddSingleton<IAstParser, CSharpAstParser>();
builder.Services.AddSingleton<IAstParser, TypeScriptAstParser>();
builder.Services.AddSingleton<IAstParser, JavaScriptAstParser>();
builder.Services.AddSingleton<IAstParser, PythonAstParser>();
builder.Services.AddSingleton<IAstParser, ConfigFileParser>();

// AST core
builder.Services.AddSingleton<AstRegistry>();
builder.Services.AddSingleton<AstEngine>();

// Graph
builder.Services.AddTransient<AstGraph>();
builder.Services.AddSingleton<DependencyGraph>();
builder.Services.AddSingleton<CallGraph>();
builder.Services.AddSingleton<DagBuilder>();
builder.Services.AddSingleton<GraphExporter>();

// Scanners
builder.Services.AddSingleton<PortScanner>();
builder.Services.AddSingleton<ApiEndpointScanner>();
builder.Services.AddSingleton<SwaggerScanner>();
builder.Services.AddSingleton<RouteScanner>();
builder.Services.AddSingleton<ConfigScanner>();

// Analyzers
builder.Services.AddSingleton<ProjectAnalyzer>();
builder.Services.AddSingleton<DtoAnalyzer>();
builder.Services.AddSingleton<DependencyAnalyzer>();
builder.Services.AddSingleton<ComplexityAnalyzer>();
builder.Services.AddSingleton<FrameworkDetector>();

// RAG
builder.Services.AddSingleton<AstRagIndexer>();
builder.Services.AddSingleton<AstRagQuery>();

// Reporters
builder.Services.AddSingleton<IAstReporter, PdfReporter>();
builder.Services.AddSingleton<IAstReporter, JsonReporter>();

// Storage
builder.Services.AddSingleton<AstStorageService>();
builder.Services.AddSingleton<AstCacheManager>();

// Main service
builder.Services.AddSingleton<AstService>();
```

---

## Data Flow: Full Scan Sequence

```
User triggers scan
      │
      ▼
AstService.ScanProjectAsync(path)
      │
      ▼
AstEngine.RunAsync(path, ctx)
      │
      ├─► ProjectAnalyzer.AnalyzeAsync(path)
      │     └── Sets ctx.Language, ctx.Framework
      │
      ├─► FrameworkDetector.Detect(path)
      │     └── Fingerprints: Next.js / ASP.NET / FastAPI / Express
      │
      ├─► ConfigScanner → finds config files, declared ports
      │
      ├─► AstRegistry.GetParser() → per file extension
      │     ├─► CSharpAstParser   (.cs files via Roslyn)
      │     ├─► TypeScriptAstParser (.ts/.tsx)
      │     ├─► JavaScriptAstParser (.js/.mjs)
      │     ├─► PythonAstParser   (.py via subprocess)
      │     └─► ConfigFileParser  (.json/.yaml/.toml)
      │
      ├─► All AstNodes collected into ctx.FileNodes
      │
      ├─► DependencyGraph.BuildFromNodes(nodes)
      ├─► CallGraph (from C# Roslyn invocations + JS/TS regex)
      ├─► DagBuilder.BuildDag(dependencyGraph)
      │
      ├─► ApiEndpointScanner.ExtractEndpoints(nodes)
      ├─► RouteScanner (framework-specific route discovery)
      ├─► SwaggerScanner (if openapi.yaml found)
      │
      ├─► DtoAnalyzer.ExtractDtos + InferMissing
      ├─► DependencyAnalyzer (NuGet/npm/pip packages)
      ├─► ComplexityAnalyzer (cyclomatic complexity)
      │
      ├─► PortScanner.ScanAsync() + ExtractDeclaredPorts
      │
      ├─► Assemble AstProjectMap
      │
      ├─► AstCacheManager → save to .projectmeta/ast_map.json
      │
      ▼
AstProjectMap returned to UI / CLI

      (optional)
      ├─► PdfReporter.GenerateForProjectAsync() → PDF with graphs
      ├─► JsonReporter → JSON export
      └─► AstRagIndexer.IndexAsync() → feeds AI context
```

---

## Graph Types Produced

| Graph | Library | Output Format | Purpose |
|---|---|---|---|
| Dependency Graph | QuikGraph | DOT / JSON / Mermaid | Module import relationships |
| Call Graph | QuikGraph | DOT / Mermaid | Method invocation chains |
| DAG | QuikGraph (toposort) | Mermaid Flowchart | Build/load order |
| Complexity Chart | OxyPlot → PNG | PDF embed | Hot-spot visualization |
| Dependency Pie | OxyPlot → PNG | PDF embed | Package manager breakdown |
| Endpoint Bar | OxyPlot → PNG | PDF embed | Route distribution by method |

---

## Port Scanning Coverage

| Port | Service |
|---|---|
| 3000 | Next.js / React / Express default |
| 3001 | CRA / Vite alt port |
| 4000 | GraphQL / Gatsby |
| 5000 | ASP.NET / Flask |
| 5001 | ASP.NET HTTPS |
| 5173 | Vite default |
| 8000 | Django / FastAPI |
| 8080 | Generic HTTP / Docker |
| 8443 | HTTPS alt |
| 9000 | SonarQube / PHP-FPM |
| 9229 | Node.js debug |
| 1433 | SQL Server |
| 3306 | MySQL |
| 5432 | PostgreSQL |
| 6379 | Redis |
| 27017 | MongoDB |

---

## PDF Report Structure

```
[Cover Page]
  Project: <name>    Language: <lang>    Framework: <framework>
  Scanned: <date>    Total files: N      Lines of code: N

[Section 1] Executive Summary
  - Endpoints: N (GET: x, POST: x, PUT: x, DELETE: x)
  - DTOs: N (discovered: x, inferred: x)
  - Dependencies: N packages
  - Open Ports: N

[Section 2] API Endpoints
  Table: | Method | Route | Auth | Request DTO | Response DTO | File:Line |

[Section 3] DTO Reference
  Per DTO: | Property | Type | Required | Annotations |

[Section 4] Dependency Analysis
  - OxyPlot Pie Chart: packages by manager
  - Table: | Package | Version | Type | Direct/Transitive |
  - Circular dependency warnings (if any)

[Section 5] Architecture Graphs
  - Dependency Graph (Mermaid → PNG)
  - DAG layers (Mermaid Flowchart → PNG)
  - Call Graph hotspots

[Section 6] Port Scan
  Table: | Port | State | Service | Source |

[Section 7] Complexity Hotspots
  - OxyPlot Bar Chart: top 10 complex methods
  - Table: | Method | File | Complexity | Threshold |

[Section 8] Swagger / OpenAPI
  - Title, version, servers
  - Schema list

[Section 9] Errors & Warnings
  - Files that failed to parse
  - Missing required config files
  - Circular dependencies flagged
```

---

## File Count Summary

| Area | Files |
|---|---|
| Core (interfaces, base, engine) | 6 |
| Parsers (C#, TS, JS, Python, Config) | 5 |
| Graph (interface, impl, dep, call, dag, export) | 6 |
| Scanners (port, api, swagger, route, config) | 5 |
| Analyzers (project, dto, dep, complexity, framework) | 5 |
| Models (7 model classes + 1 enum) | 8 |
| RAG (indexer + query) | 2 |
| Reporters (interface + PDF + JSON) | 3 |
| Storage (storage + cache) | 2 |
| CLI Command | 1 |
| **Total .cs files** | **43** |
| Razor pages | 4 |
| **Grand total** | **47** |

---

# Extension: Project Analyser — Full Implementation Plan

## What It Is

A new **sub-tab under Projects** called **Project Analyser**.
The user pastes any Git URL. Syncro clones it, runs the full AST engine,
then stores everything in a **file-system vector store (Hindsight)**.
That vectorized knowledge is the permanent "hindsight" for that project —
it can be queried at any time by AI, used to generate docs, render a Swagger UI,
explain functions, or build a cross-project knowledge graph.

```
Projects (nav item)
├── My Projects          /myprojects
└── Project Analyser     /projectanalyser      ← NEW
      ├── Tab: Clone & Analyse
      ├── Tab: Knowledge Base
      ├── Tab: API Explorer
      ├── Tab: Swagger UI
      ├── Tab: Doc Generator
      └── Tab: Graphs
```

---

## New NuGet Packages

```xml
<!-- LibGit2Sharp already pulled in via LibGit2SharpService — no new dep needed for clone -->

<!-- Static HTML templating for Swagger UI page generation -->
<PackageReference Include="Scriban" Version="5.12.1" />

<!-- Math.NET for cosine similarity in vector store -->
<PackageReference Include="MathNet.Numerics" Version="5.0.0" />

<!-- Simple in-process full-text + vector search -->
<!-- No external server needed — pure file-system store -->
```

---

## New File Map (29 C# + 7 Razor = 36 more files)

```
Services/AST/
│
├── Ingestion/
│   ├── GitCloneService.cs               [45]  Clone any git URL to temp/workspace folder
│   ├── ProjectIngestionPipeline.cs      [46]  Orchestrate: clone → AST → vectorize → store
│   ├── IngestionSession.cs              [47]  Live session model (progress, status, results)
│   └── IngestionStatus.cs              [48]  Enum + event args for progress reporting
│
├── Hindsight/
│   ├── HindsightEngine.cs               [49]  Main hindsight coordinator (query, summarize)
│   ├── VectorStore.cs                   [50]  File-system vector store (CRUD + search)
│   ├── VectorDocument.cs                [51]  Stored doc (id, content, vector, metadata)
│   ├── VectorIndex.cs                   [52]  In-memory index (loaded from disk)
│   ├── VectorQuery.cs                   [53]  Cosine similarity search engine
│   ├── DocumentChunker.cs               [54]  Chunk AstProjectMap → VectorDocuments
│   ├── EmbeddingService.cs              [55]  TF-IDF or AI-based embedding generation
│   └── TfIdfVectorizer.cs               [56]  Pure C# TF-IDF implementation
│
├── DocGen/
│   ├── IDocGenerator.cs                 [57]  Generator contract
│   ├── ApiDocGenerator.cs               [58]  Generate API docs (all endpoints + params)
│   ├── FunctionDocGenerator.cs          [59]  Generate per-function documentation via LLM
│   ├── ReadmeGenerator.cs               [60]  Generate project README from analysis
│   └── SwaggerUiGenerator.cs            [61]  Generate interactive Swagger HTML page
│
└── Knowledge/
    ├── KnowledgeBase.cs                 [62]  Manage all knowledge for a project
    ├── KnowledgeEntry.cs                [63]  Single knowledge item model
    ├── KnowledgeQuery.cs                [64]  Natural-language query → hindsight retrieval
    ├── KnowledgeSummaryService.cs       [65]  AI-summarize entire project knowledge
    └── KnowledgeExportService.cs        [66]  Export knowledge as MD/JSON/PDF

Components/Pages/Projects/
├── ProjectAnalyser.razor                [67]  Shell page: MudTabs + routing
├── Tabs/CloneAndAnalyse.razor           [68]  Git URL input, clone progress, scan results
├── Tabs/KnowledgeBaseTab.razor          [69]  Search vectorized knowledge, browse entries
├── Tabs/ApiExplorerTab.razor            [70]  Endpoint table, filter, detail panel
├── Tabs/SwaggerUiTab.razor              [71]  Render generated Swagger HTML in WebView
├── Tabs/DocGenTab.razor                 [72]  AI doc generation controls + output
└── Tabs/GraphsTab.razor                 [73]  Graph visualizations (tree, DAG, dep graph)

Services/SyncroCLI/Commands/
└── (AstCommand.cs extended — no new file)
```

---

## File-by-File Specification (Extension)

---

### [45] `Services/AST/Ingestion/GitCloneService.cs`

**Purpose:** Clone any public or private git repo to the Syncro workspace folder.
Uses `LibGit2Sharp` (already pulled in via `LibGit2SharpService`).

```csharp
namespace Syncro.Desktop.Services.AST.Ingestion;

public class GitCloneService
{
    private readonly string _workspaceRoot;
    // Default: %LOCALAPPDATA%\SyncroDesktop\analysed\

    public GitCloneService()
    {
        _workspaceRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SyncroDesktop", "analysed");
        Directory.CreateDirectory(_workspaceRoot);
    }

    // Clone repo, report progress via IProgress<CloneProgress>
    public Task<string> CloneAsync(
        string gitUrl,
        IProgress<CloneProgressInfo>? progress = null,
        string? branch = null,
        CancellationToken ct = default);

    // Derive local folder name from git URL
    // "https://github.com/user/my-repo.git" → "my-repo_20250605_143022"
    private string DeriveLocalName(string gitUrl);

    // Check if already cloned (by URL hash in manifest)
    public Task<string?> FindExistingCloneAsync(string gitUrl);

    // Delete cloned folder
    public Task DeleteCloneAsync(string localPath);

    // List all cloned repos
    public Task<List<ClonedRepoInfo>> ListClonesAsync();
}

public record CloneProgressInfo(string Stage, int PercentComplete, string? Detail);
public record ClonedRepoInfo(string GitUrl, string LocalPath, DateTime ClonedAt, long SizeBytes);
```

---

### [46] `Services/AST/Ingestion/ProjectIngestionPipeline.cs`

**Purpose:** Full end-to-end pipeline. Wires together all subsystems for a single
"analyse this git repo" operation. Each step fires a progress event the UI subscribes to.

```csharp
namespace Syncro.Desktop.Services.AST.Ingestion;

public class ProjectIngestionPipeline
{
    // Injected: GitCloneService, AstEngine, HindsightEngine,
    //           EmbeddingService, DocumentChunker, VectorStore,
    //           ApiDocGenerator, KnowledgeSummaryService

    public event Action<IngestionStatus>? StatusChanged;

    // Full pipeline: returns completed IngestionSession
    public async Task<IngestionSession> RunAsync(string gitUrl, IngestionOptions options, CancellationToken ct = default)
    {
        var session = new IngestionSession { GitUrl = gitUrl, StartedAt = DateTime.UtcNow };

        // Step 1: Clone (20%)
        Report(IngestionStatus.Cloning, 5);
        session.LocalPath = await _cloneService.CloneAsync(gitUrl, progress: ..., ct: ct);

        // Step 2: AST Scan (40%)
        Report(IngestionStatus.Scanning, 25);
        session.ProjectMap = await _engine.RunAsync(session.LocalPath, ctx, ct);

        // Step 3: Chunk + vectorize (20%)
        Report(IngestionStatus.Vectorizing, 65);
        var chunks = _chunker.Chunk(session.ProjectMap);
        var docs = await _embedding.EmbedAsync(chunks, ct);
        await _vectorStore.UpsertBatchAsync(session.LocalPath, docs, ct);

        // Step 4: Generate AI summary + docs (15%)
        Report(IngestionStatus.GeneratingDocs, 85);
        if (options.GenerateDocs)
        {
            session.GeneratedDocs = await _docGen.GenerateApiDocsAsync(
                session.ProjectMap.Endpoints, session.ProjectMap.Dtos, ct);
            session.ProjectSummary = await _summaryService.SummarizeAsync(session.ProjectMap, ct);
        }

        // Step 5: Build knowledge base entry (5%)
        Report(IngestionStatus.Storing, 97);
        await _knowledge.RegisterProjectAsync(session);

        session.CompletedAt = DateTime.UtcNow;
        Report(IngestionStatus.Done, 100);
        return session;
    }
}

public class IngestionOptions
{
    public bool GenerateDocs { get; set; } = true;
    public bool ScanPorts { get; set; } = false;   // Off for remote repos
    public bool GenerateSwagger { get; set; } = true;
    public bool DeleteCloneAfter { get; set; } = false;
    public string? Branch { get; set; }
}
```

---

### [47] `Services/AST/Ingestion/IngestionSession.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Ingestion;

public class IngestionSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string GitUrl { get; set; } = "";
    public string? LocalPath { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public IngestionStatus CurrentStatus { get; set; }
    public int ProgressPercent { get; set; }
    public AstProjectMap? ProjectMap { get; set; }
    public string? ProjectSummary { get; set; }        // AI-generated summary
    public string? GeneratedDocs { get; set; }         // Markdown API docs
    public string? SwaggerHtmlPath { get; set; }       // Path to generated swagger.html
    public List<string> Errors { get; set; } = new();
    public TimeSpan? Duration => CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt : null;
}
```

---

### [48] `Services/AST/Ingestion/IngestionStatus.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Ingestion;

public enum IngestionStatus
{
    Idle,
    Cloning,        // git clone running
    Scanning,       // AST engine parsing files
    Vectorizing,    // Chunking + embedding
    GeneratingDocs, // LLM doc generation
    Storing,        // Writing to knowledge base
    Done,
    Failed
}
```

---

### [49] `Services/AST/Hindsight/HindsightEngine.cs`

**Purpose:** The central "memory" for all analysed projects.
Provides the main API used by the UI and AI assistant.

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class HindsightEngine
{
    private readonly VectorStore _store;
    private readonly VectorQuery _query;
    private readonly KnowledgeBase _knowledge;
    private readonly AIClient _ai;

    // Ask a natural language question about a project
    // Returns AI answer enriched with hindsight context
    public Task<string> AskAsync(string projectPath, string question, CancellationToken ct = default);

    // Get hindsight context string for injection into any AI prompt
    // (used by AstRagQuery and DocGen services)
    public Task<string> GetContextAsync(string projectPath, string query, int topK = 8);

    // List all projects with vectorized hindsight
    public Task<List<KnowledgeEntry>> ListProjectsAsync();

    // Cross-project query: search across ALL vectorized projects
    public Task<List<VectorSearchResult>> SearchAllProjectsAsync(string query, int topK = 10);

    // Get full summary for a project (cached or re-generate)
    public Task<string> GetProjectSummaryAsync(string projectPath);

    // Forget a project (delete all its vector docs)
    public Task ForgetProjectAsync(string projectPath);
}
```

---

### [50] `Services/AST/Hindsight/VectorStore.cs`

**Purpose:** File-system vector store. No external database.
Each project gets a folder: `%LOCALAPPDATA%\SyncroDesktop\knowledge\{projectHash}\`.
Stores: `index.json` (metadata) + one `{chunkId}.vec.json` per vector document.

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class VectorStore
{
    private readonly string _storeRoot;
    private readonly Dictionary<string, VectorIndex> _cache = new();

    public VectorStore()
    {
        _storeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SyncroDesktop", "knowledge");
        Directory.CreateDirectory(_storeRoot);
    }

    // Upsert a batch of documents for a project
    public Task UpsertBatchAsync(string projectPath, IEnumerable<VectorDocument> docs, CancellationToken ct = default);

    // Load index for a project (from disk or cache)
    public Task<VectorIndex> LoadIndexAsync(string projectPath);

    // Delete all docs for a project
    public Task DeleteProjectAsync(string projectPath);

    // List all projects in the store
    public Task<List<string>> ListProjectPathsAsync();

    // Get raw document by id
    public Task<VectorDocument?> GetDocAsync(string projectPath, string docId);

    // Project-specific storage path (SHA256 of projectPath)
    private string GetProjectStorePath(string projectPath);
}
```

---

### [51] `Services/AST/Hindsight/VectorDocument.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class VectorDocument
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ProjectPath { get; set; } = "";
    public string Content { get; set; } = "";        // Human-readable chunk text
    public double[] Vector { get; set; } = [];       // TF-IDF or embedding vector
    public AstNodeType NodeType { get; set; }
    public string? SourceFile { get; set; }
    public string? EntityName { get; set; }          // Class/method/endpoint name
    public DateTime IndexedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Metadata { get; set; } = new();
    // Metadata keys: "route", "httpMethod", "language", "framework", "dtoName"
}

public record VectorSearchResult(VectorDocument Document, double Score);
```

---

### [52] `Services/AST/Hindsight/VectorIndex.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class VectorIndex
{
    public string ProjectPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Language { get; set; } = "";
    public string Framework { get; set; } = "";
    public string? GitUrl { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public int DocumentCount { get; set; }
    public string? ProjectSummary { get; set; }   // AI-generated summary

    // Loaded into memory — not serialized (reconstructed from doc files)
    [JsonIgnore] public List<VectorDocument> Documents { get; set; } = new();
}
```

---

### [53] `Services/AST/Hindsight/VectorQuery.cs`

**Purpose:** Cosine similarity search over loaded VectorIndex.
Uses `MathNet.Numerics` for vector math.

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

using MathNet.Numerics.LinearAlgebra;

public class VectorQuery
{
    // Search a loaded index by cosine similarity
    public List<VectorSearchResult> Search(
        VectorIndex index,
        double[] queryVector,
        int topK = 8,
        double minScore = 0.1,
        AstNodeType? filterType = null);

    // Cosine similarity between two dense vectors
    private double CosineSimilarity(double[] a, double[] b);

    // Search across multiple indexes
    public List<VectorSearchResult> SearchAll(
        IEnumerable<VectorIndex> indexes,
        double[] queryVector,
        int topK = 10);
}
```

---

### [54] `Services/AST/Hindsight/DocumentChunker.cs`

**Purpose:** Converts an `AstProjectMap` into a list of text chunks
ready for vectorization. Each chunk is self-contained and human-readable.

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class DocumentChunker
{
    // Chunk strategies (all applied):
    public List<VectorDocument> Chunk(AstProjectMap map)
    {
        var docs = new List<VectorDocument>();

        docs.AddRange(ChunkEndpoints(map));      // One doc per API endpoint
        docs.AddRange(ChunkDtos(map));           // One doc per DTO
        docs.AddRange(ChunkClasses(map));        // One doc per class
        docs.AddRange(ChunkMethods(map));        // One doc per method/function
        docs.AddRange(ChunkDependencies(map));   // One doc for dep summary
        docs.AddRange(ChunkPorts(map));          // One doc for port summary
        docs.Add(ChunkProjectSummary(map));      // One doc for overall summary

        return docs;
    }

    // Example endpoint chunk text:
    // "POST /api/users/{id} — handler: UserController.UpdateUser
    //  Parameters: id (path, int, required), body: UpdateUserDto
    //  Response: UserResponse | Requires auth: true | File: UserController.cs:42"
    private List<VectorDocument> ChunkEndpoints(AstProjectMap map);

    // Example method chunk text:
    // "Method: AuthService.GenerateToken(userId: string, role: string) → string
    //  Complexity: 4 | File: Services/AuthService.cs:78
    //  Dependencies called: JwtSecurityTokenHandler, DateTime.UtcNow"
    private List<VectorDocument> ChunkMethods(AstProjectMap map);

    // Max tokens per chunk (for LLM context window safety)
    private string TruncateToTokenBudget(string text, int maxChars = 600);
}
```

---

### [55] `Services/AST/Hindsight/EmbeddingService.cs`

**Purpose:** Converts text chunks into numeric vectors.
Primary: TF-IDF (no API, pure local).
Optional: call `AIClient` to get real embeddings if available.

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class EmbeddingService
{
    private readonly TfIdfVectorizer _tfidf;
    private readonly AIClient? _ai;

    // Embed a batch of VectorDocuments (sets .Vector on each)
    public Task<List<VectorDocument>> EmbedAsync(List<VectorDocument> docs, CancellationToken ct = default);

    // Embed a single query string for search
    public Task<double[]> EmbedQueryAsync(string query);

    // Use TF-IDF by default; switch to AI if ai != null and options say so
    private EmbeddingMode _mode = EmbeddingMode.TfIdf;

    public enum EmbeddingMode { TfIdf, AiApi }
}
```

---

### [56] `Services/AST/Hindsight/TfIdfVectorizer.cs`

**Purpose:** Full TF-IDF implementation in C#. Builds a shared vocabulary
from all project documents, then converts each doc to a sparse/dense vector.

```csharp
namespace Syncro.Desktop.Services.AST.Hindsight;

public class TfIdfVectorizer
{
    private Dictionary<string, int> _vocabulary = new();
    private Dictionary<string, double> _idfScores = new();
    private int _docCount;

    // Build vocabulary + IDF from all documents in a project
    public void Fit(IEnumerable<string> documents);

    // Vectorize a document using fitted vocabulary
    public double[] Transform(string document);

    // Fit + transform in one step
    public double[][] FitTransform(IEnumerable<string> documents);

    // Vectorize a query (uses fitted vocabulary, no IDF update)
    public double[] TransformQuery(string query);

    // Tokenize: lowercase, split on non-alphanumeric, remove stop words
    private List<string> Tokenize(string text);

    // Save/load vocabulary to disk (so re-fits aren't needed after restart)
    public Task SaveAsync(string path);
    public Task LoadAsync(string path);

    private static readonly HashSet<string> StopWords =
    [
        "the", "a", "an", "is", "are", "was", "and", "or", "in", "to",
        "of", "for", "this", "that", "it", "with", "as", "by", "at"
    ];
}
```

---

### [57] `Services/AST/DocGen/IDocGenerator.cs`

```csharp
namespace Syncro.Desktop.Services.AST.DocGen;

public interface IDocGenerator
{
    string DocType { get; }  // "api" | "function" | "readme" | "swagger-ui"
    Task<string> GenerateAsync(AstProjectMap map, CancellationToken ct = default);
}
```

---

### [58] `Services/AST/DocGen/ApiDocGenerator.cs`

**Purpose:** Uses AIClient + hindsight context to generate full Markdown API docs
for every endpoint in the project.

```csharp
namespace Syncro.Desktop.Services.AST.DocGen;

public class ApiDocGenerator : IDocGenerator
{
    public string DocType => "api";

    // Per endpoint: method, route, params, request body, response, examples
    // Batches endpoints to stay within LLM token limits
    public Task<string> GenerateAsync(AstProjectMap map, CancellationToken ct = default);

    // Build structured prompt for one endpoint
    private string BuildEndpointPrompt(AstEndpoint ep, List<AstDtoModel> dtos);

    // Merge all individual endpoint docs into one Markdown doc
    private string AssembleFinalDoc(
        AstProjectMap map,
        List<(AstEndpoint Ep, string Doc)> epDocs);

    // Output format:
    // # API Documentation — {projectName}
    // ## POST /api/users
    // **Handler:** UserController.CreateUser
    // **Auth:** Required (Bearer)
    // ### Request Body
    // | Field | Type | Required |
    // ### Response 200
    // | Field | Type |
    // ### Example
    // ```json { ... } ```
}
```

---

### [59] `Services/AST/DocGen/FunctionDocGenerator.cs`

**Purpose:** Generates docstring-style documentation for individual functions/methods
using the LLM with code context. Especially useful for undocumented code.

```csharp
namespace Syncro.Desktop.Services.AST.DocGen;

public class FunctionDocGenerator : IDocGenerator
{
    public string DocType => "function";

    // Generate docs for all methods in map that lack summary
    public Task<string> GenerateAsync(AstProjectMap map, CancellationToken ct = default);

    // Generate doc for a single method node
    public Task<string> GenerateForMethodAsync(AstNode method, string? sourceSnippet = null, CancellationToken ct = default);

    // Read source snippet (N lines around method) for context
    private Task<string> ReadSourceSnippetAsync(AstNode method, int contextLines = 10);

    // Prompt structure:
    // "Given this {language} function signature and body snippet,
    //  write a clear, concise docstring explaining what it does,
    //  its parameters, return value, and any important side effects."
}
```

---

### [60] `Services/AST/DocGen/ReadmeGenerator.cs`

**Purpose:** Generate a professional README.md for any project based on the
full AST analysis + AI summarization.

```csharp
namespace Syncro.Desktop.Services.AST.DocGen;

public class ReadmeGenerator : IDocGenerator
{
    public string DocType => "readme";

    public Task<string> GenerateAsync(AstProjectMap map, CancellationToken ct = default);

    // README sections:
    // # {Project Name}
    // > {AI one-liner}
    //
    // ## Tech Stack
    // Language: {lang} | Framework: {framework} | Deps: {count}
    //
    // ## Project Structure
    // (file tree from AstNodes, max depth 3)
    //
    // ## API Endpoints
    // Quick table of all routes
    //
    // ## Setup & Run
    // (inferred from config files + package manager)
    //
    // ## Dependencies
    // (top-level NuGet/npm/pip packages)

    private string BuildFileTree(AstProjectMap map, int maxDepth = 3);
    private string BuildEndpointTable(List<AstEndpoint> endpoints);
    private string BuildSetupInstructions(AstProjectMap map);
}
```

---

### [61] `Services/AST/DocGen/SwaggerUiGenerator.cs`

**Purpose:** Generates a self-contained `swagger.html` file that bundles
Swagger UI (via CDN links) with the generated OpenAPI YAML inline.
Can be opened in a browser or rendered in a MAUI WebView.

```csharp
namespace Syncro.Desktop.Services.AST.DocGen;

public class SwaggerUiGenerator : IDocGenerator
{
    private readonly ApiEndpointScanner _scanner;
    private readonly SwaggerScanner _swaggerScanner;

    public string DocType => "swagger-ui";

    // Returns path to generated swagger.html
    public Task<string> GenerateAsync(AstProjectMap map, CancellationToken ct = default);

    // Build OpenAPI 3.0 YAML from AstProjectMap endpoints + DTOs
    public string BuildOpenApiYaml(AstProjectMap map);

    // Render HTML template with inline YAML using Scriban
    // Template uses Swagger UI CDN: unpkg.com/swagger-ui-dist
    private string RenderHtmlTemplate(string openApiYaml, string projectName);

    // Save swagger.html to .projectmeta/swagger.html
    private Task<string> SaveHtmlAsync(AstProjectMap map, string html);

    // Output: self-contained HTML with:
    //   - Swagger UI JS/CSS from CDN
    //   - Inline spec as JS variable (no server needed)
    //   - Dark theme matching Syncro's UI
}
```

---

### [62] `Services/AST/Knowledge/KnowledgeBase.cs`

**Purpose:** Top-level knowledge management. Maintains a registry of all
projects that have been ingested and their hindsight status.

```csharp
namespace Syncro.Desktop.Services.AST.Knowledge;

public class KnowledgeBase
{
    private readonly VectorStore _store;
    private readonly AstStorageService _astStorage;
    private readonly string _registryPath;
    // Registry: %LOCALAPPDATA%\SyncroDesktop\knowledge\registry.json

    // Register a completed ingestion session
    public Task RegisterProjectAsync(IngestionSession session);

    // Load knowledge entry for a project
    public Task<KnowledgeEntry?> GetAsync(string projectPath);

    // All projects with knowledge
    public Task<List<KnowledgeEntry>> ListAllAsync();

    // Update summary for a project
    public Task UpdateSummaryAsync(string projectPath, string summary);

    // Check if a project has been indexed
    public Task<bool> IsIndexedAsync(string projectPath);

    // Remove project from knowledge base
    public Task ForgetAsync(string projectPath);
}
```

---

### [63] `Services/AST/Knowledge/KnowledgeEntry.cs`

```csharp
namespace Syncro.Desktop.Services.AST.Knowledge;

public class KnowledgeEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string ProjectPath { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string? GitUrl { get; set; }
    public string Language { get; set; } = "";
    public string Framework { get; set; } = "";
    public string? Version { get; set; }
    public DateTime IngestedAt { get; set; }
    public DateTime? LastUpdatedAt { get; set; }
    public string? Summary { get; set; }           // AI one-paragraph summary
    public int VectorDocCount { get; set; }
    public int EndpointCount { get; set; }
    public int DtoCount { get; set; }
    public int FileCount { get; set; }
    public bool HasSwaggerHtml { get; set; }
    public bool HasGeneratedDocs { get; set; }
    public string? SwaggerHtmlPath { get; set; }
    public string? GeneratedDocsPath { get; set; }
}
```

---

### [64] `Services/AST/Knowledge/KnowledgeQuery.cs`

**Purpose:** Natural-language query → retrieve from hindsight → feed to AI → return answer.
This is the "ask your codebase" interface.

```csharp
namespace Syncro.Desktop.Services.AST.Knowledge;

public class KnowledgeQuery
{
    private readonly HindsightEngine _hindsight;
    private readonly AIClient _ai;

    // Ask a question about a specific project
    public Task<KnowledgeQueryResult> AskProjectAsync(
        string projectPath, string question, CancellationToken ct = default);

    // Ask across all indexed projects ("which project uses Redis?")
    public Task<KnowledgeQueryResult> AskAllProjectsAsync(
        string question, CancellationToken ct = default);

    // Compare two projects
    public Task<string> CompareProjectsAsync(
        string pathA, string pathB, string aspect, CancellationToken ct = default);

    // Get code examples from hindsight for a pattern
    public Task<List<VectorSearchResult>> FindExamplesAsync(
        string pattern, string? projectPath = null, int topK = 5);
}

public class KnowledgeQueryResult
{
    public string Answer { get; set; } = "";
    public List<VectorSearchResult> Sources { get; set; } = new();  // Supporting chunks
    public string[] ProjectsSearched { get; set; } = [];
    public TimeSpan Duration { get; set; }
}
```

---

### [65] `Services/AST/Knowledge/KnowledgeSummaryService.cs`

**Purpose:** AI-powered summarization of a project's knowledge base.
Produces: executive summary, notable patterns, warnings, recommendations.

```csharp
namespace Syncro.Desktop.Services.AST.Knowledge;

public class KnowledgeSummaryService
{
    private readonly AIClient _ai;
    private readonly HindsightEngine _hindsight;

    // Generate full project summary using AI
    public Task<string> SummarizeAsync(AstProjectMap map, CancellationToken ct = default);

    // Generate bullet-point notes about a project
    public Task<List<string>> GenerateNotesAsync(string projectPath, CancellationToken ct = default);

    // Detect patterns: "this is a CRUD API", "uses repository pattern", etc.
    public Task<List<string>> DetectPatternsAsync(AstProjectMap map, CancellationToken ct = default);

    // Recommendations: "add pagination to GET /items", "missing error handling in AuthService"
    public Task<List<string>> GenerateRecommendationsAsync(AstProjectMap map, CancellationToken ct = default);

    // Summary prompt structure:
    // "You are a senior architect analysing a {language} {framework} project.
    //  Here is the project structure: {context from hindsight top-20 chunks}
    //  Summarize: purpose, architecture, key APIs, data models, notable patterns.
    //  Keep it under 300 words."
}
```

---

### [66] `Services/AST/Knowledge/KnowledgeExportService.cs`

**Purpose:** Export all knowledge for a project as shareable files.

```csharp
namespace Syncro.Desktop.Services.AST.Knowledge;

public class KnowledgeExportService
{
    private readonly KnowledgeBase _knowledge;
    private readonly PdfReporter _pdfReporter;
    private readonly JsonReporter _jsonReporter;

    // Export as Markdown bundle (summary + API docs + DTO reference)
    public Task<string> ExportMarkdownAsync(string projectPath, string outputDir);

    // Export as PDF (uses existing PdfReporter)
    public Task<string> ExportPdfAsync(string projectPath, string outputDir);

    // Export as JSON (all vector docs + project map)
    public Task<string> ExportJsonAsync(string projectPath, string outputDir);

    // Export Swagger HTML
    public Task<string> ExportSwaggerAsync(string projectPath, string outputDir);

    // Export everything as a ZIP archive
    public Task<string> ExportZipAsync(string projectPath, string outputDir);
}
```

---

## Razor Pages Specification

---

### [67] `Components/Pages/Projects/ProjectAnalyser.razor`

```razor
@page "/projectanalyser"
@inject ProjectIngestionPipeline Pipeline
@inject HindsightEngine Hindsight
@inject KnowledgeBase Knowledge

<MudContainer MaxWidth="MaxWidth.ExtraLarge" Class="pa-0">
    <div class="d-flex align-center justify-space-between mb-4">
        <MudText Typo="Typo.h5" Class="font-weight-bold">PROJECT ANALYSER</MudText>
        <MudChip Color="Color.Success" Size="Size.Small">
            @_indexedCount projects in hindsight
        </MudChip>
    </div>

    <MudTabs Elevation="0" Rounded="false" ApplyEffectsToContainer="true"
             @bind-ActivePanelIndex="_activeTab" Class="analyser-tabs">

        <MudTabPanel Text="Clone & Analyse" Icon="@Icons.Material.Filled.CloudDownload">
            <CloneAndAnalyse OnIngestionComplete="OnIngestionComplete" />
        </MudTabPanel>

        <MudTabPanel Text="Knowledge Base" Icon="@Icons.Material.Filled.Psychology">
            <KnowledgeBaseTab />
        </MudTabPanel>

        <MudTabPanel Text="API Explorer" Icon="@Icons.Material.Filled.Api"
                     Disabled="_selectedProject == null">
            <ApiExplorerTab ProjectMap="_selectedProject" />
        </MudTabPanel>

        <MudTabPanel Text="Swagger UI" Icon="@Icons.Material.Filled.Web"
                     Disabled="_selectedProject == null">
            <SwaggerUiTab ProjectPath="_selectedProject?.ProjectPath" />
        </MudTabPanel>

        <MudTabPanel Text="Doc Generator" Icon="@Icons.Material.Filled.AutoAwesome"
                     Disabled="_selectedProject == null">
            <DocGenTab ProjectMap="_selectedProject" />
        </MudTabPanel>

        <MudTabPanel Text="Graphs" Icon="@Icons.Material.Filled.AccountTree"
                     Disabled="_selectedProject == null">
            <GraphsTab ProjectMap="_selectedProject" />
        </MudTabPanel>
    </MudTabs>
</MudContainer>
```

---

### [68] `Components/Pages/Projects/Tabs/CloneAndAnalyse.razor`

**UI behaviour:**

1. **Input section:** Git URL text field + optional branch input + Options checkboxes (Generate Docs, Generate Swagger)
2. **"Analyse" button** → triggers `ProjectIngestionPipeline.RunAsync()`
3. **Progress stepper** (MudStepper or MudProgressLinear):
   - Cloning → Scanning → Vectorizing → Generating Docs → Storing → Done
4. **Live log** panel: streams status events from pipeline
5. **Results card** after completion:
   - Project name, language, framework badge
   - Stats: Files, Endpoints, DTOs, Vector docs
   - Buttons: "Open API Explorer", "View Swagger", "Open in Explorer"
6. **Recent analyses list**: last 5 ingested projects (from KnowledgeBase.ListAllAsync())

```csharp
// Key state:
private string _gitUrl = "";
private string? _branch;
private bool _isRunning;
private IngestionSession? _session;
private IngestionOptions _options = new();
private List<string> _log = new();
private List<KnowledgeEntry> _recent = new();

// Subscribe to Pipeline.StatusChanged event to stream progress
```

---

### [69] `Components/Pages/Projects/Tabs/KnowledgeBaseTab.razor`

**UI behaviour:**

1. **Search bar** at top: natural language query → calls `KnowledgeQuery.AskAllProjectsAsync()`
2. **Project cards grid**: each indexed project shown as a card with:
   - Project name, language badge, framework badge
   - Stats: endpoints, DTOs, files, vector docs
   - Git URL link
   - Actions: Ask, Export PDF, Export ZIP, Forget
3. **Ask panel** (slides in from right or modal):
   - Input: question text
   - Output: AI answer + source chunks listed below
4. **Search results** overlay: vector similarity results with score badges

```csharp
private string _query = "";
private List<KnowledgeEntry> _projects = new();
private KnowledgeQueryResult? _queryResult;
private bool _showAskPanel;
private KnowledgeEntry? _selectedForAsk;
```

---

### [70] `Components/Pages/Projects/Tabs/ApiExplorerTab.razor`

**UI behaviour:**

1. **Toolbar**: filter by HTTP method (chip group: ALL / GET / POST / PUT / DELETE / PATCH),
   filter by framework, search by route text
2. **MudDataGrid** of endpoints:
   - Columns: Method (color chip), Route, Handler, Auth (lock icon), DTO, File
   - Click row → expands detail panel below grid
3. **Detail panel**:
   - Full route, all parameters (table: name, type, source, required)
   - Request body DTO with properties
   - Response type
   - "Ask AI" button → pre-fills KnowledgeQuery with "Explain the {route} endpoint"
   - Copy as cURL button
4. **Summary bar**: total endpoints by method

---

### [71] `Components/Pages/Projects/Tabs/SwaggerUiTab.razor`

**UI behaviour:**

1. **Check for existing** `swagger.html` in `.projectmeta/` or `Hindsight` store
2. If found → render in `BlazorWebView` or open in system browser
3. If not found → "Generate Swagger" button → calls `SwaggerUiGenerator.GenerateAsync()`
4. **Regenerate** button to refresh after project changes
5. **Download** button for `swagger.html` (self-contained, shareable)
6. **Copy OpenAPI YAML** button

```csharp
// MAUI BlazorWebView can render local HTML files via custom URL scheme
// Alternative: open file in system default browser via Launcher.OpenAsync(fileUri)
private string? _swaggerHtmlPath;
private bool _isGenerating;

private async Task OpenInBrowser()
    => await Launcher.OpenAsync(new Uri($"file:///{_swaggerHtmlPath}"));
```

---

### [72] `Components/Pages/Projects/Tabs/DocGenTab.razor`

**UI behaviour:**

1. **Doc type selector**: radio buttons — API Docs | Function Docs | README | All
2. **Generate button** → streams output chunks from LLM
3. **Output panel**: rendered Markdown (MudMarkdown or pre-formatted)
4. **Actions**: Copy Markdown, Save to `.projectmeta/docs/`, Export as PDF
5. **Function selector**: when "Function Docs" chosen, shows a searchable list of
   all methods; user picks one → "Generate for this function"
6. **Progress**: streaming token output (shows text appearing live via `IAsyncEnumerable`)
7. **Notes panel**: bullet-point notes from `KnowledgeSummaryService.GenerateNotesAsync()`

---

### [73] `Components/Pages/Projects/Tabs/GraphsTab.razor`

**UI behaviour:**

1. **Graph type selector**: tab strip — Dependency Graph | Call Graph | DAG | Complexity
2. **Dependency graph**:
   - Rendered as `MudTreeView` (nested, collapsible)
   - Circular dependency warnings in red
3. **Call graph hotspots**:
   - MudDataGrid: method name, caller count, file, complexity
4. **DAG layers**:
   - `MudTimeline` horizontal: each layer = one column, nodes = chips
5. **Complexity chart**:
   - OxyPlot bar chart rendered to PNG, embedded as `<img>` via base64 data URI
6. **Export**: "Download as DOT", "Download as JSON", "Include in PDF"

```csharp
private string _activeGraph = "dependency";
private AstProjectMap? _map;

// OxyPlot PNG rendered server-side via OxyPlot.SkiaSharp
private string? _complexityChartBase64;

protected override async Task OnParametersSetAsync()
{
    if (ProjectMap != null)
        _complexityChartBase64 = await RenderComplexityChartAsync(ProjectMap);
}
```

---

## NavMenu Change

Add one entry after "My Projects":

```razor
<div class="nav-item">
    <NavLink class="nav-link" href="projectanalyser">
        <span class="nav-icon"><i class="bi bi-diagram-3-fill"></i></span>
        <span class="nav-text">Project Analyser</span>
    </NavLink>
</div>
```

---

## Extended DI Registration (additions to MauiProgram.cs)

```csharp
// Ingestion
builder.Services.AddSingleton<GitCloneService>();
builder.Services.AddSingleton<ProjectIngestionPipeline>();

// Hindsight / Vector Store
builder.Services.AddSingleton<TfIdfVectorizer>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<VectorStore>();
builder.Services.AddSingleton<VectorQuery>();
builder.Services.AddSingleton<DocumentChunker>();
builder.Services.AddSingleton<HindsightEngine>();

// Doc Gen
builder.Services.AddSingleton<IDocGenerator, ApiDocGenerator>();
builder.Services.AddSingleton<IDocGenerator, FunctionDocGenerator>();
builder.Services.AddSingleton<IDocGenerator, ReadmeGenerator>();
builder.Services.AddSingleton<IDocGenerator, SwaggerUiGenerator>();

// Knowledge
builder.Services.AddSingleton<KnowledgeBase>();
builder.Services.AddSingleton<KnowledgeQuery>();
builder.Services.AddSingleton<KnowledgeSummaryService>();
builder.Services.AddSingleton<KnowledgeExportService>();
```

---

## Full Data Flow: Clone URL → Hindsight Ready

```
User pastes: https://github.com/user/some-api.git
                  │
                  ▼
     GitCloneService.CloneAsync(url)
     → %LOCALAPPDATA%\SyncroDesktop\analysed\some-api_20250605\
                  │
                  ▼
     AstEngine.RunAsync(clonedPath, ctx)
     → AstProjectMap { Endpoints[24], Dtos[18], Files[143], ... }
                  │
                  ▼
     DocumentChunker.Chunk(map)
     → 200–400 VectorDocuments (endpoints, classes, methods, dtos, ports)
                  │
                  ▼
     TfIdfVectorizer.FitTransform(chunks)
     → each doc gets double[] vector (vocabulary-sized)
                  │
                  ▼
     VectorStore.UpsertBatchAsync(path, docs)
     → %LOCALAPPDATA%\SyncroDesktop\knowledge\{hash}\
          index.json          (VectorIndex metadata)
          {chunkId}.vec.json  (one file per VectorDocument)
          vocab.json          (TF-IDF vocabulary)
                  │
                  ▼
     ApiDocGenerator.GenerateAsync(map)
     + KnowledgeSummaryService.SummarizeAsync(map)
     → via AIClient → http://localhost:3020/gemini
     → Markdown API docs + project summary
                  │
                  ▼
     SwaggerUiGenerator.GenerateAsync(map)
     → .projectmeta/swagger.html (self-contained Swagger UI)
                  │
                  ▼
     KnowledgeBase.RegisterProjectAsync(session)
     → registry.json updated

══════════════════════════════════
  HINDSIGHT NOW ACTIVE
══════════════════════════════════

  User asks: "How does authentication work in this project?"
                  │
                  ▼
     KnowledgeQuery.AskProjectAsync(path, question)
                  │
                  ▼
     EmbeddingService.EmbedQueryAsync(question)
     → double[] queryVector
                  │
                  ▼
     VectorQuery.Search(index, queryVector, topK=8)
     → 8 most similar VectorDocuments (cosine similarity)
                  │
                  ▼
     HindsightEngine.BuildContextPrompt(question, chunks)
     → "Context from codebase:\n[chunk1]\n[chunk2]...\nQuestion: ..."
                  │
                  ▼
     AIClient.SendAsync(contextPrompt)
     → "Authentication uses JWT. The AuthService.GenerateToken method
        (Services/Auth/AuthService.cs:78) takes userId and role,
        creates a JwtSecurityToken with 24h expiry..."
                  │
                  ▼
     KnowledgeQueryResult { Answer, Sources[8], Duration }
     → displayed in KnowledgeBaseTab
```

---

## Hindsight File-System Layout

```
%LOCALAPPDATA%\SyncroDesktop\
├── analysed\                          ← Cloned repos
│   ├── some-api_20250605_143022\      ← Full git clone
│   └── another-project_20250605\
│
└── knowledge\                         ← Vector store
    ├── registry.json                  ← List of all KnowledgeEntry
    ├── a1b2c3d4\                      ← SHA256(projectPath) as folder
    │   ├── index.json                 ← VectorIndex metadata
    │   ├── vocab.json                 ← TF-IDF vocabulary
    │   ├── {uuid}.vec.json            ← VectorDocument (one per chunk)
    │   ├── {uuid}.vec.json
    │   └── ...
    └── e5f6a7b8\
        ├── index.json
        └── ...
```

---

## Updated File Count

| Area | Original | Extension | Total |
|---|---|---|---|
| Core | 6 | — | 6 |
| Parsers | 5 | — | 5 |
| Graph | 6 | — | 6 |
| Scanners | 5 | — | 5 |
| Analyzers | 5 | — | 5 |
| Models | 8 | — | 8 |
| RAG (basic) | 2 | — | 2 |
| Reporters | 3 | — | 3 |
| Storage | 2 | — | 2 |
| CLI Command | 1 | — | 1 |
| **Ingestion** | — | 4 | 4 |
| **Hindsight / Vector** | — | 8 | 8 |
| **Doc Gen** | — | 5 | 5 |
| **Knowledge** | — | 5 | 5 |
| **Total .cs files** | **43** | **22** | **65** |
| **Razor pages** | 4 | 7 | 11 |
| **Grand total** | **47** | **29** | **76** |
