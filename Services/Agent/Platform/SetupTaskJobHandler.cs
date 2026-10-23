using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Agent.Policy;
using Syncro.Desktop.Services.Git;

namespace Syncro.Desktop.Services.Agent.Platform
{
    public sealed record SetupOutcome(bool Ok, string Message, JObject? Result);

    /// <summary>
    /// Runs a "setup-task" platform job: clone (or fetch) the task's repo into the workspace, check out
    /// the task branch (tracking origin, or new from origin/base), activate the commit scope guard and
    /// add the repo to My Projects. The whole thing goes through <see cref="AgentGuard"/> as one
    /// "SetupTaskWorkspace" action, so the member's autonomy level, permissions and floors apply.
    /// It never stashes, resets or discards anything: a dirty working tree stops the job.
    /// </summary>
    public sealed class SetupTaskJobHandler
    {
        private readonly IGitEngine _git;
        private readonly AgentGuard _guard;
        private readonly AgentPolicyStore _store;
        private readonly GitTaskScopeService _scope;
        private readonly ProjectService _projects;
        private readonly PixelService _pixel;

        public SetupTaskJobHandler(IGitEngine git, AgentGuard guard, AgentPolicyStore store, GitTaskScopeService scope, ProjectService projects, PixelService pixel)
        {
            _git = git; _guard = guard; _store = store; _scope = scope; _projects = projects; _pixel = pixel;
        }

        public static string WorkspaceRoot(AgentPolicy p) =>
            string.IsNullOrWhiteSpace(p.PlatformAgent.WorkspaceRoot)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SyncroWorkspaces")
                : p.PlatformAgent.WorkspaceRoot.Trim();

        /// <summary>Repo folder name from its URL: "…/Org/My.Repo.git" → "My.Repo".</summary>
        public static string RepoFolderName(string repoUrl)
        {
            var m = Regex.Match(repoUrl ?? "", @"[/:]([^/:]+?)(?:\.git)?/?$");
            var name = m.Success ? m.Groups[1].Value : "repo";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return string.IsNullOrWhiteSpace(name) ? "repo" : name;
        }

        /// <summary>Decision preview so the worker can tell the platform it's waiting for approval.</summary>
        public DecisionResult Preview(PlatformJob job) => _guard.Preview("SetupTaskWorkspace", GuardInput(job), "platform");

        private string GuardInput(PlatformJob job)
        {
            var p = job.Payload;
            var repoUrl = (string?)p["repoUrl"] ?? "";
            // Root, not the repo folder: the decider would otherwise read the clone's *current* branch
            // (main after a fresh clone) and treat switching away from it as a protected-branch write.
            return new JObject
            {
                ["repoUrl"] = repoUrl,
                ["workspaceRoot"] = WorkspaceRoot(_store.Current),
                ["folder"] = RepoFolderName(repoUrl),
                ["title"] = (string?)p["title"] ?? "",
                ["targetBranch"] = (string?)p["targetBranch"] ?? "",
                ["baseBranch"] = (string?)p["baseBranch"] ?? "main",
                ["requestedBy"] = "platform job " + job.Id,
            }.ToString(Formatting.None);
        }

        public async Task<SetupOutcome> RunAsync(PlatformJob job, string deviceName, CancellationToken ct)
        {
            string? failure = null;
            JObject? result = null;

            var text = await _guard.ExecuteAsync("SetupTaskWorkspace", GuardInput(job), "platform", async () =>
            {
                try
                {
                    result = await DoSetupAsync(job, deviceName, ct);
                    return result.ToString(Formatting.None);
                }
                catch (SetupException ex) { failure = ex.Message; return "Error: " + ex.Message; }
            });

            if (failure != null) return new(false, failure, null);
            if (result == null) return new(false, text.StartsWith("Error:") ? text[6..].Trim() : text, null);   // blocked, denied or timed out
            return new(true, $"{result["branch"]} @ {((string?)result["headSha"] ?? "")[..Math.Min(7, ((string?)result["headSha"] ?? "").Length)]}", result);
        }

        private sealed class SetupException : Exception { public SetupException(string m) : base(m) { } }

