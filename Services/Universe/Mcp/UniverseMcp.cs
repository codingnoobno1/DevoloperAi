using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Universe.Indexing;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe.Mcp
{
    /// <summary>
    /// MCP server for the Universe layer (universe.md). Phase U2 exposes graph population and stats;
    /// cross-workspace mapping (U3), impact traversal (U5), and retrieval (U6) add tools here later.
    /// Registered at startup in MauiProgram via <c>orchestrator.RegisterServerAsync</c>.
    /// </summary>
    public sealed class UniverseMcp : IMcpServer
    {
        private readonly WorkspaceIndexer _indexer;
        private readonly UniverseGraphStore _store;
        private readonly CrossWorkspaceMapper _mapper;

        public UniverseMcp(WorkspaceIndexer indexer, UniverseGraphStore store, CrossWorkspaceMapper mapper)
        {
            _indexer = indexer;
            _store = store;
            _mapper = mapper;
        }

        public string Name => "UniverseMcp";

        public string Description =>
            "The Universe: a persistent cross-workspace graph of files, symbols, endpoints and API " +
            "calls. Index workspaces into it, map who-calls-whom across workspaces, and query it.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new IndexWorkspaceTool(_indexer);
            yield return new GraphStatsTool(_store);
            yield return new MapUniverseTool(_mapper);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    /// <summary>Runs the cross-workspace API mapper and reports per-endpoint consumers. Read-only-ish
    /// (writes only Consumes/DependsOn edges into the graph).</summary>
    public sealed class MapUniverseTool : IMcpTool
    {
        private readonly CrossWorkspaceMapper _mapper;
        public MapUniverseTool(CrossWorkspaceMapper mapper) => _mapper = mapper;

        public string Name => "MapUniverse";
        public string Description =>
            "Links every frontend API call to the backend endpoint it consumes across ALL indexed " +
            "workspaces, and reports which workspaces consume each endpoint (dead endpoints have none).";
        public string InputSchema => "{ }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var s = _mapper.MapAll();
                return Task.FromResult(Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    crossWorkspaceEdges = s.CrossEdges,
                    workspaceDependencies = s.WorkspaceDependencies,
                    endpoints = s.Endpoints.Select(e => new { e.Endpoint, provider = e.Workspace, consumers = e.Consumers })
                }));
            }
            catch (System.Exception ex)
            {
                return Task.FromResult(Newtonsoft.Json.JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }
}
