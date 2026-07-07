using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Connector.Storage;
using Syncro.Desktop.Services.Engine.Core;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// MCP server for the full Connector Engine: resolving a backend repo's API surface (swagger or
    /// route-scan), extracting a frontend repo's API calls, mapping the two into a connection graph,
    /// AI-drafting missing routes, applying approved changes in atomic batches, and persisting the
    /// result as connector.json/connector.md. Registered automatically at startup in MauiProgram.cs
    /// via <c>orchestrator.RegisterServerAsync</c>.
    /// </summary>
    public sealed class ConnectorMcp : IMcpServer
    {
        private readonly IBackendContractResolver _resolver;
        private readonly IFrontendContractResolver _frontend;
        private readonly IBackendGenerator _generator;
        private readonly IConnectorProjectStore _store;

        public ConnectorMcp(
            IBackendContractResolver resolver,
            IFrontendContractResolver frontend,
            IBackendGenerator generator,
            IConnectorProjectStore store)
        {
            _resolver = resolver;
            _frontend = frontend;
            _generator = generator;
            _store = store;
        }

        public string Name => "ConnectorMcp";

        public string Description =>
            "Connects a frontend repo to a backend repo: understands the backend's API surface " +
            "(swagger or route-scan), extracts the frontend's API calls, maps them into a connection " +
            "graph, can AI-draft missing routes, applies approved changes in atomic batches, and " +
            "persists the connection as connector.json/connector.md.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new ResolveBackendContractTool(_resolver);
            yield return new ExtractFrontendApiTool(_frontend);
            yield return new MapContractsTool(_frontend, _resolver);
            yield return new WriteBackendFilesTool();
            yield return new GenerateBackendForCallTool(_generator);
            yield return new SyncConnectorContractTool(_resolver, _store);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
