using Microsoft.Extensions.DependencyInjection;
using Syncro.Desktop.Services.Connector.Backend;
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
            services.AddSingleton<IFrontendContractResolver, FrontendContractResolver>();
            services.AddSingleton<IBackendGenerator, BackendGenerator>();
            services.AddSingleton<IConnectorProjectStore, ConnectorProjectStore>();
            services.AddSingleton<IFrontendRunnerService, FrontendRunnerService>();
            services.AddSingleton<ConnectorMcp>();
            return services;
        }
    }
}
