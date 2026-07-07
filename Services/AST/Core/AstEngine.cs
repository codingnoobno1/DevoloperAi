using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Models;
using Syncro.Desktop.Services.AST.Scanners;
using Syncro.Desktop.Services.AST.Analyzers;
using Syncro.Desktop.Services.AST.Graph;

namespace Syncro.Desktop.Services.AST.Core;

public class AstEngine
{
    private readonly AstRegistry _registry;
    private readonly PortScanner _portScanner;
    private readonly ApiEndpointScanner _apiScanner;
    private readonly SwaggerScanner _swaggerScanner;
    private readonly RouteScanner _routeScanner;
    private readonly ConfigScanner _configScanner;
    private readonly FrameworkDetector _frameworkDetector;
    private readonly DtoAnalyzer _dtoAnalyzer;
    private readonly DependencyAnalyzer _dependencyAnalyzer;
    private readonly ComplexityAnalyzer _complexityAnalyzer;
    private readonly GraphExporter _graphExporter;
    private readonly DagBuilder _dagBuilder;

    public AstEngine(
        AstRegistry registry,
        PortScanner portScanner,
        ApiEndpointScanner apiScanner,
        SwaggerScanner swaggerScanner,
        RouteScanner routeScanner,
        ConfigScanner configScanner,
        FrameworkDetector frameworkDetector,
        DtoAnalyzer dtoAnalyzer,
        DependencyAnalyzer dependencyAnalyzer,
        ComplexityAnalyzer complexityAnalyzer,
        GraphExporter graphExporter,
        DagBuilder dagBuilder)
    {
        _registry = registry;
        _portScanner = portScanner;
        _apiScanner = apiScanner;
        _swaggerScanner = swaggerScanner;
        _routeScanner = routeScanner;
        _configScanner = configScanner;
        _frameworkDetector = frameworkDetector;
        _dtoAnalyzer = dtoAnalyzer;
        _dependencyAnalyzer = dependencyAnalyzer;
        _complexityAnalyzer = complexityAnalyzer;
        _graphExporter = graphExporter;
        _dagBuilder = dagBuilder;
    }

    public async Task<AstProjectMap> RunAsync(string projectPath, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(ctx.CancellationToken);
        }

