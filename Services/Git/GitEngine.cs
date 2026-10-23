using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>
    /// The one git runner. Invokes the git CLI via <see cref="Process"/> (no shell, argv-array →
    /// no quoting bugs) and parses machine-readable output into the typed models. Every method is
    /// non-throwing: failures come back as <see cref="GitResult.Ok"/> = false / empty lists.
    /// </summary>
    public sealed class GitEngine : IGitEngine
    {
        private const char US = '\x1f';   // unit separator for --format fields

        public async Task<GitResult> RawAsync(string repo, string[] args, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo))
                return GitResult.Fail($"repo path does not exist: '{repo}'");

            try
            {
                var psi = new ProcessStartInfo("git")
                {
                    WorkingDirectory = repo,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // Never block on an interactive credential/host prompt — network ops fail fast instead
                // of hanging (critical for the auto-sync worker). Credentials come from the configured
                // helper / ssh-agent, never a TTY prompt.
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
                psi.Environment["GCM_INTERACTIVE"] = "Never";
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var proc = Process.Start(psi);
                if (proc == null) return GitResult.Fail("failed to start git");

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(ct);
                var output = await outTask;
                var error = await errTask;

                return new GitResult { Ok = proc.ExitCode == 0, ExitCode = proc.ExitCode, Output = output, Error = error };
            }
            catch (Exception ex)
            {
                return GitResult.Fail(ex.Message);
            }
        }

        public async Task<bool> IsRepoAsync(string repo, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "rev-parse", "--is-inside-work-tree" }, ct);
            return r.Ok && r.Output.Trim() == "true";
        }

        public async Task<string> CurrentBranchAsync(string repo, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, ct);
            return r.Ok ? r.Output.Trim() : "";
        }

        // ── status (porcelain v2 → ahead/behind + files, preserving paths with spaces) ──
        public async Task<GitStatus> StatusAsync(string repo, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "status", "--porcelain=v2", "--branch" }, ct);
            if (!r.Ok) return new GitStatus { IsRepo = false };

            string branch = "", upstream = "";
            int ahead = 0, behind = 0;
            var files = new List<GitFileChange>();

            foreach (var line in r.Output.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.Length == 0) continue;

                if (l.StartsWith("# branch.head ")) branch = l.Substring("# branch.head ".Length).Trim();
                else if (l.StartsWith("# branch.upstream ")) upstream = l.Substring("# branch.upstream ".Length).Trim();
                else if (l.StartsWith("# branch.ab "))
                {
                    foreach (var tok in l.Substring("# branch.ab ".Length).Split(' '))
                    {
                        if (tok.StartsWith("+")) int.TryParse(tok.Substring(1), out ahead);
                        else if (tok.StartsWith("-")) int.TryParse(tok.Substring(1), out behind);
                    }
                }
                else if (l.StartsWith("1 "))
                {
                    // "1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>" (8 spaces before <path>)
                    var parts = l.Split(' ', 9);
                    if (parts.Length >= 9)
                    {
                        var xy = parts[1];
                        files.Add(new GitFileChange
                        {
                            State = xy.Replace(".", "").Trim(),
                            IsStaged = xy.Length > 0 && xy[0] != '.',
                            Path = parts[8]
                        });
                    }
                }
                else if (l.StartsWith("2 "))
                {
                    // "2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path>\t<origPath>"
                    var parts = l.Split(' ', 10);
                    if (parts.Length >= 10)
                    {
                        var xy = parts[1];
                        var pathPart = parts[9].Split('\t')[0];
                        files.Add(new GitFileChange
                        {
                            State = xy.Replace(".", "").Trim(),
                            IsStaged = xy.Length > 0 && xy[0] != '.',
                            Path = pathPart
                        });
                    }
                }
                else if (l.StartsWith("u "))
                {
                    // Unmerged conflict entry: "u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>"
                    var parts = l.Split(' ', 11);
                    if (parts.Length >= 11)
                    {
                        files.Add(new GitFileChange
                        {
                            State = "U",
                            IsConflicted = true,
                            ConflictCode = parts[1],
                            Path = parts[10]
                        });
                    }
                }
                else if (l.StartsWith("? "))
                {
                    files.Add(new GitFileChange { State = "??", Path = l.Substring(2) });
                }
            }

            if (files.Count > 0)
            {
                var numStat = await RawAsync(repo, new[] { "diff", "--numstat", "HEAD" }, ct);
                if (!numStat.Ok || string.IsNullOrWhiteSpace(numStat.Output))
                    numStat = await RawAsync(repo, new[] { "diff", "--numstat" }, ct);

                var statMap = new Dictionary<string, (int add, int del)>(StringComparer.OrdinalIgnoreCase);
                if (numStat.Ok && !string.IsNullOrWhiteSpace(numStat.Output))
                {
                    foreach (var rawStat in numStat.Output.Split('\n'))
                    {
                        var sl = rawStat.TrimEnd('\r');
                        if (sl.Length == 0) continue;
                        var cols = sl.Split('\t');
                        if (cols.Length >= 3)
                        {
                            int.TryParse(cols[0], out int a);
                            int.TryParse(cols[1], out int d);
                            statMap[cols[2].Trim()] = (a, d);
                        }
                    }
                }

                foreach (var f in files)
                {
                    if (statMap.TryGetValue(f.Path, out var counts))
                    {
                        f.Additions = counts.add;
                        f.Deletions = counts.del;
                    }
                    else if (f.State == "??")
                    {
                        try
                        {
                            var full = Path.Combine(repo, f.Path);
                            if (File.Exists(full) && new FileInfo(full).Length < 512_000)
                                f.Additions = File.ReadLines(full).Take(500).Count();
                        }
                        catch { }
                    }
                }
            }

            return new GitStatus
            {
                IsRepo = true,
                Branch = branch,
                Upstream = string.IsNullOrEmpty(upstream) ? null : upstream,
                Ahead = ahead,
                Behind = behind,
                Files = files,
            };
        }

        // ── log ──
        public async Task<IReadOnlyList<GitCommit>> LogAsync(string repo, int count = 20, CancellationToken ct = default)
        {
            var fmt = $"%H{US}%h{US}%an{US}%ae{US}%aI{US}%s";
            var r = await RawAsync(repo, new[] { "log", $"-n{count}", $"--pretty=format:{fmt}" }, ct);
            if (!r.Ok) return Array.Empty<GitCommit>();

            var commits = new List<GitCommit>();
            foreach (var line in r.Output.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.Length == 0) continue;
                var f = l.Split(US);
                if (f.Length < 6) continue;
                commits.Add(new GitCommit
                {
                    Sha = f[0], ShortSha = f[1], Author = f[2], Email = f[3], Date = f[4], Subject = f[5],
                });
            }
            return commits;
        }

        // ── contributors ──
        public async Task<IReadOnlyList<GitContributor>> ContributorsAsync(string repo, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "log", "--all", $"--pretty=format:%an{US}%ae" }, ct);
            if (!r.Ok) return Array.Empty<GitContributor>();

            var by = new Dictionary<string, GitContributor>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in r.Output.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.Length == 0) continue;
                var f = l.Split(US);
                if (f.Length < 2) continue;
                var key = f[1];
                if (!by.TryGetValue(key, out var c))
                    by[key] = c = new GitContributor { Name = f[0], Email = f[1] };
                c.Commits++;
            }
            return by.Values.OrderByDescending(c => c.Commits).ToList();
        }

        // ── branches ──
        public async Task<IReadOnlyList<GitBranch>> BranchesAsync(string repo, CancellationToken ct = default)
        {
            var fmt = $"%(HEAD){US}%(refname:short){US}%(upstream:short){US}%(upstream:track)";
            var r = await RawAsync(repo, new[] { "branch", "-a", $"--format={fmt}" }, ct);
            if (!r.Ok) return Array.Empty<GitBranch>();

            var branches = new List<GitBranch>();
            foreach (var line in r.Output.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.Length == 0) continue;
                var f = l.Split(US);
                if (f.Length < 2) continue;
                var name = f[1];
                var track = f.Length > 3 ? f[3] : "";
                int ahead = 0, behind = 0;
                var am = System.Text.RegularExpressions.Regex.Match(track, @"ahead (\d+)");
                var bm = System.Text.RegularExpressions.Regex.Match(track, @"behind (\d+)");
                if (am.Success) int.TryParse(am.Groups[1].Value, out ahead);
                if (bm.Success) int.TryParse(bm.Groups[1].Value, out behind);
                branches.Add(new GitBranch
                {
                    Name = name,
                    IsCurrent = f[0].Trim() == "*",
                    IsRemote = name.StartsWith("remotes/") || (name.Contains('/', StringComparison.Ordinal) && name.StartsWith("origin/")),
                    Upstream = f.Length > 2 && f[2].Length > 0 ? f[2] : null,
                    Ahead = ahead,
                    Behind = behind,
                    Gone = track.Contains("gone", StringComparison.OrdinalIgnoreCase),
                });
            }
            return branches
                .OrderByDescending(b => b.IsCurrent)
                .ThenBy(b => b.IsRemote)
                .ThenBy(b => b.Name)
                .ToList();
        }

        // ── remotes ──
        public async Task<IReadOnlyList<GitRemote>> RemotesAsync(string repo, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "remote", "-v" }, ct);
            if (!r.Ok) return Array.Empty<GitRemote>();

            var map = new Dictionary<string, (string fetch, string push)>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in r.Output.Split('\n'))
            {
                var l = raw.Trim();
                if (l.Length == 0) continue;
                var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var name = parts[0];
                var url = parts[1];
                var kind = parts.Length > 2 ? parts[2] : "";
                map.TryGetValue(name, out var cur);
                if (kind.Contains("push", StringComparison.OrdinalIgnoreCase))
                    map[name] = (cur.fetch ?? url, url);
                else
                    map[name] = (url, cur.push ?? url);
            }

            return map.Select(kv => new GitRemote
            {
                Name = kv.Key,
                FetchUrl = kv.Value.fetch,
                PushUrl = kv.Value.push
            }).ToList();
        }

        public async Task<GitResult> SetRemoteUrlAsync(string repo, string name, string url, CancellationToken ct = default)
        {
            var existing = await RemotesAsync(repo, ct);
            if (existing.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return await RawAsync(repo, new[] { "remote", "set-url", name, url }, ct);
            return await RawAsync(repo, new[] { "remote", "add", name, url }, ct);
        }

        /// <summary>What HEAD is behind its upstream by — the commits a pull would bring in.</summary>
        public async Task<IReadOnlyList<GitCommit>> IncomingAsync(string repo, CancellationToken ct = default)
        {
            var fmt = $"%H{US}%h{US}%an{US}%ae{US}%aI{US}%s";
            var r = await RawAsync(repo, new[] { "log", "HEAD..@{u}", $"--pretty=format:{fmt}" }, ct);
            if (!r.Ok) return Array.Empty<GitCommit>();
            var commits = new List<GitCommit>();
            foreach (var line in r.Output.Split('\n'))
            {
                var l = line.TrimEnd('\r');
                if (l.Length == 0) continue;
                var f = l.Split(US);
                if (f.Length < 6) continue;
                commits.Add(new GitCommit { Sha = f[0], ShortSha = f[1], Author = f[2], Email = f[3], Date = f[4], Subject = f[5] });
            }
            return commits;
        }

        public Task<GitResult> CheckoutAsync(string repo, string branch, CancellationToken ct = default)
        {
            var local = branch.StartsWith("origin/", StringComparison.Ordinal) ? branch.Substring("origin/".Length)
                      : branch.StartsWith("remotes/origin/", StringComparison.Ordinal) ? branch.Substring("remotes/origin/".Length)
                      : branch;
            return RawAsync(repo, new[] { "checkout", local }, ct);
        }

        public Task<GitResult> CreateBranchAsync(string repo, string branch, bool checkout = true, CancellationToken ct = default) =>
            checkout
                ? RawAsync(repo, new[] { "checkout", "-b", branch }, ct)
                : RawAsync(repo, new[] { "branch", branch }, ct);

        public Task<GitResult> DiffAsync(string repo, string? refSpec = null, bool stat = true, CancellationToken ct = default)
        {
            var args = new List<string> { "diff" };
            if (stat) args.Add("--stat");
            if (!string.IsNullOrWhiteSpace(refSpec)) args.Add(refSpec!);
            return RawAsync(repo, args.ToArray(), ct);
        }

        public async Task<GitResult> FileDiffAsync(string repo, string filePath, CancellationToken ct = default)
        {
            // Try tracked diff vs HEAD first
            var r = await RawAsync(repo, new[] { "diff", "HEAD", "--", filePath }, ct);
            if (r.Ok && !string.IsNullOrWhiteSpace(r.Output))
                return r;

            // Try unstaged diff
            var u = await RawAsync(repo, new[] { "diff", "--", filePath }, ct);
            if (u.Ok && !string.IsNullOrWhiteSpace(u.Output))
                return u;

            // Untracked new file preview
            var fullPath = Path.Combine(repo, filePath);
            if (File.Exists(fullPath))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(fullPath, ct);
                    var sb = new StringBuilder();
                    sb.AppendLine($"--- /dev/null");
                    sb.AppendLine($"+++ b/{filePath}");
                    sb.AppendLine($"@@ -0,0 +1,{Math.Min(lines.Length, 300)} @@");
                    foreach (var line in lines.Take(300))
                        sb.AppendLine("+" + line);
                    if (lines.Length > 300)
                        sb.AppendLine($"+... ({lines.Length - 300} more lines)");
                    return new GitResult { Ok = true, ExitCode = 0, Output = sb.ToString() };
                }
                catch (Exception ex)
                {
                    return GitResult.Fail(ex.Message);
                }
            }

            return new GitResult { Ok = true, ExitCode = 0, Output = "(No textual diff or binary file)" };
        }

        public Task<GitResult> CommitDiffAsync(string repo, string sha, CancellationToken ct = default) =>
            RawAsync(repo, new[] { "show", "--stat", "--patch", sha }, ct);

        // ── sync ──
        public Task<GitResult> FetchAsync(string repo, bool prune = true, CancellationToken ct = default) =>
            RawAsync(repo, prune ? new[] { "fetch", "--all", "--prune" } : new[] { "fetch", "--all" }, ct);

        public Task<GitResult> PullAsync(string repo, bool ffOnly = true, CancellationToken ct = default) =>
            RawAsync(repo, ffOnly ? new[] { "pull", "--ff-only" } : new[] { "pull" }, ct);

        public Task<GitResult> PushAsync(string repo, bool setUpstream = false, CancellationToken ct = default) =>
            RawAsync(repo, setUpstream ? new[] { "push", "--set-upstream", "origin", "HEAD" } : new[] { "push" }, ct);

        // ── write & staging ──
        public Task<GitResult> StageFileAsync(string repo, string path, CancellationToken ct = default) =>
            RawAsync(repo, new[] { "add", "--", path }, ct);

        public async Task<GitResult> UnstageFileAsync(string repo, string path, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "restore", "--staged", "--", path }, ct);
            if (r.Ok) return r;
            return await RawAsync(repo, new[] { "reset", "HEAD", "--", path }, ct);
        }

        public async Task<GitResult> StageHunkAsync(string repo, string patchContent, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(patchContent))
                return GitResult.Fail("patch content is empty");

            var tmpPatch = Path.Combine(Path.GetTempPath(), $"syncro-hunk-{Guid.NewGuid():N}.patch");
            try
            {
                var normalized = patchContent.Replace("\r\n", "\n");
                if (!normalized.EndsWith('\n')) normalized += "\n";
                await File.WriteAllTextAsync(tmpPatch, normalized, Encoding.UTF8, ct);
                return await RawAsync(repo, new[] { "apply", "--cached", "--unidiff-zero", tmpPatch }, ct);
            }
            catch (Exception ex)
            {
                return GitResult.Fail(ex.Message);
            }
            finally
            {
                try { if (File.Exists(tmpPatch)) File.Delete(tmpPatch); } catch { }
            }
        }

        public async Task<GitResult> CommitAsync(string repo, string message, bool stageAll = true, bool sign = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message)) return GitResult.Fail("commit message required");
            if (stageAll)
            {
                var add = await RawAsync(repo, new[] { "add", "-A" }, ct);
                if (!add.Ok) return add;
            }
            var args = new List<string> { "commit", "-m", message };
            if (sign) args.Add("-S");
            return await RawAsync(repo, args.ToArray(), ct);
        }

        public async Task<GitResult> CommitFilesAsync(string repo, string message, IReadOnlyList<string> files, bool sign = false, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message)) return GitResult.Fail("commit message required");
            if (files == null || files.Count == 0)
                return await CommitAsync(repo, message, stageAll: true, sign: sign, ct: ct);

            // Reset index first so only the explicitly selected files are committed
            await RawAsync(repo, new[] { "reset" }, ct);
            var addArgs = new List<string> { "add", "--" };
            addArgs.AddRange(files);
            var add = await RawAsync(repo, addArgs.ToArray(), ct);
            if (!add.Ok) return add;

            var commitArgs = new List<string> { "commit", "-m", message };
            if (sign) commitArgs.Add("-S");
            return await RawAsync(repo, commitArgs.ToArray(), ct);
        }

        // ── rollback (safe tier) ──
        public async Task<GitResult> RestoreAsync(string repo, string path, CancellationToken ct = default)
        {
            var r = await RawAsync(repo, new[] { "restore", "--staged", "--worktree", "--", path }, ct);
            if (r.Ok) return r;
            return await RawAsync(repo, new[] { "checkout", "--", path }, ct);
        }

        public Task<GitResult> RevertAsync(string repo, string sha, CancellationToken ct = default) =>
            RawAsync(repo, new[] { "revert", "--no-edit", sha }, ct);

        public Task<GitResult> MergeAsync(string repo, string branch, string strategy = "ort", string? strategyOption = "diff-algorithm=histogram", CancellationToken ct = default)
        {
            var args = new List<string> { "merge", "-s", string.IsNullOrWhiteSpace(strategy) ? "ort" : strategy };
            if (!string.IsNullOrWhiteSpace(strategyOption))
            {
                args.Add("-X");
                args.Add(strategyOption!);
            }
            args.Add(branch);
            return RawAsync(repo, args.ToArray(), ct);
        }

        public Task<GitResult> CherryPickAsync(string repo, string commitOrRange, bool traceable = true, bool threeWay = true, int? mainlineParent = null, CancellationToken ct = default)
        {
            // threeWay is accepted for API compatibility but ignored: `git cherry-pick` is always a
            // 3-way merge and has no -3/--3way option (that flag belongs to `git am`/`git apply`).
            var args = new List<string> { "cherry-pick" };
            if (traceable) args.Add("-x");
            if (mainlineParent is > 0)
            {
                args.Add("-m");
                args.Add(mainlineParent.Value.ToString());
            }
            args.Add(commitOrRange);
            return RawAsync(repo, args.ToArray(), ct);
        }

        public async Task<GitResult> ResolveConflictSideAsync(string repo, string filePath, bool takeTheirs, CancellationToken ct = default)
        {
            var side = takeTheirs ? "--theirs" : "--ours";
            var co = await RawAsync(repo, new[] { "checkout", side, "--", filePath }, ct);
            if (!co.Ok) return co;
            return await RawAsync(repo, new[] { "add", "--", filePath }, ct);
        }

        public Task<GitResult> BackupRefAsync(string repo, string operationId, CancellationToken ct = default) =>
            RawAsync(repo, new[] { "update-ref", $"refs/syncro/backup/{operationId}", "HEAD" }, ct);

        public async Task<GitResult> RestoreBackupRefAsync(string repo, string operationId, CancellationToken ct = default)
        {
            var refName = operationId.StartsWith("refs/", StringComparison.Ordinal)
                ? operationId
                : $"refs/syncro/backup/{operationId}";
            await RawAsync(repo, new[] { "merge", "--abort" }, ct);
            await RawAsync(repo, new[] { "cherry-pick", "--abort" }, ct);
            return await RawAsync(repo, new[] { "reset", "--hard", refName }, ct);
        }
    }
}
