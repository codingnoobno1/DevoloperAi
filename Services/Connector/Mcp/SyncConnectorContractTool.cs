using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Storage;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// Re-resolves a backend repo's API surface and persists it into connector.json (+ a
    /// regenerated connector.md summary) at <c>projectRoot</c>. The frontend section, if any, is
    /// preserved untouched — this only ever updates the backend side until the frontend extractor
    /// lands.
    /// </summary>
    public sealed class SyncConnectorContractTool : IMcpTool
    {
        private readonly IBackendContractResolver _resolver;
        private readonly IConnectorProjectStore _store;

        public SyncConnectorContractTool(IBackendContractResolver resolver, IConnectorProjectStore store)
        {
            _resolver = resolver;
            _store = store;
        }

        public string Name => "SyncConnectorContract";

        public string Description =>
            "Resolves a backend repo's current API surface and writes it into connector.json + " +
            "connector.md at projectRoot. Frontend info, if present, is left untouched.";

        public string InputSchema =>
            "{ \"projectRoot\": \"string\", \"backendPath\": \"string\", \"runCommand\": \"string?\", \"port\": \"int?\" }";

        public bool RequiresAdminApproval => false;

        private sealed class ToolInput
        {
            public string ProjectRoot { get; set; } = "";
            public string BackendPath { get; set; } = "";
            public string? RunCommand { get; set; }
            public int? Port { get; set; }
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

                var contract = await _resolver.ResolveAsync(input.BackendPath);
                var project = await _store.UpsertBackendAsync(input.ProjectRoot, contract, input.RunCommand, input.Port);

                return JsonConvert.SerializeObject(new
                {
                    success = true,
                    projectRoot = input.ProjectRoot,
                    backendStack = contract.Stack.ToString(),
                    routeCount = contract.Routes.Count,
                    notes = contract.Notes,
                    project
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
