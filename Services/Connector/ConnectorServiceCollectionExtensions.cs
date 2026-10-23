using Microsoft.Extensions.DependencyInjection;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Codegen;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Connector.Mcp;
using Syncro.Desktop.Services.Connector.Storage;

namespace Syncro.Desktop.Services.Connector
{
    /// <summary>
    /// DI wiring for the Connector Engine. Additive and optional — calling this does not affect
    /// any existing service. Wire it up in MauiProgram.cs when the connector UI/CLI is enabled:
    /// <code>builder.Services.AddSyncroConnector();</code>
    /// Depends on <c>ILLMProvider</c> already being registered (see MauiProgram.cs) for
    /// <see cref="BackendGenerator"/>'s AI route drafting.
    /// </summary>
    public static class ConnectorServiceCollectionExtensions
    {
        public static IServiceCollection AddSyncroConnector(this IServiceCollection services)
        {
            services.AddSingleton<IBackendContractResolver, BackendContractResolver>();
            // Frontend resolution is AST-first: ThunderScanFrontendResolver consumes a prior
            // `thunder scan` (connector/api.json) when present, else delegates to the heuristic resolver.
            services.AddSingleton<FrontendContractResolver>();
            services.AddSingleton<IFrontendContractResolver>(sp =>
                new ThunderScanFrontendResolver(sp.GetRequiredService<FrontendContractResolver>()));
            services.AddSingleton<IBackendGenerator, BackendGenerator>();
            services.AddSingleton<IConnectorProjectStore, ConnectorProjectStore>();
            // Contract Engine — first-class owner of the frontend<->backend contract + breaking-drift
            // detection (baseline snapshot in <projectRoot>/.syncro/contract.baseline.json).
            services.AddSingleton<Contract.ContractSnapshotStore>();
            services.AddSingleton<Contract.IContractEngine, Contract.ContractEngine>();
            services.AddSingleton<Contract.IContractProjectLocator, Contract.ConnectorProjectLocator>();
            // Auto-reverify: on a backend/frontend file change (from the Workspace watcher) re-run the
            // contract check so drift fires on save. Requires AddSyncroWorkspace(); resolve this once at
            // startup so it subscribes to the bus.
            services.AddSingleton<Contract.ContractReverifySubscriber>();
            services.AddSingleton<IFrontendRunnerService, FrontendRunnerService>();
            // Multi-project discovery + scan (points at a root folder, resolves every project in it).
            services.AddSingleton<ProjectDomain.WorkspaceDiscoveryService>();
            services.AddSingleton<Scan.MultiProjectScanService>();
            services.AddSingleton<ConnectorMcp>();
            // Phase 3 codegen: ContractDriftDetected → DartBlocCodegen → Dart files written.
            services.AddSingleton<DartBlocCodegen>();
            services.AddSingleton<ContractDriftCodegenSubscriber>();
            return services;
        }
    }
}
