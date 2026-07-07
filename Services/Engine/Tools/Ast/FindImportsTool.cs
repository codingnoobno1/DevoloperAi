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
    public class FindImportsTool : IMcpTool
    {
        private readonly IEnumerable<IAstParser> _parsers;

        public FindImportsTool(IEnumerable<IAstParser> parsers)
        {
            _parsers = parsers;
        }

        public string Name => "FindImports";
        public string Description => "Parses a specific file to find all import/using directives. Uses native Roslyn for C# files.";
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
                    
                    var imports = nodes
                        .Where(n => n.Type == AstNodeType.Import)
                        .Select(n => new { 
                            Name = n.Name, 
                            Line = n.LineNumber 
                        })
                        .ToList();

                    return JsonConvert.SerializeObject(new { path = input.absolutePath, imports = imports });
                }

                // Fallback to Regex
                string content = await File.ReadAllTextAsync(input.absolutePath);
                var regex = new Regex(@"^(using|import)\s+([A-Za-z0-9_\.]+);", RegexOptions.Compiled | RegexOptions.Multiline);
                var matches = regex.Matches(content);
                
                var symbols = new List<string>();
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 2)
                    {
                        symbols.Add(match.Groups[2].Value);
                    }
                }

                return JsonConvert.SerializeObject(new { path = input.absolutePath, imports = symbols, fallback = true });
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
