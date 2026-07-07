using System;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// Continuous worker (runtime.md R2/§5) that samples every managed process: TCP-probes its port
    /// for readiness (publishing <see cref="RuntimeEventKind.ProcessHealthy"/>) and reads real cpu%/mem
    /// from the PID (publishing <see cref="RuntimeEventKind.Metric"/>). No LLM — this is what keeps the
    /// dashboard's "🟢 Running · port 5000 · cpu 12% · 210 MB" tiles live.
    /// </summary>
    public sealed class HealthWorker : IRuntimeWorker
    {
        private readonly StackRunManager _runManager;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

        public HealthWorker(StackRunManager runManager)
        {
            _runManager = runManager;
        }

        public string Id => "health";
        public WorkerTier Tier => WorkerTier.Continuous;

        public async Task RunAsync(IEventBus bus, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var rp in _runManager.Running())
                {
                    try { Sample(bus, rp); }
                    catch { /* never let one bad sample kill the loop */ }
                }

                try { await Task.Delay(Interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private static void Sample(IEventBus bus, RunningProcess rp)
        {
            // ── readiness: a port answering is a stronger signal than "process alive" ──
            if (rp.Spec.Port > 0)
            {
                bool open = IsPortOpen(rp.Spec.Port);
                if (open)
                {
                    rp.State = "healthy";
                    bus.Publish(RuntimeEvent.Create("health", rp.Spec.WorkspaceId,
                        RuntimeEventKind.ProcessHealthy, rp.Id,
                        new Dictionary<string, string> { ["port"] = rp.Spec.Port.ToString(), ["name"] = rp.Spec.Label }));
                }
            }

            // ── cpu / memory from the PID ──
            if (rp.Pid is int pid)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    proc.Refresh();

                    double memMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1);

                    var now = DateTime.UtcNow;
                    var cpuNow = proc.TotalProcessorTime;
                    double cpuPercent = 0;
                    var elapsedMs = (now - rp.LastCpuSample).TotalMilliseconds;
                    if (elapsedMs > 0 && rp.LastCpuTime > TimeSpan.Zero)
                    {
                        var usedMs = (cpuNow - rp.LastCpuTime).TotalMilliseconds;
                        cpuPercent = Math.Round(usedMs / (elapsedMs * Environment.ProcessorCount) * 100.0, 1);
                        if (cpuPercent < 0) cpuPercent = 0;
                    }
                    rp.LastCpuTime = cpuNow;
                    rp.LastCpuSample = now;

                    rp.CpuPercent = cpuPercent;
                    rp.MemoryMb = memMb;

                    bus.Publish(RuntimeEvent.Create("health", rp.Spec.WorkspaceId,
                        RuntimeEventKind.Metric, rp.Id,
                        new Dictionary<string, string>
                        {
                            ["cpu"] = cpuPercent.ToString("0.0"),
                            ["memMb"] = memMb.ToString("0.0"),
                            ["name"] = rp.Spec.Label
                        }));
                }
                catch
                {
                    // Process gone between snapshot and sample — the run loop will emit the exit event.
                }
            }
        }

        private static bool IsPortOpen(int port)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.BeginConnect("127.0.0.1", port, null, null);
                bool ok = connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(400));
                if (ok)
                {
                    client.EndConnect(connect);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
