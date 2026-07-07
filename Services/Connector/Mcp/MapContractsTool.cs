using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Connector.Matching;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// The full connector pass: resolves the frontend's calls and the backend's routes, then maps
    /// them into a connection report (matched / method-mismatch / shape-mismatch / missing, plus
    /// orphan routes). Read-only — this is the data behind the connection graph.
    /// </summary>
    public sealed class MapContractsTool : IMcpTool
    {
        private readonly IFrontendContractResolver _frontend;
        private readonly IBackendContractResolver _backend;

        public MapContractsTool(IFrontendContractResolver frontend, IBackendContractResolver backend)
        {
            _frontend = frontend;
            _backend = backend;
        }

        public string Name => "MapContracts";

        public string Description =>
            "Resolves a frontend repo's API calls and a backend repo's routes, then maps each call " +
            "to a route: matched, method-mismatch, shape-mismatch, or missing. Also lists backend " +
            "routes no frontend call reaches. Read-only.";

        public string InputSchema => "{ \"frontendPath\": \"string\", \"backendPath\": \"string\" }";

        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { frontendPath = "", backendPath = "" });
                if (string.IsNullOrWhiteSpace(input?.frontendPath) || string.IsNullOrWhiteSpace(input.backendPath))
                    return JsonConvert.SerializeObject(new { error = "frontendPath and backendPath are both required." });

                var frontend = await _frontend.ResolveAsync(input.frontendPath);
                var backend = await _backend.ResolveAsync(input.backendPath);
                var report = ContractMatcher.Match(frontend.Calls, backend.Routes);

                return JsonConvert.SerializeObject(new
                {
                    frontendStack = frontend.Stack.ToString(),
                    backendStack = backend.Stack.ToString(),
                    summary = new { report.Matched, report.ShapeMismatch, report.MethodMismatch, report.Missing, orphanRoutes = report.OrphanRoutes.Count },
                    edges = report.Edges.Select(e => new
                    {
                        method = e.Call.Method.ToString().ToUpperInvariant(),
                        call = e.Call.PathTemplate,
                        route = e.Route?.PathTemplate,
                        state = e.State.ToString(),
                        diffs = e.Diffs
                    }),
                    orphanRoutes = report.OrphanRoutes.Select(r => new { method = r.Method.ToString().ToUpperInvariant(), path = r.PathTemplate })
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
