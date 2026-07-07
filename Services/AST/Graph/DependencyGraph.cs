using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Graph;

public class DependencyGraph
{
    private readonly AstGraph _graph = new();

    public AstGraph Graph => _graph;

    public void BuildFromNodes(IEnumerable<AstNode> nodes)
    {
        var nodeList = nodes.ToList();
        
        // 1. Add all nodes to graph
        foreach (var node in nodeList)
        {
            _graph.AddNode(node);
        }

        // 2. Draw dependency edges based on imports / dependencies
        foreach (var node in nodeList)
        {
            if (node.Type == Models.AstNodeType.Module)
            {
                // Find what file this module is declared in or what file imports it
                // e.g. Node ID "filePath::import::moduleName" -> "filePath" uses "moduleName"
                if (node.Id.Contains("::import::"))
                {
                    string importerFile = node.FilePath;
                    string importedModule = node.Name;
                    _graph.AddEdge(importerFile, importedModule, "imports");
                }
                else if (node.Id.Contains("::dependency::") || node.Id.Contains("::pip::"))
                {
                    _graph.AddEdge(node.FilePath, node.Name, "depends_on");
                }
            }
        }
    }

    public IReadOnlyList<string> GetDirectDeps(string moduleId)
    {
        return _graph.GetNeighbors(moduleId)
            .Select(n => n.Id)
            .ToList();
    }

    public IReadOnlyList<string> GetAllTransitiveDeps(string moduleId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        
        queue.Enqueue(moduleId);
        visited.Add(moduleId);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            var neighbors = _graph.GetNeighbors(current);
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor.Id))
                {
                    visited.Add(neighbor.Id);
                    queue.Enqueue(neighbor.Id);
                }
            }
        }

        visited.Remove(moduleId); // Remove starting node
        return visited.ToList().AsReadOnly();
    }

    public IReadOnlyList<IReadOnlyList<string>> FindCircularDeps()
    {
        var circulars = new List<List<string>>();
        var edges = _graph.GetEdges();
        
        // Simple cycle detection using Depth-First Search for each node
        var vertices = edges.Select(e => e.From).Concat(edges.Select(e => e.To)).Distinct().ToList();
        foreach (var startNode in vertices)
        {
            var path = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (DfsFindCycle(startNode, startNode, visited, path, circulars))
            {
                // Circular detected and added
            }
        }

        return circulars.Select(c => (IReadOnlyList<string>)c.AsReadOnly()).ToList().AsReadOnly();
    }

    private bool DfsFindCycle(string current, string target, HashSet<string> visited, List<string> path, List<List<string>> circulars)
    {
        visited.Add(current);
        path.Add(current);

        var neighbors = _graph.GetNeighbors(current);
        foreach (var neighbor in neighbors)
        {
            if (neighbor.Id.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                path.Add(neighbor.Id);
                // Cycle found
                if (!circulars.Any(c => c.SequenceEqual(path)))
                {
                    circulars.Add(new List<string>(path));
                }
                return true;
            }

            if (!visited.Contains(neighbor.Id))
            {
                if (DfsFindCycle(neighbor.Id, target, visited, path, circulars))
                {
                    return true;
                }
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    public string ToMermaidGraph()
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        var edges = _graph.GetEdges();
        foreach (var edge in edges)
        {
            string fromLabel = System.IO.Path.GetFileName(edge.From);
            string toLabel = System.IO.Path.GetFileName(edge.To);
            sb.AppendLine($"    {CleanId(edge.From)}[\"{fromLabel}\"] -->|{edge.EdgeType}| {CleanId(edge.To)}[\"{toLabel}\"]");
        }

        return sb.ToString();
    }

    private string CleanId(string rawId)
    {
        // Replace non-alphanumeric chars for Mermaid compatibility
        return RegexReplace(rawId, @"[^a-zA-Z0-9_]", "_");
    }

    private string RegexReplace(string input, string pattern, string replacement)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, replacement);
    }
}
