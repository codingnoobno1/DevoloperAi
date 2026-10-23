# Project Syncro (web) ↔ Syncro Desktop agent — clarity & joint plan (2026-10-23)

## The goal

The web platform's **admin dashboard** is the control room. From there a lead:
- assigns duties (who works on which files),
- watches the pull requests that come back, and
- runs a conflict simulation before anything is merged.

On each member's PC, **Syncro Desktop** receives that member's work, clones the repo, checks out the task
branch and guards the commits. When the member finishes, it verifies the work and opens the PR. Three
members on three PCs should run end-to-end without anyone copying repo links or branch names by hand.

```
Admin dashboard (web)                         Member PCs (Syncro Desktop × 3)
─────────────────────                         ───────────────────────────────
1 assign task + files + branch ──► API ──►    2 agent picks up task, auto-clones, checks out branch
                                              3 member + agent work; scope guard + Agent Settings
5 PR board: gates, conflicts, merge ◄─ API ◄─ 4 Verify & Complete → push branch → open PR
6 conflict simulator (job) ──────► API ──►    agent runs git merge-tree, uploads per-file result
```

---

## Part 1 — What the web platform has today

**Which code was checked:** `Syncro.Desktop\Services\pixel-platform` at commit `18b2043` (today, pushed,
level with `origin/master`). Next.js 16, Mongoose, next-auth.

⚠️ There is a second, stale clone at `D:\pixelclubaup\pixel-platform` (last commit 2026-08-04), and
`CLAUDE.md` points to it. My earlier audit read that copy. The team and file-tree endpoints **do exist**
in the current code and on the live site.

### 1.1 Real and working

| Area | What exists | Where |
|---|---|---|
| Auth & roles | next-auth, JWT for mobile/desktop, roles (`master_admin`, `sankalp_associate`, `sankalp_member`) | `lib/auth`, `api/mobile/login` |
| Admin dashboard | Real counts: users, proposals, projects, tasks; recent users and proposals | `admin/dashboard` |
| Admin projects | List, detail, status/progress edit, members & lead, **basic** task create (title, description, assignee, deadline, priority) | `admin/projects/[id]` (712 lines) |
| Task model with git scope | `repoUrl`, `baseBranch`, `targetBranch`, `assignedFiles`, `allowedGlobs`, `readOnlyFiles`, `fileLockMode` (advisory/strict/exclusive), `latestCommitSha`, `commitCount`, `pullRequestUrl`, `verificationGate` | `models/Task.ts` |
| Duty assignment API with collision check | Creating a task checks the files against other active tasks and returns **409 with the colliding files** unless the lock mode is advisory or `forceAssign` is set | `api/mobile/project-progress/tasks` (POST) |
| My tasks + teammate locks | A member's tasks, with locks held by teammates grouped per project | `api/mobile/user/tasks` (live: 401 without a token) |
| Team roster | Project roles (lead/architect/backend/frontend/mobile/sre/qa), owned paths, max active tasks, workload, locked files | `api/mobile/projects/[id]/team` (live) |
| Git file tree | Desktop uploads the tracked files and branches; the server stores them and returns locks | `api/mobile/git/tree` (live) |
| Member task pages | My tasks, project tasks, team progress, weekly review | `(app)/tasks`, `(app)/projects/[id]/…` |
| Desktop download page | Landing page only | `(app)/desktop` |

### 1.2 Partly there

| Area | Gap |
|---|---|
| Admin task form | Doesn't expose the git-scope fields. Admins can't set files, globs, lock mode or target branch, and don't see 409 collisions. Today only the desktop can create scoped tasks |
| Admin team panel | Uses `api/admin/projects/[id]/team` (members only). Roles and owned paths are only editable through the mobile API |
| Verification gate | Stores 4 booleans only. The desktop now sends a status per gate ("passed", "failed", "not run: …"), but the update route copies just the booleans, so "not run" turns into `false` and looks like "failed" |
| PR link | `pullRequestUrl` is a plain string the desktop can set. Nothing reads PR state |

### 1.3 Fake on the web side

