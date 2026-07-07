using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DeveloperAI.BusinessLogic;

namespace Syncro.Desktop.Services.Mcp
{
    public class LocalMcpServer
    {
        private readonly AIClient _ai;

        public LocalMcpServer(AIClient ai)
        {
            _ai = ai;
        }

        public class MethodologyResponse
        {
            public string TaskTitle { get; set; } = "";
            public string Methodology { get; set; } = "";
            public List<string> RestrictedFiles { get; set; } = new();
        }

        public async Task<MethodologyResponse> GenerateMethodologyAsync(string workspacePath, string scopeDescription)
        {
            var fileTree = BuildFileTree(workspacePath);

            var prompt = $@"You are a senior software architect reviewing a developer's assigned task.

TASK SCOPE:
{scopeDescription}

WORKSPACE FILES:
{fileTree}

Based on the task scope and the actual files in the workspace, respond with ONLY a JSON object (no markdown, no code fences):
{{
  ""methodology"": ""### Step 1: ...\n\n### Step 2: ..."",
  ""restrictedFiles"": [""relative/path/file1.ts"", ""relative/path/file2.cs""]
}}

Rules:
- methodology: 3-6 numbered steps in markdown, tailored to the scope and actual codebase
- restrictedFiles: only files directly relevant to this scope (to prevent merge conflicts with other devs)
- Use real file paths from the workspace list above";

            try
            {
                var (raw, error) = await _ai.CallLLM(prompt, "code", workspacePath, "gemini-2.5-flash");

                if (!string.IsNullOrEmpty(raw))
                {
                    var parsed = ParseJsonResponse(raw);
                    if (parsed != null) return parsed;
                }
            }
            catch { /* fall through to fallback */ }

            // Fallback: at least return real file list even if AI call fails
            return new MethodologyResponse
            {
                TaskTitle = "Task Analysis",
                Methodology = $"### Step 1: Understand the scope\n\n{scopeDescription}\n\n### Step 2: Review relevant files\n\nExamine the files listed in the restricted scope below.\n\n### Step 3: Implement and test\n\nMake targeted changes, run tests, and commit.",
                RestrictedFiles = GuessRestrictedFiles(workspacePath, scopeDescription),
            };
        }

        // ── helpers ──────────────────────────────────────────────────

        private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", "bin", "obj", ".vs", "dist", "build",
            ".next", "__pycache__", ".venv", "venv", "coverage", ".cache"
        };

        private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".razor", ".ts", ".tsx", ".js", ".jsx", ".py", ".java",
            ".go", ".rs", ".cpp", ".c", ".h", ".swift", ".kt", ".dart",
            ".json", ".yaml", ".yml", ".toml", ".xml", ".csproj", ".sln"
        };

        private static string BuildFileTree(string root)
        {
            if (!Directory.Exists(root)) return "(workspace not found)";

            var sb = new StringBuilder();
            var files = new List<string>();

            CollectFiles(root, root, files, depth: 0);

            foreach (var f in files.Take(120))
                sb.AppendLine(f);

            if (files.Count > 120)
                sb.AppendLine($"... and {files.Count - 120} more files");

            return sb.ToString();
        }

        private static void CollectFiles(string root, string dir, List<string> result, int depth)
        {
            if (depth > 6) return;

            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (SkipDirs.Contains(name)) continue;
                    CollectFiles(root, sub, result, depth + 1);
                }
                foreach (var file in Directory.GetFiles(dir))
                {
                    if (CodeExtensions.Contains(Path.GetExtension(file)))
                        result.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
            }
            catch { /* skip inaccessible dirs */ }
        }

        private static MethodologyResponse? ParseJsonResponse(string raw)
        {
            try
            {
                // Strip markdown fences if the model wrapped it
                var json = Regex.Replace(raw.Trim(), @"^```[a-z]*\n?|```$", "", RegexOptions.Multiline).Trim();

                // Find the JSON object boundaries
                var start = json.IndexOf('{');
                var end   = json.LastIndexOf('}');
                if (start < 0 || end < 0) return null;
                json = json.Substring(start, end - start + 1);

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                var methodology = root.TryGetProperty("methodology", out var m) ? m.GetString() ?? "" : "";
                var files = new List<string>();
                if (root.TryGetProperty("restrictedFiles", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var el in arr.EnumerateArray())
                        if (el.GetString() is string s && !string.IsNullOrEmpty(s))
                            files.Add(s);

                if (string.IsNullOrEmpty(methodology) && files.Count == 0) return null;

                return new MethodologyResponse
                {
                    TaskTitle    = "AI Analysis Complete",
                    Methodology  = methodology,
                    RestrictedFiles = files,
                };
            }
            catch { return null; }
        }

        private static List<string> GuessRestrictedFiles(string root, string scope)
        {
            if (!Directory.Exists(root)) return new();

            var allFiles = new List<string>();
            CollectFiles(root, root, allFiles, 0);

            var keywords = scope
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3)
                .Select(w => w.ToLower())
                .ToHashSet();

            return allFiles
                .Where(f => keywords.Any(k => f.ToLower().Contains(k)))
                .Take(10)
                .ToList();
        }
    }
}
