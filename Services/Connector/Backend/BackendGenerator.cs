using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Default <see cref="IBackendGenerator"/>. Prompts <see cref="ILLMProvider"/> for strict JSON
    /// describing the files needed to satisfy the requested endpoints, validates the shape before
    /// trusting it (same defensive parsing <c>MobilePreview.razor</c> uses for its AI responses),
    /// and returns a proposal — it never writes to disk.
    /// </summary>
    public sealed class BackendGenerator : IBackendGenerator
    {
        private readonly ILLMProvider _llm;

        public BackendGenerator(ILLMProvider llm)
        {
            _llm = llm;
        }

        public async Task<BackendGenerationResult> GenerateAsync(BackendGenerationRequest request, CancellationToken ct = default)
        {
            if (request.Endpoints == null || request.Endpoints.Count == 0)
                return new BackendGenerationResult { Success = false, Error = "At least one endpoint is required." };

            ct.ThrowIfCancellationRequested();

            string systemContext = BuildSystemContext(request);
            string userPrompt = BuildUserPrompt(request);

            string raw;
            try
            {
                raw = await _llm.GenerateResponseAsync(systemContext, userPrompt);
            }
            catch (Exception ex)
            {
                return new BackendGenerationResult { Success = false, Error = $"LLM call failed: {ex.Message}" };
            }

            if (string.IsNullOrWhiteSpace(raw))
                return new BackendGenerationResult { Success = false, Error = "AI returned an empty response." };

            return ParseResponse(raw);
        }

        private static string BuildSystemContext(BackendGenerationRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are generating backend source files for an existing repository.");
            sb.AppendLine($"Target stack: {request.Stack}");
            if (!string.IsNullOrWhiteSpace(request.Architecture))
                sb.AppendLine($"Target architecture: {request.Architecture}");

            if (request.ExistingRoutes.Count > 0)
            {
                sb.AppendLine("Routes the backend ALREADY exposes (do not duplicate or conflict with these):");
                foreach (var r in request.ExistingRoutes)
                    sb.AppendLine($"  - {r.Method.ToString().ToUpperInvariant()} {r.PathTemplate}");
            }

            if (!string.IsNullOrWhiteSpace(request.TemplateGuidance))
            {
                sb.AppendLine("Reference template for this stack's folder conventions and style:");
                sb.AppendLine(request.TemplateGuidance);
            }

            sb.AppendLine(
                "CRITICAL: Respond ONLY with valid JSON matching this schema: " +
                "{ \"explanation\": \"summary\", \"files\": [ { \"path\": \"relative/path\", " +
                "\"content\": \"...\", \"action\": \"create|modify\" } ] }. " +
                "Paths must be relative to the backend root. Do NOT return markdown or prose outside the JSON.");

            return sb.ToString();
        }

        private static string BuildUserPrompt(BackendGenerationRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Generate the files needed to implement the following endpoint(s):");
            foreach (var ep in request.Endpoints)
            {
                sb.AppendLine($"- {ep.Method.ToString().ToUpperInvariant()} {ep.PathTemplate}");
                if (!string.IsNullOrWhiteSpace(ep.Description))
                    sb.AppendLine($"  Description: {ep.Description}");
                if (ep.RequestFields.Count > 0)
                    sb.AppendLine($"  Request fields: {string.Join(", ", ep.RequestFields.Select(f => $"{f.Name}:{f.Type}{(f.Required ? "" : "?")}"))}");
                if (ep.ResponseFields.Count > 0)
                    sb.AppendLine($"  Response fields: {string.Join(", ", ep.ResponseFields.Select(f => $"{f.Name}:{f.Type}"))}");
            }
            return sb.ToString();
        }

        private static BackendGenerationResult ParseResponse(string raw)
        {
            string cleaned = raw.Trim();
            int firstBrace = cleaned.IndexOf('{');
            int lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);

            JObject json;
            try
            {
                json = JObject.Parse(cleaned);
            }
            catch (JsonException ex)
            {
                return new BackendGenerationResult { Success = false, Error = $"AI response was not valid JSON: {ex.Message}" };
            }

            var filesToken = json["files"] as JArray;
            if (filesToken == null)
                return new BackendGenerationResult { Success = false, Error = "AI response is missing a 'files' array." };

            var result = new BackendGenerationResult
            {
                Explanation = json["explanation"]?.ToString()
            };

            foreach (var f in filesToken)
            {
                var path = f["path"]?.ToString();
                var content = f["content"]?.ToString();
                var action = f["action"]?.ToString();

                if (string.IsNullOrWhiteSpace(path) || content == null)
                    return new BackendGenerationResult { Success = false, Error = "Every generated file needs a non-empty 'path' and 'content'." };

                result.Files.Add(new GeneratedBackendFile
                {
                    Path = path,
                    Content = content,
                    Action = string.Equals(action, "modify", StringComparison.OrdinalIgnoreCase) ? "modify" : "create"
                });
            }

            if (result.Files.Count == 0)
                return new BackendGenerationResult { Success = false, Error = "AI proposed zero files." };

            result.Success = true;
            return result;
        }
    }
}
