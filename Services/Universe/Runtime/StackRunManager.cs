using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// Runs any stack as a managed process (runtime.md R2) — the generalization of what
    /// <c>FlutterService</c> does for Flutter, to every stack via its <c>run:</c> command. Uses
    /// CliWrap's event stream (same as CliScaffolder, so <c>npm</c>/<c>flutter</c> resolve on Windows)
    /// and captures the PID from <see cref="StartedCommandEvent"/> so the Health worker can read real
    /// cpu/memory. Publishes lifecycle facts to the bus; the graph updater persists them. No LLM.
    /// </summary>
    public sealed class StackRunManager : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly EnvironmentBootstrapper _bootstrapper;
        private readonly ConcurrentDictionary<string, RunningProcess> _procs = new();

        public StackRunManager(IEventBus bus, EnvironmentBootstrapper bootstrapper)
        {
            _bus = bus;
            _bootstrapper = bootstrapper;
        }

        public RunStatus Start(RunSpec spec)
        {
            var id = NodeId(spec);
            Stop(id); // replace any prior run of the same stack

            var rp = new RunningProcess
            {
                Id = id,
                Spec = spec,
                State = "starting",
                StartedAt = DateTime.UtcNow,
                Cts = new CancellationTokenSource()
            };
            _procs[id] = rp;

            rp.Loop = Task.Run(() => RunLoopAsync(rp));

            Publish(rp, RuntimeEventKind.ProcessStarted, new Dictionary<string, string>
            {
                ["name"] = spec.Label,
                ["port"] = spec.Port > 0 ? spec.Port.ToString() : "",
                ["command"] = $"{spec.Command} {spec.Arguments}".Trim(),
                ["workspace"] = spec.WorkspaceId
            });

            return ToStatus(rp);
        }

        public void Stop(string id)
        {
            if (!_procs.TryRemove(id, out var rp)) return;
            try { rp.Cts?.Cancel(); } catch { }
            KillTree(rp.Pid);
            rp.State = "stopped";
            Publish(rp, RuntimeEventKind.ProcessStopped, new Dictionary<string, string> { ["name"] = rp.Spec.Label });
        }

        public void StopAll()
        {
            foreach (var id in _procs.Keys.ToList()) Stop(id);
        }

        public IReadOnlyList<RunStatus> Snapshot() => _procs.Values.Select(ToStatus).ToList();

        /// <summary>Live processes, for the Health worker to sample.</summary>
        public IReadOnlyList<RunningProcess> Running() => _procs.Values.ToList();

        private async Task RunLoopAsync(RunningProcess rp)
        {
            var spec = rp.Spec;
            try
            {
                // Pre-scripts (runtime.md R4): install deps before starting — deterministic, no LLM.
                if (!spec.SkipBootstrap)
                {
                    rp.State = "installing";
                    var bootstrapped = await _bootstrapper.EnsureAsync(spec, _bus, rp.Cts!.Token).ConfigureAwait(false);
                    if (!bootstrapped)
                    {
                        rp.State = "crashed";
                        Publish(rp, RuntimeEventKind.ProcessCrashed, new Dictionary<string, string> { ["error"] = "dependency bootstrap failed", ["name"] = spec.Label });
                        _procs.TryRemove(rp.Id, out _);
                        return;
                    }
                    if (rp.Cts.IsCancellationRequested) return;
                }

                var cmd = Cli.Wrap(spec.Command)
                    .WithArguments(spec.Arguments)
                    .WithWorkingDirectory(string.IsNullOrWhiteSpace(spec.WorkingDirectory) ? Environment.CurrentDirectory : spec.WorkingDirectory)
                    .WithValidation(CommandResultValidation.None);

                if (spec.Env.Count > 0)
                    cmd = cmd.WithEnvironmentVariables(spec.Env);

                await foreach (var evt in cmd.ListenAsync(rp.Cts!.Token))
                {
                    switch (evt)
                    {
                        case StartedCommandEvent started:
                            rp.Pid = started.ProcessId;
                            rp.State = "running";
                            Publish(rp, RuntimeEventKind.PortOpened, new Dictionary<string, string>
                            {
                                ["pid"] = started.ProcessId.ToString(),
                                ["port"] = spec.Port > 0 ? spec.Port.ToString() : "",
                                ["name"] = spec.Label
                            });
                            break;

                        case StandardOutputCommandEvent stdOut:
                            rp.Append(stdOut.Text);
                            break;

                        case StandardErrorCommandEvent stdErr:
                            rp.Append(stdErr.Text);
                            break;

                        case ExitedCommandEvent exited:
                            rp.State = exited.ExitCode == 0 ? "stopped" : "crashed";
                            Publish(rp,
                                exited.ExitCode == 0 ? RuntimeEventKind.ProcessStopped : RuntimeEventKind.ProcessCrashed,
                                new Dictionary<string, string> { ["exitCode"] = exited.ExitCode.ToString(), ["name"] = spec.Label });
                            _procs.TryRemove(rp.Id, out _);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) { /* stopped by user */ }
            catch (Exception ex)
            {
                rp.State = "crashed";
                Publish(rp, RuntimeEventKind.ProcessCrashed, new Dictionary<string, string> { ["error"] = ex.Message, ["name"] = spec.Label });
                _procs.TryRemove(rp.Id, out _);
            }
        }

        private void Publish(RunningProcess rp, RuntimeEventKind kind, IReadOnlyDictionary<string, string> data) =>
            _bus.Publish(RuntimeEvent.Create("run", rp.Spec.WorkspaceId, kind, rp.Id, data));

        private static RunStatus ToStatus(RunningProcess rp) => new()
        {
            Id = rp.Id, Label = rp.Spec.Label, WorkspaceId = rp.Spec.WorkspaceId,
            State = rp.State, Pid = rp.Pid, Port = rp.Spec.Port, StartedAt = rp.StartedAt,
            CpuPercent = rp.CpuPercent, MemoryMb = rp.MemoryMb
        };

        private static string NodeId(RunSpec spec) => $"proc:{spec.WorkspaceId}:{spec.Label}";

        // Kill the whole tree — a dev server (`npm run dev`) spawns children that would otherwise leak.
        // Windows-only app, so taskkill /T is the reliable path.
        private static void KillTree(int? pid)
        {
            if (pid is not int p) return;
            try
            {
                using var kill = Process.Start(new ProcessStartInfo("taskkill", $"/T /F /PID {p}")
                {
                    CreateNoWindow = true, UseShellExecute = false
                });
                kill?.WaitForExit(3000);
            }
            catch { /* best-effort */ }
        }

        public void Dispose() => StopAll();
    }

    /// <summary>Internal live-process record held by the run manager.</summary>
    public sealed class RunningProcess
    {
        public string Id { get; set; } = "";
        public RunSpec Spec { get; set; } = new();
        public int? Pid { get; set; }
        public string State { get; set; } = "starting";
        public DateTime StartedAt { get; set; }
        public CancellationTokenSource? Cts { get; set; }
        public Task? Loop { get; set; }

        public double CpuPercent { get; set; }
        public double MemoryMb { get; set; }

        // CPU-sampling bookkeeping (Health worker).
        public TimeSpan LastCpuTime { get; set; } = TimeSpan.Zero;
        public DateTime LastCpuSample { get; set; } = DateTime.UtcNow;

        private readonly object _outLock = new();
        private readonly Queue<string> _output = new();

        public void Append(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_outLock)
            {
                _output.Enqueue(line);
                while (_output.Count > 200) _output.Dequeue();
            }
        }

        public IReadOnlyList<string> RecentOutput()
        {
            lock (_outLock) return _output.ToList();
        }
    }
}
