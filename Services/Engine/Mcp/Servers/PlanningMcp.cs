using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class PlanningMcp : IMcpServer
    {
        public string Name => "Planning MCP";
        public string Description => "Deconstructs a user goal into discrete, achievable tasks.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Planning.CreateTaskGraphTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Planning.UpdateTaskStatusTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
