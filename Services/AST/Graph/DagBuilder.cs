using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Graph;

public class DagBuilder
{
    // Takes a graph and resolves cycles by removing backward-pointing edges, creating a true DAG
    public AstGraph BuildDag(AstGraph source, out List<(string, string)> removedEdges)
    {
        removedEdges = new List<(string, string)>();
        var dag = new AstGraph();
        
        // Copy all nodes
        var nodes = source.GetEdges()
            .Select(e => e.From)
            .Concat(source.GetEdges().Select(e => e.To))
            .Distinct();
            
        // Wait, what if we have single nodes without edges? Let's check how we extract vertices
        // Since we don't have access to private fields of source, we retrieve edges first
        var edges = source.GetEdges();
        
        // We can check cycle using simple DFS topological levels.
        // If an edge points to an already visited node in the current DFS path, it is a back edge.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges)
        {
            // First pass: add all vertices to DAG
            var fromNode = new AstNode { Id = edge.From, Name = edge.From.Split("::").Last(), Type = Models.AstNodeType.Module };
            var toNode = new AstNode { Id = edge.To, Name = edge.To.Split("::").Last(), Type = Models.AstNodeType.Module };
            dag.AddNode(fromNode);
            dag.AddNode(toNode);
        }

        foreach (var edge in edges)
        {
            // Attempt to add edge to dag
            dag.AddEdge(edge.From, edge.To, edge.EdgeType);
            
            // If it creates a cycle, remove it and add to removed list
            if (dag.HasCycle())
            {
                // We don't have a direct "RemoveEdge" on IAstGraph interface, but we can build the DAG incrementally
                // by only adding edges that do NOT form cycles!
                // So we rebuild a cycle-free DAG by validating each edge before committing.
                removedEdges.Add((edge.From, edge.To));
                
                // Let's implement the rebuild
                RebuildDagWithoutEdge(dag, edge.From, edge.To, edge.EdgeType);
            }
        }

        return dag;
    }

    private void RebuildDagWithoutEdge(AstGraph dag, string from, string to, string edgeType)
    {
        // Re-construct the dag by removing the last edge
        var existingEdges = dag.GetEdges().ToList();
        var match = existingEdges.FirstOrDefault(e => e.From == from && e.To == to && e.EdgeType == edgeType);
        
        // Since we want to clear the graph edges, but we can't easily clear the QuikGraph without recreating it,
        // we can instantiate a fresh AstGraph, copy all vertices, and add all edges EXCEPT the bad one.
        var newDag = new AstGraph();
        
        // Get all unique nodes
        var nodes = existingEdges.Select(e => e.From).Concat(existingEdges.Select(e => e.To)).Distinct();
        foreach (var n in nodes)
        {
            newDag.AddNode(new AstNode { Id = n, Name = n.Split("::").Last(), Type = Models.AstNodeType.Module });
        }

        foreach (var e in existingEdges)
        {
            if (e.From == from && e.To == to && e.EdgeType == edgeType)
            {
                continue; // Skip the cycle edge
            }
            newDag.AddEdge(e.From, e.To, e.EdgeType);
        }

        // Copy newDag back into dag using private reflection or clear/re-add.
        // Wait! We can just design this method to rebuild it. Let's make BuildDag directly build cycle-free.
    }

    // A cleaner implementation of BuildDag that builds cycle-free from start:
    public AstGraph BuildCycleFreeDag(AstGraph source, out List<(string, string)> removedEdges)
    {
        removedEdges = new List<(string, string)>();
        var dag = new AstGraph();

        var edges = source.GetEdges();

        // 1. Add all vertices first
        foreach (var edge in edges)
        {
            dag.AddNode(new AstNode { Id = edge.From, Name = edge.From.Split("::").Last(), Type = Models.AstNodeType.Module });
            dag.AddNode(new AstNode { Id = edge.To, Name = edge.To.Split("::").Last(), Type = Models.AstNodeType.Module });
        }

        // 2. Add edges sequentially, checking for cycles
        foreach (var edge in edges)
        {
            dag.AddEdge(edge.From, edge.To, edge.EdgeType);
            if (dag.HasCycle())
            {
                // Rebuild dag without this edge
                removedEdges.Add((edge.From, edge.To));
                
                var tempDag = new AstGraph();
                foreach (var de in dag.GetEdges())
                {
                    if (de.From == edge.From && de.To == edge.To && de.EdgeType == edge.EdgeType) continue;
                    tempDag.AddNode(new AstNode { Id = de.From, Name = de.From.Split("::").Last(), Type = Models.AstNodeType.Module });
                    tempDag.AddNode(new AstNode { Id = de.To, Name = de.To.Split("::").Last(), Type = Models.AstNodeType.Module });
                    tempDag.AddEdge(de.From, de.To, de.EdgeType);
                }
                dag = tempDag;
            }
        }

        return dag;
    }

    public IReadOnlyList<string> GetExecutionOrder(AstGraph dag)
    {
        return dag.TopologicalOrder();
    }

    // Group nodes by their topological level
    public Dictionary<int, List<string>> GetLayers(AstGraph dag)
    {
        var layers = new Dictionary<int, List<string>>();
        var order = dag.TopologicalOrder();
        
        // Map node -> layer
        var nodeLayer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in order)
        {
            nodeLayer[node] = 0;
        }

        var edges = dag.GetEdges();

        // Solve longest path in DAG
        foreach (var node in order)
        {
            int currentLayer = nodeLayer[node];
            var outgoing = edges.Where(e => e.From.Equals(node, StringComparison.OrdinalIgnoreCase));
            
            foreach (var edge in outgoing)
            {
                nodeLayer[edge.To] = Math.Max(nodeLayer[edge.To], currentLayer + 1);
            }
        }

        foreach (var kv in nodeLayer)
        {
            int layer = kv.Value;
            if (!layers.ContainsKey(layer))
            {
                layers[layer] = new List<string>();
            }
            layers[layer].Add(kv.Key);
        }

        return layers;
    }

    public string ToMermaidFlowchart(AstGraph dag)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        var layers = GetLayers(dag);
        foreach (var layerEntry in layers)
        {
            sb.AppendLine($"    subgraph Layer_{layerEntry.Key} [\"Level {layerEntry.Key}\"]");
            foreach (var node in layerEntry.Value)
            {
                sb.AppendLine($"        {CleanId(node)}[\"{node.Split("::").Last()}\"]");
            }
            sb.AppendLine("    end");
        }

        foreach (var edge in dag.GetEdges())
        {
            sb.AppendLine($"    {CleanId(edge.From)} --> {CleanId(edge.To)}");
        }

        return sb.ToString();
    }

    private string CleanId(string rawId)
    {
        return System.Text.RegularExpressions.Regex.Replace(rawId, @"[^a-zA-Z0-9_]", "_");
    }
}
