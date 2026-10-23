using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Security
{
    /// <summary>
    /// Native, dependency-free SAST scanner: walks a source tree and applies <see cref="SecurityRules"/>
    /// to each file, producing findings with file:line + snippet and a CI-style severity gate. Pure
    /// managed C# (regex + file IO) — no external binary, no Python, no network — so it ships with the
    /// .NET/MAUI app and behaves identically on Windows, macOS and Linux.
    ///
    /// Bundled Go scanners (trivy, gitleaks) can be layered on later via the workflow runner when
    /// present; this engine is the always-available baseline.
    /// </summary>
    public sealed class SecurityScanner
    {
        private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".dart_tool", "dist", "build", "out",
            "venv", ".venv", "__pycache__", "packages", "vendor", ".next", ".vs", ".idea", "Migrations"
        };

        private static readonly HashSet<string> ScanExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".py", ".dart", ".java", ".kt",
            ".go", ".rb", ".php", ".env", ".json", ".yml", ".yaml", ".config", ".xml", ".properties"
        };

        public Task<SecurityScanReport> ScanAsync(string root, Severity failOn = Severity.High, CancellationToken ct = default) =>
            Task.Run(() => Scan(root, failOn, ct), ct);

        public SecurityScanReport Scan(string root, Severity failOn = Severity.High, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var report = new SecurityScanReport { Root = root ?? "", FailOn = failOn };
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                report.ElapsedMs = sw.ElapsedMilliseconds;
                return report;
            }

            foreach (var file in EnumerateSource(root, ct))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file).ToLowerInvariant();

                string[] lines;
                try { lines = File.ReadAllLines(file); } catch { continue; }
                report.FilesScanned++;

                var rel = Path.GetRelativePath(root, file);
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.Length == 0 || line.Length > 4000) continue;   // skip minified/huge lines

                    foreach (var rule in SecurityRules.All)
                    {
                        if (rule.Extensions.Length > 0 && !rule.Extensions.Contains(ext)) continue;
                        if (!rule.Pattern.IsMatch(line)) continue;

                        report.Findings.Add(new SecurityFinding
                        {
                            RuleId = rule.Id,
                            Title = rule.Title,
                            Severity = rule.Severity,
                            Category = rule.Category,
                            Why = rule.Why,
                            File = rel,
                            Line = i + 1,
                            Snippet = Trim(line),
                        });
                    }
                }
            }

            report.Findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            report.ElapsedMs = sw.ElapsedMilliseconds;
            return report;
        }

        private static IEnumerable<string> EnumerateSource(string root, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); } catch { continue; }
                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.') && name is not ".env") continue;
                    if (!SkipDirs.Contains(name)) stack.Push(sub);
                }

                string[] files;
                try { files = Directory.GetFiles(dir); } catch { continue; }
                foreach (var f in files)
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    var fname = Path.GetFileName(f);
                    if (ScanExts.Contains(ext) || fname.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
                        yield return f;
                }
            }
        }

        private static string Trim(string line)
        {
            var t = line.Trim();
            return t.Length > 200 ? t.Substring(0, 200) + "…" : t;
        }
    }
}
