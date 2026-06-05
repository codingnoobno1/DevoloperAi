using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Buildalyzer;
using Buildalyzer.Workspaces;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Parsers;

public class CSharpAstParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".cs" };

    public async Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return Array.Empty<AstNode>();

        try
        {
            string sourceCode = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, cancellationToken: ctx.CancellationToken);
            var root = await syntaxTree.GetRootAsync(ctx.CancellationToken);
            
            var walker = new CSharpWalker(filePath, ctx);
            walker.Visit(root);
            
            return walker.Nodes;
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Error parsing C# file {filePath}: {ex.Message}");
            return Array.Empty<AstNode>();
        }
    }

    public async Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx)
    {
        var allNodes = new List<AstNode>();
        var csprojFiles = Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories);

        if (csprojFiles.Length > 0)
        {
            try
            {
                // Attempt to load workspace using Buildalyzer
                var manager = new AnalyzerManager();
                foreach (var csproj in csprojFiles)
                {
                    if (ctx.CancellationToken.IsCancellationRequested) break;
                    
                    var analyzer = manager.GetProject(csproj);
                    var workspace = analyzer.GetWorkspace();
                    
                    foreach (var project in workspace.CurrentSolution.Projects)
                    {
                        var compilation = await project.GetCompilationAsync(ctx.CancellationToken);
                        if (compilation == null) continue;

                        foreach (var doc in project.Documents)
                        {
                            if (doc.FilePath == null || !File.Exists(doc.FilePath)) continue;
                            
                            var nodes = await ParseFileAsync(doc.FilePath, ctx);
                            allNodes.AddRange(nodes);
                        }
                    }
                }
                return allNodes;
            }
            catch (Exception ex)
            {
                ctx.Errors.Add($"Buildalyzer load failed, falling back to file walker. Error: {ex.Message}");
            }
        }

        // Fallback: search files recursively
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

    // Inner SyntaxWalker to traverse classes, methods, and attributes
    private class CSharpWalker : CSharpSyntaxWalker
    {
        private readonly string _filePath;
        private readonly AstContext _ctx;
        private string? _currentNamespace;
        private string? _currentClass;
        
        public List<AstNode> Nodes { get; } = new();

        public CSharpWalker(string filePath, AstContext ctx)
        {
            _filePath = filePath;
            _ctx = ctx;
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            _currentNamespace = node.Name.ToString();
            base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            var parentNamespace = _currentNamespace;
            _currentNamespace = node.Name.ToString();
            base.VisitNamespaceDeclaration(node);
            _currentNamespace = parentNamespace;
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            string className = node.Identifier.ValueText;
            var parentClass = _currentClass;
            _currentClass = className;

            string id = $"{_filePath}::{className}";
            var astNode = new AstNode
            {
                Id = id,
                Name = className,
                Type = AstNodeType.Class,
                FilePath = _filePath,
                LineNumber = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1,
                Namespace = _currentNamespace,
                Summary = ExtractXmlDocSummary(node)
            };

            // Capture attributes (like controller routes)
            ParseRouteAttributes(node.AttributeLists, astNode);

            Nodes.Add(astNode);

            base.VisitClassDeclaration(node);
            _currentClass = parentClass;
        }

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
        {
            string interfaceName = node.Identifier.ValueText;
            string id = $"{_filePath}::{interfaceName}";
            
            var astNode = new AstNode
            {
                Id = id,
                Name = interfaceName,
                Type = AstNodeType.Interface,
                FilePath = _filePath,
                LineNumber = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1,
                Namespace = _currentNamespace,
                Summary = ExtractXmlDocSummary(node)
            };

            Nodes.Add(astNode);
            base.VisitInterfaceDeclaration(node);
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (_currentClass == null) return;

            string methodName = node.Identifier.ValueText;
            string id = $"{_filePath}::{_currentClass}::{methodName}";
            
            var astNode = new AstNode
            {
                Id = id,
                Name = methodName,
                Type = AstNodeType.Method,
                FilePath = _filePath,
                LineNumber = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1,
                Namespace = _currentNamespace,
                ReturnType = node.ReturnType.ToString(),
                Summary = ExtractXmlDocSummary(node)
            };

            // Extract parameters
            foreach (var parameter in node.ParameterList.Parameters)
            {
                string paramType = parameter.Type?.ToString() ?? "object";
                string paramName = parameter.Identifier.ValueText;
                astNode.Parameters.Add($"{paramType} {paramName}");
            }

            // Capture HTTP action attributes (HttpGet, HttpPost, Route etc.)
            ParseRouteAttributes(node.AttributeLists, astNode);

            Nodes.Add(astNode);
            base.VisitMethodDeclaration(node);
        }

        private void ParseRouteAttributes(SyntaxList<AttributeListSyntax> attributeLists, AstNode astNode)
        {
            foreach (var attrList in attributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    string attrName = attr.Name.ToString();
                    
                    // Route attributes
                    if (attrName.Equals("Route", StringComparison.OrdinalIgnoreCase) || 
                        attrName.Equals("RouteAttribute", StringComparison.OrdinalIgnoreCase))
                    {
                        var arg = attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString();
                        if (arg != null)
                        {
                            astNode.Route = arg.Trim('"');
                            astNode.Type = AstNodeType.Route; // Escalate node type to Route
                        }
                    }
                    
                    // HTTP Methods attributes
                    if (attrName.Equals("HttpGet", StringComparison.OrdinalIgnoreCase))
                    {
                        astNode.HttpMethods.Add("GET");
                        astNode.Type = AstNodeType.Route;
                        ExtractMethodSpecificRoute(attr, astNode);
                    }
                    else if (attrName.Equals("HttpPost", StringComparison.OrdinalIgnoreCase))
                    {
                        astNode.HttpMethods.Add("POST");
                        astNode.Type = AstNodeType.Route;
                        ExtractMethodSpecificRoute(attr, astNode);
                    }
                    else if (attrName.Equals("HttpPut", StringComparison.OrdinalIgnoreCase))
                    {
                        astNode.HttpMethods.Add("PUT");
                        astNode.Type = AstNodeType.Route;
                        ExtractMethodSpecificRoute(attr, astNode);
                    }
                    else if (attrName.Equals("HttpDelete", StringComparison.OrdinalIgnoreCase))
                    {
                        astNode.HttpMethods.Add("DELETE");
                        astNode.Type = AstNodeType.Route;
                        ExtractMethodSpecificRoute(attr, astNode);
                    }
                    else if (attrName.Equals("HttpPatch", StringComparison.OrdinalIgnoreCase))
                    {
                        astNode.HttpMethods.Add("PATCH");
                        astNode.Type = AstNodeType.Route;
                        ExtractMethodSpecificRoute(attr, astNode);
                    }
                }
            }
        }

        private void ExtractMethodSpecificRoute(AttributeSyntax attr, AstNode astNode)
        {
            var arg = attr.ArgumentList?.Arguments.FirstOrDefault()?.Expression.ToString();
            if (arg != null)
            {
                astNode.Route = arg.Trim('"');
            }
        }

        private string? ExtractXmlDocSummary(SyntaxNode node)
        {
            var xmlTrivia = node.GetLeadingTrivia()
                .Select(i => i.GetStructure())
                .OfType<DocumentationCommentTriviaSyntax>()
                .FirstOrDefault();

            if (xmlTrivia == null) return null;

            var summaryElement = xmlTrivia.Content
                .OfType<XmlElementSyntax>()
                .FirstOrDefault(e => e.StartTag.Name.ToString().Equals("summary", StringComparison.OrdinalIgnoreCase));

            if (summaryElement == null) return null;

            return string.Join(" ", summaryElement.Content.Select(c => c.ToString().Trim()))
                .Replace("///", "")
                .Trim();
        }
    }
}
