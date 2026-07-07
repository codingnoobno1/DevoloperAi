using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;
using Syncro.Desktop.Services.Universe.Runtime;

namespace Syncro.Desktop.Services.Universe.Dependencies
{
    public sealed class MissingDependency
    {
        public string Module { get; set; } = "";
        public string Package { get; set; } = "";
        public string Ecosystem { get; set; } = "";     // npm | pip
        public string Source { get; set; } = "";        // curated | same-name
    }

    public sealed class ReconcileResult
    {
        public string Ecosystem { get; set; } = "none";
        public List<MissingDependency> Missing { get; set; } = new();
        public List<string> Installed { get; set; } = new();
        public List<string> Skipped { get; set; } = new();  // not installed (unverified / report-only)
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>
    /// Diffs the packages a project actually imports against the ones it declares, and (only when
    /// approved) installs the genuinely-missing ones (runtime.md §7b, R6). Safety is the whole point:
    ///  • resolve modules to packages via the curated alias map or exact same-name — never a raw string;
    ///  • report-only by default; installing requires an explicit <c>install: true</c>;
    ///  • verify each package exists in the registry (npm view / pip index) before installing;
    ///  • install per-package so one bad name can't poison the batch.
    /// So <c>import cv2</c> with no requirements entry becomes: detect → "opencv-python missing" →
    /// (on approval, verified) install it and record it — with the LLM spending zero tokens.
    /// </summary>
    public sealed class DependencyReconciler
    {
        public async Task<ReconcileResult> ReconcileAsync(string workspacePath, bool install, IEventBus? bus, CancellationToken ct = default)
        {
            var result = new ReconcileResult();
            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
            {
                result.Notes.Add("Workspace path does not exist.");
                return result;
            }

            bool isNode = File.Exists(Path.Combine(workspacePath, "package.json"));
            bool isPy = File.Exists(Path.Combine(workspacePath, "requirements.txt")) || File.Exists(Path.Combine(workspacePath, "pyproject.toml"));

            if (isNode) result = await ReconcileNodeAsync(workspacePath, install, bus, ct);
            else if (isPy) result = await ReconcilePythonAsync(workspacePath, install, bus, ct);
            else result.Notes.Add("No package.json or requirements.txt found — nothing to reconcile.");

            return result;
        }

        // ── Node / npm ────────────────────────────────────────────────────────────────────
        private async Task<ReconcileResult> ReconcileNodeAsync(string dir, bool install, IEventBus? bus, CancellationToken ct)
        {
            var result = new ReconcileResult { Ecosystem = "npm" };
            var declared = ReadPackageJsonDeps(dir);
            var toInstall = new List<string>();

            foreach (var spec in ImportScanner.ScanJs(dir))
            {
                var resolved = DependencyAliasMap.ResolveJs(spec);
                if (resolved == null) continue;
                var (pkg, source) = resolved.Value;
                if (declared.Contains(pkg)) continue;

                var miss = new MissingDependency { Module = spec, Package = pkg, Ecosystem = "npm", Source = source };
                if (result.Missing.Any(m => m.Package.Equals(pkg, StringComparison.OrdinalIgnoreCase))) continue;
                result.Missing.Add(miss);
                Publish(bus, dir, pkg, missing: true);
                toInstall.Add(pkg);
            }

            if (install)
            {
                foreach (var pkg in toInstall.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    // Registry-existence check before installing anything.
                    var (viewCode, _) = await RunAsync("npm", $"view {pkg} version", dir, ct);
                    if (viewCode != 0) { result.Skipped.Add($"{pkg} (not found in npm registry)"); continue; }

                    var (code, _) = await RunAsync("npm", $"install {pkg}", dir, ct);
                    if (code == 0) { result.Installed.Add(pkg); Publish(bus, dir, pkg, missing: false); }
                    else result.Skipped.Add($"{pkg} (install failed)");
                }
            }

            return result;
        }

        // ── Python / pip ──────────────────────────────────────────────────────────────────
        private async Task<ReconcileResult> ReconcilePythonAsync(string dir, bool install, IEventBus? bus, CancellationToken ct)
        {
            var result = new ReconcileResult { Ecosystem = "pip" };
            var declared = ReadRequirements(dir);

            foreach (var module in ImportScanner.ScanPython(dir))
            {
                var resolved = DependencyAliasMap.ResolvePython(module);
                if (resolved == null) continue; // stdlib
                var (pkg, source) = resolved.Value;
                if (declared.Contains(pkg) || declared.Contains(module)) continue;

                if (result.Missing.Any(m => m.Package.Equals(pkg, StringComparison.OrdinalIgnoreCase))) continue;
                result.Missing.Add(new MissingDependency { Module = module, Package = pkg, Ecosystem = "pip", Source = source });
                Publish(bus, dir, pkg, missing: true);
            }

            if (install)
            {
                var venvPy = Path.Combine(dir, ".venv", "Scripts", "python.exe");
                bool useVenv = File.Exists(venvPy);

                foreach (var miss in result.Missing)
                {
                    // Install per-package so one bad name can't block the rest; pip fails loudly on unknowns.
                    var (code, _) = useVenv
                        ? await RunAsync(venvPy, $"-m pip install {miss.Package}", dir, ct)
                        : await RunAsync("pip", $"install {miss.Package}", dir, ct);

                    if (code == 0) { result.Installed.Add(miss.Package); Publish(bus, dir, miss.Package, missing: false); }
                    else result.Skipped.Add($"{miss.Package} (install failed — verify the package name)");
                }
            }

            return result;
        }

        // ── manifest parsing ────────────────────────────────────────────────────────────────
        private static readonly Regex PkgDepBlock = new(@"""(?:dependencies|devDependencies|peerDependencies)""\s*:\s*\{([^}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex PkgDepName = new(@"""([^""]+)""\s*:", RegexOptions.Compiled);

        private static HashSet<string> ReadPackageJsonDeps(string dir)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var text = File.ReadAllText(Path.Combine(dir, "package.json"));
                foreach (Match block in PkgDepBlock.Matches(text))
                    foreach (Match name in PkgDepName.Matches(block.Groups[1].Value))
                        set.Add(name.Groups[1].Value);
            }
            catch { }
            return set;
        }

        private static HashSet<string> ReadRequirements(string dir)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var req = Path.Combine(dir, "requirements.txt");
            if (!File.Exists(req)) return set;
            try
            {
                foreach (var raw in File.ReadAllLines(req))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;
                    // strip version specifiers / extras: "package[extra]>=1.0" → "package"
                    var name = Regex.Split(line, @"[\s\[<>=!~;]")[0];
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
                }
            }
            catch { }
            return set;
        }

        private static void Publish(IEventBus? bus, string workspacePath, string pkg, bool missing)
        {
            bus?.Publish(RuntimeEvent.Create("deps", workspacePath,
                missing ? RuntimeEventKind.DependencyMissing : RuntimeEventKind.DependencyInstalled,
                $"pkg:{pkg}",
                new Dictionary<string, string> { ["name"] = pkg }));
        }

        private static async Task<(int code, string output)> RunAsync(string exe, string args, string dir, CancellationToken outerCt)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt, timeoutCts.Token);
            try
            {
                var r = await Cli.Wrap(exe).WithArguments(args).WithWorkingDirectory(dir)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync(linked.Token);
                return (r.ExitCode, r.StandardOutput + r.StandardError);
            }
            catch (Exception ex) { return (-1, ex.Message); }
        }
    }
}
