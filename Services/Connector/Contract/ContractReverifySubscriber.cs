using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>
    /// Closes the loop between the watcher (Phase 2) and the Contract Engine (Phase 3): when a file
    /// under a connected project's backend (or frontend) tree changes, it re-runs
    /// <see cref="IContractEngine.VerifyAsync"/>, which publishes <c>ContractDriftDetected</c> on
    /// breaking drift. Edits are coalesced per workspace and debounced so a burst of saves triggers one
    /// verify.
    ///
    /// The <c>FileChanged</c> handler is cheap and synchronous (it only records the changed path);
    /// the async locate+verify happens on <see cref="FlushAsync"/>, which the debounce timer calls (and
    /// tests call directly for determinism).
    /// </summary>
    public sealed class ContractReverifySubscriber : IDisposable
    {
        private readonly IContractEngine _engine;
        private readonly IContractProjectLocator _locator;
        private readonly IDisposable _subscription;
        private readonly object _gate = new();
        private readonly Dictionary<string, HashSet<string>> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Timers.Timer? _timer;

        public ContractReverifySubscriber(
            IEventBus bus,
            IContractEngine engine,
            IContractProjectLocator locator,
            double debounceMs = 800)
        {
            _engine = engine;
            _locator = locator;
            _subscription = bus.Subscribe<FileChanged>(OnFileChanged);

            if (debounceMs > 0)
            {
                _timer = new System.Timers.Timer(debounceMs) { AutoReset = true };
                _timer.Elapsed += async (_, _) => await FlushAsync();
                _timer.Start();
            }
        }

        private void OnFileChanged(FileChanged e)
        {
            lock (_gate)
            {
                if (!_pending.TryGetValue(e.WorkspacePath, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    _pending[e.WorkspacePath] = set;
                }
                set.Add(e.FilePath);
            }
        }

        /// <summary>Re-verify every workspace with pending changes that touched its backend/frontend
        /// tree. Public so tests can pump it deterministically instead of waiting on the timer.</summary>
        public async Task FlushAsync()
        {
            List<KeyValuePair<string, HashSet<string>>> batch;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                batch = new List<KeyValuePair<string, HashSet<string>>>(_pending);
                _pending.Clear();
            }

            foreach (var (root, changed) in batch)
            {
                try
                {
                    var (backend, frontend) = await _locator.LocateAsync(root);
                    if (string.IsNullOrWhiteSpace(backend)) continue;   // not a connected project

                    var touchesContract = false;
                    foreach (var path in changed)
                        if (RelevanceFilter.IsUnder(path, backend) || RelevanceFilter.IsUnder(path, frontend))
                        {
                            touchesContract = true;
                            break;
                        }
                    if (!touchesContract) continue;

                    // VerifyAsync publishes ContractDriftDetected itself when it finds breaking drift.
                    await _engine.VerifyAsync(root, frontend, backend!);
                }
                catch
                {
                    // A reverify failure must never take down the reactor; the next change retries.
                }
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _subscription.Dispose();
        }
    }
}
