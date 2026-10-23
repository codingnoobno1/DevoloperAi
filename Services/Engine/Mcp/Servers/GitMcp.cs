using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Tools.Git;
using Syncro.Desktop.Services.Git;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class GitMcp : IMcpServer
    {
        public string Name => "Git MCP";
        public string Description => "Local + remote version control: history, contributors, branches, fetch, commit, push, rollback.";

        // One engine backs every git tool (git-sync-engine.md). Injected so the whole app shares one.
        private readonly IGitEngine _git;
        private readonly GitCommitDrafter _drafter;

        public GitMcp(IGitEngine git, GitCommitDrafter drafter)
        {
            _git = git;
            _drafter = drafter;
        }

        public IEnumerable<IMcpTool> GetTools()
        {
            // Existing tools (kept for compatibility).
            yield return new CloneRepositoryTool();
            yield return new GitStatusTool();
            yield return new CommitTool();

            // Engine-backed surface — read (safe).
            yield return new GitLogTool(_git);
            yield return new GitContributorsTool(_git);
            yield return new GitBranchesTool(_git);
            yield return new GitFetchTool(_git);
            yield return new DraftCommitMessageTool(_drafter);

            // Write / outward (approval-gated).
            yield return new GitPushTool(_git);
            yield return new GitRollbackTool(_git);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
