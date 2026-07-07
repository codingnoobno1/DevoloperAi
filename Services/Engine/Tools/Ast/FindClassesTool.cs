using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.Engine.Tools.Ast
{
    public class FindClassesTool : IMcpTool
    {
        private readonly IEnumerable<IAstParser> _parsers;

        public FindClassesTool(IEnumerable<IAstParser> parsers)
        {
            _parsers = parsers;
        }

        public string Name => "FindClasses";
        public string Description => "Parses a specific file to find all class and struct definitions. Uses native Roslyn for C# files.";
        public string InputSchema => "{ \"absolutePath\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { absolutePath = "" });
                if (string.IsNullOrEmpty(input?.absolutePath) || !File.Exists(input.absolutePath))
                {
                    return "{ \"error\": \"Valid absolutePath is required.\" }";
                }

                string ext = Path.GetExtension(input.absolutePath);
                var parser = _parsers.FirstOrDefault(p => p.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));

                if (parser != null)
                {
                    // Use native AST Parser (e.g. Roslyn for C#)
                    var ctx = new AstContext { CancellationToken = CancellationToken.None };
                    var nodes = await parser.ParseFileAsync(input.absolutePath, ctx);
                    
                    var classes = nodes
                        .Where(n => n.Type == AstNodeType.Class || n.Type == AstNodeType.Interface)
                        .Select(n => new { Name = n.Name, Type = n.Type.ToString(), Namespace = n.Namespace, Line = n.LineNumber })
                        .ToList();

                    return JsonConvert.SerializeObject(new { path = input.absolutePath, classes = classes });
                }

                // Fallback to Regex for unsupported languages until Tree-sitter is integrated
                string content = await File.ReadAllTextAsync(input.absolutePath);
                var regex = new Regex(@"\b(class|struct)\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);
                var matches = regex.Matches(content);
                
                var symbols = new List<string>();
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 2)
                    {
                        symbols.Add($"{match.Groups[1].Value} {match.Groups[2].Value}");
                    }
                }

                return JsonConvert.SerializeObject(new { path = input.absolutePath, classes = symbols, fallback = true });
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
