using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// Extracts the API-call surface of a frontend repo (Flutter/Next/React/Vite/Angular/Blazor)
    /// into a normalized list of calls. Read-only.
    /// </summary>
    public sealed class ExtractFrontendApiTool : IMcpTool
    {
        private readonly IFrontendContractResolver _resolver;

        public ExtractFrontendApiTool(IFrontendContractResolver resolver)
        {
            _resolver = resolver;
        }

        public string Name => "ExtractFrontendApi";

        public string Description =>
            "Scans a frontend repository and returns every HTTP call it makes (method + path), " +
            "with the detected frontend stack. Read-only.";

        public string InputSchema => "{ \"frontendPath\": \"string\" }";

        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { frontendPath = "" });
                if (string.IsNullOrWhiteSpace(input?.frontendPath))
                    return JsonConvert.SerializeObject(new { error = "frontendPath is required." });

                var contract = await _resolver.ResolveAsync(input.frontendPath);

                return JsonConvert.SerializeObject(new
                {
                    frontendPath = contract.FrontendPath,
                    stack = contract.Stack.ToString(),
                    callCount = contract.Calls.Count,
                    notes = contract.Notes,
                    calls = contract.Calls.Select(c => new
                    {
                        method = c.Method.ToString().ToUpperInvariant(),
                        path = c.PathTemplate,
                        client = c.Client,
                        file = c.FilePath,
                        line = c.Line
                    })
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