        var map = new AstProjectMap
        {
            ProjectPath = projectPath,
            ProjectName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar))
        };

        try
        {
            // 1. Scan/Find configurations
            var configFiles = _configScanner.FindConfigFiles(projectPath);
            foreach (var cfg in configFiles)
            {
                map.ConfigFiles[cfg.Key] = cfg.Value;
            }

            // 2. Detect Language & Framework using FrameworkDetector
            map.Framework = _frameworkDetector.Detect(projectPath, configFiles);
            map.Language = _frameworkDetector.DetectLanguage(map.Framework);
            ctx.Framework = map.Framework.ToLower();
            ctx.ProjectLanguage = map.Language.ToLower();

            // 3. Walk & Parse source files recursively
            var allSourceNodes = new List<AstNode>();
            await WalkAndParseSourceFiles(projectPath, allSourceNodes, ctx);
            map.Nodes.AddRange(allSourceNodes);

            // 4. Parse configurations as AST nodes
            var configParser = _registry.GetParser(".json");
            if (configParser != null)
            {
                foreach (var cfg in configFiles)
                {
                    if (ctx.CancellationToken.IsCancellationRequested) break;
                    var cfgNodes = await configParser.ParseFileAsync(cfg.Value, ctx);
                    bool isExt = CheckIfExternalOrBoilerplate(cfg.Value);
                    foreach (var n in cfgNodes)
                    {
                        n.IsExternalOrBoilerplate = isExt;
                    }
                    map.Nodes.AddRange(cfgNodes);
                }
            }

            // 5. Run complexity analysis on all method nodes
            foreach (var node in map.Nodes)
            {
                if (node.Type == AstNodeType.Method)
                {
                    _complexityAnalyzer.AnalyzeNode(node);
                }
            }

            // 6. Build DTO class map & Dependency list using modular Analyzers
            map.Dtos.AddRange(_dtoAnalyzer.Analyze(map.Nodes.Where(n => !n.IsExternalOrBoilerplate)));
            map.Dependencies.AddRange(_dependencyAnalyzer.Analyze(map.Nodes.Where(n => !n.IsExternalOrBoilerplate)));

            // 7. Run Swagger scanner
            if (ctx.ParseSwagger)
            {
                var swaggerFiles = _swaggerScanner.FindSwaggerFiles(projectPath);
                var specs = new List<SwaggerSpec>();
                foreach (var file in swaggerFiles)
                {
                    if (ctx.CancellationToken.IsCancellationRequested) break;
                    var spec = await _swaggerScanner.ParseAsync(file);
                    specs.Add(spec);
                    map.SwaggerSpecs.Add(spec);
                }
                
                if (specs.Count > 0)
                {
                    var mergedSpec = _swaggerScanner.MergeSpecs(specs);
                    map.Endpoints.AddRange(_swaggerScanner.ToAstEndpoints(mergedSpec));
                }
            }

            // 8. Route & API scanning
            var sourceEndpoints = _apiScanner.ExtractEndpoints(map.Nodes.Where(n => !n.IsExternalOrBoilerplate));
            map.Endpoints.AddRange(sourceEndpoints);

            // Framework specific scanning
            if (map.Framework.Equals("Next.js", StringComparison.OrdinalIgnoreCase))
            {
                var nextAppRoutes = _routeScanner.ScanNextJsAppDir(projectPath);
                var nextPagesRoutes = _routeScanner.ScanNextJsPagesDir(projectPath);
                map.Endpoints.AddRange(nextAppRoutes);
                map.Endpoints.AddRange(nextPagesRoutes);
            }

            // Normalize and deduplicate final endpoints
            map.Endpoints = _routeScanner.Normalize(map.Endpoints);

            // 9. Port Scanning
            var declaredPorts = await _portScanner.ExtractDeclaredPortsAsync(projectPath);
            map.Ports.AddRange(declaredPorts);

            if (ctx.ScanPorts)
            {
                var activePorts = await _portScanner.ScanAsync("localhost", 200);
                foreach (var active in activePorts)
                {
                    var existing = map.Ports.FirstOrDefault(p => p.Port == active.Port);
                    if (existing != null)
                    {
                        existing.State = "Open";
                    }
                    else
                    {
                        map.Ports.Add(active);
                    }
                }
            }

            // 10. Graph Construction & Serialization
            BuildAndSerializeGraphs(map);
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"AST Ingestion Pipeline failed: {ex.Message}");
        }

        map.Errors.AddRange(ctx.Errors);
        return map;
    }

    private bool CheckIfExternalOrBoilerplate(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        
        string normPath = filePath.Replace('\\', '/').ToLower();
        return normPath.Contains("/.next/") || 
               normPath.Contains("/node_modules/") || 
               normPath.Contains("/venv/") || 
               normPath.Contains("/bin/") || 
               normPath.Contains("/obj/") || 
               normPath.Contains("/dist/") ||
               normPath.Contains("/build/") ||
               normPath.Contains("/__pycache__/");
    }

    private async Task WalkAndParseSourceFiles(string dir, List<AstNode> allNodes, AstContext ctx)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await WalkAndParseSourceFilesInternal(dir, allNodes, ctx, visited);
    }

    private async Task WalkAndParseSourceFilesInternal(string dir, List<AstNode> allNodes, AstContext ctx, HashSet<string> visited)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return;
        if (string.IsNullOrEmpty(dir)) return;

        // Normalize path to check for cycles
        string canonicalPath;
        try
        {
            canonicalPath = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!visited.Add(canonicalPath))
            {
                return; // Cycle detected!
            }
        }
        catch
        {
            return; // Invalid path
        }

        bool isExt = CheckIfExternalOrBoilerplate(dir);

        // Get files safely
        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Could not access files in directory '{dir}': {ex.Message}");
            return;
        }

        foreach (var file in files)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;
            try
            {
                string ext = Path.GetExtension(file);
                var parser = _registry.GetParser(ext);
                if (parser != null)
                {
                    var nodes = await parser.ParseFileAsync(file, ctx);
                    foreach (var n in nodes)
                    {
                        n.IsExternalOrBoilerplate = isExt || CheckIfExternalOrBoilerplate(file);
                    }
                    allNodes.AddRange(nodes);
                }
            }
            catch (Exception ex)
            {
                ctx.Errors.Add($"Error parsing file '{file}': {ex.Message}");
            }
        }

        // Get subdirectories safely
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dir);
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Could not access subdirectories in '{dir}': {ex.Message}");
            return;
        }

        foreach (var subDir in subDirs)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return;

            // Check if it's a symbolic link/junction
            try
            {
                var attrs = File.GetAttributes(subDir);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue; // Skip reparse points (symlinks/junctions) to prevent cycles
                }
            }
            catch
            {
                continue; // Skip inaccessible directories
            }

            string folderName = Path.GetFileName(subDir);
            if (ctx.IgnorePatterns.Contains(folderName, StringComparer.OrdinalIgnoreCase) ||
                folderName.StartsWith(".") ||
                folderName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("build", StringComparison.OrdinalIgnoreCase) ||
                folderName.Equals("venv", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await WalkAndParseSourceFilesInternal(subDir, allNodes, ctx, visited);
        }
    }

    private void BuildAndSerializeGraphs(AstProjectMap map)
    {
        try
        {
            // Build dependency graph using only source nodes (excluding boilerplate/chunks)
            var sourceNodes = map.Nodes.Where(n => !n.IsExternalOrBoilerplate).ToList();

            var depGraph = new DependencyGraph();
            depGraph.BuildFromNodes(sourceNodes);
            map.DependencyGraphDot = _graphExporter.ToDot(depGraph.Graph, "DependencyGraph");

            // Build call graph using only source nodes
            var callGraph = new CallGraph();
            var methodNodes = sourceNodes.Where(n => n.Type == AstNodeType.Method).ToList();
            foreach (var caller in methodNodes)
            {
                foreach (var callee in methodNodes)
                {
                    if (caller.Id != callee.Id && 
                        caller.Summary != null && 
                        caller.Summary.Contains(callee.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        callGraph.AddCall(caller.Id, callee.Id);
                    }
                }
            }
            map.CallGraphDot = _graphExporter.ToDot(callGraph.Graph, "CallGraph");

            // Build DAG execution layers
            var cycleFreeDag = _dagBuilder.BuildCycleFreeDag(depGraph.Graph, out _);
            map.DagFlowchartMermaid = _dagBuilder.ToMermaidFlowchart(cycleFreeDag);
        }
        catch
        {
            // Suppress graphing errors
        }
    }
}
