# scriptsfeatureai.md — Git Workflow Scripts, AI Git Commands & PR System

Documentation for the **15 dynamic PowerShell scripts** in [`scripts/git/`](scripts/git/) that
close the gaps in Syncro's git subsystem: **workflow repair**, **safe push**, **account
connection**, **AI-assisted commits/PRs**, and **Groq enablement**.

Each script is standalone, non-throwing, and safe-by-default. They share one helper,
[`_common.ps1`](scripts/git/_common.ps1), and follow the same rules as
`Services/Git/GitEngine.cs`: never hang on an auth prompt (`GIT_TERMINAL_PROMPT=0`), never bake
secrets into a file, and gate every destructive action behind `-Yes`.

---

## Why these exist (mapped to real gaps)

The exploration of `Services/Git/*` and `Components/Pages/Git/GitManager.razor` found that the
engine is a deliberately **safe tier** (status/branch/log/fetch/pull-ff/push/commit/revert) with
**no** merge, rebase, stash, reset, conflict resolution, remote management, clone, **PR creation**,
or **account linking** — and Groq's key is only read from an env var (`Initialize` /
`ValidateConnectionAsync` are dead code). These scripts fill exactly those holes:

| Gap in the app | Script that fills it |
|---|---|
| No repo health check / repair | `git-doctor.ps1`, `git-repair.ps1` |
| Two-click push with no guard-rails | `git-safe-push.ps1`, `git-sync.ps1` |
| **No GitHub/PAT/SSH account connection** | `git-connect-github.ps1`, `git-connect-ssh.ps1` |
| No remote management UI | `git-set-remote.ps1` |
| No branch conventions | `git-new-branch.ps1` |
| Commit drafting only in-app | `git-commit-ai.ps1` (mirrors `GitCommitDrafter`) |
| **No PR creation anywhere** | `git-open-pr.ps1` |
| No conflict UI | `git-resolve-conflicts.ps1` |
| No safe undo (reset excluded from engine) | `git-undo.ps1` |
| No cleanup | `git-cleanup.ps1` |
| **No way to enable Groq** | `enable-groq.ps1`, `groq-test.ps1` |

---

## The 15 scripts

### Repair & health
| Script | Does | Example |
|---|---|---|
| `git-doctor.ps1` | Diagnose detached HEAD, stale `index.lock`, in-progress merge/rebase, missing upstream, drift, unreachable remote, oversized files. `-Fix` applies safe repairs. | `./git-doctor.ps1 -Fix` |
| `git-repair.ps1` | Abort stuck merge/rebase/cherry-pick, clear stale locks, refresh index, `-Fsck` integrity check. | `./git-repair.ps1 -AbortOps -Fsck` |

### Push & sync
| Script | Does | Example |
|---|---|---|
| `git-safe-push.ps1` | Set upstream if missing, integrate remote first, refuse force unless `-Force` (uses `--force-with-lease`). | `./git-safe-push.ps1 -Rebase` |
| `git-sync.ps1` | Fetch + fast-forward pull + optional push; stops on divergence instead of guessing. | `./git-sync.ps1 -Push` |

### Account connection
| Script | Does | Example |
|---|---|---|
| `git-connect-github.ps1` | HTTPS + PAT: set identity, configure credential manager, store token via `git credential approve`, verify with `ls-remote`. Token from `-Token` or `$env:GH_TOKEN` — never written to disk. | `$env:GH_TOKEN='ghp_…'; ./git-connect-github.ps1 -GitHubUser codingnoobno1` |
| `git-connect-ssh.ps1` | Generate ed25519 key if absent, load agent, set `core.sshCommand`, print the public key to register, verify `ssh -T`. | `./git-connect-ssh.ps1 -Email you@example.com` |
| `git-set-remote.ps1` | Add/update a remote, convert HTTPS↔SSH, verify reachability. Accepts `owner/repo` shorthand. | `./git-set-remote.ps1 -Repo codingnoobno1/Syncro.Desktop -As ssh` |

