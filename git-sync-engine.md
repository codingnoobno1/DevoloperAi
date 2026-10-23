# Git Sync Engine — Analysis & Plan

> Unify Syncro's scattered git code into one engine that can **auto-fetch, commit, push, show
> history, roll back, and show branches/contributors**, driven by the LLM (via the E1 MCP bridge)
> and by a **dynamic script/workflow runner** — with credentials (SSH / token / GPG) handled through
> git's own native mechanisms, never stored raw.
>
> Companion to `syncro-codeoss-mcp.md` / `extension-system.md`. Status: **PLAN**, grounded in a recon
> of the real code (2026-07-16). Phase gates below.

---

## 0. What exists today (facts from the code)

Git logic is **spread across five services and three different process runners** — this fragmentation
is the core problem.

| Where | What it does | Runner |
|---|---|---|
| `Services/Engine/Mcp/Servers/GitMcp` | **The only LLM-reachable surface** — 3 tools: `CloneRepository`, `GitStatus`, `Commit` | CliWrap |
| `Services/ProgramLogic/GitCommandService` (`IGitService`) | The richest: `RunGitCommand`, status, repo info, current branch, **Pull, Push, Commit**, language % | `CommandExecutionService` |
| `Services/Git/GitAutomationService` | SSH-aware **clone** (`GIT_SSH_COMMAND = ssh -i <key>`), `ValidateSshKey` (defaults `~/.ssh/id_rsa`) | CliWrap |
| `Services/SyncroCLI/Providers/Git/GitBranchService` | `CreateBranch`, `Checkout`, `Merge` | `ProcessRunner` |
| `Services/AST/Git/GitRepoService` | Clone storage dir under `SyncroWorkspace/cloned_repos` | Process |

**Dynamic script / workflow foundation already exists and is good:**
- `Services/ProgramLogic/BatchFileExecutionService` — writes `scriptContent` to a `.bat`, runs `cmd /C`, returns output. **The dynamic bat runner.**
- `Services/AgentCli/Scripts/ScriptStore` + `ScriptRecord` — persisted scripts with `Shell` (bat|sh|ps1|py), `{{placeholders}}` (**"never bake secrets"**), `Safe`/`Flagged`, `Source` (seed|llm|human), run stats.
- `ScriptRunnerAdapter` — resolves placeholders at run time, **safety-scans** (`ScriptSafetyScanner`) before running, executes via `IProcessExecutor`.

**Credentials:** SSH is wired **only for clone**; **no GPG signing, no token/PAT** path anywhere.

---

## 1. The gap (map of the ask → reality)

| You want | Today | Verdict |
|---|---|---|
| Auto-fetch latest → local | `Pull` exists; **no fetch, no polling loop** | ⚠️ partial |
| Auto commit | `Commit`/`CommitChanges` exist | ✅ |
| Push | `Push` exists but **not MCP-exposed, not automated** | ⚠️ hidden |
| Show previous commit list | — | ❌ **new** (`git log`) |
| Roll back | only `RollbackPatchTool` (unrelated) | ❌ **new** (restore/reset/revert) |
| Branch + contributors | current branch ✅; create/checkout ✅; **contributors ❌** | ⚠️ partial (`shortlog`) |
| Built-in dynamic .bat runner | `BatchFileExecutionService` + `ScriptStore` | ✅ reuse |
| SSH keys automatically | clone only | ⚠️ extend to fetch/push |
| GPG signing | — | ❌ **new** |
| Token / PAT | — | ❌ **new** (credential helper) |
| Workflow builder | `ScriptStore` stores single scripts | ⚠️ needs multi-step composition |

**One-line diagnosis:** the *pieces* exist but are fragmented, mostly invisible to the LLM, missing
history/rollback/contributors, and have no credential abstraction or automation loop.

---

## 2. Architecture — one engine, three consumers

```
                    ┌──────────────  IGitEngine (Services/Git/)  ──────────────┐
                    │  ONE process runner (consolidate the 3) + result parser  │
                    │  fetch · pull · stage · commit(-S?) · push · log · diff  │
                    │  branches · contributors · restore/reset/revert (rollback)│
                    │  status · ahead/behind                                   │
                    │        │                    │                     │       │
                    │  GitCredentialResolver   (guardrails)      (parsers→models)│
                    │  ssh-agent / GIT_SSH_CMD                                   │
                    │  credential.helper (PAT)                                  │
                    │  commit.gpgsign (GPG)                                     │
                    └───────┬───────────────┬──────────────────┬───────────────┘
                            │               │                  │
              ┌─────────────▼──┐   ┌────────▼────────┐   ┌─────▼──────────────────┐
              │ GitSyncWorker  │   │ GitMcp (3 → N)  │   │ Workflow builder        │
              │ IRuntimeWorker │   │ log/rollback/   │   │ (ScriptStore + Batch    │
              │ periodic fetch │   │ push/contribs…  │   │  runner): compose git+  │
              │ ahead/behind   │   │ → editor chat   │   │  shell steps → saved,   │
              │ opt-in auto    │   │ + Syncro Groq   │   │  runnable, safety-scanned│
              │ commit/push    │   └─────────────────┘   └─────────────────────────┘
              └────────────────┘
```

