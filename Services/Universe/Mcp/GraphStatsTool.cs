using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe.Mcp
{
    /// <summary>Reports the current size and membership of the Universe graph. Read-only.</summary>
    public sealed class GraphStatsTool : IMcpTool
    {
        private readonly UniverseGraphStore _store;

        public GraphStatsTool(UniverseGraphStore store)
        {
            _store = store;
        }

        public string Name => "UniverseGraphStats";

        public string Description =>
            "Returns the Universe graph size (node/edge/workspace counts) and the list of registered " +
            "workspaces with their detected stacks. Read-only.";

        public string InputSchema => "{ }";

        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var (nodes, edges, workspaces) = _store.Stats();
                var list = _store.Workspaces().Select(w => new
                {
                    w.Id, w.Name, w.Path, stacks = w.Stacks, lastIndexed = w.LastIndexedAt
                });

                return Task.FromResult(JsonConvert.SerializeObject(new
                {
                    nodes, edges, workspaces, list
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }
}
