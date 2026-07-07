using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class GitMcp : IMcpServer
    {
        public string Name => "Git MCP";
        public string Description => "Handles all local and remote version control.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Git.CloneRepositoryTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Git.GitStatusTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.Git.CommitTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
