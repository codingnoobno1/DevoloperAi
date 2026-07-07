using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using OxyPlot;
using OxyPlot.Series;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.AST.Graph;

public class GraphExporter
{
    // Exports graph as DOT format for Graphviz rendering
    public string ToDot(AstGraph graph, string graphName = "AstGraph")
    {
        return graph.ExportDot();
    }

    // Exports graph as adjacency list JSON
    public string ToJson(AstGraph graph)
    {
        return graph.ExportJson();
    }

    // Exports graph to Mermaid flowchart
    public string ToMermaid(AstGraph graph)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");
        
        var edges = graph.GetEdges();
        foreach (var edge in edges)
        {
            sb.AppendLine($"    {CleanId(edge.From)}[\"{CleanName(edge.From)}\"] -->|{edge.EdgeType}| {CleanId(edge.To)}[\"{CleanName(edge.To)}\"]");
        }
        return sb.ToString();
    }

    // Exports to D3.js force-directed graph format: { nodes: [ { id, name, group } ], links: [ { source, target, value } ] }
    public string ToD3Json(AstGraph graph)
    {
        var edges = graph.GetEdges();
        var uniqueNodes = edges.Select(e => e.From)
            .Concat(edges.Select(e => e.To))
            .Distinct();

        var nodesList = uniqueNodes.Select(node => new
        {
            id = node,
            name = CleanName(node),
            group = GuessNodeGroup(node)
        }).ToList();

        var linksList = edges.Select(edge => new
        {
            source = edge.From,
            target = edge.To,
            value = edge.EdgeType
        }).ToList();

        return JsonConvert.SerializeObject(new { nodes = nodesList, links = linksList }, Formatting.Indented);
    }

    // Creates an OxyPlot chart model summarizing graph node distributions
    public PlotModel ToOxyPlotModel(AstGraph graph, string title)
    {
        var plotModel = new PlotModel { Title = title };
        var pieSeries = new PieSeries
        {
            StrokeThickness = 2.0,
            InsideLabelPosition = 0.5,
            AngleSpan = 360,
            StartAngle = 0
        };

        // Classify unique nodes by their namespace or type
        var edges = graph.GetEdges();
        var uniqueNodes = edges.Select(e => e.From).Concat(edges.Select(e => e.To)).Distinct().ToList();
        
        var typeCounts = new Dictionary<string, int>();
        foreach (var node in uniqueNodes)
        {
            string group = GuessNodeGroup(node);
            if (typeCounts.ContainsKey(group)) typeCounts[group]++;
            else typeCounts[group] = 1;
        }

        foreach (var kv in typeCounts)
        {
            pieSeries.Slices.Add(new PieSlice(kv.Key, kv.Value));
        }

        plotModel.Series.Add(pieSeries);
        return plotModel;
    }

    private string CleanId(string rawId)
    {
        return System.Text.RegularExpressions.Regex.Replace(rawId, @"[^a-zA-Z0-9_]", "_");
    }

    private string CleanName(string rawId)
    {
        return rawId.Split("::").Last().Replace("\"", "\\\"");
    }

    private string GuessNodeGroup(string rawId)
    {
        if (rawId.Contains("::import::")) return "Imports";
        if (rawId.Contains("::dependency::") || rawId.Contains("::pip::")) return "Packages";
        if (rawId.Contains("::env::")) return "Env";
        if (rawId.Contains("::service::")) return "Docker";
        if (rawId.Contains("::") && rawId.Split("::").Length > 2) return "Method";
        return "Class/Module";
    }
}