        private async Task<JObject> DoSetupAsync(PlatformJob job, string deviceName, CancellationToken ct)
        {
            var p = job.Payload;
            var repoUrl = ((string?)p["repoUrl"] ?? "").Trim();
            var baseBranch = ((string?)p["baseBranch"] ?? "main").Trim();
            var target = ((string?)p["targetBranch"] ?? "").Trim();
            if (repoUrl.Length == 0) throw new SetupException("The task has no repo URL.");
            if (target.Length == 0) throw new SetupException("The task has no target branch.");
            if (!Regex.IsMatch(target, @"^[A-Za-z0-9._/-]+$") || target.Contains("..") || target.StartsWith("-"))
                throw new SetupException($"Refusing unusual branch name '{target}'.");
            if (_store.Current.Bypass.ProtectedBranches.Any(g => PermissionChecker.GlobMatch(target, g)))
                throw new SetupException($"The task branch '{target}' is a protected branch on this PC. Ask the admin to give the task its own branch.");

            var root = WorkspaceRoot(_store.Current);
            Directory.CreateDirectory(root);
            var repoPath = Path.Combine(root, RepoFolderName(repoUrl));
            bool cloned = false;

            // 1. Clone, or reuse an existing clone of the SAME repo.
            if (!Directory.Exists(repoPath) || !Directory.EnumerateFileSystemEntries(repoPath).Any())
            {
                var c = await _git.RawAsync(root, new[] { "clone", "--origin", "origin", repoUrl, Path.GetFileName(repoPath) }, ct);
                if (!c.Ok) throw new SetupException("git clone failed: " + FirstLine(c.Error) + " (private repos need git credentials on this PC)");
                cloned = true;
            }
            else
            {
                if (!await _git.IsRepoAsync(repoPath, ct))
                    throw new SetupException($"'{repoPath}' exists and is not a git repository. Move it or change the workspace folder.");
                var remotes = await _git.RemotesAsync(repoPath, ct);
                var want = GitTaskScopeService.NormalizeRemote(repoUrl);
                if (!remotes.Any(r => GitTaskScopeService.NormalizeRemote(r.FetchUrl) == want))
                    throw new SetupException($"'{repoPath}' is a clone of a different repository. Move it or change the workspace folder.");
                var f = await _git.RawAsync(repoPath, new[] { "fetch", "origin", "--prune" }, ct);
                if (!f.Ok) throw new SetupException("git fetch failed: " + FirstLine(f.Error));
            }

            // 2. Check out the task branch without touching uncommitted work.
            var status = await _git.StatusAsync(repoPath, ct);
            if (!string.Equals(status.Branch, target, StringComparison.Ordinal))
            {
                if (!status.IsClean)
                    throw new SetupException($"'{repoPath}' has {status.Files.Count} uncommitted change(s) on '{status.Branch}'. Commit or stash them, then ask the admin to re-send the setup job.");

                async Task<bool> Has(string r) => (await _git.RawAsync(repoPath, new[] { "rev-parse", "--verify", "--quiet", r }, ct)).Ok;
                GitResult co;
                if (await Has("refs/heads/" + target)) co = await _git.RawAsync(repoPath, new[] { "checkout", target }, ct);
                else if (await Has("refs/remotes/origin/" + target)) co = await _git.RawAsync(repoPath, new[] { "checkout", "-b", target, "--track", "origin/" + target }, ct);
                else if (await Has("refs/remotes/origin/" + baseBranch)) co = await _git.RawAsync(repoPath, new[] { "checkout", "-b", target, "origin/" + baseBranch }, ct);
                else throw new SetupException($"Base branch '{baseBranch}' doesn't exist on origin.");
                if (!co.Ok) throw new SetupException("git checkout failed: " + FirstLine(co.Error));
            }

            var head = (await _git.RawAsync(repoPath, new[] { "rev-parse", "HEAD" }, ct)).Output.Trim();

            // 3. Commit scope guard for this repo (with teammate locks when the platform returns them).
            var task = await FindTaskAsync(job) ?? FromPayload(job);
            _scope.ActivateTaskForRepo(repoPath, task);

            // 4. Show it in My Projects.
            bool known = _projects.GetProjects().Any(x => string.Equals(Path.GetFullPath(x.Path).TrimEnd('\\'), Path.GetFullPath(repoPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (!known) _projects.ImportProject(repoPath);

            return new JObject
            {
                ["repoPath"] = repoPath,
                ["branch"] = target,
                ["baseBranch"] = baseBranch,
                ["headSha"] = head,
                ["cloned"] = cloned,
                ["deviceName"] = deviceName,
            };
        }

        private async Task<AgentTaskModel?> FindTaskAsync(PlatformJob job)
        {
            try
            {
                var mine = await _pixel.GetAssignedTasksAsync();
                return mine.FirstOrDefault(t => t.Id == job.TaskId);
            }
            catch { return null; }
        }

        private static AgentTaskModel FromPayload(PlatformJob job)
        {
            var p = job.Payload;
            return new AgentTaskModel
            {
                Id = job.TaskId,
                ProjectId = job.ProjectId,
                ProjectTitle = (string?)p["projectTitle"] ?? "",
                Title = (string?)p["title"] ?? "",
                RepoUrl = (string?)p["repoUrl"] ?? "",
                BaseBranch = (string?)p["baseBranch"] ?? "main",
                TargetBranch = (string?)p["targetBranch"] ?? "",
                AssignedFiles = p["assignedFiles"]?.ToObject<System.Collections.Generic.List<string>>() ?? new(),
                AllowedGlobs = p["allowedGlobs"]?.ToObject<System.Collections.Generic.List<string>>() ?? new(),
                ReadOnlyFiles = p["readOnlyFiles"]?.ToObject<System.Collections.Generic.List<string>>() ?? new(),
                FileLockMode = (string?)p["fileLockMode"] ?? "strict",
                Status = "in-progress",
            };
        }

        private static string FirstLine(string s) => (s ?? "").Trim().Split('\n').FirstOrDefault()?.Trim() ?? "";
    }
}
