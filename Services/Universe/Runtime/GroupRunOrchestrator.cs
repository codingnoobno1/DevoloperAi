using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// Runs a whole connected stack as one action, in dependency order (orchestrator.md G1/G2):
    /// wave 0 databases/infra → wave 1 backends → wave 2 frontends. Members within a wave start
    /// **in parallel**; the orchestrator gates on port-readiness before advancing so a frontend
    /// doesn't come up before the API it needs. Each member still bootstraps its own deps first
    /// (StackRunManager → EnvironmentBootstrapper), so "run the stack" installs and launches
    /// everything with zero LLM involvement.
    /// </summary>
    public sealed class GroupRunOrchestrator
    {
        private readonly StackRunManager _run;

        public GroupRunOrchestrator(StackRunManager run)
        {
            _run = run;
        }

        public async Task<IReadOnlyList<RunStatus>> RunGroupAsync(
            IReadOnlyList<RunSpec> members,
            IEventBus? bus = null,
            CancellationToken ct = default)
        {
            var started = new List<RunStatus>();
            if (members == null || members.Count == 0) return started;

            foreach (var wave in members.GroupBy(m => m.Wave).OrderBy(g => g.Key))
            {
                if (ct.IsCancellationRequested) break;

                var waveMembers = wave.ToList();
                foreach (var m in waveMembers)
                    started.Add(_run.Start(m));   // non-blocking; runs in the background

                // Gate on the members that expose a port — best-effort, bounded, never hangs the group.
                var ports = waveMembers.Where(m => m.Port > 0).Select(m => m.Port).Distinct().ToList();
                await WaitForHealthyAsync(ports, TimeSpan.FromSeconds(90), ct).ConfigureAwait(false);
            }

            bus?.Publish(RuntimeEvent.Create("group-run", "", RuntimeEventKind.MappingChanged, "group",
                new Dictionary<string, string> { ["members"] = started.Count.ToString() }));

            return started;
        }

        /// <summary>Stop every managed process (kill-tree) — the "stop all" for a group.</summary>
        public void StopGroup() => _run.StopAll();

        private static async Task WaitForHealthyAsync(List<int> ports, TimeSpan timeout, CancellationToken ct)
        {
            if (ports.Count == 0) return;

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout && !ct.IsCancellationRequested)
            {
                if (ports.All(p => PortProbe.IsOpen(p))) return;
                try { await Task.Delay(1000, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
            // Timed out waiting for one or more ports — proceed anyway so the group never deadlocks
            // (a slow-starting service can still come up; the Health worker keeps reporting on it).
        }
    }
}
