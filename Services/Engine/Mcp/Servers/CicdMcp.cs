using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Tools.Cicd;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    /// <summary>CI/CD pipeline authoring — generates/writes GitHub Actions workflows (git-sync-engine.md / cicd).</summary>
    public class CicdMcp : IMcpServer
    {
        public string Name => "CI/CD MCP";
        public string Description => "Generates and writes CI pipelines (build + test + security) for a repo.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new GeneratePipelineTool();   // safe — returns YAML
            yield return new WritePipelineTool();      // gated — writes to disk
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