| Area | Problem |
|---|---|
| `api/git/scan` | Repo stats are `Math.random()` (comment: "In real app: octokit…") |
| `api/mobile/git/sync` | Returns hard-coded stats (124 commits, 12 stars, 3 forks…) |

### 1.4 Missing entirely

| Needed for the goal | Status |
|---|---|
| **PR control** (list PRs, checks, review state, mergeable, merge) | ❌ No GitHub integration at all (no octokit, no GitHub token/App) |
| **Conflict simulator on the dashboard** | ❌ Exists only in Syncro Desktop. The web server can't run git, so it needs an agent to run it |
| **Desktop agent registry** (which PCs are online, for whom, which version, where the workspace is) | ❌ Only user presence exists, not devices |
| **Job queue** (tell a specific agent: set up task / simulate / verify / open PR) | ❌ None. Desktop only polls "my tasks" when a page is open |
| **Auto-clone** without the member clicking | ❌ Desktop clones only when the member clicks "accept" on AI Tasks |

## Part 2 — What the desktop has today (relevant parts)

| Capability | Status |
|---|---|
| Fetch my tasks, team, locks; create/update/delete tasks; upload file tree | ✅ real API calls (no fake seed data any more) |
| Accept a task → clone to `Documents\SyncroWorkspaces\<taskId>` if missing → checkout/create target branch → activate scope guard → mark in-progress | ✅ real, but **manual** (button) |
| Commit scope guard (in-scope / read-only / teammate lock / strict vs advisory) | ✅ real; only for a task matching the open repo |
| Verify & Complete (SAST, build, tests, contracts; complete only if they pass) | ✅ real |
| Conflict simulation (`git merge-tree`), history learning, resolver | ✅ real, local only |
| Agent Settings decider, approval inbox, audit | ✅ real |
| Push branch + open PR | ❌ "Open PR" only prepares a form; there's no GitHub API call |
| Background worker that acts on the platform's instructions | ❌ none |

---

## Part 3 — Target workflow, step by step

| # | Who / where | What happens | Backed by |
|---|---|---|---|
| 1 | Admin, web | Create project, link GitHub repo, set team roster (roles, owned paths) | existing + **W1** admin roster UI |
| 2 | Admin, web | Assign duty: title, member, files/globs, lock mode, base and target branch. A collision shows the conflicting files and owner before saving | existing 409 API + **W1** admin form |
| 3 | Server | Creates a `setup-task` job for that member's registered PCs | **W2** devices + jobs |
| 4 | Member PC | Agent worker claims the job, clones (or fetches) the repo into the workspace, creates/checks out the target branch, activates the scope guard, reports `ready` with device, path and HEAD sha. Runs through Agent Settings, so auto-clone is a policy choice | **D2** job worker |
| 5 | Member PC | Member + agent work; commits outside scope or on teammate locks are blocked (strict) or warned (advisory) | existing |
| 6 | Member PC | Verify & Complete → real gates → push branch → ask the platform to open the PR → task gets the PR URL and per-gate statuses | **D3** push + **W3** PR API |
| 7 | Admin, web | **PR board**: per project, each task's PR with gates, GitHub checks, review state, mergeable, conflict report; approve / request changes / merge (merge only if gates passed and not blocked) | **W3** GitHub App |
| 8 | Admin, web | **Conflict simulator**: pick PR → target (or "all open PRs into main"); the server queues a `simulate` job to an online agent, which runs git merge-tree and uploads per-file results (type, rule, risk). It also runs automatically when a task branch is pushed | **W4** + **D4** |
| 9 | Admin, web | **Agents panel**: each member's PCs online/offline, version, current task, last job, failures | **W2** |

## Part 4 — Work plan (paired phases)

### Phase 1 — Make duty assignment usable from the admin dashboard
**W1 (web)**
- Admin task form gets file picker (from the stored file tree), globs, read-only files, lock mode, base/target branch. The 409 collision list is shown inline with a "force assign" option for admins only.
- Admin team panel edits roles, owned paths and max active tasks (reuse `mobile/projects/[id]/team` logic in the admin route).
- `verificationGate` schema gains `buildStatus`, `testsStatus`, `sastStatus`, `contractsStatus` (strings), and the update route stores them.
- Remove the fake git stats: show "not synced" until W3 provides real GitHub numbers.

