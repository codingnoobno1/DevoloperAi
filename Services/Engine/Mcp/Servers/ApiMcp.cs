using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class ApiMcp : IMcpServer
    {
        public string Name => "API MCP";
        public string Description => "Understands network requests, endpoints, and OpenAPI schemas.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Api.FetchEndpointTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Api.ParseSwaggerTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
