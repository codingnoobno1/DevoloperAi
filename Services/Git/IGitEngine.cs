using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>
    /// The single git surface for Syncro — consolidates what used to live across GitMcp,
    /// GitCommandService, GitAutomationService and GitBranchService behind one process runner and one
    /// set of typed models (git-sync-engine.md). Everything else (MCP tools, the sync worker, the
    /// workflow builder, the UI) talks to this.
    /// </summary>
    public interface IGitEngine
    {
        /// <summary>Run an arbitrary git command in <paramref name="repo"/> (escape hatch).</summary>
        Task<GitResult> RawAsync(string repo, string[] args, CancellationToken ct = default);

        Task<bool> IsRepoAsync(string repo, CancellationToken ct = default);

        // ── read (safe) ──
        Task<GitStatus> StatusAsync(string repo, CancellationToken ct = default);
        Task<string> CurrentBranchAsync(string repo, CancellationToken ct = default);
        Task<IReadOnlyList<GitCommit>> LogAsync(string repo, int count = 20, CancellationToken ct = default);
        Task<IReadOnlyList<GitContributor>> ContributorsAsync(string repo, CancellationToken ct = default);
        Task<IReadOnlyList<GitBranch>> BranchesAsync(string repo, CancellationToken ct = default);
        Task<IReadOnlyList<GitRemote>> RemotesAsync(string repo, CancellationToken ct = default);
        Task<GitResult> DiffAsync(string repo, string? refSpec = null, bool stat = true, CancellationToken ct = default);
        Task<GitResult> FileDiffAsync(string repo, string filePath, CancellationToken ct = default);
        Task<GitResult> CommitDiffAsync(string repo, string sha, CancellationToken ct = default);

        /// <summary>Commits the current branch is BEHIND its upstream by (incoming on next pull).</summary>
        Task<IReadOnlyList<GitCommit>> IncomingAsync(string repo, CancellationToken ct = default);
        /// <summary>Switch to an existing branch (strips a leading <c>origin/</c> for remote-tracking checkout).</summary>
        Task<GitResult> CheckoutAsync(string repo, string branch, CancellationToken ct = default);
        Task<GitResult> CreateBranchAsync(string repo, string branch, bool checkout = true, CancellationToken ct = default);
        Task<GitResult> SetRemoteUrlAsync(string repo, string name, string url, CancellationToken ct = default);

        // ── sync ──
        Task<GitResult> FetchAsync(string repo, bool prune = true, CancellationToken ct = default);
        Task<GitResult> PullAsync(string repo, bool ffOnly = true, CancellationToken ct = default);
        Task<GitResult> PushAsync(string repo, bool setUpstream = false, CancellationToken ct = default);

        // ── write & staging ──
        Task<GitResult> StageFileAsync(string repo, string path, CancellationToken ct = default);
        Task<GitResult> UnstageFileAsync(string repo, string path, CancellationToken ct = default);
        Task<GitResult> StageHunkAsync(string repo, string patchContent, CancellationToken ct = default);
        Task<GitResult> CommitAsync(string repo, string message, bool stageAll = true, bool sign = false, CancellationToken ct = default);
        Task<GitResult> CommitFilesAsync(string repo, string message, IReadOnlyList<string> files, bool sign = false, CancellationToken ct = default);

        // ── rollback & conflict/cherry-pick operations ──
        Task<GitResult> RestoreAsync(string repo, string path, CancellationToken ct = default);
        Task<GitResult> RevertAsync(string repo, string sha, CancellationToken ct = default);
        Task<GitResult> MergeAsync(string repo, string branch, string strategy = "ort", string? strategyOption = "diff-algorithm=histogram", CancellationToken ct = default);
        Task<GitResult> CherryPickAsync(string repo, string commitOrRange, bool traceable = true, bool threeWay = true, int? mainlineParent = null, CancellationToken ct = default);
        Task<GitResult> ResolveConflictSideAsync(string repo, string filePath, bool takeTheirs, CancellationToken ct = default);
        Task<GitResult> BackupRefAsync(string repo, string operationId, CancellationToken ct = default);
        Task<GitResult> RestoreBackupRefAsync(string repo, string operationId, CancellationToken ct = default);
    }
}
