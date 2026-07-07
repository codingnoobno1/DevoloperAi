using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// Drafts backend files to implement one or more endpoints via AI. This tool never writes to
    /// disk — it returns a proposal that a human (or a Monaco diff view, once built) approves, then
    /// applies with <c>WriteBackendFilesTool</c>. Kept as a separate tool from the writer so the AI
    /// can never draft and apply in the same step.
    /// </summary>
    public sealed class GenerateBackendForCallTool : IMcpTool
    {
        private readonly IBackendGenerator _generator;

        public GenerateBackendForCallTool(IBackendGenerator generator)
        {
            _generator = generator;
        }

        public string Name => "GenerateBackendForCall";

        public string Description =>
            "Drafts backend source files (via AI) to implement one or more endpoints. Returns the " +
            "proposed files without writing them — apply the result with WriteBackendFilesTool once " +
            "reviewed.";

        public string InputSchema =>
            "{ \"stack\": \"string (Express|FastApi|Flask|Django|SpringBoot|AspNetCore)\", " +
            "\"architecture\": \"string?\", " +
            "\"endpoints\": [ { \"method\": \"string\", \"path\": \"string\", \"description\": \"string?\", " +
            "\"requestFields\": [{\"name\":\"string\",\"type\":\"string\",\"required\":\"bool\"}]?, " +
            "\"responseFields\": [{\"name\":\"string\",\"type\":\"string\"}]? } ], " +
            "\"templateGuidance\": \"string?\" }";

        public bool RequiresAdminApproval => false; // no disk access — pure LLM draft

        private sealed class FieldInput
        {
            public string Name { get; set; } = "";
            public string Type { get; set; } = "any";
            public bool Required { get; set; }
        }

        private sealed class EndpointInput
        {
            public string Method { get; set; } = "GET";
            public string Path { get; set; } = "";
            public string? Description { get; set; }
            public List<FieldInput> RequestFields { get; set; } = new();
            public List<FieldInput> ResponseFields { get; set; } = new();
        }

        private sealed class ToolInput
        {
            public string Stack { get; set; } = "";
            public string? Architecture { get; set; }
            public List<EndpointInput> Endpoints { get; set; } = new();
            public string? TemplateGuidance { get; set; }
        }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ToolInput>(inputJson);
                if (input == null || input.Endpoints.Count == 0)
                    return JsonConvert.SerializeObject(new { error = "At least one endpoint is required." });

                if (!Enum.TryParse<BackendStack>(input.Stack, ignoreCase: true, out var stack))
                    stack = BackendStack.Unknown;

                var request = new BackendGenerationRequest
                {
                    Stack = stack,
                    Architecture = input.Architecture,
                    TemplateGuidance = input.TemplateGuidance,
                    Endpoints = input.Endpoints.Select(e => new EndpointSpec
                    {
                        Method = e.Method.ToHttpVerb(),
                        PathTemplate = e.Path,
                        Description = e.Description,
                        RequestFields = e.RequestFields.Select(f => new ApiField { Name = f.Name, Type = f.Type, Required = f.Required }).ToList(),
                        ResponseFields = e.ResponseFields.Select(f => new ApiField { Name = f.Name, Type = f.Type }).ToList()
                    }).ToList()
                };

                var result = await _generator.GenerateAsync(request);

                return JsonConvert.SerializeObject(new
                {
                    success = result.Success,
                    error = result.Error,
                    explanation = result.Explanation,
                    files = result.Files.Select(f => new { f.Path, f.Content, f.Action })
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
