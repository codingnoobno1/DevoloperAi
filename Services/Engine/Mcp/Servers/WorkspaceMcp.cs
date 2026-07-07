using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class WorkspaceMcp : IMcpServer
    {
        public string Name => "Workspace MCP";
        public string Description => "Handles the high-level boundaries and live environment state of the project.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Workspace.ListProjectsTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Workspace.WorkspaceSummaryTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
