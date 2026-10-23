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
    /// Deterministic, zero-GPU extraction of outbound HTTP calls across Flutter, Web (React/Next/Angular/ReactNative),
    /// .NET (Blazor/MAUI/HttpClient), Kotlin/Java (Retrofit/Ktor/OkHttp), C++ (cpr/httplib), Rust (reqwest), and Go.
    /// Paths are normalized through <see cref="PathNormalizer"/> so they line up with backend routes.
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

        // ── Web / React / React Native: axios.get('/x') | api.post('/x', body) | fetch('/x') ──
        private static readonly Regex AxiosRx = new(
            @"\b(axios|api|http|client|instance|_api|apiClient)\s*\.\s*(get|post|put|patch|delete)\s*(?:<[^>]*>)?\s*\(\s*[`'""]([^`'""]+)[`'""]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

                        var windowText = i + 2 < lines.Length
                            ? lines[i] + " " + lines[i + 1] + " " + lines[i + 2]
                            : lines[i];
                        var methodMatch = FetchMethodRx.Match(windowText);
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

        // ── Blazor / .NET MAUI: http.GetAsync("/x") | _httpClient.PostAsJsonAsync($"{BaseUrl}/x", body) ──
        private static readonly Regex HttpClientRx = new(
            @"\.\s*(Get|Post|Put|Patch|Delete)(?:Async|FromJsonAsync|AsJsonAsync|StringAsync)?\s*(?:<[^>]*>)?\s*\(\s*[$@]?[""']([^""']+)[""']",
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

        // ── Kotlin / Java (Retrofit @GET("path"), @POST("path"), Ktor client.get("path")) ──
        private static readonly Regex RetrofitRx = new(
            @"@(GET|POST|PUT|PATCH|DELETE)\s*\(\s*(?:value\s*=\s*)?""([^""]+)""\s*\)",
            RegexOptions.Compiled);
        private static readonly Regex KtorClientRx = new(
            @"\b(client|httpClient|api)\s*\.\s*(get|post|put|patch|delete)\s*(?:<[^>]*>)?\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<ApiCallSite> KotlinJvm(string root)
        {
            var calls = new List<ApiCallSite>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".kt", ".java" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }

                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in RetrofitRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[2].Value;
                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath.StartsWith('/') ? rawPath : "/" + rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "retrofit"
                        });
                    }
                }
            }

            calls.AddRange(ScanReceiverVerb(root, new[] { ".kt", ".java" }, KtorClientRx,
                receiverGroup: 1, verbGroup: 2, pathGroup: 3, clientOf: _ => "ktor-client"));
            return calls;
        }

        // ── C++ (cpr::Get, httplib::Client.Get/Post) ──────────────────────────────────────
        private static readonly Regex CppClientRx = new(
            @"\b(?:cpr::|cli\.|client\.|http\.)(Get|Post|Put|Patch|Delete)\s*\([^{""]*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<ApiCallSite> Cpp(string root)
        {
            var calls = new List<ApiCallSite>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".cpp", ".cc", ".cxx", ".hpp", ".h" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in CppClientRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[2].Value;
                        if (!LooksLikePath(rawPath)) continue;
                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "cpp-http"
                        });
                    }
                }
            }
            return calls;
        }

        // ── Rust (reqwest::get, client.post) ──────────────────────────────────────────────
        private static readonly Regex RustClientRx = new(
            @"\b(?:reqwest|client|http)\s*(?:::|\.)\s*(get|post|put|patch|delete)\s*\(\s*(?:&?format!\s*\()?\s*""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static List<ApiCallSite> Rust(string root)
        {
            var calls = new List<ApiCallSite>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".rs" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in RustClientRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[2].Value;
                        if (!LooksLikePath(rawPath)) continue;
                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "reqwest"
                        });
                    }
                }
            }
            return calls;
        }

        // ── Go (http.Get, http.Post, http.NewRequest) ─────────────────────────────────────
        private static readonly Regex GoHttpCallRx = new(
            @"\b(?:http|client)\s*\.\s*(Get|Post|Head)\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled);
        private static readonly Regex GoNewReqRx = new(
            @"NewRequest(?:WithContext)?\s*\([^""]*""(GET|POST|PUT|PATCH|DELETE)""\s*,\s*""([^""]+)""",
            RegexOptions.Compiled);

        public static List<ApiCallSite> Go(string root)
        {
            var calls = new List<ApiCallSite>();
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".go" }))
            {
                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (Match m in GoHttpCallRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[2].Value;
                        if (!LooksLikePath(rawPath)) continue;
                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "net/http"
                        });
                    }
                    foreach (Match m in GoNewReqRx.Matches(lines[i]))
                    {
                        var rawPath = m.Groups[2].Value;
                        if (!LooksLikePath(rawPath)) continue;
                        calls.Add(new ApiCallSite
                        {
                            Method = m.Groups[1].Value.ToHttpVerb(),
                            PathTemplate = PathNormalizer.NormalizeCallPath(rawPath),
                            FilePath = file,
                            Line = i + 1,
                            Client = "net/http"
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
            calls.AddRange(KotlinJvm(root));
            calls.AddRange(Cpp(root));
            calls.AddRange(Rust(root));
            calls.AddRange(Go(root));
            return calls;
        }

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
