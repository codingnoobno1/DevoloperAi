using System.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>Per-repo automation policy. Everything outward-facing is OFF by default.</summary>
    public sealed class GitSyncPolicy
    {
        public bool AutoFetch { get; set; } = true;          // read-only — safe
        public bool AutoCommit { get; set; } = false;        // opt-in
        public bool AutoPush { get; set; } = false;          // opt-in AND guarded (outward)
        public string AutoCommitMessage { get; set; } = "chore: auto-sync";
        /// <summary>Branches auto-commit/push may touch. Empty = none (safest).</summary>
        public HashSet<string> BranchAllowlist { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Latest observed sync state for a repo (what the UI badge shows).</summary>
    public sealed class GitSyncState
    {
        public string Repo { get; init; } = "";
        public string Branch { get; set; } = "";
        public int Ahead { get; set; }
        public int Behind { get; set; }
        public bool Dirty { get; set; }
        public DateTime LastFetch { get; set; }
        public string LastAction { get; set; } = "";
    }

    /// <summary>
    /// Auto-sync worker (git-sync-engine.md §5). On an interval, per registered repo: fetch
    /// (read-only) and refresh ahead/behind. Auto-commit/auto-push are opt-in per policy and pass
    /// hard guardrails first — auto-push is outward-facing, so it only runs when the user has
    /// explicitly enabled it for a branch on the allowlist and the tree/state is safe. Destructive
    /// ops are never automated.
    /// </summary>
    public sealed class GitSyncService : IDisposable
    {
        private readonly IGitEngine _git;
        private readonly ConcurrentDictionary<string, GitSyncPolicy> _repos = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, GitSyncState> _state = new(StringComparer.OrdinalIgnoreCase);
        private readonly Syncro.Desktop.Services.Agent.Policy.AgentPolicyStore? _store;
        private CancellationTokenSource? _cts;

        public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);

        public GitSyncService(IGitEngine git) => _git = git;

        /// <summary>
        /// Persistent mode (agentsettings.md D13): registrations live in the agent policy store
        /// (Sync &amp; Pull tab) instead of only in memory, so they survive restarts.
        /// </summary>
        public GitSyncService(IGitEngine git, Syncro.Desktop.Services.Agent.Policy.AgentPolicyStore store)
        {
            _git = git;
            _store = store;
            LoadFrom(store.Current);
            store.Changed += LoadFrom;
        }

        public IReadOnlyCollection<GitSyncState> States => (IReadOnlyCollection<GitSyncState>)_state.Values;

        public bool IsRegistered(string repo) => _repos.ContainsKey(repo);

        public IReadOnlyDictionary<string, GitSyncPolicy> Registrations => _repos;

        public void Register(string repo, GitSyncPolicy? policy = null)
        {
            _repos[repo] = policy ?? new GitSyncPolicy();
            Persist($"auto-sync enabled for {repo}");
        }

        public void Unregister(string repo)
        {
            _repos.TryRemove(repo, out _);
            _state.TryRemove(repo, out _);
            Persist($"auto-sync disabled for {repo}");
        }

        private void LoadFrom(Syncro.Desktop.Services.Agent.Policy.AgentPolicy p)
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, p.GitSync.IntervalMinutes));
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in p.GitSync.Repos)
            {
                if (string.IsNullOrWhiteSpace(r.Repo)) continue;
                wanted.Add(r.Repo);
                var pol = new GitSyncPolicy
                {
                    AutoFetch = r.AutoFetch,
                    AutoCommit = r.AutoCommit,
                    AutoPush = r.AutoPush,
                    AutoCommitMessage = string.IsNullOrWhiteSpace(r.AutoCommitMessage) ? "chore: auto-sync" : r.AutoCommitMessage,
                };
                foreach (var b in r.BranchAllowlist) pol.BranchAllowlist.Add(b);
                _repos[r.Repo] = pol;
            }
            foreach (var key in _repos.Keys)
                if (!wanted.Contains(key)) { _repos.TryRemove(key, out _); _state.TryRemove(key, out _); }
        }

        private void Persist(string reason)
        {
            if (_store == null) return;
            var snapshot = _repos.ToArray();
            _store.Update(p =>
            {
                p.GitSync.Repos = snapshot.Select(kv => new Syncro.Desktop.Services.Agent.Policy.GitSyncRepo
                {
                    Repo = kv.Key,
                    AutoFetch = kv.Value.AutoFetch,
                    AutoCommit = kv.Value.AutoCommit,
                    AutoPush = kv.Value.AutoPush,
                    AutoCommitMessage = kv.Value.AutoCommitMessage,
                    BranchAllowlist = kv.Value.BranchAllowlist.ToList(),
                }).ToList();
            }, reason);
        }

        public void Start()
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Stop() { try { _cts?.Cancel(); } catch { } _cts = null; }

        private async Task LoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var (repo, policy) in _repos)
                {
                    try { await TickAsync(repo, policy, ct); } catch { /* keep the loop alive */ }
                }
                try { await Task.Delay(Interval, ct); } catch { break; }
            }
        }

        /// <summary>One sync pass for a repo. Public so the UI / an MCP tool can trigger it on demand.</summary>
        public async Task<GitSyncState> TickAsync(string repo, GitSyncPolicy policy, CancellationToken ct = default)
        {
            var state = _state.GetOrAdd(repo, r => new GitSyncState { Repo = r });

            if (policy.AutoFetch)
            {
                await _git.FetchAsync(repo, prune: true, ct);
                state.LastFetch = DateTime.UtcNow;
            }

            var st = await _git.StatusAsync(repo, ct);
            state.Branch = st.Branch;
            state.Ahead = st.Ahead;
            state.Behind = st.Behind;
            state.Dirty = !st.IsClean;
            state.LastAction = "fetch";

            // ── guarded auto-commit ──
            if (policy.AutoCommit && !st.IsClean && Allowed(policy, st))
            {
                var c = await _git.CommitAsync(repo, policy.AutoCommitMessage, stageAll: true, ct: ct);
                if (c.Ok) { state.LastAction = "auto-commit"; st = await _git.StatusAsync(repo, ct); state.Ahead = st.Ahead; state.Dirty = !st.IsClean; }
            }

            // ── guarded auto-push (outward — only ff to tracked upstream) ──
            if (policy.AutoPush && st.Ahead > 0 && st.Behind == 0 && Allowed(policy, st) && st.Upstream != null)
            {
                var p = await _git.PushAsync(repo, ct: ct);
                if (p.Ok) { state.LastAction = "auto-push"; state.Ahead = 0; }
            }

            return state;
        }

        /// <summary>All guardrails for any automated write: allowlisted branch, no in-progress merge.</summary>
        private static bool Allowed(GitSyncPolicy policy, GitStatus st) =>
            st.IsRepo
            && !string.IsNullOrEmpty(st.Branch)
            && st.Branch != "(detached)"
            && policy.BranchAllowlist.Contains(st.Branch);

        public void Dispose() => Stop();
    }
}
