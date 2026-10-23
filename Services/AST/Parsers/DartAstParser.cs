using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Parsers
{
    public class DartAstParser : IAstParser
    {
        public IReadOnlyList<string> SupportedExtensions => new[] { ".dart" };

        public async Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx)
        {
            if (ctx.CancellationToken.IsCancellationRequested) return Array.Empty<AstNode>();

            var nodes = new List<AstNode>();
            try
            {
                var content = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);

                // Simple regex-based parsing for BLoC/Flutter patterns
                var classRegex = new Regex(@"class\s+(?<name>\w+)\s*(extends\s+(?<base>\w+(?:<[^>]+>)?))?\s*(implements\s+(?<interfaces>[^{]+))?\s*\{", RegexOptions.Compiled);
                
                var matches = classRegex.Matches(content);
                foreach (Match match in matches)
                {
                    var className = match.Groups["name"].Value;
                    var baseClass = match.Groups["base"].Success ? match.Groups["base"].Value : "";
                    
                    var type = AstNodeType.Class;
                    
                    if (baseClass.StartsWith("Cubit") || baseClass.StartsWith("Bloc"))
                        type = AstNodeType.Cubit;
                    else if (className.EndsWith("Repository") || className.EndsWith("Service") || className.EndsWith("Api"))
                        type = AstNodeType.Repository;
                    else if (baseClass == "StatelessWidget" || baseClass == "StatefulWidget")
                        type = AstNodeType.Widget;

                    nodes.Add(new AstNode
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = className,
                        Type = type,
                        FilePath = filePath,
                        LineNumber = GetLineNumber(content, match.Index)
                    });
                }
            }
            catch (Exception ex)
            {
                ctx.Errors.Add($"Error parsing Dart file {filePath}: {ex.Message}");
            }
            return nodes;
        }

        public async Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx)
        {
            var nodes = new List<AstNode>();
            var dartFiles = Directory.GetFiles(rootPath, "*.dart", SearchOption.AllDirectories)
                .Where(f => !f.Contains(".dart_tool") && !f.Contains("build\\"));

            foreach (var file in dartFiles)
            {
                if (ctx.CancellationToken.IsCancellationRequested) break;
                nodes.AddRange(await ParseFileAsync(file, ctx));
            }
            return nodes;
        }

        private int GetLineNumber(string content, int index)
        {
            return content.Take(index).Count(c => c == '\n') + 1;
        }
    }
}
