using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Scanners;

public class ApiEndpointScanner
{
    public List<AstEndpoint> ExtractEndpoints(IEnumerable<AstNode> nodes)
    {
        var endpoints = new List<AstEndpoint>();
        var routeNodes = nodes.Where(n => n.Type == AstNodeType.Route || !string.IsNullOrEmpty(n.Route));

        foreach (var node in routeNodes)
        {
            string routePath = node.Route ?? "/";
            
            // Normalize path formatting
            if (!routePath.StartsWith("/")) routePath = "/" + routePath;

            var methods = node.HttpMethods.Count > 0 ? node.HttpMethods : new List<string> { "GET" };
            foreach (var method in methods)
            {
                var ep = new AstEndpoint
                {
                    Method = method,
                    Path = routePath,
                    FilePath = node.FilePath,
                    LineNumber = node.LineNumber,
                    HandlerMethod = node.Name,
                    ControllerName = Path.GetFileNameWithoutExtension(node.FilePath)
                };

                // Map parameters
                foreach (var param in node.Parameters)
                {
                    ep.Parameters.Add(param);
                }

                endpoints.Add(ep);
            }
        }

        ResolveRouteParams(endpoints, nodes);
        return endpoints;
    }

    public void ResolveRouteParams(List<AstEndpoint> endpoints, IEnumerable<AstNode> allNodes)
    {
        foreach (var ep in endpoints)
        {
            // Extract route parameters: e.g. /users/{id} or /posts/:postId
            var paramMatches = Regex.Matches(ep.Path, @"\{([a-zA-Z0-9_]+)\}");
            foreach (Match match in paramMatches)
            {
                string paramName = match.Groups[1].Value;
                // If it's not already in the parameters list, check if we can infer it
                if (!ep.Parameters.Any(p => p.EndsWith(paramName, StringComparison.OrdinalIgnoreCase)))
                {
                    // Find the matching method node to extract parameter type
                    var methodNode = allNodes.FirstOrDefault(n => n.FilePath == ep.FilePath && n.Name == ep.HandlerMethod);
                    if (methodNode != null)
                    {
                        var matchingParam = methodNode.Parameters.FirstOrDefault(p => p.Split(' ').Last().Equals(paramName, StringComparison.OrdinalIgnoreCase));
                        if (matchingParam != null)
                        {
                            ep.Parameters.Add(matchingParam);
                            continue;
                        }
                    }
                    ep.Parameters.Add($"string {paramName}"); // Fallback to string type
                }
            }
        }
    }

    public Dictionary<string, List<AstEndpoint>> GroupByController(List<AstEndpoint> endpoints)
    {
        return endpoints
            .GroupBy(e => e.ControllerName)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public string ToOpenApiYaml(List<AstEndpoint> endpoints, string title, string version = "1.0.0")
    {
        var sb = new StringBuilder();
        sb.AppendLine("openapi: 3.0.0");
        sb.AppendLine("info:");
        sb.AppendLine($"  title: {title}");
        sb.AppendLine($"  version: {version}");
        sb.AppendLine("paths:");

        var groupedByPath = endpoints.GroupBy(e => e.Path);
        foreach (var pathGroup in groupedByPath)
        {
            sb.AppendLine($"  {pathGroup.Key}:");
            foreach (var ep in pathGroup)
            {
                sb.AppendLine($"    {ep.Method.ToLower()}:");
                sb.AppendLine($"      summary: \"Exposes {ep.HandlerMethod} in {ep.ControllerName}\"");
                sb.AppendLine("      responses:");
                sb.AppendLine("        '200':");
                sb.AppendLine("          description: Successful response");

                if (ep.Parameters.Count > 0)
                {
                    sb.AppendLine("      parameters:");
                    foreach (var param in ep.Parameters)
                    {
                        var parts = param.Split(' ');
                        string type = parts.Length > 1 ? parts[0].ToLower() : "string";
                        string name = parts.Last();

                        // Map common C# types to OpenAPI types
                        if (type.Contains("int") || type.Contains("long")) type = "integer";
                        else if (type.Contains("bool")) type = "boolean";
                        else if (type.Contains("double") || type.Contains("float") || type.Contains("decimal")) type = "number";
                        else type = "string";

                        sb.AppendLine($"        - name: {name}");
                        sb.AppendLine("          in: query");
                        sb.AppendLine("          required: false");
                        sb.AppendLine("          schema:");
                        sb.AppendLine($"            type: {type}");
                    }
                }
            }
        }

        return sb.ToString();
    }
}
