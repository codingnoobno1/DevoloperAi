using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Verification;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class PatchMcp : IMcpServer
    {
        public string Name => "Patch MCP";
        public string Description => "Surgically modifies existing code without destroying the file. Every edit is build- and test-verified before it is allowed to persist.";

        private readonly IVerificationEngine _verify;

        public PatchMcp(IVerificationEngine verify) => _verify = verify;

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Patch.ApplyPatchTool(_verify);
            yield return new Syncro.Desktop.Services.Engine.Tools.Patch.ValidatePatchTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Patch.RollbackPatchTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
