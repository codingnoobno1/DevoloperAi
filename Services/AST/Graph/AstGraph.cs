using System;
using System.Collections.Generic;
using System.Linq;
using QuikGraph;
using QuikGraph.Algorithms;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Graph;

public class AstGraph : IAstGraph
{
    private readonly BidirectionalGraph<string, TaggedEdge<string, string>> _graph = new();
    private readonly Dictionary<string, AstNode> _nodes = new(StringComparer.OrdinalIgnoreCase);

    public void AddNode(AstNode node)
    {
        if (!_nodes.ContainsKey(node.Id))
        {
            _nodes[node.Id] = node;
            _graph.AddVertex(node.Id);
        }
    }

    public void AddEdge(string fromId, string toId, string edgeType)
    {
        // Ensure both vertices exist in graph
        if (!_nodes.ContainsKey(fromId))
        {
            AddNode(new AstNode { Id = fromId, Name = fromId.Split("::").Last(), Type = Models.AstNodeType.Module });
        }
        if (!_nodes.ContainsKey(toId))
        {
            AddNode(new AstNode { Id = toId, Name = toId.Split("::").Last(), Type = Models.AstNodeType.Module });
        }

        var edge = new TaggedEdge<string, string>(fromId, toId, edgeType);
        _graph.AddEdge(edge);
    }

    public IReadOnlyList<AstNode> GetNeighbors(string nodeId)
    {
        if (!_nodes.ContainsKey(nodeId)) return Array.Empty<AstNode>();
        
        var neighbors = new List<AstNode>();
        if (_graph.TryGetOutEdges(nodeId, out var edges))
        {
            foreach (var edge in edges)
            {
                if (_nodes.TryGetValue(edge.Target, out var neighbor))
                {
                    neighbors.Add(neighbor);
                }
            }
        }
        return neighbors;
    }

    public IReadOnlyList<(string From, string To, string EdgeType)> GetEdges()
    {
        return _graph.Edges
            .Select(e => (e.Source, e.Target, e.Tag))
            .ToList()
            .AsReadOnly();
    }

    public bool HasCycle()
    {
        // QuikGraph extension helper to check DAG
        return !_graph.IsDirectedAcyclicGraph();
    }

    public IReadOnlyList<string> TopologicalOrder()
    {
        var result = new List<string>();
        try
        {
            // Kahn's algorithm implementation for robust topological sorting
            var inDegrees = new Dictionary<string, int>();
            foreach (var vertex in _graph.Vertices)
            {
                inDegrees[vertex] = 0;
            }

            foreach (var edge in _graph.Edges)
            {
                inDegrees[edge.Target]++;
            }

            var queue = new Queue<string>();
            foreach (var vertex in inDegrees.Keys)
            {
                if (inDegrees[vertex] == 0)
                {
                    queue.Enqueue(vertex);
                }
            }

            while (queue.Count > 0)
            {
                string u = queue.Dequeue();
                result.Add(u);

                if (_graph.TryGetOutEdges(u, out var edges))
                {
                    foreach (var edge in edges)
                    {
                        inDegrees[edge.Target]--;
                        if (inDegrees[edge.Target] == 0)
                        {
                            queue.Enqueue(edge.Target);
                        }
                    }
                }
            }

            if (result.Count != _graph.VertexCount)
            {
                // Graph has a cycle, topological sort is partial. Return what we solved
            }
        }
        catch
        {
            // Fail gracefully
        }
        return result.AsReadOnly();
    }

    public string ExportDot()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("digraph AstGraph {");
        sb.AppendLine("  rankdir=LR;");
        sb.AppendLine("  node [shape=box, style=filled, fillcolor=lightblue];");

        foreach (var node in _nodes.Values)
        {
            string label = $"{node.Name}\\n({node.Type})";
            sb.AppendLine($"  \"{node.Id}\" [label=\"{label}\"];");
        }

        foreach (var edge in _graph.Edges)
        {
            sb.AppendLine($"  \"{edge.Source}\" -> \"{edge.Target}\" [label=\"{edge.Tag}\"];");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    public string ExportJson()
    {
        var adjacencyList = new Dictionary<string, List<AdjacencyEntry>>();
        foreach (var vertex in _graph.Vertices)
        {
            var list = new List<AdjacencyEntry>();
            if (_graph.TryGetOutEdges(vertex, out var edges))
            {
                foreach (var edge in edges)
                {
                    list.Add(new AdjacencyEntry { Target = edge.Target, Relationship = edge.Tag });
                }
            }
            adjacencyList[vertex] = list;
        }

        return JsonConvert.SerializeObject(new { 
            nodes = _nodes.Values.Select(n => new { n.Id, n.Name, Type = n.Type.ToString(), n.FilePath, n.LineNumber }),
            adjacency = adjacencyList 
        }, Formatting.Indented);
    }

    private class AdjacencyEntry
    {
        public string Target { get; set; } = "";
        public string Relationship { get; set; } = "";
    }
}
