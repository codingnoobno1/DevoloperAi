using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Contract;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// Verifies a project's frontend↔backend contract: detects <b>breaking drift</b> in the backend
    /// surface versus the stored baseline (e.g. a removed response field the Flutter side still reads)
    /// and, when a frontend path is given, maps calls onto routes. Pass <c>updateBaseline=true</c> to
    /// record the current backend surface as the new agreed baseline. Read-only unless updateBaseline.
    /// </summary>
    public sealed class VerifyContractTool : IMcpTool
    {
        private readonly IContractEngine _engine;

        public VerifyContractTool(IContractEngine engine) => _engine = engine;

        public string Name => "VerifyContract";

        public string Description =>
            "Verifies the frontend<->backend contract for a project. Diffs the backend's live API " +
            "surface against the stored baseline to find BREAKING changes (removed/retyped response " +
            "fields, removed routes, newly-required request fields) that break the frontend at runtime " +
            "even though it still compiles; optionally maps a frontend repo's calls onto the routes. " +
            "Set updateBaseline=true to record the current surface as the new baseline.";

        public string InputSchema =>
            "{ \"projectRoot\": \"string\", \"backendPath\": \"string\", \"frontendPath\": \"string?\", \"updateBaseline\": \"bool?\" }";

        public bool RequiresAdminApproval => false;

        private sealed class ToolInput
        {
            public string ProjectRoot { get; set; } = "";
            public string BackendPath { get; set; } = "";
            public string? FrontendPath { get; set; }
            public bool UpdateBaseline { get; set; }
        }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ToolInput>(inputJson);
                if (string.IsNullOrWhiteSpace(input?.ProjectRoot))
                    return JsonConvert.SerializeObject(new { error = "projectRoot is required." });
                if (string.IsNullOrWhiteSpace(input.BackendPath))
                    return JsonConvert.SerializeObject(new { error = "backendPath is required." });

                var r = await _engine.VerifyAsync(
                    input.ProjectRoot, input.FrontendPath, input.BackendPath, input.UpdateBaseline);

                return JsonConvert.SerializeObject(new
                {
                    ok = r.Ok,
                    summary = r.Summary,
                    hasBaseline = r.HasBaseline,
                    baselineUpdated = r.BaselineUpdated,
                    drift = r.Drift == null ? null : new
                    {
                        total = r.Drift.Total,
                        breaking = r.Drift.BreakingCount,
                        changes = r.Drift.Changes.Select(c => new
                        {
                            kind = c.Kind.ToString(),
                            target = c.Target,
                            detail = c.Detail,
                            breaking = c.Breaking
                        })
                    },
                    connection = r.Connection == null ? null : new
                    {
                        r.Connection.Matched,
                        r.Connection.ShapeMismatch,
                        r.Connection.MethodMismatch,
                        r.Connection.Missing,
                        orphanRoutes = r.Connection.OrphanRoutes.Count
                    }
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
