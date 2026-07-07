using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Parsers;

public class JavaScriptAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".js", ".mjs", ".cjs" };

    public async Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return Array.Empty<AstNode>();

        var nodes = new List<AstNode>();
        try
        {
            string content = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);

            // 1. Express routing calls: app.get('/route', ...), router.post('/route', ...)
            // Match: (app|router|route)\.(get|post|put|delete|patch)\s*\(\s*['"`]([^'"`]+)['"`]
            var expressRegex = new Regex(@"(app|router|route)\.(get|post|put|delete|patch|use)\s*\(\s*['""]([^'""]+)['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var expressMatches = expressRegex.Matches(content);
            foreach (Match match in expressMatches)
            {
                string verb = match.Groups[2].Value.ToUpper();
                string routePath = match.Groups[3].Value;
                string id = $"{filePath}::{verb}::{routePath}";

                nodes.Add(new AstNode
                {
                    Id = id,
                    Name = $"{verb} {routePath}",
                    Type = AstNodeType.Route,
                    FilePath = filePath,
                    LineNumber = GetLineNumber(content, match.Index),
                    HttpMethods = new List<string> { verb },
                    Route = routePath,
                    Summary = $"Express/Node route definition: {verb} {routePath}"
                });
            }

            // 2. CommonJS require() dependencies
            var requireRegex = new Regex(@"require\s*\(\s*['""]([^'""]+)['""]\s*\)", RegexOptions.Compiled);
            var requireMatches = requireRegex.Matches(content);
            foreach (Match match in requireMatches)
            {
                string moduleName = match.Groups[1].Value;
                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::require::{moduleName}",
                    Name = moduleName,
                    Type = AstNodeType.Module,
                    FilePath = filePath,
                    LineNumber = GetLineNumber(content, match.Index),
                    Summary = $"CommonJS dependency: require('{moduleName}')"
                });
            }

            // 3. ESM import statements
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
                    Summary = $"ESM dependency: import from '{importPath}'"
                });
            }

            // 4. Exported functions
            // Match: export function name(...) or exports.name = ...
            var functionExportRegex = new Regex(@"export\s+(async\s+)?function\s+(\w+)\b", RegexOptions.Compiled);
            var functionMatches = functionExportRegex.Matches(content);
            foreach (Match match in functionMatches)
            {
                string funcName = match.Groups[2].Value;
                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::{funcName}",
                    Name = funcName,
                    Type = AstNodeType.Method,
                    FilePath = filePath,
                    LineNumber = GetLineNumber(content, match.Index),
                    Summary = $"Exported JS function: {funcName}"
                });
            }

            // 5. Classes & Fields (Models/DTOs)
            var classRegex = new Regex(@"^(?:export\s+(?:default\s+)?)?class\s+(\w+)\b", RegexOptions.Compiled | RegexOptions.Multiline);
            var classMatches = classRegex.Matches(content);
            foreach (Match match in classMatches)
            {
                string className = match.Groups[1].Value;
                string classId = $"{filePath}::{className}";
                nodes.Add(new AstNode
                {
                    Id = classId,
                    Name = className,
                    Type = AstNodeType.Class,
                    FilePath = filePath,
                    LineNumber = GetLineNumber(content, match.Index),
                    Summary = $"JavaScript Class {className}"
                });

                // Parse standard property assignments: this.prop = ...
                var propRegex = new Regex(@"this\.(\w+)\s*=", RegexOptions.Compiled);
                var propMatches = propRegex.Matches(content);
                var seenProps = new HashSet<string>();
                foreach (Match pm in propMatches)
                {
                    string propName = pm.Groups[1].Value;
                    if (seenProps.Add(propName))
                    {
                        nodes.Add(new AstNode
                        {
                            Id = $"{classId}::{propName}",
                            Name = propName,
                            Type = AstNodeType.Property,
                            FilePath = filePath,
                            LineNumber = GetLineNumber(content, pm.Index),
                            ReturnType = "any",
                            Summary = $"Property {propName} of class {className}"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Error parsing JS file {filePath}: {ex.Message}");
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
