using System.Collections.Generic;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Core;

public class AstNode : IAstNode
{
    public string Id { get; init; } = "";
    public AstNodeType Type { get; init; }
    public string Name { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string? Namespace { get; init; }
    public string? ReturnType { get; init; }
    public List<string> Parameters { get; init; } = new();
    public List<string> Children { get; init; } = new();
    public Dictionary<string, object> Metadata { get; init; } = new();
    public List<string> HttpMethods { get; init; } = new();   // e.g. GET, POST
    public string? Route { get; init; }                        // e.g. "/api/users"
    public string? Summary { get; init; }                      // XML documentation or python docstrings

    // Implement IAstNode explicitly or implicitly
    IReadOnlyList<string> IAstNode.Children => Children.AsReadOnly();
    IDictionary<string, object> IAstNode.Metadata => Metadata;
}
