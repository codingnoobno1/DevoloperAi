using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Models;
using Syncro.Desktop.Services.AST.Scanners;

namespace Syncro.Desktop.Services.AST.Core;

public class AstEngine
{
    private readonly AstRegistry _registry;
    private readonly PortScanner _portScanner;
    private readonly ApiEndpointScanner _apiScanner;
    private readonly SwaggerScanner _swaggerScanner;
    private readonly RouteScanner _routeScanner;
    private readonly ConfigScanner _configScanner;

    public AstEngine(
        AstRegistry registry,
        PortScanner portScanner,
        ApiEndpointScanner apiScanner,
        SwaggerScanner swaggerScanner,
        RouteScanner routeScanner,
        ConfigScanner configScanner)
    {
        _registry = registry;
        _portScanner = portScanner;
        _apiScanner = apiScanner;
        _swaggerScanner = swaggerScanner;
        _routeScanner = routeScanner;
        _configScanner = configScanner;
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

            // 2. Detect Language & Framework
            DetectLanguageAndFramework(map, ctx);

            // 3. Walk & Parse source files recursively
            var allSourceNodes = new List<AstNode>();
            await WalkAndParseSourceFiles(projectPath, allSourceNodes, ctx);
            map.Nodes.AddRange(allSourceNodes);

            // 4. Parse configurations as AST nodes (dependencies, script nodes, connection string metadata)
            var configParser = _registry.GetParser(".json"); // ConfigFileParser handles JSON/Yaml/txt/env
            if (configParser != null)
            {
                foreach (var cfg in configFiles)
                {
                    if (ctx.CancellationToken.IsCancellationRequested) break;
                    var cfgNodes = await configParser.ParseFileAsync(cfg.Value, ctx);
                    map.Nodes.AddRange(cfgNodes);
                }
            }

            // 5. Build DTO class map & Dependency list from parsed nodes
            ExtractDtosAndDependencies(map);

            // 6. Run Swagger scanner
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

            // 7. Route & API scanning
            var sourceEndpoints = _apiScanner.ExtractEndpoints(map.Nodes);
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

            // 8. Port Scanning
            var declaredPorts = await _portScanner.ExtractDeclaredPortsAsync(projectPath);
            map.Ports.AddRange(declaredPorts);

            if (ctx.ScanPorts)
            {
                var activePorts = await _portScanner.ScanAsync("localhost", 200);
                // Merge active ports with declared ports
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
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"AST Ingestion Pipeline failed: {ex.Message}");
        }

        map.Errors.AddRange(ctx.Errors);
        return map;
    }

    private void DetectLanguageAndFramework(AstProjectMap map, AstContext ctx)
    {
        // Guess language based on config files
        if (map.ConfigFiles.Keys.Any(k => k.Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            map.Language = "TypeScript/JavaScript";
            ctx.ProjectLanguage = "typescript";
            
            // Check framework
            if (Directory.Exists(Path.Combine(map.ProjectPath, "app")) || Directory.Exists(Path.Combine(map.ProjectPath, "pages")))
            {
                map.Framework = "Next.js";
                ctx.Framework = "nextjs";
            }
            else
            {
                map.Framework = "Express/Node";
                ctx.Framework = "express";
            }
        }
        else if (map.ConfigFiles.Keys.Any(k => k.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            map.Language = "C#";
            ctx.ProjectLanguage = "csharp";
            map.Framework = "ASP.NET Core";
            ctx.Framework = "aspnet";
        }
        else if (map.ConfigFiles.Keys.Any(k => k.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || k.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)))
        {
            map.Language = "Python";
            ctx.ProjectLanguage = "python";
            map.Framework = "Flask/FastAPI";
            ctx.Framework = "fastapi";
        }
        else
        {
            // Default heuristics based on file extensions
            map.Language = "Unknown";
            ctx.ProjectLanguage = "unknown";
            map.Framework = "Generic";
            ctx.Framework = "generic";
        }
    }

    private async Task WalkAndParseSourceFiles(string dir, List<AstNode> allNodes, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        foreach (var file in Directory.GetFiles(dir))
        {
            string ext = Path.GetExtension(file);
            var parser = _registry.GetParser(ext);
            if (parser != null)
            {
                var nodes = await parser.ParseFileAsync(file, ctx);
                allNodes.AddRange(nodes);
            }
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            string folderName = Path.GetFileName(subDir);
            if (ctx.IgnorePatterns.Contains(folderName, StringComparer.OrdinalIgnoreCase)) continue;
            await WalkAndParseSourceFiles(subDir, allNodes, ctx);
        }
    }

    private void ExtractDtosAndDependencies(AstProjectMap map)
    {
        foreach (var node in map.Nodes)
        {
            // Extract DTOs
            if (node.Type == AstNodeType.Dto || node.Name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) || node.Name.EndsWith("Model", StringComparison.OrdinalIgnoreCase))
            {
                // Ensure class/interface from source files is registered
                if (node.Type == AstNodeType.Class || node.Type == AstNodeType.Interface || node.Type == AstNodeType.Dto)
                {
                    if (!map.Dtos.Any(d => d.Name == node.Name && d.FilePath == node.FilePath))
                    {
                        var dto = new AstDtoModel
                        {
                            Name = node.Name,
                            FilePath = node.FilePath,
                            LineNumber = node.LineNumber
                        };
                        
                        // Extract class properties
                        var childProps = map.Nodes.Where(n => n.FilePath == node.FilePath && n.Type == AstNodeType.Property && n.Id.StartsWith(node.Id));
                        foreach (var prop in childProps)
                        {
                            dto.Properties[prop.Name] = prop.ReturnType ?? "object";
                        }

                        map.Dtos.Add(dto);
                    }
                }
            }

            // Extract packages/dependencies from configuration Nodes
            if (node.Type == AstNodeType.Module && node.Id.Contains("::dependency::") || node.Id.Contains("::pip::"))
            {
                string type = node.Id.Contains("::dependency::") ? "npm" : "pip";
                string version = node.Metadata.TryGetValue("version", out var v) ? v.ToString() ?? "" : "";
                
                if (!map.Dependencies.Any(d => d.Name == node.Name))
                {
                    map.Dependencies.Add(new AstDependencyInfo
                    {
                        Name = node.Name,
                        Version = version,
                        Type = type,
                        IsTransitive = false
                    });
                }
            }
        }
    }
}
