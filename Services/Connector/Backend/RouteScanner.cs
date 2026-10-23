using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Deterministically extracts HTTP routes from backend source across all supported ecosystems
    /// (ASP.NET Core, Next.js App/Pages Router, NestJS, Express/Fastify, FastAPI/Flask, Spring Boot,
    /// Kotlin Ktor, C++ Crow/Drogon/httplib, Rust Axum/Actix/Rocket, Go Gin/Echo/Fiber/Chi/net-http).
    /// Zero-GPU, file+line accurate.
    /// </summary>
    internal static class RouteScanner
    {
        public static List<BackendRoute> Scan(string backendPath, BackendStack stack, out List<string> notes)
        {
            notes = new List<string>();
            var routes = stack switch
            {
                BackendStack.Express => ScanExpress(backendPath).Concat(ScanNextJs(backendPath)).Concat(ScanNestJs(backendPath)).ToList(),
                BackendStack.NextJsApi => ScanNextJs(backendPath).Concat(ScanExpress(backendPath)).ToList(),
                BackendStack.NestJs => ScanNestJs(backendPath).Concat(ScanExpress(backendPath)).ToList(),
                BackendStack.FastApi => ScanDecorated(backendPath, isFlask: false),
                BackendStack.Flask or BackendStack.Django => ScanDecorated(backendPath, isFlask: true),
                BackendStack.SpringBoot => ScanSpring(backendPath).Concat(ScanKtor(backendPath)).ToList(),
                BackendStack.KotlinKtor => ScanKtor(backendPath).Concat(ScanSpring(backendPath)).ToList(),
                BackendStack.AspNetCore => ScanAspNet(backendPath),
                BackendStack.CppCrow => ScanCpp(backendPath),
                BackendStack.RustAxum => ScanRust(backendPath),
                BackendStack.GoHttp => ScanGo(backendPath),
                _ => ScanAllHeuristics(backendPath)
            };

            var deduped = Dedupe(routes);
            notes.Add($"Route scan found {deduped.Count} route(s) for stack '{stack}'.");
            return deduped;
        }

        // ── Express / Fastify — app.get('/x', …) | router.post("/x", …) | fastify.get('/x') ──
        private static readonly Regex ExpressRx = new(
            @"\b(?:app|router|server|fastify)\s*\.\s*(get|post|put|patch|delete|head|options)\s*\(\s*[""'`]([^""'`]+)[""'`]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static List<BackendRoute> ScanExpress(string root)
            => ScanWithRegex(root, new[] { ".js", ".ts", ".mjs", ".cjs" }, ExpressRx,
                verbGroup: 1, pathGroup: 2);

        // ── Next.js App Router (app/api/**/route.ts) & Pages Router (pages/api/**.ts) ──────
        private static readonly Regex NextExportVerbRx = new(
            @"\bexport\s+(?:async\s+)?(?:function|const)\s+(GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS)\b",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanNextJs(string root)
        {
            var routes = new List<BackendRoute>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".ts", ".tsx", ".js", ".jsx" }))
            {
                var fileName = Path.GetFileName(file);
                bool isAppRouter = fileName.StartsWith("route.", StringComparison.OrdinalIgnoreCase);
                string rel = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (isAppRouter)
                {
                    // e.g. src/app/api/mobile/proposals/user/[id]/route.ts -> /api/mobile/proposals/user/{id}
                    string dirRel = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? "";
                    if (dirRel.StartsWith("src/app/", StringComparison.OrdinalIgnoreCase))
                        dirRel = dirRel.Substring("src/app".Length);
                    else if (dirRel.StartsWith("app/", StringComparison.OrdinalIgnoreCase))
                        dirRel = dirRel.Substring("app".Length);
                    else if (dirRel.Equals("src/app", StringComparison.OrdinalIgnoreCase) || dirRel.Equals("app", StringComparison.OrdinalIgnoreCase))
                        dirRel = "/";

                    // Strip route groups like (auth)
                    dirRel = Regex.Replace(dirRel, @"/\([^/]+\)", "");
                    // Convert [id] or [...slug] to {id}
                    dirRel = Regex.Replace(dirRel, @"\[\.\.\.(\w+)\]", "{$1}");
                    dirRel = Regex.Replace(dirRel, @"\[(\w+)\]", "{$1}");
                    string routePath = NormalizePath(dirRel);

                    string[] lines;
                    try { lines = File.ReadAllLines(file); } catch { continue; }
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var m = NextExportVerbRx.Match(lines[i]);
                        if (!m.Success) continue;
                        routes.Add(new BackendRoute
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = routePath,
                            FilePath = file,
                            Line = i + 1,
                            Source = RouteSource.RouteScan
                        });
                    }
                }
                else if (rel.Contains("pages/api/", StringComparison.OrdinalIgnoreCase))
                {
                    int idx = rel.IndexOf("pages/api/", StringComparison.OrdinalIgnoreCase);
                    string apiRel = rel.Substring(idx + "pages".Length); // /api/...
                    apiRel = Regex.Replace(apiRel, @"\.(ts|tsx|js|jsx)$", "", RegexOptions.IgnoreCase);
                    if (apiRel.EndsWith("/index", StringComparison.OrdinalIgnoreCase))
                        apiRel = apiRel.Substring(0, apiRel.Length - "/index".Length);
                    apiRel = Regex.Replace(apiRel, @"\[(\w+)\]", "{$1}");

                    routes.Add(new BackendRoute
                    {
                        Method = HttpVerb.Get,
                        PathTemplate = NormalizePath(apiRel),
                        FilePath = file,
                        Line = 1,
                        Source = RouteSource.RouteScan
                    });
                }
            }
            return routes;
        }

        // ── NestJS — @Controller('users') + @Get(':id') ───────────────────────────────────
        private static readonly Regex NestCtrlRx = new(@"@Controller\s*\(\s*[""'`]?([^""'`)]*)[""'`]?\s*\)", RegexOptions.Compiled);
        private static readonly Regex NestVerbRx = new(@"@(Get|Post|Put|Patch|Delete)\s*\(\s*(?:[""'`]([^""'`]*)[""'`])?\s*\)", RegexOptions.Compiled);

        private static List<BackendRoute> ScanNestJs(string root)
        {
            var routes = new List<BackendRoute>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".ts" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                string prefix = "";
                for (int i = 0; i < lines.Length; i++)
                {
                    var cm = NestCtrlRx.Match(lines[i]);
                    if (cm.Success) prefix = cm.Groups[1].Value;

                    var vm = NestVerbRx.Match(lines[i]);
                    if (vm.Success)
                    {
                        var verb = vm.Groups[1].Value;
                        var sub = vm.Groups[2].Success ? vm.Groups[2].Value : "";
                        routes.Add(new BackendRoute
                        {
                            Method = verb.ToHttpVerb(),
                            PathTemplate = NormalizePath(CombineRoute(prefix, sub, "")),
                            FilePath = file,
                            Line = i + 1,
                            Source = RouteSource.RouteScan
                        });
                    }
                }
            }
            return routes;
        }

        // ── C++ Crow / Drogon / cpp-httplib ───────────────────────────────────────────────
        // Crow: CROW_ROUTE(app, "/path").methods("POST"_method) or CROW_BP_ROUTE(bp, "/path")
        private static readonly Regex CrowRouteRx = new(
            @"\bCROW_(?:BP_)?ROUTE\s*\(\s*\w+\s*,\s*""([^""]+)""\s*\)(?:\s*\.\s*methods\s*\(([^)]+)\))?",
            RegexOptions.Compiled);
        // Drogon: ADD_METHOD_TO(Ctrl::fn, "/path", Post)
        private static readonly Regex DrogonRouteRx = new(
            @"\bADD_METHOD_TO\s*\([^,]+,\s*""([^""]+)""\s*,\s*(Get|Post|Put|Patch|Delete)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // cpp-httplib: svr.Get("/path", ...) | server.Post("/path", ...)
        private static readonly Regex CppHttpLibRx = new(
            @"\b(?:svr|server|app)\s*(?:\.|->)\s*(Get|Post|Put|Patch|Delete)\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanCpp(string root)
        {
            var routes = new List<BackendRoute>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".cpp", ".cc", ".cxx", ".hpp", ".h" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // Also check next line if .methods(...) is wrapped across lines
                    var combined = i + 1 < lines.Length ? line + " " + lines[i + 1] : line;

                    var cm = CrowRouteRx.Match(combined);
                    if (cm.Success && line.Contains("CROW_", StringComparison.Ordinal))
                    {
                        var path = NormalizePath(cm.Groups[1].Value);
                        var methodsGroup = cm.Groups[2].Success ? cm.Groups[2].Value : "GET";
                        var methodMatches = Regex.Matches(methodsGroup, @"(GET|POST|PUT|PATCH|DELETE)", RegexOptions.IgnoreCase);
                        if (methodMatches.Count == 0)
                        {
                            routes.Add(new BackendRoute
                            {
                                Method = HttpVerb.Get,
                                PathTemplate = path,
                                FilePath = file,
                                Line = i + 1,
                                Source = RouteSource.RouteScan
                            });
                        }
                        else
                        {
                            foreach (Match mm in methodMatches)
                            {
                                routes.Add(new BackendRoute
                                {
                                    Method = mm.Groups[1].Value.ToHttpVerb(),
                                    PathTemplate = path,
                                    FilePath = file,
                                    Line = i + 1,
                                    Source = RouteSource.RouteScan
                                });
                            }
                        }
                    }

                    var dm = DrogonRouteRx.Match(line);
                    if (dm.Success)
                    {
                        routes.Add(new BackendRoute
                        {
                            Method = dm.Groups[2].Value.ToHttpVerb(),
                            PathTemplate = NormalizePath(dm.Groups[1].Value),
                            FilePath = file,
                            Line = i + 1,
                            Source = RouteSource.RouteScan
                        });
                    }

                    var hm = CppHttpLibRx.Match(line);
                    if (hm.Success)
                    {
                        routes.Add(new BackendRoute
                        {
                            Method = hm.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = NormalizePath(hm.Groups[2].Value),
                            FilePath = file,
                            Line = i + 1,
                            Source = RouteSource.RouteScan
                        });
                    }
                }
            }
            return routes;
        }

        // ── Rust Axum / Actix-Web / Rocket ────────────────────────────────────────────────
        // Actix / Rocket attribute: #[get("/path")], #[post("/path")]
        private static readonly Regex RustAttrRx = new(
            @"#\[\s*(get|post|put|patch|delete)\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // Axum: .route("/path", get(handler).post(handler))
        private static readonly Regex RustAxumRx = new(
            @"\.route\s*\(\s*""([^""]+)""\s*,\s*([^)]+)\)",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanRust(string root)
        {
            var routes = ScanWithRegex(root, new[] { ".rs" }, RustAttrRx, verbGroup: 1, pathGroup: 2);
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".rs" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in RustAxumRx.Matches(lines[i]))
                    {
                        var path = NormalizePath(m.Groups[1].Value);
                        var verbs = Regex.Matches(m.Groups[2].Value, @"\b(get|post|put|patch|delete)\s*\(", RegexOptions.IgnoreCase);
                        foreach (Match v in verbs)
                        {
                            routes.Add(new BackendRoute
                            {
                                Method = v.Groups[1].Value.ToHttpVerb(),
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

        // ── Go Gin / Echo / Fiber / Chi / net/http ────────────────────────────────────────
        private static readonly Regex GoRouterRx = new(
            @"\b\w+\s*\.\s*(GET|POST|PUT|PATCH|DELETE|Get|Post|Put|Patch|Delete)\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled);
        private static readonly Regex GoStdMuxRx = new(
            @"HandleFunc\s*\(\s*""(?:(GET|POST|PUT|PATCH|DELETE)\s+)?([^""]+)""",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanGo(string root)
        {
            var routes = ScanWithRegex(root, new[] { ".go" }, GoRouterRx, verbGroup: 1, pathGroup: 2);
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".go" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    var m = GoStdMuxRx.Match(lines[i]);
                    if (!m.Success) continue;
                    var verb = m.Groups[1].Success ? m.Groups[1].Value : "GET";
                    routes.Add(new BackendRoute
                    {
                        Method = verb.ToHttpVerb(),
                        PathTemplate = NormalizePath(m.Groups[2].Value),
                        FilePath = file,
                        Line = i + 1,
                        Source = RouteSource.RouteScan
                    });
                }
            }
            return routes;
        }

        // ── Kotlin Ktor — get("/path") { ... } | post("/path") { ... } ────────────────────
        private static readonly Regex KtorRx = new(
            @"^\s*(get|post|put|patch|delete)\s*\(\s*""([^""]+)""\s*\)",
            RegexOptions.Compiled);

        private static List<BackendRoute> ScanKtor(string root)
            => ScanWithRegex(root, new[] { ".kt" }, KtorRx, verbGroup: 1, pathGroup: 2);

        // ── FastAPI / Flask decorators — @app.get("/x") | @router.post("/x") ────────────
        private static readonly Regex DecoratedRx = new(
            @"@\w+\.(get|post|put|patch|delete|head|options)\s*\(\s*[""']([^""']+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

                string? pendingRoute = null;
                string classPrefix = "";
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

        // Unknown stack: try every pattern across all supported languages.
        private static List<BackendRoute> ScanAllHeuristics(string root)
        {
            var routes = new List<BackendRoute>();
            routes.AddRange(ScanExpress(root));
            routes.AddRange(ScanNextJs(root));
            routes.AddRange(ScanNestJs(root));
            routes.AddRange(ScanDecorated(root, isFlask: true));
            routes.AddRange(ScanSpring(root));
            routes.AddRange(ScanKtor(root));
            routes.AddRange(ScanAspNet(root));
            routes.AddRange(ScanCpp(root));
            routes.AddRange(ScanRust(root));
            routes.AddRange(ScanGo(root));
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
            path = Regex.Replace(path, @":([A-Za-z0-9_]+)", "{$1}");        // Express/FastAPI/Gin ":id" → "{id}"
            path = Regex.Replace(path, @"<(?:[^:>]+:)?([A-Za-z0-9_]+)>", "{$1}"); // Flask/Crow "<int>" or "<int:id>" → "{id}"
            path = path.Replace("//", "/");
            if (!path.StartsWith('/')) path = "/" + path;
            return path;
        }
    }
}
