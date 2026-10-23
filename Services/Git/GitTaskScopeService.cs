using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.Buffered;

namespace Syncro.Desktop.Services.Git
{
    public enum GitFileScopeKind
    {
        NoActiveTask,
        InAssignedScope,
        ReadOnlyReference,
        LockedByTeammate,
        OutOfScope
    }

    public sealed class GitFileScopeVerdict
    {
        public GitFileScopeKind Kind { get; init; } = GitFileScopeKind.NoActiveTask;
        public string BadgeText { get; init; } = "";
        public string CssClass { get; init; } = "";
        public string? LockedByName { get; init; }
        public string? LockedByTaskTitle { get; init; }
        public string LockMode { get; init; } = "strict";
        public bool IsAllowedToCommit =>
            Kind == GitFileScopeKind.NoActiveTask ||
            Kind == GitFileScopeKind.InAssignedScope ||
            string.Equals(LockMode, "advisory", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class CommitScopeCheckResult
    {
        public bool Allowed { get; init; }
        public string Message { get; init; } = "";
        public List<string> OutOfScopeFiles { get; init; } = new();
        public List<GitFileLockModel> TeammateLockViolations { get; init; } = new();
    }

    /// <summary>
    /// Coordinates active Receiver Git Task scopes across AgentTaskDashboard and GitManager,
    /// evaluates file-level ownership/lock boundaries, and lists tracked files from local Git repos.
    /// </summary>
    public sealed class GitTaskScopeService
    {
        private readonly Dictionary<string, AgentTaskModel> _activeTaskByRepo = new(StringComparer.OrdinalIgnoreCase);

        public AgentTaskModel? CurrentActiveTask { get; private set; }
        public string? CurrentActiveRepoPath { get; private set; }

        // Handoff state when Team Lead clicks "Assign Selected Files to Teammate" in GitManager
        public string? PendingDispatchRepoPath { get; private set; }
        public List<string> PendingDispatchFiles { get; private set; } = new();

        public event Action? OnScopeChanged;

        public void ActivateTaskForRepo(string repoPath, AgentTaskModel task)
        {
            if (string.IsNullOrWhiteSpace(repoPath)) return;
            var normalized = NormalizeRepoPath(repoPath);
            _activeTaskByRepo[normalized] = task;
            CurrentActiveTask = task;
            CurrentActiveRepoPath = normalized;
            OnScopeChanged?.Invoke();
        }

        public void ClearTaskForRepo(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath)) return;
            var normalized = NormalizeRepoPath(repoPath);
            _activeTaskByRepo.Remove(normalized);
            if (string.Equals(CurrentActiveRepoPath, normalized, StringComparison.OrdinalIgnoreCase))
            {
                CurrentActiveTask = null;
                CurrentActiveRepoPath = null;
            }
            OnScopeChanged?.Invoke();
        }

        public AgentTaskModel? GetActiveTaskForRepo(string? repoPath)
        {
            if (!string.IsNullOrWhiteSpace(repoPath))
            {
                var normalized = NormalizeRepoPath(repoPath);
                if (_activeTaskByRepo.TryGetValue(normalized, out var task))
                    return task;
            }
            return CurrentActiveTask;
        }

