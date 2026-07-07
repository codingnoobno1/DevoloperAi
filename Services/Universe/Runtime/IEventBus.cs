using System;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// The runtime event bus (runtime.md §4). Workers <see cref="Publish"/> facts; the graph updater
    /// and dashboard <see cref="Subscribe"/>. Publish is non-blocking; delivery is ordered on a single
    /// dispatch loop so subscribers never race the publisher.
    /// </summary>
    public interface IEventBus
    {
        void Publish(RuntimeEvent evt);

        /// <summary>Register an async handler. Dispose the return value to unsubscribe.</summary>
        IDisposable Subscribe(Func<RuntimeEvent, Task> handler);

        /// <summary>Synchronous fan-out hook (e.g. the dashboard push) — invoked on the dispatch loop.</summary>
        event Action<RuntimeEvent>? OnEvent;

        void Start();
        void Stop();
    }
}
