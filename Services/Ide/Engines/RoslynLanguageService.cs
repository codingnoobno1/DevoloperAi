using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.CodeAnalysis.Text;
using Buildalyzer;
using Buildalyzer.Workspaces;

namespace Syncro.Desktop.Services.Ide.Engines
{
    public class RoslynLanguageService : IRoslynLanguageService
    {
        private AdhocWorkspace? _workspace;
        private string? _rootPath;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public async Task LoadWorkspaceAsync(string rootPath)
        {
            await _lock.WaitAsync();
            try
            {
                _rootPath = rootPath;
                _workspace?.Dispose();
                _workspace = new AdhocWorkspace();

                var csprojFiles = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);
                if (csprojFiles.Length > 0)
                {
                    try
                    {
                        var manager = new AnalyzerManager();
                        foreach (var csproj in csprojFiles)
                        {
                            var analyzer = manager.GetProject(csproj);
                            analyzer.AddToWorkspace(_workspace);
                        }
                    }
                    catch
                    {
                        // Fallback logic could go here
                    }
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task UpdateDocumentAsync(string uri, string content)
        {
            await _lock.WaitAsync();
            try
            {
                if (_workspace == null) return;
                string filePath = new Uri(uri).LocalPath;

                var document = _workspace.CurrentSolution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

                if (document != null)
                {
                    var newText = SourceText.From(content);
                    var newSolution = document.WithText(newText).Project.Solution;
                    _workspace.TryApplyChanges(newSolution);
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<(Document? doc, int position)> GetDocumentAndPositionAsync(string uri, int line, int column)
        {
            if (_workspace == null) return (null, 0);
            string filePath = new Uri(uri).LocalPath;

            var document = _workspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (document == null) return (null, 0);

            var text = await document.GetTextAsync();
            // line and column from Monaco are 1-based. SourceText lines are 0-based.
            if (line < 1 || line > text.Lines.Count) return (document, 0);
            
            var lineText = text.Lines[line - 1];
            // column is 1-based, we want 0-based offset within the line
            int position = lineText.Start + Math.Min(column - 1, lineText.Span.Length);
            
            return (document, position);
        }

        public async Task<IEnumerable<CompletionItemDto>> GetCompletionsAsync(string uri, int line, int column)
        {
            var results = new List<CompletionItemDto>();
            await _lock.WaitAsync();
            try
            {
                var (document, position) = await GetDocumentAndPositionAsync(uri, line, column);
                if (document == null) return results;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) return results;

                var symbols = await Recommender.GetRecommendedSymbolsAtPositionAsync(
                    semanticModel, position, _workspace!);

                foreach (var symbol in symbols.Distinct(SymbolEqualityComparer.Default).Cast<ISymbol>())
                {
                    int kind = GetMonacoCompletionItemKind(symbol);
                    results.Add(new CompletionItemDto
                    {
                        Label = symbol.Name,
                        InsertText = symbol.Name,
                        Detail = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        Kind = kind
                    });
                }
            }
            catch
            {
                // Ignore exceptions during completion
            }
            finally
            {
                _lock.Release();
            }
            return results;
        }

        public async Task<HoverDto?> GetHoverAsync(string uri, int line, int column)
        {
            await _lock.WaitAsync();
            try
            {
                var (document, position) = await GetDocumentAndPositionAsync(uri, line, column);
                if (document == null) return null;

                var semanticModel = await document.GetSemanticModelAsync();
                var root = await document.GetSyntaxRootAsync();
                if (semanticModel == null || root == null) return null;

                var token = root.FindToken(position);
                var node = token.Parent;
                if (node == null) return null;

                var symbolInfo = semanticModel.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? semanticModel.GetDeclaredSymbol(node);

                if (symbol != null)
                {
                    string doc = symbol.GetDocumentationCommentXml() ?? "";
                    string display = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    string content = $"**{display}**\n\n{doc}";
                    return new HoverDto { Content = content.Trim() };
                }
            }
            catch
            {
            }
            finally
            {
                _lock.Release();
            }
            return null;
        }

        public async Task<IEnumerable<DiagnosticDto>> GetDiagnosticsAsync(string uri)
        {
            var results = new List<DiagnosticDto>();
            await _lock.WaitAsync();
            try
            {
                if (_workspace == null) return results;
                string filePath = new Uri(uri).LocalPath;

                var document = _workspace.CurrentSolution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

                if (document == null) return results;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel == null) return results;

                var diagnostics = semanticModel.GetDiagnostics();
                foreach (var diag in diagnostics)
                {
                    var lineSpan = diag.Location.GetLineSpan();
                    if (lineSpan.Path != document.FilePath) continue;

                    results.Add(new DiagnosticDto
                    {
                        Id = diag.Id,
                        Message = diag.GetMessage(),
                        StartLineNumber = lineSpan.StartLinePosition.Line + 1,
                        StartColumn = lineSpan.StartLinePosition.Character + 1,
                        EndLineNumber = lineSpan.EndLinePosition.Line + 1,
                        EndColumn = lineSpan.EndLinePosition.Character + 1,
                        Severity = GetMonacoSeverity(diag.Severity)
                    });
                }
            }
            finally
            {
                _lock.Release();
            }
            return results;
        }

        private int GetMonacoCompletionItemKind(ISymbol symbol)
        {
            return symbol.Kind switch
            {
                SymbolKind.Method => 1, // Method
                SymbolKind.Property => 9, // Property
                SymbolKind.Field => 3, // Field
                SymbolKind.Event => 7, // Event
                SymbolKind.NamedType => ((INamedTypeSymbol)symbol).TypeKind == TypeKind.Interface ? 7 : 6, // Interface/Class
                SymbolKind.Namespace => 8, // Module
                SymbolKind.Parameter => 4, // Variable
                SymbolKind.Local => 4, // Variable
                _ => 17 // Reference
            };
        }

        private int GetMonacoSeverity(DiagnosticSeverity severity)
        {
            return severity switch
            {
                DiagnosticSeverity.Error => 8, // Error
                DiagnosticSeverity.Warning => 4, // Warning
                DiagnosticSeverity.Info => 2, // Info
                DiagnosticSeverity.Hidden => 1, // Hint
                _ => 1
            };
        }
    }
}