Everything routes through **one** `IGitEngine`. The three existing runners collapse into it. The
existing `ScriptStore`/`BatchFileExecutionService` become the workflow-builder substrate.

---

## 3. The operation set (`IGitEngine`)

| Op | git | Status |
|---|---|---|
| `FetchAsync` | `fetch --all --prune` | new |
| `PullAsync` | `pull --ff-only` (configurable) | reuse |
| `StatusAsync` | `status --porcelain=v2 --branch` → ahead/behind + files | upgrade (porcelain v2) |
| `CommitAsync` | `add -A` + `commit -m … [-S]` | reuse + GPG opt |
| `PushAsync` | `push [--set-upstream]` | reuse + expose |
| `LogAsync` | `log --pretty=format:… -n N` → `[{sha,author,email,date,subject}]` | **new** |
| `DiffAsync` | `diff [--stat] <ref>` | new |
| `RollbackAsync` | tiered: `restore` (file) / `revert` (safe, new commit) / `reset --hard` (**Flagged**) | **new** |
| `BranchesAsync` | `branch -a --format=…` + current | upgrade |
| `ContributorsAsync` | `shortlog -sne --all` → `[{name,email,commits}]` | **new** |
| `BranchGraphAsync` | `log --all --graph --oneline` (for the UI) | new |

All return typed models (`GitCommit`, `GitContributor`, `GitBranch`, `GitStatus{ahead,behind,files}`)
— not raw strings — so the UI and MCP tools share one shape.

---

## 4. Credentials & signing — native, never stored raw

**Rule zero: Syncro never holds a raw PAT, SSH private key, or GPG passphrase.** It configures git to
use the OS's own secure mechanisms and lets git/ssh/gpg do the auth. (`ScriptRecord` already encodes
this instinct — "never bake secrets".)

`GitCredentialResolver` auto-detects per remote:

| Mechanism | How Syncro wires it | Secret lives in |
|---|---|---|
| **SSH key** | `GIT_SSH_COMMAND = ssh -i <key> -o StrictHostKeyChecking=accept-new`; prefer a running **ssh-agent** | disk (`~/.ssh`) / agent |
| **Token (PAT)** | git **credential helper** (`manager`/`store`) or a scoped `GIT_ASKPASS` shim that reads the OS credential manager | Windows Credential Manager |
| **GPG signing** | set `commit.gpgsign=true` + `user.signingkey`; git invokes gpg/gpg-agent | gpg keyring |

- HTTPS remote + PAT present → credential helper. SSH remote → key/agent. Signing is opt-in per repo.
- The engine **detects** what's available (`ssh -T`, `gpg --list-secret-keys`, helper config) and
  reports it; it never prompts for or stores the secret itself.
- Entering a credential value is **out of scope for the app** — that stays in the OS tools, per
  Syncro's safety rules.

---

## 5. Auto-sync worker (`GitSyncWorker : IRuntimeWorker`)

