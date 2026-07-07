using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Universe.Indexing;

namespace Syncro.Desktop.Services.Universe.Mcp
{
    /// <summary>
    /// Registers a folder as a workspace in the Universe and populates the graph from it (AST
    /// symbols + Connector endpoints/calls). Idempotent — re-running refreshes the workspace.
    /// </summary>
    public sealed class IndexWorkspaceTool : IMcpTool
    {
        private readonly WorkspaceIndexer _indexer;

        public IndexWorkspaceTool(WorkspaceIndexer indexer)
        {
            _indexer = indexer;
        }

        public string Name => "IndexWorkspace";

        public string Description =>
            "Registers a folder as a Universe workspace and indexes it into the cross-workspace graph " +
            "(files, symbols, endpoints, API calls, and intra-workspace consumes edges).";

        public string InputSchema => "{ \"workspacePath\": \"string\" }";

        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "" });
                if (string.IsNullOrWhiteSpace(input?.workspacePath))
                    return JsonConvert.SerializeObject(new { error = "workspacePath is required." });

                var r = await _indexer.IndexAsync(input.workspacePath);

                return JsonConvert.SerializeObject(new
                {
                    workspaceId = r.WorkspaceId,
                    r.Files, r.Symbols, r.Endpoints, r.Calls, r.Edges,
                    stacks = r.Stacks,
                    notes = r.Notes
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