### Branch & commit
| Script | Does | Example |
|---|---|---|
| `git-new-branch.ps1` | Update base, branch off it, enforce `feat/…`/`fix/…` naming, optional publish. | `./git-new-branch.ps1 -Name login-page -Publish` |
| `git-commit-ai.ps1` | Stage, generate a Conventional-Commits message with Groq, show, commit. `-DryRun` to preview. | `./git-commit-ai.ps1 -DryRun` |

### PR system
| Script | Does | Example |
|---|---|---|
| `git-open-pr.ps1` | Push branch, then open a PR via **`gh` CLI** if present, else the **GitHub REST API** with a PAT. Title/body auto-drafted from commits by Groq unless supplied. | `./git-open-pr.ps1 -Base main -Draft` |

### Conflicts, undo, cleanup
| Script | Does | Example |
|---|---|---|
| `git-resolve-conflicts.ps1` | List conflicts; apply `ours`/`theirs`/`manual`/`abort`; `-Continue` to finish. | `./git-resolve-conflicts.ps1 -Strategy theirs` |
| `git-undo.ps1` | Safe undo: `last` (soft reset, keep changes), `unstage`, `discard` (stashes first), `hard` (needs `-Ref -Yes`, stashes first). | `./git-undo.ps1 -Mode last` |
| `git-cleanup.ps1` | Prune dead remote branches, delete merged locals (protected list respected), optional `-Aggressive` gc. | `./git-cleanup.ps1 -Aggressive -Yes` |

### AI enablement
| Script | Does | Example |
|---|---|---|
| `enable-groq.ps1` | Set `GROQ_API_KEY`, validate with the same ping `GroqProvider.ValidateConnectionAsync` uses, persist at User scope. Key never written to the repo. | `./enable-groq.ps1 -Key gsk_…` |
| `groq-test.ps1` | Confirm the key works, list models, run a live completion. | `./groq-test.ps1 -ListModels` |

---

## AI git commands — how they work

Both AI scripts call the **same Groq endpoint and model the app hardcodes**
(`https://api.groq.com/openai/v1/chat/completions`, `llama-3.1-8b-instant` — see
`GroqProvider.cs:16,19`) via `Invoke-Groq` in `_common.ps1`. No new key, no new config.

- **`git-commit-ai.ps1`** is the scripted twin of `GitCommitDrafter`: it diffs the staged changes
  (bounded to 12 000 chars, same as the engine), asks Groq for one Conventional-Commits message,
  strips stray code fences, and commits. Use `-DryRun` to see the message without committing, `-Sign`
  to GPG-sign.
- **`git-open-pr.ps1`** feeds the branch's commit subjects to Groq and asks for a
  `{"title","body"}` JSON object, then opens the PR. Pass `-NoAI` to skip Groq and use a plain
  title/body derived from the branch name and commit list.

**Prerequisite:** run `./enable-groq.ps1` once. Without a valid `GROQ_API_KEY`, the AI scripts fail
fast with a clear message; the non-AI paths (`git-open-pr.ps1 -NoAI`) still work.

---

## PR opening system

`git-open-pr.ps1` is a two-tier opener so it works whether or not the GitHub CLI is installed:

1. **`gh` CLI** — if `gh` is on PATH and authenticated, it runs `gh pr create --base … --head … --title … --body …` (with `--draft` when asked). This is the preferred path.
2. **REST fallback** — otherwise it parses `owner/repo` from `origin`, reads a PAT from `-Token` /
   `$env:GH_TOKEN`, and `POST`s to `https://api.github.com/repos/{owner}/{repo}/pulls`. It prints the
   returned PR number and URL.

Flow it assumes (and enforces): you're on a **feature branch** (it refuses `main`/`master`), the
branch is **published** (it publishes with `--set-upstream` if needed), and there are **commits
ahead of the base**. Pair it with `git-new-branch.ps1` → `git-commit-ai.ps1` → `git-open-pr.ps1` for
a full feature loop.