        public void SetPendingDispatch(string repoPath, IEnumerable<string> relativeFiles)
        {
            PendingDispatchRepoPath = repoPath;
            PendingDispatchFiles = relativeFiles
                .Select(NormalizeRelativePath)
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public (string? RepoPath, List<string> Files) ConsumePendingDispatch()
        {
            var repo = PendingDispatchRepoPath;
            var files = new List<string>(PendingDispatchFiles);
            PendingDispatchRepoPath = null;
            PendingDispatchFiles.Clear();
            return (repo, files);
        }

        public GitFileScopeVerdict EvaluateFileScope(string? repoPath, string relativeFilePath)
        {
            var task = GetActiveTaskForRepo(repoPath);
            if (task == null)
            {
                return new GitFileScopeVerdict { Kind = GitFileScopeKind.NoActiveTask };
            }

            string rel = NormalizeRelativePath(relativeFilePath);
            if (string.IsNullOrEmpty(rel))
            {
                return new GitFileScopeVerdict { Kind = GitFileScopeKind.NoActiveTask };
            }

            // 1. Check if locked by a teammate's active task
            var teammateLock = task.TeammateLockedFiles.FirstOrDefault(l =>
                PathsMatch(rel, l.FilePath));
            if (teammateLock != null)
            {
                return new GitFileScopeVerdict
                {
                    Kind = GitFileScopeKind.LockedByTeammate,
                    BadgeText = $"LOCKED · @{teammateLock.LockedByName.Split(' ')[0]}",
                    CssClass = "scope-locked",
                    LockedByName = teammateLock.LockedByName,
                    LockedByTaskTitle = teammateLock.TaskTitle,
                    LockMode = task.FileLockMode
                };
            }

            // 2. Check if explicitly in AssignedFiles or AllowedGlobs
            bool inAssigned = task.AssignedFiles.Any(f => PathsMatch(rel, f)) ||
                              task.AllowedGlobs.Any(g => MatchesGlob(rel, g));
            if (inAssigned)
            {
                return new GitFileScopeVerdict
                {
                    Kind = GitFileScopeKind.InAssignedScope,
                    BadgeText = "IN SCOPE",
                    CssClass = "scope-in",
                    LockMode = task.FileLockMode
                };
            }

            // 3. Check if ReadOnly reference file
            bool isReadOnly = task.ReadOnlyFiles.Any(f => PathsMatch(rel, f));
            if (isReadOnly)
            {
                return new GitFileScopeVerdict
                {
                    Kind = GitFileScopeKind.ReadOnlyReference,
                    BadgeText = "READ-ONLY",
                    CssClass = "scope-readonly",
                    LockMode = task.FileLockMode
                };
            }

            // 4. Out of scope
            return new GitFileScopeVerdict
            {
                Kind = GitFileScopeKind.OutOfScope,
                BadgeText = "OUT OF SCOPE",
                CssClass = "scope-out",
                LockMode = task.FileLockMode
            };
        }

        public CommitScopeCheckResult ValidateFilesForCommit(string? repoPath, IEnumerable<string> relativeFiles)
        {
            var task = GetActiveTaskForRepo(repoPath);
            if (task == null)
            {
                return new CommitScopeCheckResult { Allowed = true, Message = "No active task scope constraint." };
            }

            var outOfScope = new List<string>();
            var lockViolations = new List<GitFileLockModel>();

            foreach (var raw in relativeFiles)
            {
                string rel = NormalizeRelativePath(raw);
                var verdict = EvaluateFileScope(repoPath, rel);

                if (verdict.Kind == GitFileScopeKind.LockedByTeammate)
                {
                    var lockItem = task.TeammateLockedFiles.FirstOrDefault(l => PathsMatch(rel, l.FilePath))
                        ?? new GitFileLockModel { FilePath = rel, LockedByName = verdict.LockedByName ?? "Teammate", TaskTitle = verdict.LockedByTaskTitle ?? "" };
                    lockViolations.Add(lockItem);
                }
                else if (verdict.Kind == GitFileScopeKind.OutOfScope || verdict.Kind == GitFileScopeKind.ReadOnlyReference)
                {
                    outOfScope.Add(rel);
                }
            }

            bool isAdvisory = string.Equals(task.FileLockMode, "advisory", StringComparison.OrdinalIgnoreCase);
            if (lockViolations.Count == 0 && outOfScope.Count == 0)
            {
                return new CommitScopeCheckResult
                {
                    Allowed = true,
                    Message = $"All {relativeFiles.Count()} file(s) match assigned scope for '{task.Title}'."
                };
            }

            string summary = "";
            if (lockViolations.Count > 0)
            {
                var first = lockViolations[0];
                summary += $"{lockViolations.Count} file(s) locked by @{first.LockedByName} ('{first.FilePath}'). ";
            }
            if (outOfScope.Count > 0)
            {
                summary += $"{outOfScope.Count} file(s) outside assigned task scope ('{outOfScope[0]}').";
            }

            return new CommitScopeCheckResult
            {
                Allowed = isAdvisory,
                Message = summary.Trim(),
                OutOfScopeFiles = outOfScope,
                TeammateLockViolations = lockViolations
            };
        }

        /// <summary>
        /// Reads tracked files from a local Git repository (`git ls-files`) for the visual file-set dispatcher.
        /// </summary>
        public async Task<List<string>> GetTrackedRepoFilesAsync(string repoPath, int maxFiles = 1500)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return result;

            try
            {
                var cmd = await Cli.Wrap("git")
                    .WithArguments(new[] { "ls-files" })
                    .WithWorkingDirectory(repoPath)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                if (cmd.ExitCode == 0 && !string.IsNullOrWhiteSpace(cmd.StandardOutput))
                {
                    foreach (var line in cmd.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var clean = NormalizeRelativePath(line);
                        if (!string.IsNullOrEmpty(clean) && !IsIgnoredPath(clean))
                        {
                            result.Add(clean);
                            if (result.Count >= maxFiles) break;
                        }
                    }
                    return result;
                }
            }
            catch { }

            // Fallback filesystem enumeration if git ls-files isn't available
            try
            {
                foreach (var file in Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories))
                {
                    var rel = NormalizeRelativePath(Path.GetRelativePath(repoPath, file));
                    if (!IsIgnoredPath(rel))
                    {
                        result.Add(rel);
                        if (result.Count >= maxFiles) break;
                    }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Checks out or creates the task's dedicated TargetBranch in the local repository.
        /// </summary>
        public async Task<(bool Success, string Message)> CheckoutTaskBranchAsync(string repoPath, string targetBranch, string baseBranch = "main")
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return (false, "Local repository path does not exist.");

            if (string.IsNullOrWhiteSpace(targetBranch))
                return (false, "Task target branch is empty.");

            try
            {
                // Check if branch already exists locally
                var checkLocal = await Cli.Wrap("git")
                    .WithArguments(new[] { "rev-parse", "--verify", targetBranch })
                    .WithWorkingDirectory(repoPath)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                if (checkLocal.ExitCode == 0)
                {
                    var co = await Cli.Wrap("git")
                        .WithArguments(new[] { "checkout", targetBranch })
                        .WithWorkingDirectory(repoPath)
                        .WithValidation(CommandResultValidation.None)
                        .ExecuteBufferedAsync();
                    return co.ExitCode == 0
                        ? (true, $"Switched to existing branch '{targetBranch}'.")
                        : (false, co.StandardError.Trim());
                }

                // Create and switch to new task branch from current HEAD
                var createCo = await Cli.Wrap("git")
                    .WithArguments(new[] { "checkout", "-b", targetBranch })
                    .WithWorkingDirectory(repoPath)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                return createCo.ExitCode == 0
                    ? (true, $"Created and checked out new task branch '{targetBranch}'.")
                    : (false, createCo.StandardError.Trim());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// True when <paramref name="task"/> is for the repository at <paramref name="repoPath"/>: its
        /// project's local path is that folder, or its repo URL matches one of the repo's remotes.
        /// </summary>
        public static bool TaskBelongsToRepo(AgentTaskModel task, string repoPath, IEnumerable<string> remoteUrls, string? projectLocalPath)
        {
            if (!string.IsNullOrWhiteSpace(projectLocalPath) &&
                string.Equals(NormalizeRepoPath(projectLocalPath), NormalizeRepoPath(repoPath), StringComparison.OrdinalIgnoreCase))
                return true;
            var want = NormalizeRemote(task.RepoUrl);
            return want.Length > 0 && remoteUrls.Any(u => NormalizeRemote(u) == want);
        }

        /// <summary>"git@github.com:Org/Repo.git" and "https://github.com/org/repo" → "github.com/org/repo".</summary>
        public static string NormalizeRemote(string? url)
        {
            var u = (url ?? "").Trim().ToLowerInvariant();
            if (u.Length == 0) return "";
            u = Regex.Replace(u, @"^[a-z+]+://", "");
            u = Regex.Replace(u, @"^[^@/]+@", "");
            u = u.Replace(':', '/');
            if (u.EndsWith(".git")) u = u[..^4];
            return u.TrimEnd('/');
        }

        private static bool IsIgnoredPath(string rel)
        {
            return rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                   rel.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                   rel.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) ||
                   rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                   rel.StartsWith(".vs/", StringComparison.OrdinalIgnoreCase) ||
                   rel.StartsWith(".next/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRepoPath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim();
            }
        }

        public static string NormalizeRelativePath(string path)
        {
            return (path ?? "").Replace('\\', '/').Trim().TrimStart('/');
        }

        public static bool PathsMatch(string pathA, string pathB)
        {
            var a = NormalizeRelativePath(pathA);
            var b = NormalizeRelativePath(pathB);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                return true;

            // Also match if one is a suffix of the other when sub-project paths are used
            if (a.EndsWith("/" + b, StringComparison.OrdinalIgnoreCase) ||
                b.EndsWith("/" + a, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public static bool MatchesGlob(string relativePath, string glob)
        {
            var path = NormalizeRelativePath(relativePath);
            var pattern = NormalizeRelativePath(glob);
            if (string.IsNullOrEmpty(pattern)) return false;

            if (pattern.EndsWith("/**", StringComparison.Ordinal))
            {
                var prefix = pattern[..^3];
                return path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase);
            }

            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", @"[^/]*")
                .Replace(@"\?", ".") + "$";

            return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
        }
    }
}
