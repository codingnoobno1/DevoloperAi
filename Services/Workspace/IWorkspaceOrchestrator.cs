using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Workspace
{
    /// <summary>
    /// The thin orchestrator (idearchitecture.md Decision 1): owns workspace <b>lifecycle</b> and the
    /// <see cref="IEventBus"/> — nothing else. It holds NO domain logic; graph, language, build,
    /// contract, diagnostics, AI, and git all live in their own engines and coordinate through events.
    /// If a method here ever does real work, it belongs in an engine instead.
    /// </summary>
    public interface IWorkspaceOrchestrator
    {
        /// <summary>The shared event bus every engine publishes to / subscribes on.</summary>
        IEventBus Events { get; }

        /// <summary>The currently-open workspace root, or null when none is open.</summary>
        string? CurrentWorkspace { get; }

        /// <summary>Open (or switch to) a workspace root. Publishes <see cref="WorkspaceOpened"/>.</summary>
        void Open(string workspacePath);

        /// <summary>Close the current workspace. Publishes <see cref="WorkspaceClosed"/>.</summary>
        void Close();
    }
}