Same pattern as `HealthWorker`. Per registered repo, on an interval:
1. `fetch --prune` (read-only, always safe) → update ahead/behind → emit a Universe/runtime event.
2. **Optional, opt-in, guarded** auto-commit / auto-push:
   - Off by default. Enabled per-repo with an explicit policy.
   - **Guardrails (all must pass):** branch on an allowlist · working tree matches an author-approved
     pattern · not detached HEAD · no merge/rebase in progress · push only `--ff` to the tracked
     upstream. Anything else → surface, don't act.
   - Auto-**push** is outward-facing and hard to reverse → it **notifies and waits for confirmation**
     unless the user has durably opted that repo into unattended push. (Matches the "confirm
     outward-facing actions" rule.)
3. Never auto-runs a destructive op (reset --hard, force-push, branch delete) — those are always manual.

---

## 6. Workflow builder (on the existing script engine)

Compose multi-step git+shell workflows, dynamically generated and saved — built on `ScriptStore` +
`BatchFileExecutionService`, not a new runner.

- A **`GitWorkflow`** = ordered steps, each a git op *or* a `ScriptRecord` (bat|sh|ps1|py), with
  `{{placeholders}}` resolved at run time (secrets never baked, per the existing rule).
- The builder **materializes** the workflow into a dynamically generated script (bat on Windows,
  sh elsewhere) and runs it through `BatchFileExecutionService`, streaming output.
- Every step passes the existing **`ScriptSafetyScanner`**; destructive steps are `Flagged` →
  human approval required. Success/failure feeds the `ScriptRecord` run stats.
- Examples: "sync-all" (fetch→ff-pull→build→test across N repos), "release" (tag→push→notify),
  "hotfix" (branch→cherry-pick→push). Authored in UI or drafted by the LLM, then saved + reused.

---

## 7. MCP tools (GitMcp 3 → N; auto-surface via E1)

Each new `IMcpTool` appears in code-oss chat and Syncro's Groq loop with zero wiring. Read tools are
`RequiresAdminApproval=false`; write/outward tools are gated.

| Tool | Gate |
|---|---|
| `GitLog` / `GitContributors` / `GitBranches` / `GitDiff` / `GitStatus` | open (read-only) |
| `GitFetch` | open (read-only fetch) |
| `GitCommit` (`-S` opt) / `GitPush` | **approval** (write / outward) |
| `GitRollback` (restore/revert) | **approval**; `reset --hard`/force-push **refused** from the LLM path |
| `RunGitWorkflow` | **approval** + safety scanner |

So the editor agent can *answer* freely ("who are the top contributors?", "show the last 10 commits",
"what changed vs main?") and *act* only behind approval — the same posture as the E1 endpoint.

---

## 8. UI (extends `Components/Pages/Git/GitManager.razor`)

- **History** tab: commit list (sha/author/date/subject), click → diff; branch graph (`--graph`).
- **Contributors** tab: `shortlog -sne` with commit counts (bars).
- **Sync** bar: ahead/behind badges, Fetch/Pull/Push buttons, auto-sync toggle + policy.
- **Rollback**: per-file restore, per-commit revert; `reset --hard` behind a typed-confirmation dialog.
- **Workflows**: build/run/save git+shell workflows; live output.
- **Credentials**: read-only status ("SSH key ✓ · PAT via Credential Manager ✓ · GPG signing off") —
  detection only, never an input for the secret itself.

---

## 9. Phases (each ends with a runnable gate)

### G0 — Unify: `IGitEngine` + one runner
Collapse the 3 runners into one; port existing ops (status/pull/push/commit/branch) behind typed
models. No behavior change, just consolidation.
- **Gate (headless):** engine reports `StatusAsync` (ahead/behind + files) and `CurrentBranch` on the
  Syncro repo, identical to today's output.

### G1 — History, contributors, rollback (read + safe write)
`LogAsync`, `ContributorsAsync`, `BranchesAsync`, `DiffAsync`, `RollbackAsync` (restore/revert only).
- **Gate:** on this repo, `LogAsync(10)` returns 10 typed commits; `ContributorsAsync` matches
  `git shortlog -sne`; `revert` of a scratch commit produces a new commit (tree unchanged).

### G2 — MCP tools + editor chat
Register the read tools + gated write tools; surface via E1.
- **Gate (over `POST /mcp`):** `tools/call GitLog{n:5}` returns 5 commits; `GitContributors` returns
  the list; `GitPush` without approval is refused.

### G3 — Credentials & signing
`GitCredentialResolver` (SSH/agent/PAT-helper/GPG detection + wiring); GPG-signed commit path.
- **Gate:** engine reports detected mechanisms; a signed commit on a test repo shows `gpg: Signature`
  in `git log --show-signature` (only if a key exists — else reports "no signing key", no error).

### G4 — Auto-sync worker
`GitSyncWorker` periodic fetch + ahead/behind events; opt-in guarded auto-commit/push (off by default).
- **Gate:** worker fetches on interval and emits ahead/behind; auto-push stays disabled until
  explicitly enabled and every guardrail passes.

### G5 — Workflow builder
`GitWorkflow` model + materializer over `ScriptStore`/`BatchFileExecutionService`; safety-scanned.
- **Gate:** a saved "fetch→ff-pull→status" workflow runs across 2 repos and streams output; a workflow
  containing `reset --hard` is Flagged and blocked without approval.

**First slice: G0 + G1 + G2** — unify, add history/contributors/rollback, and make it all LLM-drivable.
G3/G4/G5 layer credentials, automation, and workflows on top.

---

## 10. Security & risk

| Risk | Mitigation |
|---|---|
| Auto-push corrupts a shared branch | Off by default; branch allowlist + `--ff`-only + confirmation for outward push |
| Secrets leak into scripts/logs | Never bake secrets (existing rule); credentials via OS git mechanisms only; scrub tokens from streamed output |
| Destructive rollback (`reset --hard`, force-push) | Never from the LLM path; UI requires typed confirmation; always `Flagged` in workflows |
| Three runners drift / double-quote bugs | G0 consolidates to one runner with one arg-escaping path |
| LLM triggers a write it shouldn't | Write/outward MCP tools are approval-gated (same as E1's posture); reads are free |
| GPG/SSH absent on a machine | Detect-and-report, never fail hard; features degrade to "unavailable + how to enable" |
