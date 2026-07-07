using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// Runs the boring, deterministic setup a stack needs before it can run — <c>npm install</c>,
    /// <c>python -m venv</c> + <c>pip install</c>, <c>flutter pub get</c>, <c>dotnet restore</c> —
    /// detected from the workspace's manifest (runtime.md §7a, R4). These are lookups, not decisions,
    /// so the LLM never spends a token driving them. A manifest-hash marker means it only re-installs
    /// when dependencies actually change.
    /// </summary>
    public sealed class EnvironmentBootstrapper
    {
        private sealed record BootstrapStep(string Exe, string Args);

        public async Task<bool> EnsureAsync(RunSpec spec, IEventBus bus, CancellationToken ct)
        {
            var dir = spec.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return true; // nothing to bootstrap

            var (steps, manifestFile) = Resolve(spec);
            if (steps.Count == 0)
                return true; // no known package manager — nothing to do

            // Skip if the manifest hasn't changed since the last successful bootstrap.
            var currentHash = manifestFile != null && File.Exists(manifestFile) ? HashFile(manifestFile) : "";
            var markerPath = Path.Combine(dir, ".syncro", "bootstrap.hash");
            if (!string.IsNullOrEmpty(currentHash) && File.Exists(markerPath))
            {
                try { if (File.ReadAllText(markerPath).Trim() == currentHash) return true; }
                catch { /* re-run on unreadable marker */ }
            }

            var nodeId = $"bootstrap:{spec.WorkspaceId}:{spec.Label}";
            Publish(bus, spec, RuntimeEventKind.ProcessStarted, nodeId, new() { ["name"] = $"install ({spec.Label})", ["kind"] = "bootstrap" });

            foreach (var step in steps)
            {
                var ok = await RunStepAsync(dir, step, ct);
                if (!ok)
                {
                    Publish(bus, spec, RuntimeEventKind.ProcessCrashed, nodeId, new() { ["name"] = $"install ({spec.Label})", ["failedStep"] = $"{step.Exe} {step.Args}" });
                    return false;
                }
            }

            // Record success so we don't re-install on every run.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
                if (!string.IsNullOrEmpty(currentHash)) File.WriteAllText(markerPath, currentHash);
            }
            catch { /* marker is an optimization, not required */ }

            Publish(bus, spec, RuntimeEventKind.ProcessStopped, nodeId, new() { ["name"] = $"install ({spec.Label})", ["kind"] = "bootstrap" });
            return true;
        }

        // ── detect package manager → ordered install steps ──────────────────────────────────
        private static (List<BootstrapStep> steps, string? manifest) Resolve(RunSpec spec)
        {
            var dir = spec.WorkingDirectory;
            var steps = new List<BootstrapStep>();

            // Explicit override wins.
            if (!string.IsNullOrWhiteSpace(spec.InstallCommand))
            {
                var cmd = spec.InstallCommand.Trim();
                int sp = cmd.IndexOf(' ');
                steps.Add(new BootstrapStep(sp > -1 ? cmd[..sp] : cmd, sp > -1 ? cmd[(sp + 1)..] : ""));
                return (steps, FirstExisting(dir, "package.json", "requirements.txt", "pubspec.yaml"));
            }

            if (File.Exists(Path.Combine(dir, "package.json")))
            {
                steps.Add(new BootstrapStep("npm", "install"));
                return (steps, Path.Combine(dir, "package.json"));
            }

            if (File.Exists(Path.Combine(dir, "pubspec.yaml")))
            {
                steps.Add(new BootstrapStep("flutter", "pub get"));
                return (steps, Path.Combine(dir, "pubspec.yaml"));
            }

            if (File.Exists(Path.Combine(dir, "requirements.txt")))
            {
                var venvPython = Path.Combine(dir, ".venv", "Scripts", "python.exe");
                steps.Add(new BootstrapStep("python", "-m venv .venv"));
                steps.Add(new BootstrapStep(venvPython, "-m pip install -r requirements.txt"));
                return (steps, Path.Combine(dir, "requirements.txt"));
            }

            var csproj = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj != null)
            {
                steps.Add(new BootstrapStep("dotnet", "restore"));
                return (steps, csproj);
            }

            if (File.Exists(Path.Combine(dir, "pyproject.toml")))
            {
                var venvPython = Path.Combine(dir, ".venv", "Scripts", "python.exe");
                steps.Add(new BootstrapStep("python", "-m venv .venv"));
                steps.Add(new BootstrapStep(venvPython, "-m pip install ."));
                return (steps, Path.Combine(dir, "pyproject.toml"));
            }

            return (steps, null);
        }

        private static async Task<bool> RunStepAsync(string dir, BootstrapStep step, CancellationToken outerCt)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt, timeoutCts.Token);

            try
            {
                var cmd = Cli.Wrap(step.Exe)
                    .WithArguments(step.Args)
                    .WithWorkingDirectory(dir)
                    .WithEnvironmentVariables(new Dictionary<string, string?>
                    {
                        ["CI"] = "true",
                        ["npm_config_progress"] = "false",
                        ["FORCE_COLOR"] = "0"
                    })
                    .WithValidation(CommandResultValidation.None);

                await foreach (var evt in cmd.ListenAsync(linked.Token))
                {
                    if (evt is ExitedCommandEvent exited)
                        return exited.ExitCode == 0;
                }
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch { return false; }
        }

        private static void Publish(IEventBus bus, RunSpec spec, RuntimeEventKind kind, string nodeId, Dictionary<string, string> data) =>
            bus.Publish(RuntimeEvent.Create("bootstrap", spec.WorkspaceId, kind, nodeId, data));

        private static string? FirstExisting(string dir, params string[] names) =>
            names.Select(n => Path.Combine(dir, n)).FirstOrDefault(File.Exists);

        private static string HashFile(string path)
        {
            try
            {
                using var stream = File.OpenRead(path);
                return Convert.ToHexString(SHA1.HashData(stream));
            }
            catch { return ""; }
        }
    }
}
