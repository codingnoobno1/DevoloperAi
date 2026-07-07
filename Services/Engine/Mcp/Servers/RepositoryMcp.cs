using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class RepositoryMcp : IMcpServer
    {
        public string Name => "Repository MCP";
        public string Description => "Scans the codebase at a macro level to build structural graphs and find configurations.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Repository.DetectProjectTypeTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Repository.RepositoryTreeTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Repository.ScanRepositoryTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
