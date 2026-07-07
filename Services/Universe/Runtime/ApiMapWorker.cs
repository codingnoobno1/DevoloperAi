using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Universe.Indexing;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// Continuous worker (runtime.md R5) that keeps the cross-workspace API map fresh. Re-runs the
    /// <see cref="CrossWorkspaceMapper"/> on a slow loop and publishes <see cref="RuntimeEventKind.MappingChanged"/>
    /// when it links anything — so the dashboard's mapped/missing counts stay live. Pure graph work,
    /// no LLM. R9's file-watcher will make this event-driven instead of polled.
    /// </summary>
    public sealed class ApiMapWorker : IRuntimeWorker
    {
        private readonly CrossWorkspaceMapper _mapper;
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        public ApiMapWorker(CrossWorkspaceMapper mapper)
        {
            _mapper = mapper;
        }

        public string Id => "apimap";
        public WorkerTier Tier => WorkerTier.Continuous;

        public async Task RunAsync(IEventBus bus, CancellationToken ct)
        {
            // Small initial delay so first-run indexing can populate the graph.
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var summary = _mapper.MapAll();
                    if (summary.CrossEdges > 0)
                    {
                        bus.Publish(RuntimeEvent.Create("apimap", "", RuntimeEventKind.MappingChanged, "universe",
                            new Dictionary<string, string>
                            {
                                ["crossEdges"] = summary.CrossEdges.ToString(),
                                ["workspaceDeps"] = summary.WorkspaceDependencies.ToString()
                            }));
                    }
                }
                catch { /* mapping must never kill the loop */ }

                try { await Task.Delay(Interval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
