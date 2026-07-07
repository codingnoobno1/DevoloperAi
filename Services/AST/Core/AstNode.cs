using System.Collections.Generic;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Core;

public class AstNode : IAstNode
{
    public string Id { get; set; } = "";
    public AstNodeType Type { get; set; }
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string? Namespace { get; set; }
    public string? ReturnType { get; set; }
    public List<string> Parameters { get; set; } = new();
    public List<string> Children { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<string> HttpMethods { get; set; } = new();   // e.g. GET, POST
    public string? Route { get; set; }                        // e.g. "/api/users"
    public string? Summary { get; set; }                      // XML documentation or python docstrings
    public bool IsExternalOrBoilerplate { get; set; }

    // Implement IAstNode explicitly or implicitly
    IReadOnlyList<string> IAstNode.Children => Children.AsReadOnly();
    IDictionary<string, object> IAstNode.Metadata => Metadata;
}
