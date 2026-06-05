using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Scanners;

public class RouteScanner
{
    // Next.js App Router (app/ directory page/route conventions)
    public List<AstEndpoint> ScanNextJsAppDir(string rootPath)
    {
        var endpoints = new List<AstEndpoint>();
        string appPath = Path.Combine(rootPath, "app");
        if (!Directory.Exists(appPath)) return endpoints;

        // Find all page.tsx, page.ts, route.ts, route.tsx files
        var routeFiles = Directory.GetFiles(appPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith("page.tsx") || f.EndsWith("page.ts") || f.EndsWith("route.ts") || f.EndsWith("route.tsx"));

        foreach (var file in routeFiles)
        {
            string normalized = file.Replace("\\", "/");
            string relative = normalized.Substring(appPath.Replace("\\", "/").Length);
            
            // Deduce path route
            string route = relative;
            string[] suffixes = { "/route.ts", "/route.tsx", "/page.tsx", "/page.ts" };
            foreach (var suffix in suffixes)
            {
                if (route.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    route = route.Substring(0, route.Length - suffix.Length);
                    break;
                }
            }

            if (string.IsNullOrEmpty(route) || route.Equals("page.tsx") || route.Equals("page.ts") || route.Equals("route.ts") || route.Equals("route.tsx"))
            {
                route = "/";
            }
            else
            {
                route = "/" + route.Trim('/');
            }

            // Handle path params e.g. [id] -> {id}
            route = route.Replace("[", "{").Replace("]", "}");

            bool isRoute = file.EndsWith("route.ts") || file.EndsWith("route.tsx");
            if (isRoute)
            {
                // In app/api/route.ts, HTTP methods are async functions (GET, POST, etc.)
                try
                {
                    string content = File.ReadAllText(file);
                    var matches = Regex.Matches(content, @"export\s+(async\s+)?function\s+(GET|POST|PUT|DELETE|PATCH)\b");
                    foreach (Match m in matches)
                    {
                        endpoints.Add(new AstEndpoint
                        {
                            Method = m.Groups[2].Value,
                            Path = route,
                            FilePath = file,
                            ControllerName = "NextJsAppRoute",
                            HandlerMethod = m.Groups[2].Value
                        });
                    }
                }
                catch
                {
                    // Fail gracefully
                }
            }
            else
            {
                // Page defaults to GET
                endpoints.Add(new AstEndpoint
                {
                    Method = "GET",
                    Path = route,
                    FilePath = file,
                    ControllerName = "NextJsAppPage",
                    HandlerMethod = "default"
                });
            }
        }

        return Normalize(endpoints);
    }

