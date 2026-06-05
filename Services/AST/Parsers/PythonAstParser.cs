using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Parsers;

public class PythonAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".py" };

    public async Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return Array.Empty<AstNode>();

        if (IsPythonAvailable())
        {
            try
            {
                string jsonDump = await RunPythonAstDump(filePath);
                if (!string.IsNullOrEmpty(jsonDump))
                {
                    var dumpedNodes = JsonConvert.DeserializeObject<List<PythonDumpedNode>>(jsonDump);
                    if (dumpedNodes != null)
                    {
                        var nodes = new List<AstNode>();
                        foreach (var dn in dumpedNodes)
                        {
                            var type = dn.Type == "Class" ? AstNodeType.Class : AstNodeType.Method;
                            var astNode = new AstNode
                            {
                                Id = $"{filePath}::{dn.Name}",
                                Name = dn.Name,
                                Type = type,
                                FilePath = filePath,
                                LineNumber = dn.Line,
                                Summary = $"Python {dn.Type} definition"
                            };

                            // Check decorators for FastAPI/Flask routing
                            if (dn.Decorators != null)
                            {
                                foreach (var dec in dn.Decorators)
                                {
                                    ParseDecoratorRoute(dec, astNode);
                                }
                            }

                            nodes.Add(astNode);
                        }
                        return nodes;
                    }
                }
            }
            catch (Exception ex)
            {
                ctx.Errors.Add($"Python subprocess parser failed for {filePath}, falling back to regex. Error: {ex.Message}");
            }
        }

        // Fallback: Regex Parsing
        return ParseFileHeuristic(filePath, ctx);
    }

    private void ParseDecoratorRoute(string dec, AstNode astNode)
    {
        // Matches: app.route('/path', methods=['GET', 'POST']) or app.get('/path')
        // Regex to extract path:
        var routeMatch = Regex.Match(dec, @"\.(route|get|post|put|delete)\s*\(\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        if (routeMatch.Success)
        {
            astNode.Route = routeMatch.Groups[2].Value;
            astNode.Type = AstNodeType.Route;

            string method = routeMatch.Groups[1].Value.ToUpper();
            if (method == "ROUTE")
            {
                // Parse methods array: methods=['GET', 'POST']
                var methodsMatch = Regex.Match(dec, @"methods\s*=\s*\[(.*?)\]", RegexOptions.IgnoreCase);
                if (methodsMatch.Success)
                {
                    var methods = methodsMatch.Groups[1].Value
                        .Split(',')
                        .Select(m => m.Trim('\'', '"', ' '))
                        .Where(m => !string.IsNullOrEmpty(m));
                    astNode.HttpMethods.AddRange(methods);
                }
                else
                {
                    astNode.HttpMethods.Add("GET"); // Flask default
                }
            }
            else
            {
                astNode.HttpMethods.Add(method);
            }
        }
    }

    private List<AstNode> ParseFileHeuristic(string filePath, AstContext ctx)
    {
        var nodes = new List<AstNode>();
        try
        {
            string[] lines = File.ReadAllLines(filePath);
            string? currentClass = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int lineNumber = i + 1;

                // 1. Class definition
                var classMatch = Regex.Match(line, @"^class\s+(\w+)\b");
                if (classMatch.Success)
                {
                    currentClass = classMatch.Groups[1].Value;
                    nodes.Add(new AstNode
                    {
                        Id = $"{filePath}::{currentClass}",
                        Name = currentClass,
                        Type = AstNodeType.Class,
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        Summary = "Python class definition"
                    });
                    continue;
                }

                // 2. Function definition
                var funcMatch = Regex.Match(line, @"^(async\s+)?def\s+(\w+)\b");
                if (funcMatch.Success)
                {
                    string funcName = funcMatch.Groups[2].Value;
                    string parent = currentClass != null ? $"{currentClass}::" : "";
                    
                    var astNode = new AstNode
                    {
                        Id = $"{filePath}::{parent}{funcName}",
                        Name = funcName,
                        Type = AstNodeType.Method,
                        FilePath = filePath,
                        LineNumber = lineNumber,
                        Summary = "Python function definition"
                    };

                    // Check decorators on preceding lines (up to 3 lines prior)
                    for (int j = Math.Max(0, i - 3); j < i; j++)
                    {
                        string prevLine = lines[j].Trim();
                        if (prevLine.StartsWith("@"))
                        {
                            ParseDecoratorRoute(prevLine, astNode);
                        }
                    }

                    nodes.Add(astNode);
                }
            }
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Regex parser failed for Python file {filePath}: {ex.Message}");
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

    private bool IsPythonAvailable()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "python";
            process.StartInfo.Arguments = "--version";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit(1000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> RunPythonAstDump(string filePath)
    {
        // Python helper script to parse AST and dump it as JSON
        string pyScript = @"
import ast, json, sys
def parse_node(node):
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
        decs = []
        for d in node.decorator_list:
            try:
                import astunparse
                decs.append(astunparse.unparse(d).strip())
            except:
                try:
                    decs.append(ast.unparse(d).strip())
                except:
                    if hasattr(d, 'id'): decs.append('@' + d.id)
                    elif hasattr(d, 'func') and hasattr(d.func, 'id'): decs.append('@' + d.func.id)
        return {
            'Type': 'Method',
            'Name': node.name,
            'Line': node.lineno,
            'Decorators': decs
        }
    elif isinstance(node, ast.ClassDef):
        return {
            'Type': 'Class',
            'Name': node.name,
            'Line': node.lineno,
            'Decorators': []
        }
    return None

try:
    with open(sys.argv[1], 'r', encoding='utf-8') as f:
        tree = ast.parse(f.read())
    results = []
    for node in ast.walk(tree):
        p = parse_node(node)
        if p: results.append(p)
    print(json.dumps(results))
except Exception as e:
    sys.exit(1)
";

        string tempScriptPath = Path.Combine(Path.GetTempPath(), "syncro_ast_helper.py");
        await File.WriteAllTextAsync(tempScriptPath, pyScript);

        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "python";
            process.StartInfo.Arguments = $"\"{tempScriptPath}\" \"{filePath}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return output;
        }
        finally
        {
            if (File.Exists(tempScriptPath)) File.Delete(tempScriptPath);
        }
    }

    private class PythonDumpedNode
    {
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public int Line { get; set; }
        public List<string>? Decorators { get; set; }
    }
}
