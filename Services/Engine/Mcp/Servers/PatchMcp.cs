using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class PatchMcp : IMcpServer
    {
        public string Name => "Patch MCP";
        public string Description => "Surgically modifies existing code without destroying the file.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Patch.ApplyPatchTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Patch.ValidatePatchTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Patch.RollbackPatchTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
