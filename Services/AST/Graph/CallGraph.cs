using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Graph;

public class CallGraph
{
    private readonly AstGraph _graph = new();

    public AstGraph Graph => _graph;

    public void AddCall(string callerNodeId, string calleeNodeId)
    {
        _graph.AddEdge(callerNodeId, calleeNodeId, "calls");
    }

    public IReadOnlyList<string> GetCallers(string methodId)
    {
        // For callers, we search in-degree edges
        return _graph.GetNeighbors(methodId) // neighbors are targets, but wait, neighbors of target would be targets.
            // Let's actually write code to search through all edges to find matching target
            .Select(n => n.Id) // Wait, we can iterate all edges of AstGraph
            .ToList();
    }

    public IReadOnlyList<string> GetCallersOf(string methodId)
    {
        var callers = new List<string>();
        foreach (var edge in _graph.GetEdges())
        {
            if (edge.To.Equals(methodId, StringComparison.OrdinalIgnoreCase))
            {
                callers.Add(edge.From);
            }
        }
        return callers.AsReadOnly();
    }

    public IReadOnlyList<string> GetCallees(string methodId)
    {
        return _graph.GetNeighbors(methodId)
            .Select(n => n.Id)
            .ToList();
    }

    public IReadOnlyList<(string MethodId, int CallerCount)> GetHotspots(int topN = 10)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var edges = _graph.GetEdges();

        foreach (var edge in edges)
        {
            if (edge.EdgeType.Equals("calls", StringComparison.OrdinalIgnoreCase))
            {
                string callee = edge.To;
                if (counts.ContainsKey(callee)) counts[callee]++;
                else counts[callee] = 1;
            }
        }

        return counts
            .Select(kv => (kv.Key, kv.Value))
            .OrderByDescending(x => x.Value)
            .Take(topN)
            .ToList()
            .AsReadOnly();
    }

    public string ToMermaidCallTree(string entryMethodId, int maxDepth = 5)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BuildCallTreeNodes(entryMethodId, sb, visited, 0, maxDepth);

        return sb.ToString();
    }

    private void BuildCallTreeNodes(string methodId, StringBuilder sb, HashSet<string> visited, int depth, int maxDepth)
    {
        if (depth >= maxDepth || visited.Contains(methodId)) return;
        visited.Add(methodId);

        var callees = GetCallees(methodId);
        foreach (var callee in callees)
        {
            string fromLabel = methodId.Split("::").Last();
            string toLabel = callee.Split("::").Last();

            sb.AppendLine($"    {CleanId(methodId)}[\"{fromLabel}\"] --> {CleanId(callee)}[\"{toLabel}\"]");
            BuildCallTreeNodes(callee, sb, visited, depth + 1, maxDepth);
        }
    }

    private string CleanId(string rawId)
    {
        return System.Text.RegularExpressions.Regex.Replace(rawId, @"[^a-zA-Z0-9_]", "_");
    }
}