**D1 (desktop)**
- Nothing new needed beyond what's built. Re-test against the live endpoints with a real account.

### Phase 2 — Agents on each PC get their work automatically
**W2 (web)**
- Model `AgentDevice` { userId, deviceId, name, os, appVersion, workspaceRoot, lastSeenAt, status }.
- `POST /api/mobile/agents/register`, `POST /api/mobile/agents/heartbeat` (every 60 s).
- Model `AgentJob` { type: `setup-task` | `simulate` | `verify` | `open-pr`, projectId, taskId, targetUserId, targetDeviceId?, payload, status: queued → claimed → running → done | failed, result, claimedBy, timestamps, attempts }.
- `GET /api/mobile/agents/jobs?claim=1` (atomic claim, 5-min lease), `POST /api/mobile/agents/jobs/{id}/result`.
- Creating or reassigning a task enqueues `setup-task` for the assignee.
- Admin **Agents panel**.

**D2 (desktop)**
- `DeviceIdentity` (stable id, name) + register/heartbeat in the runtime coordinator.
- `AgentJobWorker : IRuntimeWorker` polls every 30 s and claims jobs. It runs `setup-task` with the existing clone → branch → scope code and reports the result. Every step goes through `AgentGuard`, so the member's autonomy level and permissions apply. New Agent Settings option: "Auto-accept setup jobs for my own tasks" (off = it shows in the approval inbox).
- Workspace root setting (default `Documents\SyncroWorkspaces`).

### Phase 3 — PR control
**W3 (web)**
- Recommendation: **GitHub App** installed on the org (server-side token, fine-grained permissions). Store the installation per project. The alternative is a per-member PAT, but that spreads tokens across PCs and can't merge on behalf of the lead.
- `POST /api/mobile/tasks/{id}/pr` opens the PR from target → base branch with a body built from the gate statuses.
- A webhook (pull_request, check_suite, pull_request_review) updates the task: PR state, checks, mergeable.
- Admin **PR board** with approve / request changes / merge. Merge is refused when a required gate failed or GitHub reports conflicts.

**D3 (desktop)**
- After Verify & Complete passes: push the branch with the member's own git credentials (existing credential resolver), then call the PR endpoint and save the PR URL.

### Phase 4 — Conflict simulator on the dashboard
**W4 (web)**
- "Simulate" button on the PR board and project page; enqueues a `simulate` job for any online agent that has the repo (the lead's PC first).
- Stores and shows the result table (file, git code, conflict type, rule, risk, decision).
- Auto-enqueue when a task branch is pushed (from the webhook).

**D4 (desktop)**
- The job handler calls the existing `ConflictResolutionService.SimulateReplayAsync` (git merge-tree, no writes) after a `git fetch`, and uploads the report.

### Phase 5 — Three-PC acceptance test
1. Three real member accounts, each with Syncro Desktop installed and registered → all three appear online in the Agents panel.
2. The lead assigns three tasks with non-overlapping files. A fourth task that overlaps returns the 409 collision list.
3. Each PC auto-clones and checks out its branch within a minute. The task shows `ready` with device and sha.
4. Each member commits; an out-of-scope commit is blocked in strict mode.
5. Each runs Verify & Complete → PR opened → three PRs on the PR board with gate statuses.
6. Simulate "all open PRs into main" → the dashboard shows the files that conflict between PRs and suggests a merge order.
7. Merge in that order; the board updates from webhooks.

## Part 5 — Decisions for you

1. **Canonical web repo path.** Keep `Syncro.Desktop\Services\pixel-platform` (current) and retire `D:\pixelclubaup\pixel-platform`? CLAUDE.md should point to whichever you keep.
2. **GitHub access.** GitHub App (recommended) or per-member tokens?
3. **Where simulations run.** On the lead's PC (simple) or on a dedicated always-on runner PC (reliable)?
4. **Auto-clone consent.** Should "auto-accept setup jobs" default to on for a member's own tasks, or start in the approval inbox?
5. **Order.** Proposal: Phase 1 → 2 → 3 → 4, with the three-PC test at the end of Phase 2 (assignment + auto-clone) and again at the end.
