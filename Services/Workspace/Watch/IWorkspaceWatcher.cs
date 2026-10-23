namespace Syncro.Desktop.Services.Workspace.Watch
{
    /// <summary>
    /// Watches the open workspace and turns filesystem changes into debounced <c>FileChanged</c> events
    /// on the <see cref="Events.IEventBus"/>. Lifecycle-bound to the workspace: it starts on
    /// <c>WorkspaceOpened</c> and stops on <c>WorkspaceClosed</c>, so callers just need to make sure it
    /// is constructed once (resolve it from DI at startup).
    /// </summary>
    public interface IWorkspaceWatcher
    {
        bool IsWatching { get; }
    }
}
