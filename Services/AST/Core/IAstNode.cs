using System.Collections.Generic;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Core;

public interface IAstNode
{
    string Id { get; }           // Unique identifier: e.g. "filePath::ClassName::MethodName"
    AstNodeType Type { get; }
    string Name { get; }
    string FilePath { get; }
    int LineNumber { get; }
    IReadOnlyList<string> Children { get; }  // Child node IDs
    IDictionary<string, object> Metadata { get; }  // Key-value attributes (e.g. access modifiers, annotations)
}
