using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Workspace
{
    /// <inheritdoc cref="IWorkspaceOrchestrator"/>
    public sealed class WorkspaceOrchestrator : IWorkspaceOrchestrator
    {
        public IEventBus Events { get; }
        public string? CurrentWorkspace { get; private set; }

        public WorkspaceOrchestrator(IEventBus events) => Events = events;

        public void Open(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath)) return;

            // Switching workspaces closes the previous one first.
            if (CurrentWorkspace is { } prev && prev != workspacePath)
                Events.Publish(new WorkspaceClosed(prev));

            CurrentWorkspace = workspacePath;
            Events.Publish(new WorkspaceOpened(workspacePath));
        }

        public void Close()
        {
            if (CurrentWorkspace is not { } path) return;
            CurrentWorkspace = null;
            Events.Publish(new WorkspaceClosed(path));
        }
    }
}
