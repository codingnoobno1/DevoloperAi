using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Git;

namespace Syncro.Desktop.Services.Engine.Tools.Git
{
    // ── read tools (safe) ──────────────────────────────────────────────────────

    /// <summary>Recent commit history — the "previous commit list".</summary>
    public sealed class GitLogTool : IMcpTool
    {
        private readonly IGitEngine _git;
        public GitLogTool(IGitEngine git) => _git = git;
        public string Name => "GitLog";
        public string Description => "Lists recent commits (sha, author, date, subject) for a repo.";
        public string InputSchema => "{ \"repo\": \"string\", \"count\": \"number\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "", count = 20 });
            if (string.IsNullOrWhiteSpace(i?.repo)) return Err("repo required");
            var commits = await _git.LogAsync(i.repo, i.count <= 0 ? 20 : i.count);
            return JsonConvert.SerializeObject(new { count = commits.Count, commits });
        }
        private static string Err(string m) => JsonConvert.SerializeObject(new { error = m });
    }

    /// <summary>Contributors ranked by commit count.</summary>
    public sealed class GitContributorsTool : IMcpTool
    {
        private readonly IGitEngine _git;
        public GitContributorsTool(IGitEngine git) => _git = git;
        public string Name => "GitContributors";
        public string Description => "Lists repo contributors with their commit counts (across all branches).";
        public string InputSchema => "{ \"repo\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "" });
            if (string.IsNullOrWhiteSpace(i?.repo)) return Err("repo required");
            var c = await _git.ContributorsAsync(i.repo);
            return JsonConvert.SerializeObject(new { count = c.Count, contributors = c });
        }
        private static string Err(string m) => JsonConvert.SerializeObject(new { error = m });
    }

    /// <summary>Branches (local + remote) with current + upstream.</summary>
    public sealed class GitBranchesTool : IMcpTool
    {
        private readonly IGitEngine _git;
        public GitBranchesTool(IGitEngine git) => _git = git;
        public string Name => "GitBranches";
        public string Description => "Lists local and remote branches, marking the current one and upstreams.";
        public string InputSchema => "{ \"repo\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "" });
            if (string.IsNullOrWhiteSpace(i?.repo)) return Err("repo required");
            var b = await _git.BranchesAsync(i.repo);
            var status = await _git.StatusAsync(i.repo);
            return JsonConvert.SerializeObject(new { current = status.Branch, ahead = status.Ahead, behind = status.Behind, branches = b });
        }
        private static string Err(string m) => JsonConvert.SerializeObject(new { error = m });
    }

    /// <summary>Fetch from remotes (read-only; updates ahead/behind).</summary>
    public sealed class GitFetchTool : IMcpTool
    {
        private readonly IGitEngine _git;
        public GitFetchTool(IGitEngine git) => _git = git;
        public string Name => "GitFetch";
        public string Description => "Fetches the latest from all remotes (prune). Read-only — does not modify the working tree.";
        public string InputSchema => "{ \"repo\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "" });
            if (string.IsNullOrWhiteSpace(i?.repo)) return JsonConvert.SerializeObject(new { error = "repo required" });
            var r = await _git.FetchAsync(i.repo);
            var s = await _git.StatusAsync(i.repo);
            return JsonConvert.SerializeObject(new { ok = r.Ok, output = r.Output, error = r.Error, ahead = s.Ahead, behind = s.Behind });
        }
    }

    /// <summary>Draft a Conventional-Commits message from the pending diff (LLM). Text only — safe.</summary>
    public sealed class DraftCommitMessageTool : IMcpTool
    {
        private readonly GitCommitDrafter _drafter;
        public DraftCommitMessageTool(GitCommitDrafter drafter) => _drafter = drafter;
        public string Name => "DraftCommitMessage";
        public string Description => "Drafts a Conventional-Commits message from a repo's pending changes. Returns text only — does not commit.";
        public string InputSchema => "{ \"repo\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "" });
            if (string.IsNullOrWhiteSpace(i?.repo)) return JsonConvert.SerializeObject(new { error = "repo required" });
            var msg = await _drafter.DraftAsync(i.repo);
            return JsonConvert.SerializeObject(new { message = msg });
        }
    }

    // ── write / outward tools (approval-gated) ─────────────────────────────────

    /// <summary>Push to the remote — outward-facing, gated.</summary>
    public sealed class GitPushTool : IMcpTool
    {
        private readonly IGitEngine _git;
        public GitPushTool(IGitEngine git) => _git = git;
        public string Name => "GitPush";
        public string Description => "Pushes committed changes to the remote. Outward-facing — requires approval in Syncro Desktop.";
        public string InputSchema => "{ \"repo\": \"string\", \"setUpstream\": \"boolean\" }";
        public bool RequiresAdminApproval => true;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "", setUpstream = false });
            if (string.IsNullOrWhiteSpace(i?.repo)) return JsonConvert.SerializeObject(new { error = "repo required" });
            var r = await _git.PushAsync(i.repo, i.setUpstream);
            return JsonConvert.SerializeObject(new { ok = r.Ok, output = r.Output, error = r.Error });
        }
    }

    /// <summary>Safe rollback: restore a file, or revert a commit (new commit). Never reset --hard.</summary>
    public sealed class GitRollbackTool : IMcpTool
    {
        private readonly IGitEngine _git;
        public GitRollbackTool(IGitEngine git) => _git = git;
        public string Name => "GitRollback";
        public string Description => "Safe rollback: mode 'restore' (discard a file's changes) or 'revert' (undo a commit with a new commit). Destructive resets are not available here — do those in Syncro Desktop.";
        public string InputSchema => "{ \"repo\": \"string\", \"mode\": \"restore|revert\", \"path\": \"string\", \"sha\": \"string\" }";
        public bool RequiresAdminApproval => true;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { repo = "", mode = "", path = "", sha = "" });
            if (string.IsNullOrWhiteSpace(i?.repo)) return JsonConvert.SerializeObject(new { error = "repo required" });

            GitResult r = i.mode switch
            {
                "restore" when !string.IsNullOrWhiteSpace(i.path) => await _git.RestoreAsync(i.repo, i.path),
                "revert" when !string.IsNullOrWhiteSpace(i.sha) => await _git.RevertAsync(i.repo, i.sha),
                _ => GitResult.Fail("mode must be 'restore' (with path) or 'revert' (with sha)"),
            };
            return JsonConvert.SerializeObject(new { ok = r.Ok, output = r.Output, error = r.Error });
        }
    }
}
