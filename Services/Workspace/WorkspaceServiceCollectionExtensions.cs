using Microsoft.Extensions.DependencyInjection;
using Syncro.Desktop.Services.Workspace.Diagnostics;
using Syncro.Desktop.Services.Workspace.Events;
using Syncro.Desktop.Services.Workspace.Index;
using Syncro.Desktop.Services.Workspace.Watch;

namespace Syncro.Desktop.Services.Workspace
{
    /// <summary>
    /// DI wiring for the Workspace kernel (Phase 1). Additive and optional — registering it does not
    /// change any existing behavior; it just makes the <see cref="IEventBus"/> and
    /// <see cref="IWorkspaceOrchestrator"/> available so engines can publish/subscribe. Call from
    /// MauiProgram: <c>builder.Services.AddSyncroWorkspace();</c>
    /// </summary>
    public static class WorkspaceServiceCollectionExtensions
    {
        public static IServiceCollection AddSyncroWorkspace(this IServiceCollection services)
        {
            services.AddSingleton<IEventBus, InMemoryEventBus>();
            services.AddSingleton<IWorkspaceOrchestrator, WorkspaceOrchestrator>();
            // The watcher self-subscribes to WorkspaceOpened/Closed in its constructor, so it must be
            // constructed once at startup: resolve IWorkspaceWatcher in MauiProgram after Build().
            services.AddSingleton<IWorkspaceWatcher, FileSystemWorkspaceWatcher>();
            // Phase 2: watcher-driven indexer — WorkspaceOpened/FileChanged → WorkspaceIndexer → WorkspaceIndexed.
            // Requires WorkspaceIndexer (Universe) to be registered; AddSyncroUniverse() covers it.
            services.AddSingleton<IndexEngineSubscriber>();
            // Phase 2: Problems Hub — one stream for Roslyn/LSP + security + contract + build + agent.
            // DiagnosticsSubscriber feeds it from WorkspaceIndexed/ContractDriftDetected; the editor
            // shell pushes Roslyn diagnostics in directly.
            services.AddSingleton<IProblemsHub, ProblemsHub>();
            services.AddSingleton<DiagnosticsSubscriber>();
            return services;
        }
    }
}
