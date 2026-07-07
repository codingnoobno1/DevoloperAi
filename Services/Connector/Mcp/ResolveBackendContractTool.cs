using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// MCP tool: resolves a backend repo's API surface (swagger → route-scan → empty) and returns it
    /// as JSON for the agent / connector graph. Read-only, no approval required.
    /// </summary>
    public sealed class ResolveBackendContractTool : IMcpTool
    {
        private readonly IBackendContractResolver _resolver;

        public ResolveBackendContractTool(IBackendContractResolver? resolver = null)
        {
            _resolver = resolver ?? new BackendContractResolver();
        }

        public string Name => "ResolveBackendContract";

        public string Description =>
            "Resolves the HTTP API surface of a backend repository. Prefers a swagger/openapi contract, " +
            "falls back to scanning source for route declarations (Express, FastAPI, Flask, Spring, ASP.NET). " +
            "Returns the detected stack and a normalized list of routes (method + path + shapes).";

        public string InputSchema => "{ \"backendPath\": \"string\" }";

        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { backendPath = "" });
                if (string.IsNullOrWhiteSpace(input?.backendPath))
                    return "{ \"error\": \"backendPath is required.\" }";

                var contract = await _resolver.ResolveAsync(input.backendPath);

                var result = new
                {
                    backendPath = contract.BackendPath,
                    stack = contract.Stack.ToString(),
                    primarySource = contract.PrimarySource.ToString(),
                    swaggerPath = contract.SwaggerPath,
                    totalRoutes = contract.Routes.Count,
                    routes = contract.Routes.Select(r => new
                    {
                        id = r.Id,
                        method = r.Method.ToString().ToUpperInvariant(),
                        path = r.PathTemplate,
                        source = r.Source.ToString(),
                        operationId = r.OperationId,
                        summary = r.Summary,
                        file = r.FilePath,
                        line = r.Line,
                        request = r.RequestSchema.Select(f => new { f.Name, f.Type, f.Required }),
                        response = r.ResponseSchema.Select(f => new { f.Name, f.Type, f.Required })
                    }),
                    notes = contract.Notes
                };

                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
