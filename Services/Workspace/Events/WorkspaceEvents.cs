using System.Collections.Generic;

namespace Syncro.Desktop.Services.Workspace.Events
{
    /// <summary>
    /// Base for every event on the workspace <see cref="IEventBus"/>. Events are the ONLY way engines
    /// communicate across boundaries (idearchitecture.md guardrail #2) — no engine calls another's
    /// internals. Kept as immutable records carrying primitives so no engine leaks its own types onto
    /// the bus.
    /// </summary>
    public abstract record WorkspaceEvent(string WorkspacePath);

    public sealed record WorkspaceOpened(string WorkspacePath) : WorkspaceEvent(WorkspacePath);

    public sealed record WorkspaceClosed(string WorkspacePath) : WorkspaceEvent(WorkspacePath);

    public enum FileChangeKind { Created, Modified, Deleted, Renamed }

    public sealed record FileChanged(string WorkspacePath, string FilePath, FileChangeKind Kind)
        : WorkspaceEvent(WorkspacePath);

    /// <summary>Emitted after an incremental index pass completes (Phase 2 will raise this).</summary>
    public sealed record WorkspaceIndexed(string WorkspacePath, int FilesIndexed)
        : WorkspaceEvent(WorkspacePath);

    /// <summary>
    /// Emitted by the BuildEngine after any build/analyze pass (idearchitecture.md Decision 2).
    /// Carries counts + a summary only; the full <c>BuildResult</c> with per-diagnostic detail is
    /// returned to the caller, so subscribers that need it ask the engine rather than the bus.
    /// </summary>
    public sealed record BuildDone(
        string WorkspacePath,
        bool Ok,
        string Stack,
        int ErrorCount,
        int WarningCount,
        string Summary) : WorkspaceEvent(WorkspacePath);

    /// <summary>
    /// Raised by the Contract Engine when the backend surface drifts from the baseline in a way that
    /// breaks consumers. Carries primitives only so subscribers (diagnostics hub, AI, UI) need no
    /// dependency on the Connector types.
    /// </summary>
    public sealed record ContractDriftDetected(
        string WorkspacePath,
        int BreakingCount,
        int TotalChanges,
        IReadOnlyList<string> BreakingSummaries) : WorkspaceEvent(WorkspacePath);
}
