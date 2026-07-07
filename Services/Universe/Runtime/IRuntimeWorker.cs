using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>How often a worker runs — the scheduler tiers from runtime.md §5.</summary>
    public enum WorkerTier
    {
        Continuous,   // always-on loop (Run, Health)
        OnChange,     // triggered by a file-change debounce (AST, ApiMap, Dependency, Schema, Git)
        OnDemand      // user click or slow periodic (Build, Test, Docker refresh)
    }

    /// <summary>
    /// A background worker in the Runtime Engine. Workers observe some slice of the ecosystem and
    /// <see cref="IEventBus.Publish"/> facts about it — no LLM involved. The coordinator hosts them;
    /// R2+ adds concrete workers (Run, Health, …). R1 ships the contract and an empty roster.
    /// </summary>
    public interface IRuntimeWorker
    {
        string Id { get; }
        WorkerTier Tier { get; }
        Task RunAsync(IEventBus bus, CancellationToken ct);
    }
}
