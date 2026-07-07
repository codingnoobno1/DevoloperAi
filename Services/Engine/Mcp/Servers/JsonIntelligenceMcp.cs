using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class JsonIntelligenceMcp : IMcpServer
    {
        public string Name => "JSON Intelligence MCP";
        public string Description => "Dynamically maps, infers, and validates complex JSON structures.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Json.ExtractSchemaTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Json.ValidatePayloadTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
