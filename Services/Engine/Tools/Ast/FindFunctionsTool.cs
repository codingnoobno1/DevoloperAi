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
    public class FindFunctionsTool : IMcpTool
    {
        private readonly IEnumerable<IAstParser> _parsers;

        public FindFunctionsTool(IEnumerable<IAstParser> parsers)
        {
            _parsers = parsers;
        }

        public string Name => "FindFunctions";
        public string Description => "Parses a specific file to find all function/method definitions. Uses native Roslyn for C# files.";
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
                    
                    var functions = nodes
                        .Where(n => n.Type == AstNodeType.Method)
                        .Select(n => new { 
                            Name = n.Name, 
                            ReturnType = n.ReturnType, 
                            Parameters = n.Parameters,
                            Line = n.LineNumber 
                        })
                        .ToList();

                    return JsonConvert.SerializeObject(new { path = input.absolutePath, functions = functions });
                }

                // Fallback to Regex
                string content = await File.ReadAllTextAsync(input.absolutePath);
                var regex = new Regex(@"\b(void|int|string|bool|Task)\s+([A-Za-z0-9_]+)\s*\(", RegexOptions.Compiled);
                var matches = regex.Matches(content);
                
                var symbols = new List<string>();
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 2)
                    {
                        symbols.Add($"{match.Groups[1].Value} {match.Groups[2].Value}()");
                    }
                }

                return JsonConvert.SerializeObject(new { path = input.absolutePath, functions = symbols, fallback = true });
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
