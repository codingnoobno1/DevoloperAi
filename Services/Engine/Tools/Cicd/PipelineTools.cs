using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Cicd;
using Syncro.Desktop.Services.Connector.ProjectDomain;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Cicd
{
    internal static class PipelineStackDetect
    {
        /// <summary>Infer the pipeline stack for a repo (uses the connector domain detector).</summary>
        public static PipelineStack Detect(string path, string? explicitStack)
        {
            if (!string.IsNullOrWhiteSpace(explicitStack))
                return PipelineBuilder.FromStack(explicitStack!);
            try
            {
                var node = new ConnectorWorkspaceNode { Path = path };
                DomainDetector.Detect(node);
                return PipelineBuilder.FromStack(node.Stack);
            }
            catch { return PipelineStack.Generic; }
        }
    }

    /// <summary>Generate a CI pipeline (YAML) for a repo — text only, safe.</summary>
    public sealed class GeneratePipelineTool : IMcpTool
    {
        public string Name => "GeneratePipeline";
        public string Description => "Generates a GitHub Actions CI pipeline (build + test + Trivy/Gitleaks security) for a repo. Returns YAML — does not write it.";
        public string InputSchema => "{ \"path\": \"string\", \"stack\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { path = "", stack = "" });
            if (string.IsNullOrWhiteSpace(i?.path))
                return Task.FromResult(JsonConvert.SerializeObject(new { error = "path required" }));

            var stack = PipelineStackDetect.Detect(i.path, i.stack);
            var p = new PipelineBuilder().Build(stack);
            return Task.FromResult(JsonConvert.SerializeObject(new { stack = stack.ToString(), file = p.Path, yaml = p.Yaml }));
        }
    }

    /// <summary>Write the generated CI pipeline into the repo — modifies the working tree, gated.</summary>
    public sealed class WritePipelineTool : IMcpTool
    {
        public string Name => "WritePipeline";
        public string Description => "Writes a GitHub Actions CI pipeline to .github/workflows/ci.yml in the repo. Modifies the working tree — requires approval in Syncro Desktop.";
        public string InputSchema => "{ \"path\": \"string\", \"stack\": \"string\" }";
        public bool RequiresAdminApproval => true;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { path = "", stack = "" });
            if (string.IsNullOrWhiteSpace(i?.path) || !Directory.Exists(i.path))
                return JsonConvert.SerializeObject(new { error = "existing repo path required" });

            try
            {
                var stack = PipelineStackDetect.Detect(i.path, i.stack);
                var p = new PipelineBuilder().Build(stack);
                var full = Path.Combine(i.path, p.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                await File.WriteAllTextAsync(full, p.Yaml);
                return JsonConvert.SerializeObject(new { ok = true, written = full, stack = stack.ToString() });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
