using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class KnowledgeMcp : IMcpServer
    {
        public string Name => "Knowledge MCP";
        public string Description => "Maintains session memory, past decisions, and hindsight.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Knowledge.StoreHindsightTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Knowledge.QueryHindsightTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Knowledge.UpdateArchitectureTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
