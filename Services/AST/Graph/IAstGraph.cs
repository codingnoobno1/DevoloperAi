using System.Collections.Generic;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Graph;

public interface IAstGraph
{
    void AddNode(AstNode node);
    void AddEdge(string fromId, string toId, string edgeType);
    IReadOnlyList<AstNode> GetNeighbors(string nodeId);
    IReadOnlyList<(string From, string To, string EdgeType)> GetEdges();
    bool HasCycle();
    IReadOnlyList<string> TopologicalOrder();
    string ExportDot();
    string ExportJson();
}
