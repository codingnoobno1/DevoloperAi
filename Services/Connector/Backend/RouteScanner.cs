using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Heuristically extracts HTTP routes from backend source when no swagger contract exists.
    /// Per-stack regex passes; best-effort, file+line accurate. No request/response shapes
    /// (those come from swagger or, later, the AST) — this answers "what routes exist".
    /// </summary>
    internal static class RouteScanner
    {
        public static List<BackendRoute> Scan(string backendPath, BackendStack stack, out List<string> notes)
        {
            notes = new List<string>();
            var routes = stack switch
            {
                BackendStack.Express => ScanExpress(backendPath),
                BackendStack.FastApi => ScanDecorated(backendPath, isFlask: false),
                BackendStack.Flask => ScanDecorated(backendPath, isFlask: true),
                BackendStack.SpringBoot => ScanSpring(backendPath),
                BackendStack.AspNetCore => ScanAspNet(backendPath),
                _ => ScanAllHeuristics(backendPath)
            };

            var deduped = Dedupe(routes);
            notes.Add($"Route scan found {deduped.Count} route(s) for stack '{stack}'.");
            return deduped;
        }

        // ── Express / Node — app.get('/x', …) | router.post("/x", …) ────────────────────
        private static readonly Regex ExpressRx = new(
            @"\b(?:app|router)\s*\.\s*(get|post|put|patch|delete|head|options)\s*\(\s*[""'`]([^""'`]+)[""'`]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static List<BackendRoute> ScanExpress(string root)
            => ScanWithRegex(root, new[] { ".js", ".ts", ".mjs", ".cjs" }, ExpressRx,
                verbGroup: 1, pathGroup: 2);

        // ── FastAPI / Flask decorators — @app.get("/x") | @router.post("/x") ────────────
        private static readonly Regex DecoratedRx = new(
            @"@\w+\.(get|post|put|patch|delete|head|options)\s*\(\s*[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Flask: @app.route("/x", methods=["GET","POST"])
        private static readonly Regex FlaskRouteRx = new(
            @"@\w+\.route\s*\(\s*[""']([^""']+)[""']\s*(?:,\s*methods\s*=\s*\[([^\]]*)\])?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static List<BackendRoute> ScanDecorated(string root, bool isFlask)
        {
            var routes = ScanWithRegex(root, new[] { ".py" }, DecoratedRx, verbGroup: 1, pathGroup: 2);

            if (isFlask)
            {
                foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".py" }))
                {
                    string[] lines;
                    try { lines = File.ReadAllLines(file); } catch { continue; }

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var m = FlaskRouteRx.Match(lines[i]);
                        if (!m.Success) continue;

                        var path = NormalizePath(m.Groups[1].Value);
                        var methodsRaw = m.Groups[2].Success ? m.Groups[2].Value : "GET";
                        var methods = Regex.Matches(methodsRaw, @"[""'](\w+)[""']")
                                           .Select(x => x.Groups[1].Value)
                                           .DefaultIfEmpty("GET");

                        foreach (var method in methods)
                        {
                            routes.Add(new BackendRoute
                            {
                                Method = method.ToHttpVerb(),
                                PathTemplate = path,
                                FilePath = file,
                                Line = i + 1,
                                Source = RouteSource.RouteScan
                            });
                        }
                    }
                }
            }

            return routes;
        }

        // ── Spring Boot — @GetMapping("/x") | @RequestMapping(value="/x", method=…) ─────
        private static readonly Regex SpringRx = new(
            @"@(Get|Post|Put|Patch|Delete)Mapping\s*\(\s*(?:value\s*=\s*)?[""']([^""']+)[""']",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanSpring(string root)
        {
            var routes = new List<BackendRoute>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".java", ".kt" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    var m = SpringRx.Match(lines[i]);
                    if (!m.Success) continue;

                    routes.Add(new BackendRoute
                    {
                        Method = m.Groups[1].Value.ToHttpVerb(),
                        PathTemplate = NormalizePath(m.Groups[2].Value),
                        FilePath = file,
                        Line = i + 1,
                        Source = RouteSource.RouteScan
                    });
                }
            }
            return routes;
        }

        // ── ASP.NET — controller attribute routing + minimal API app.MapGet("/x", …) ────
        // Controller routing is stateful: a class-level [Route("api/[controller]")] prefixes the
        // method-level [HttpGet("id")] and the [controller]/[action] tokens must be expanded — the
        // old attribute-only scan missed all of that (and any [HttpGet] with no explicit template).
        private static readonly Regex AspClassRouteRx = new(
            @"\[Route\s*\(\s*[""']([^""']+)[""']",
            RegexOptions.Compiled);

        private static readonly Regex AspControllerClassRx = new(
            @"\bclass\s+(\w+?)Controller\b",
            RegexOptions.Compiled);

        private static readonly Regex AspHttpRx = new(
            @"\[Http(Get|Post|Put|Patch|Delete)(?:\s*\(\s*[""']([^""']*)[""']\s*\))?\]",
            RegexOptions.Compiled);

        private static readonly Regex AspMinimalRx = new(
            @"\.Map(Get|Post|Put|Patch|Delete)\s*\(\s*[""']([^""']+)[""']",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanAspNet(string root)
        {
            var routes = new List<BackendRoute>();

            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".cs" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                string? pendingRoute = null;   // last [Route("...")] seen, awaiting its class
                string classPrefix = "";       // resolved controller-level prefix
                string controller = "";

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    var cr = AspClassRouteRx.Match(line);
                    if (cr.Success) pendingRoute = cr.Groups[1].Value;

                    var cc = AspControllerClassRx.Match(line);
                    if (cc.Success)
                    {
                        controller = cc.Groups[1].Value;
                        classPrefix = (pendingRoute ?? "").Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase);
                        pendingRoute = null;
                    }

                    var hm = AspHttpRx.Match(line);
                    if (hm.Success)
                    {
                        var verb = hm.Groups[1].Value;
                        var sub = hm.Groups[2].Success ? hm.Groups[2].Value : "";
                        routes.Add(new BackendRoute
                        {
                            Method = verb.ToHttpVerb(),
                            PathTemplate = NormalizePath(CombineRoute(classPrefix, sub, controller)),
                            FilePath = file,
                            Line = i + 1,
                            Source = RouteSource.RouteScan
                        });
                    }
                }
            }

            routes.AddRange(ScanWithRegex(root, new[] { ".cs" }, AspMinimalRx, verbGroup: 1, pathGroup: 2));
            return routes;
        }

        private static string CombineRoute(string prefix, string sub, string controller)
        {
            sub = (sub ?? "").Replace("[controller]", controller, StringComparison.OrdinalIgnoreCase).Replace("[action]", "");
            prefix = (prefix ?? "").Replace("[action]", "");
            if (sub.StartsWith('/')) return sub;
            if (string.IsNullOrEmpty(sub)) return prefix;
            if (string.IsNullOrEmpty(prefix)) return sub;
            return prefix.TrimEnd('/') + "/" + sub.TrimStart('/');
        }

        // Unknown stack: try every pattern across common extensions.
        private static List<BackendRoute> ScanAllHeuristics(string root)
        {
            var routes = new List<BackendRoute>();
            routes.AddRange(ScanExpress(root));
            routes.AddRange(ScanDecorated(root, isFlask: true));
            routes.AddRange(ScanSpring(root));
            routes.AddRange(ScanAspNet(root));
            return routes;
        }

        // ── shared regex pass ───────────────────────────────────────────────────────────
        private static List<BackendRoute> ScanWithRegex(
            string root, string[] extensions, Regex rx, int verbGroup, int pathGroup)
        {
            var routes = new List<BackendRoute>();
            foreach (var file in SourceWalker.EnumerateFiles(root, extensions))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in rx.Matches(lines[i]))
                    {
                        if (!m.Success) continue;
                        routes.Add(new BackendRoute
                        {
                            Method = m.Groups[verbGroup].Value.ToHttpVerb(),
                            PathTemplate = NormalizePath(m.Groups[pathGroup].Value),
                            FilePath = file,
                            Line = i + 1,
                            Source = RouteSource.RouteScan
                        });
                    }
                }
            }
            return routes;
        }

        private static List<BackendRoute> Dedupe(List<BackendRoute> routes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<BackendRoute>();
            foreach (var r in routes)
            {
                if (seen.Add(r.Id))
                    result.Add(r);
            }
            return result;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "/";
            path = Regex.Replace(path, @"\{(\w+)(?::[^}{]+)?\}", "{$1}");  // ASP.NET "{id:int}" → "{id}"
            path = Regex.Replace(path, @":([A-Za-z0-9_]+)", "{$1}");        // Express/FastAPI ":id" → "{id}"
            path = Regex.Replace(path, @"<(?:[^:>]+:)?([A-Za-z0-9_]+)>", "{$1}"); // Flask "<int:id>" → "{id}"
            path = path.Replace("//", "/");
            if (!path.StartsWith('/')) path = "/" + path;
            return path;
        }
    }
}
