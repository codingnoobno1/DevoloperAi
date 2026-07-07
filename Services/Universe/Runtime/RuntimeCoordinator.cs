using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// The engine that never sleeps (runtime.md §3). Owns the event bus, wires the graph updater to
    /// it, and hosts the background workers. Facts flow: workers → bus → graph updater → LiteDB, with
    /// the dashboard subscribing to the same bus. Starting it with no workers (R1) is harmless — the
    /// dispatch loop simply idles until R2 adds the Run/Health workers.
    ///
    /// R1 auto-starts continuous workers; on-change/on-demand tiers are driven by the scheduler in R9.
    /// </summary>
    public sealed class RuntimeCoordinator : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly GraphUpdater _updater;
        private readonly IReadOnlyList<IRuntimeWorker> _workers;

        private CancellationTokenSource? _cts;
        private readonly List<Task> _running = new();
        private int _started;

        public RuntimeCoordinator(IEventBus bus, GraphUpdater updater, IEnumerable<IRuntimeWorker> workers)
        {
            _bus = bus;
            _updater = updater;
            _workers = workers?.ToList() ?? new List<IRuntimeWorker>();
        }

        public bool IsRunning => _started == 1;

        public IReadOnlyList<string> WorkerIds => _workers.Select(w => w.Id).ToList();

        public IEventBus Bus => _bus;

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;

            _cts = new CancellationTokenSource();
            _updater.Attach(_bus);
            _bus.Start();

            foreach (var worker in _workers.Where(w => w.Tier == WorkerTier.Continuous))
                _running.Add(Task.Run(() => SafeRunAsync(worker, _cts.Token)));
        }

        /// <summary>Trigger an on-change / on-demand worker by id (used by the scheduler / UI later).</summary>
        public void Trigger(string workerId)
        {
            if (_cts == null) return;
            var worker = _workers.FirstOrDefault(w => w.Id == workerId);
            if (worker != null)
                _running.Add(Task.Run(() => SafeRunAsync(worker, _cts.Token)));
        }

        private async Task SafeRunAsync(IRuntimeWorker worker, CancellationToken ct)
        {
            try { await worker.RunAsync(_bus, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { /* normal shutdown */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Runtime] Worker '{worker.Id}' failed: {ex.Message}");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _bus.Stop();
            _started = 0;
        }

        public void Dispose() => Stop();
    }
}