> This is the PR capability the app has nowhere else — `GitCopilotService`'s `SuggestedReviewers`
> field and PR intents are unpopulated dead code, and `GitManager.razor` has no PR UI. A future UI
> button can simply shell out to this script.

---

## Recommended end-to-end workflows

**First-time account setup**
```bash
./enable-groq.ps1 -Key gsk_your_key          # turn on AI features
$env:GH_TOKEN='ghp_your_pat'
./git-connect-github.ps1 -GitHubUser codingnoobno1 -Email you@example.com
```

**Feature → PR**
```bash
./git-new-branch.ps1 -Name mobile-run-selector
# …code…
./git-commit-ai.ps1                          # AI commit message
./git-open-pr.ps1 -Base main                 # push + open PR (AI title/body)
```

**"My repo is broken"**
```bash
./git-doctor.ps1                             # diagnose
./git-repair.ps1 -AbortOps                   # unstick merge/rebase, clear locks
./git-sync.ps1 -Push                         # get back in sync
```

---

## Wiring the scripts into the app

Two existing mechanisms can run these without a new subsystem:

1. **`ShellMcp.RunBatchScript`** (`Services/Engine/Mcp/Servers/ShellMcp.cs`, `RequiresAdminApproval=true`)
   — writes a `.bat` and runs it in a visible `cmd.exe` window. Good for the interactive/long-running
   ones (`git-connect-ssh`, `git-open-pr`). Wrap with `powershell -ExecutionPolicy Bypass -File …`.
2. **`GitWorkflowRunner`** (`Services/Git/GitWorkflowRunner.cs`) — materializes ordered steps into a
   temp script, runs it head-less, and `GitWorkflowSafety.Scan` blocks destructive patterns unless
   approved. Better for the repair/sync scripts since it's cross-platform and already gated.

**Progress bridge:** `_common.ps1` best-effort-writes step/ok/warn/err lines to the named pipe
`syncro-agent-bridge` — the same pipe `Syncro.CLI/Scripts/clone-repo.ps1` already uses — so a running
desktop app can stream live progress. It silently no-ops when the app isn't listening.

**Suggested MCP additions** (not yet present): `GitOpenPr`, `GitConnectAccount`, and `EnableGroq`
tools that shell out to these scripts would give the LLM agent and the Git Manager UI the PR/account
capabilities they currently lack. `GitOpenPr` and `GitConnectAccount` should carry
`RequiresAdminApproval=true`.

---

## Safety model

- **No secrets on disk.** Tokens/keys come from parameters or environment variables and are stored
  only in the OS credential manager / user env — never echoed, never written into a script or the repo.
- **Destructive ops gated.** Force-push, hard reset, discard, gc, and branch deletion require `-Yes`
  (or an interactive `yes`), and `git-undo` takes a safety stash before `discard`/`hard`.
- **Fail fast, don't hang.** `GIT_TERMINAL_PROMPT=0` + `GCM_INTERACTIVE=Never` mean auth problems
  error immediately instead of blocking on a hidden prompt.
- **Non-throwing.** The `Invoke-Git` wrapper returns `{Code, Out}` so scripts branch on exit codes
  and print actionable messages rather than stack traces.

---

## Notes / limitations

- Scripts are **PowerShell** (Windows-first, matching the desktop target). The account/PR flows use
  `gh`/PAT and GitHub REST; GitLab/Bitbucket are not covered.
- `git-connect-ssh.ps1` generates a **passphrase-less** key so it can run head-less; rely on the
  ssh-agent for at-rest protection, or add a passphrase manually for higher security.
- All scripts were parse-validated; `git-doctor` and `git-sync` were smoke-tested against this repo
  (branch/upstream/remote-reachability/ahead-behind all verified working).
