using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Frontend
{
    /// <summary>
    /// Heuristic, regex-based extraction of HTTP calls from frontend source, one pass per stack.
    /// Best-effort and file+line accurate. No request/response shapes yet (those need real AST) —
    /// this answers "which endpoints does the frontend call". Paths are normalized through
    /// <see cref="PathNormalizer"/> so they line up with backend routes.
    /// </summary>
    internal static class FrontendApiExtractors
    {
        // ── Flutter: dio.get('/x') | _dio.post<T>('/x', ...) | http.get(Uri.parse('...')) ──
        private static readonly Regex FlutterRx = new(
            @"\b(dio|_dio|client|_client|http|_http)\s*\.\s*(get|post|put|patch|delete)\s*(?:<[^>]*>)?\s*\(\s*(?:Uri\.parse\(\s*)?[`'""]([^`'""]+)[`'""]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<ApiCallSite> Flutter(string root) =>
            ScanReceiverVerb(root, new[] { ".dart" }, FlutterRx,
                receiverGroup: 1, verbGroup: 2, pathGroup: 3,
                clientOf: r => r.Equals("http", StringComparison.OrdinalIgnoreCase) || r.Equals("_http", StringComparison.OrdinalIgnoreCase) ? "http" : "dio");

        // ── Web: axios.get('/x') | api.post('/x', body) | client.put<T>('/x') ──────────────
        private static readonly Regex AxiosRx = new(
            @"\b(axios|api|http|client|instance|_api|apiClient)\s*\.\s*(get|post|put|patch|delete)\s*(?:<[^>]*>)?\s*\(\s*[`'""]([^`'""]+)[`'""]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // fetch('/x')  — method is optional and may appear in an options object on the same line.
        private static readonly Regex FetchRx = new(
            @"\bfetch\s*\(\s*[`'""]([^`'""]+)[`'""]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FetchMethodRx = new(
            @"method\s*:\s*[`'""](\w+)[`'""]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<ApiCallSite> Web(string root)
        {
            var calls = ScanReceiverVerb(root, WebExtensions, AxiosRx,
                receiverGroup: 1, verbGroup: 2, pathGroup: 3, clientOf: _ => "axios");

            // fetch pass — needs custom method handling.
            foreach (var file in SourceWalker.EnumerateFiles(root, WebExtensions))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in FetchRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[1].Value;
                        if (!LooksLikePath(rawPath)) continue;

                        var methodMatch = FetchMethodRx.Match(lines[i]);
                        var verb = (methodMatch.Success ? methodMatch.Groups[1].Value : "GET").ToHttpVerb();

                        calls.Add(new ApiCallSite
                        {
                            Method = verb,
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "fetch"
                        });
                    }
                }
            }

            return calls;
        }

        // ── Blazor / .NET: http.GetAsync("/x") | _http.PostAsJsonAsync("/x", body)
        //                    | GetFromJsonAsync<T>("/x") ─────────────────────────────────────
        private static readonly Regex HttpClientRx = new(
            @"\.\s*(Get|Post|Put|Patch|Delete)(?:Async|FromJsonAsync|AsJsonAsync)?\s*(?:<[^>]*>)?\s*\(\s*[$@]?[""']([^""']+)[""']",
            RegexOptions.Compiled);

        public static List<ApiCallSite> Blazor(string root)
        {
            var calls = new List<ApiCallSite>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".cs", ".razor" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in HttpClientRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[2].Value;
                        if (!LooksLikePath(rawPath)) continue;

                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "HttpClient"
                        });
                    }
                }
            }
            return calls;
        }

        public static List<ApiCallSite> AllHeuristics(string root)
        {
            var calls = new List<ApiCallSite>();
            calls.AddRange(Flutter(root));
            calls.AddRange(Web(root));
            calls.AddRange(Blazor(root));
            return calls;
        }

        // ── shared machinery ──────────────────────────────────────────────────────────────
        private static readonly string[] WebExtensions = { ".ts", ".tsx", ".js", ".jsx", ".mjs", ".vue" };

        private static List<ApiCallSite> ScanReceiverVerb(
            string root, string[] extensions, Regex rx,
            int receiverGroup, int verbGroup, int pathGroup, Func<string, string> clientOf)
        {
            var calls = new List<ApiCallSite>();
            foreach (var file in SourceWalker.EnumerateFiles(root, extensions))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in rx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[pathGroup].Value;
                        if (!LooksLikePath(rawPath)) continue;

                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[verbGroup].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = clientOf(m.Groups[receiverGroup].Value)
                        });
                    }
                }
            }
            return calls;
        }

        // Filter out non-path string args (e.g. dict.get("key")) — require something path-shaped.
        private static bool LooksLikePath(string raw) =>
            !string.IsNullOrWhiteSpace(raw) &&
            (raw.Contains('/') || raw.StartsWith('$') || raw.Contains('{') || raw.Contains("://"));

        public static List<ApiCallSite> Dedupe(List<ApiCallSite> calls)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<ApiCallSite>();
            foreach (var c in calls)
                if (seen.Add(c.Id))
                    result.Add(c);
            return result;
        }
    }
}
