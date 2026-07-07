using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.projectgenerator.Models;

namespace Syncro.Desktop.Services.projectgenerator.Testing
{
    public sealed class CharacterizationCase
    {
        public string Label { get; set; } = "";
        public ProjectCreationRequest Request { get; set; } = new();
    }

    public sealed class CharacterizationFileSnapshot
    {
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public sealed class CharacterizationResult
    {
        public string Label { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<CharacterizationFileSnapshot> Files { get; set; } = new();
    }

    /// <summary>
    /// Dev-only characterization harness — safeupgrade.md Phase 2. Runs
    /// <see cref="IProjectCreationService"/> for every project type/combo the current wizard can
    /// produce, into a throwaway temp directory, and snapshots the resulting file tree verbatim.
    ///
    /// This intentionally captures TODAY'S behavior, including the known B1–B12 bugs documented in
    /// projectgenerator.md (wrong Vite/Next branch, registry overwrite, port collisions, ...).
    /// Phase 3 fixes those bugs one at a time; each fix is expected to change specific, named
    /// entries in the diff this harness produces — anything else moving is a sign the "fix" did
    /// more than intended.
    ///
    /// Never runs as part of normal app startup — only when MauiProgram.cs sees a
    /// <c>--characterize=baseline|verify</c> command-line flag (Phase 2 itself is "tests only, no
    /// product change", so this must be fully inert by default).
    /// </summary>
    public static class CharacterizationHarness
    {
        // Mirrors every type/sub-framework/group-combo a user can currently reach through
        // CreateProjectDialog's stepper. UseNativeCli is always false and SyncWithAgent always
        // false so the harness is deterministic and dependency-free (no network, no installed
        // toolchains, no AST graph) — it exercises exactly the static-template scaffolding path
        // where B1–B12 live.
        public static List<CharacterizationCase> DefaultCases(string rootTempDir) => new()
        {
            SingleCase("python_fastapi", rootTempDir, "FastAPI"),
            SingleCase("python_flask", rootTempDir, "Flask"),
            SingleCase("python_django", rootTempDir, "Django"),
            SingleCase("node_express", rootTempDir, "Express/Node"),
            SingleCase("node_next", rootTempDir, "Next.js"),
            SingleCase("node_vite", rootTempDir, "Vite React"),
            SingleCase("mobile_flutter", rootTempDir, "Flutter"),
            SingleCase("api_spring_boot", rootTempDir, "Spring Boot"),
            GroupCase("group_default", rootTempDir, new List<string> { "Next.js", "FastAPI" }),
            GroupCase("group_vite_express", rootTempDir, new List<string> { "React/Next/Vite", "Express/Node" }),
        };

        private static CharacterizationCase SingleCase(string label, string root, string type) => new()
        {
            Label = label,
            Request = new ProjectCreationRequest
            {
                Name = label,
                Path = Path.Combine(root, label),
                Kind = CreationKind.Single,
                Type = type,
                UseNativeCli = false,
                SyncWithAgent = false
            }
        };

        private static CharacterizationCase GroupCase(string label, string root, List<string> components) => new()
        {
            Label = label,
            Request = new ProjectCreationRequest
            {
                Name = label,
                Path = Path.Combine(root, label),
                Kind = CreationKind.Group,
                GroupComponents = components,
                UseNativeCli = false,
                SyncWithAgent = false
            }
        };

        public static async Task<List<CharacterizationResult>> RunAsync(IProjectCreationService service)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "syncro_characterization_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tempRoot);

            var results = new List<CharacterizationResult>();

            try
            {
                foreach (var testCase in DefaultCases(tempRoot))
                {
                    var result = new CharacterizationResult { Label = testCase.Label };
                    try
                    {
                        var progress = new Progress<string>(_ => { });
                        var outcome = await service.CreateAsync(testCase.Request, progress);
                        result.Success = outcome.Success;
                        result.Error = outcome.Error;
                        result.Files = SnapshotFiles(testCase.Request.Path);
                    }
                    catch (Exception ex)
                    {
                        result.Success = false;
                        result.Error = ex.Message;
                    }

                    results.Add(result);
                }
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { /* best-effort cleanup */ }
            }

            return results;
        }

        private static List<CharacterizationFileSnapshot> SnapshotFiles(string projectPath)
        {
            var files = new List<CharacterizationFileSnapshot>();
            if (!Directory.Exists(projectPath))
                return files;

            foreach (var file in Directory.EnumerateFiles(projectPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(projectPath, file).Replace('\\', '/');
                string content;
                try { content = File.ReadAllText(file); }
                catch { content = "<binary or unreadable>"; }
                files.Add(new CharacterizationFileSnapshot { Path = rel, Content = content });
            }

            return files.OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        }

        public static string Serialize(List<CharacterizationResult> results) =>
            JsonConvert.SerializeObject(results, Formatting.Indented);

        public static List<CharacterizationResult> Deserialize(string json) =>
            JsonConvert.DeserializeObject<List<CharacterizationResult>>(json) ?? new List<CharacterizationResult>();

        /// <summary>
        /// Compares a freshly captured snapshot against a saved baseline. Empty result = identical
        /// output — the actual tripwire Phase 3 fixes are checked against.
        /// </summary>
        public static List<string> Diff(List<CharacterizationResult> baseline, List<CharacterizationResult> current)
        {
            var diffs = new List<string>();
            var baseMap = baseline.ToDictionary(r => r.Label);
            var curMap = current.ToDictionary(r => r.Label);

            foreach (var label in baseMap.Keys.Union(curMap.Keys).OrderBy(l => l, StringComparer.Ordinal))
            {
                if (!baseMap.TryGetValue(label, out var b)) { diffs.Add($"[{label}] NEW case (not in baseline)"); continue; }
                if (!curMap.TryGetValue(label, out var c)) { diffs.Add($"[{label}] MISSING (was in baseline, absent from this run)"); continue; }

                if (b.Success != c.Success)
                    diffs.Add($"[{label}] success changed: {b.Success} -> {c.Success}");

                var bFiles = b.Files.ToDictionary(f => f.Path, f => f.Content);
                var cFiles = c.Files.ToDictionary(f => f.Path, f => f.Content);

                foreach (var path in bFiles.Keys.Except(cFiles.Keys).OrderBy(p => p, StringComparer.Ordinal))
                    diffs.Add($"[{label}] file removed: {path}");
                foreach (var path in cFiles.Keys.Except(bFiles.Keys).OrderBy(p => p, StringComparer.Ordinal))
                    diffs.Add($"[{label}] file added: {path}");
                foreach (var path in bFiles.Keys.Intersect(cFiles.Keys).OrderBy(p => p, StringComparer.Ordinal))
                    if (bFiles[path] != cFiles[path])
                        diffs.Add($"[{label}] file changed: {path}");
            }

            return diffs;
        }
    }
}
