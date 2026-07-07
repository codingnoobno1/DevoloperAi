using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Parsers;

public class TypeScriptAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".ts", ".tsx" };

    public async Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return Array.Empty<AstNode>();

        var nodes = new List<AstNode>();
        try
        {
            string content = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);
            string normalizedPath = filePath.Replace("\\", "/");
            
            // Check if it's a Next.js App Router route file
            bool isNextJsRoute = normalizedPath.Contains("/app/") && 
                                 (normalizedPath.EndsWith("route.ts") || normalizedPath.EndsWith("route.tsx") || normalizedPath.EndsWith("page.tsx") || normalizedPath.EndsWith("page.ts"));
            
            bool isRouteFile = normalizedPath.EndsWith("route.ts") || normalizedPath.EndsWith("route.tsx");
            bool isPageFile = normalizedPath.EndsWith("page.tsx") || normalizedPath.EndsWith("page.ts");

            string? nextRoute = null;
            if (isRouteFile || isPageFile)
            {
                nextRoute = DeriveNextJsRoute(normalizedPath, ctx.RootPath.Replace("\\", "/"));
            }

            // 1. Classes, Interfaces, Types & their properties/fields (DTOs/Models)
            string[] lines = content.Split('\n');
            string? currentScopeId = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                
                // Track start of interface/class/type
                var scopeMatch = Regex.Match(line, @"export\s+(interface|type|class)\s+(\w+)\b");
                if (scopeMatch.Success)
                {
                    string kind = scopeMatch.Groups[1].Value;
                    string name = scopeMatch.Groups[2].Value;
                    currentScopeId = $"{filePath}::{name}";
                    
                    nodes.Add(new AstNode
                    {
                        Id = currentScopeId,
                        Name = name,
                        Type = kind == "class" ? AstNodeType.Class : AstNodeType.Dto,
                        FilePath = filePath,
                        LineNumber = i + 1,
                        Summary = $"TypeScript {kind} {name}"
                    });
                    continue;
                }

                // Track end of scope
                if (line == "}" || line == "};")
                {
                    currentScopeId = null;
                    continue;
                }

                // If inside a class/interface/type scope, parse fields/properties
                if (currentScopeId != null)
                {
                    var propMatch = Regex.Match(line, @"^(?:public|private|protected|readonly\s+)?(\w+)\??\s*:\s*([^;=]+)");
                    if (propMatch.Success)
                    {
                        string propName = propMatch.Groups[1].Value;
                        string propType = propMatch.Groups[2].Value.Trim();
                        
                        // Exclude keywords
                        if (propName != "export" && propName != "import" && propName != "return" && propName != "class" && propName != "interface" && propName != "type")
                        {
                            nodes.Add(new AstNode
                            {
                                Id = $"{currentScopeId}::{propName}",
                                Name = propName,
                                Type = AstNodeType.Property,
                                FilePath = filePath,
                                LineNumber = i + 1,
                                ReturnType = propType,
                                Summary = $"Property {propName} of type {propType}"
                            });
                        }
                    }
                }
            }

            // 2. Next.js Route handlers (GET, POST, etc. exported functions in route.ts)
            if (isRouteFile)
            {
                // Match: export async function GET(...) or export function POST(...)
                var handlerRegex = new Regex(@"export\s+(async\s+)?function\s+(GET|POST|PUT|DELETE|PATCH)\b", RegexOptions.Compiled);
                var handlerMatches = handlerRegex.Matches(content);
                foreach (Match match in handlerMatches)
                {
                    string httpMethod = match.Groups[2].Value;
                    string id = $"{filePath}::{httpMethod}";
                    
                    nodes.Add(new AstNode
                    {
                        Id = id,
                        Name = httpMethod,
                        Type = AstNodeType.Route,
                        FilePath = filePath,
                        LineNumber = GetLineNumber(content, match.Index),
                        HttpMethods = new List<string> { httpMethod },
                        Route = nextRoute,
                        Summary = $"Next.js API Handler for {httpMethod} {nextRoute}"
                    });
                }
            }

            // 3. Next.js Page view
            if (isPageFile)
            {
                var defaultExportRegex = new Regex(@"export\s+default\s+(async\s+)?function\s+(\w+)\b", RegexOptions.Compiled);
                var defaultMatch = defaultExportRegex.Match(content);
                if (defaultMatch.Success)
                {
                    string componentName = defaultMatch.Groups[2].Value;
                    nodes.Add(new AstNode
                    {
                        Id = $"{filePath}::{componentName}",
                        Name = componentName,
                        Type = AstNodeType.Route,
                        FilePath = filePath,
                        LineNumber = GetLineNumber(content, defaultMatch.Index),
                        HttpMethods = new List<string> { "GET" },
                        Route = nextRoute,
                        Summary = $"Next.js Page Component rendering {nextRoute}"
                    });
                }
            }

            // 4. Imports (Dependencies)
            var importRegex = new Regex(@"import\s+.*?\s+from\s+['""]([^'""]+)['""]", RegexOptions.Compiled);
            var importMatches = importRegex.Matches(content);
            foreach (Match match in importMatches)
            {
                string importPath = match.Groups[1].Value;
                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::import::{importPath}",
                    Name = importPath,
                    Type = AstNodeType.Module,
                    FilePath = filePath,
                    LineNumber = GetLineNumber(content, match.Index),
                    Summary = $"Imports {importPath}"
                });
            }
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Error parsing TS file {filePath}: {ex.Message}");
        }

        return nodes;
    }

    public async Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx)
    {
        var allNodes = new List<AstNode>();
        await WalkDirectoryAsync(rootPath, allNodes, ctx);
        return allNodes;
    }

    private async Task WalkDirectoryAsync(string dir, List<AstNode> allNodes, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return;

        foreach (var file in Directory.GetFiles(dir))
        {
            string ext = Path.GetExtension(file);
            if (SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                var nodes = await ParseFileAsync(file, ctx);
                allNodes.AddRange(nodes);
            }
        }

        foreach (var subDir in Directory.GetDirectories(dir))
        {
            string folderName = Path.GetFileName(subDir);
            if (ctx.IgnorePatterns.Contains(folderName, StringComparer.OrdinalIgnoreCase)) continue;
            await WalkDirectoryAsync(subDir, allNodes, ctx);
        }
    }

    private string DeriveNextJsRoute(string filePath, string rootPath)
    {
        // Force forward slashes
        filePath = filePath.Replace("\\", "/");
        rootPath = rootPath.Replace("\\", "/").TrimEnd('/');

        int appIndex = filePath.IndexOf("/app/", StringComparison.OrdinalIgnoreCase);
        if (appIndex == -1) return "/";

        string relativeToApp = filePath.Substring(appIndex + 5); // Strip up to "/app/"
        
        // Remove /route.ts, /route.tsx, /page.tsx, /page.ts
        string route = relativeToApp;
        string[] stripSuffixes = { "/route.ts", "/route.tsx", "/page.tsx", "/page.ts" };
        foreach (var suffix in stripSuffixes)
        {
            if (route.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                route = route.Substring(0, route.Length - suffix.Length);
                break;
            }
        }

        if (string.IsNullOrEmpty(route) || route.Equals("route.ts") || route.Equals("route.tsx") || route.Equals("page.tsx") || route.Equals("page.ts"))
        {
            return "/";
        }

        // Handle path parameters e.g. [id] -> {id}
        route = route.Replace("[", "{").Replace("]", "}");

        return "/" + route.Trim('/');
    }

    private int GetLineNumber(string content, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n') line++;
        }
        return line;
    }
}
