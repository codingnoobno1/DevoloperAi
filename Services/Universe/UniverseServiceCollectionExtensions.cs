using Microsoft.Extensions.DependencyInjection;
using Syncro.Desktop.Services.Universe.Indexing;
using Syncro.Desktop.Services.Universe.Mcp;
using Syncro.Desktop.Services.Universe.Runtime;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe
{
    /// <summary>
    /// DI wiring for the Universe backend (universe.md U1–U2, runtime.md R1). Registers the persistent
    /// graph store, the workspace registry, the indexer, the MCP server, and the runtime engine
    /// (event bus + graph updater + coordinator). The <c>IKnowledgeEngine</c> swap to
    /// <see cref="UniverseKnowledgeEngine"/> is done in MauiProgram alongside the Engine registrations.
    /// </summary>
    public static class UniverseServiceCollectionExtensions
    {
        public static IServiceCollection AddSyncroUniverse(this IServiceCollection services)
        {
            services.AddSingleton<UniverseGraphStore>();
            services.AddSingleton<UniverseRegistry>();
            services.AddSingleton<WorkspaceIndexer>();
            services.AddSingleton<CrossWorkspaceMapper>();   // R5 N-consumer mapping
            services.AddSingleton<UniverseMcp>();

            // Runtime Engine (runtime.md R1) — event bus + graph updater + coordinator.
            services.AddSingleton<IEventBus, EventBus>();
            services.AddSingleton<GraphUpdater>();
            services.AddSingleton<RuntimeCoordinator>();

            // Runtime Engine (runtime.md R2) — Run manager + Health worker + control MCP.
            services.AddSingleton<EnvironmentBootstrapper>();   // R4 pre-scripts
            services.AddSingleton<Dependencies.DependencyReconciler>();  // R6 AST→env auto-install
            services.AddSingleton<StackRunManager>();
            services.AddSingleton<GroupRunOrchestrator>();   // multi-project parallel run (waves)
            services.AddSingleton<IRuntimeWorker, HealthWorker>();
            services.AddSingleton<IRuntimeWorker, ApiMapWorker>();   // R5 continuous mapping
            services.AddSingleton<Mcp.RuntimeMcp>();
            return services;
        }
    }
}