    // Next.js Pages Router (pages/api/ directory conventions)
    public List<AstEndpoint> ScanNextJsPagesDir(string rootPath)
    {
        var endpoints = new List<AstEndpoint>();
        string pagesPath = Path.Combine(rootPath, "pages");
        if (!Directory.Exists(pagesPath)) return endpoints;

        var apiFiles = Directory.GetFiles(pagesPath, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ts") || f.EndsWith(".tsx") || f.EndsWith(".js") || f.EndsWith(".jsx"));

        foreach (var file in apiFiles)
        {
            string normalized = file.Replace("\\", "/");
            string relative = normalized.Substring(pagesPath.Replace("\\", "/").Length);
            
            // Deduce path route
            string route = relative;
            string ext = Path.GetExtension(route);
            route = route.Substring(0, route.Length - ext.Length); // Remove extension
            
            if (route.EndsWith("/index", StringComparison.OrdinalIgnoreCase))
            {
                route = route.Substring(0, route.Length - 6);
            }
            route = "/" + route.Trim('/');

            // Handle [param] to {param}
            route = route.Replace("[", "{").Replace("]", "}");

            // Default to exposing GET, POST etc. since it's a generic handler
            bool isApi = route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
            if (isApi)
            {
                endpoints.Add(new AstEndpoint { Method = "GET", Path = route, FilePath = file, ControllerName = "NextJsPagesApi", HandlerMethod = "handler" });
                endpoints.Add(new AstEndpoint { Method = "POST", Path = route, FilePath = file, ControllerName = "NextJsPagesApi", HandlerMethod = "handler" });
            }
            else
            {
                endpoints.Add(new AstEndpoint { Method = "GET", Path = route, FilePath = file, ControllerName = "NextJsPagesView", HandlerMethod = "default" });
            }
        }

        return Normalize(endpoints);
    }

    // Express: scan routing calls in JavaScript nodes
    public List<AstEndpoint> ScanExpressRoutes(string rootPath, List<AstNode> jsNodes)
    {
        var endpoints = new List<AstEndpoint>();
        var expressNodes = jsNodes.Where(n => n.Type == AstNodeType.Route && !string.IsNullOrEmpty(n.Route));
        
        foreach (var node in expressNodes)
        {
            var methods = node.HttpMethods.Count > 0 ? node.HttpMethods : new List<string> { "GET" };
            foreach (var method in methods)
            {
                endpoints.Add(new AstEndpoint
                {
                    Method = method,
                    Path = node.Route ?? "/",
                    FilePath = node.FilePath,
                    LineNumber = node.LineNumber,
                    ControllerName = "ExpressRouter",
                    HandlerMethod = node.Name
                });
            }
        }

        return Normalize(endpoints);
    }

    // Flask/FastAPI: scan python blueprint/route nodes
    public List<AstEndpoint> ScanPythonRoutes(string rootPath, List<AstNode> pyNodes)
    {
        var endpoints = new List<AstEndpoint>();
        var pyRoutes = pyNodes.Where(n => n.Type == AstNodeType.Route && !string.IsNullOrEmpty(n.Route));

        foreach (var node in pyRoutes)
        {
            var methods = node.HttpMethods.Count > 0 ? node.HttpMethods : new List<string> { "GET" };
            foreach (var method in methods)
            {
                endpoints.Add(new AstEndpoint
                {
                    Method = method,
                    Path = node.Route ?? "/",
                    FilePath = node.FilePath,
                    LineNumber = node.LineNumber,
                    ControllerName = "PythonController",
                    HandlerMethod = node.Name
                });
            }
        }

        return Normalize(endpoints);
    }

    // ASP.NET Core conventional / attribute routing nodes
    public List<AstEndpoint> ScanAspNetRoutes(string rootPath, List<AstNode> csNodes)
    {
        var endpoints = new List<AstEndpoint>();
        var csRoutes = csNodes.Where(n => n.Type == AstNodeType.Route && !string.IsNullOrEmpty(n.Route));

        foreach (var node in csRoutes)
        {
            var methods = node.HttpMethods.Count > 0 ? node.HttpMethods : new List<string> { "GET" };
            foreach (var method in methods)
            {
                endpoints.Add(new AstEndpoint
                {
                    Method = method,
                    Path = node.Route ?? "/",
                    FilePath = node.FilePath,
                    LineNumber = node.LineNumber,
                    ControllerName = node.Namespace ?? "AspNetController",
                    HandlerMethod = node.Name
                });
            }
        }

        return Normalize(endpoints);
    }

    // Deduplicate and normalize routes (lowercase, strip trailing slashes, remove doubles)
    public List<AstEndpoint> Normalize(List<AstEndpoint> endpoints)
    {
        var normalizedList = new List<AstEndpoint>();
        foreach (var ep in endpoints)
        {
            string cleanPath = ep.Path.Trim();
            if (cleanPath != "/") cleanPath = cleanPath.TrimEnd('/');
            
            cleanPath = Regex.Replace(cleanPath, @"//+", "/"); // double slash to single
            if (!cleanPath.StartsWith("/")) cleanPath = "/" + cleanPath;

            // Normalize path parameter syntax: :id to {id}
            cleanPath = Regex.Replace(cleanPath, @":([a-zA-Z0-9_]+)", "{$1}");

            if (!normalizedList.Any(e => e.Path.Equals(cleanPath, StringComparison.OrdinalIgnoreCase) && e.Method.Equals(ep.Method, StringComparison.OrdinalIgnoreCase)))
            {
                ep.Path = cleanPath;
                normalizedList.Add(ep);
            }
        }
        return normalizedList;
    }
}
