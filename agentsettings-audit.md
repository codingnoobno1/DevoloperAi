# Agent Settings & SRE panels — real vs fake audit (2026-10-23)

How to read this:
- **Real:** the UI calls a backend that does the work, or saves a value that running code reads.
- **Saved only:** the value is saved to `policy.json`, but no code reads it.
- **Fake:** the UI shows something the backend never produced, or claims behaviour that doesn't exist.

Method: every `@bind`, `Items=` and `@onclick` on `/settings/agent` was traced to its handler and to any
code outside the page and model files that reads it. GitManager's incident panel and the AI Tasks page
were traced the same way. The conflict engine was run against a scratch repo (35/35 checks passed).

---

## 1. Agent Settings page — by tab

| Tab | Status | What is real | Saved only / fake |
|---|---|---|---|
| Model & Provider | 🟢 mostly real | Key save/test/remove (Credential Manager + live Groq call), agent model, temperature, max tool calls, daily budget, offline mode, usage bar | Saved only: resolver model, fallback model, resolver temperature, tokens per hunk, redact secrets, context sources (all labelled "Not active yet") |
| Tools & Capabilities | 🟢 real | Tool table from the live registry, overrides used by the decider, decision preview runs the real decider | — |
| Context & Retrieval | ⚪ saved only | — | Whole tab (labelled) |
| System Prompt | ⚪ saved only | — | Whole tab (labelled) |
| Guardrails | 🟡 mixed | Autonomy level, bands, protected branches, 4 of 6 weights | **Fake:** "Per-Tool Approval Overrides" chips are hard-coded (`ShellExec · Blocked`, `GitCherryPick · Guarded Auto`, `ApplyPatch · Auto if Verified`) and ignore saved overrides. **Wrong text:** the hard-floors list (see §2). **Saved only:** verification-passed and rerere bonuses. No UI for sandbox folders, so "Full Auto Sandbox" behaves like Auto + notify |
| Permissions | 🟢 real | Shell, path and network lists are enforced; test boxes call the real checker | — |
| Safety & Privacy | ⚪ saved only | — | Whole tab (labelled) |
| Environments & Freezes | 🟡 mixed | Branch → environment map, Freeze now | Saved only: scheduled freezes, break-glass (labelled) |
| Merge & Conflict Resolution | 🟡 mixed | Conflict types, rule order, git codes, approval mode, shadow mode, else action are used by the engine. Form and Plain-English rule entry work | **Fake:** "YAML" input mode — the text box is never parsed, and Add creates a form rule instead. **Misleading:** the technique catalog lists ~35 techniques with no "implemented" marker, but only T2/T13, T10 and whole-file ours/theirs run. **Saved only:** custom "Script" techniques (never executed), gate definitions (required/skippable/timeout), min confidence, global order/prefix/suffix (labelled) |
| Cherry-pick & Backport | 🟡 mixed | `-x` and mainline used by `cherry_pick` | Dedupe, revert guard, empty handling, order, backport, group pick (labelled). Three-way toggle does nothing — git has no such option |
| Sync & Pull | 🟡 mixed | Repos, fetch/commit/push toggles, allowlists, interval | Pull strategy, autostash, conflict style, rerere (labelled) |
| Commit Style | ⚪ saved only | — | Whole tab (labelled) |
| Pull Requests | ⚪ saved only | — | Whole tab (labelled) |
| Project Inheritance / Overrides | ⚪ saved only | Shows current values | Whole tab (labelled); scopes don't exist |
| Rule Import | 🟡 mixed | `.gitattributes` import and history scan are real (git merge-tree). Stack packs add preset rules | Packs mostly use techniques that aren't implemented, so those rules always hand off to a human. Plain English is a keyword match (now labelled) |
| Human Review | 🟢 real | Live approval queue, batch approve, timeout, notify | — |
| Automation | ⚪ saved only | Per-rule shadow mode (Rules tab) is honoured | Whole tab (labelled) |
| Simulator | 🟢 real | git merge-tree, real codes and types, gates shown as "not run" | Tab text still says "temporary worktree sandbox" and "merge or cherry-pick" — it's merge-tree and merge only |
| Audit Log | 🟢 real | Live jsonl log, filters, open folder | — |
| Insights | 🟡 real but overclaimed | Counts come from the policy and the audit log | Text says "auto-resolve rate", "rule reliability" and "live reliability telemetry" — none of these are computed |
| Top bar | 🟢 real | Save, Discard, Export YAML, Simulate | Export modal is titled `.syncro/agent-policy.yaml`, but nothing writes or reads that file |

## 2. Hard floors vs what the decider enforces

| Shown in UI | Reality |
|---|---|
| "Never force-push or reset --hard" — LOCKED ON | Only an editable line in the shell deny list; the user can delete it |
| "Never write directly to protected branches" | Allowed after typed confirmation |
| "Never bypass when SAST or .env secrets are touched" | `.env`/key files need typed confirmation; SAST isn't run by the decider |
| "Never bypass breaking API contract" | **Not implemented** — the decider never checks contracts |
| "Block autonomous actions during Freeze Windows" | Freeze now makes changes **ask** (not block); scheduled windows are ignored |
| *(missing from list)* | Real floors not shown: blocked paths, unknown hosts, external-editor requests, unknown tools, irreversible actions, timeout = deny |

Autonomy level text is also off. "Suggest: stage only" — it asks; nothing is staged. "Auto + Notify … 1-click
undo" — there is no undo. "Full Auto Sandbox: throwaway worktree" — it uses sandbox folders, and they
can't be set in the UI.

## 3. GitManager — incident panel and task scope

| Feature | Status |
|---|---|
| Incident banner, latest commit, git evidence | 🟢 real since last pass (no invented incident) |
| Gate cards / inline gate summary | 🟢 real; "Not run" until SAST/build/tests run |
| "Run gates on working tree" | 🟢 real SAST + build + tests |
| Proposed fix / patch step | ⚪ not built (says so) |
| **Task scope guard on commit** | 🔴 **Fake data with real impact.** See below |

**Task scope guard.** When PixelService can't reach the platform (or the user isn't logged in), it builds a seed
store with an invented project, invented teammates (Rahul Sharma, Sarah Chen, Deepinder Singh,
`@sankalp.dev` emails) and a **strict** task assigned to you on this repo. On load, GitManager activates
the first open task for **whatever repo is open**, without checking that the task belongs to it. In strict
mode, commits are then **blocked** for any file outside `Services/Git/**` and `Components/Pages/Projects/**`,
and for files "locked" by the invented teammates (e.g. `SecurityScanner.cs`, `MultiProjectScanService.cs`).

## 4. AI Tasks page

| Feature | Status |
|---|---|
| Projects, tasks, create/update/delete task | 🟡 real API calls with a silent local fallback seeded with invented data (§3) |
| Team roster add/remove | 🟢 **Correction:** `/api/mobile/projects/{id}/team` exists in the current web code (`Services/pixel-platform`, commit 18b2043) and is live. The first audit read a stale clone at `D:\pixelclubaup\pixel-platform`. The desktop now reports the real response either way |
| Sync repo file tree | 🟢 **Correction:** `/api/mobile/git/tree` exists and is live (same stale-clone mistake). A failed request is now reported as not synced instead of success |
| Methodology generator | 🟡 calls a Gemini proxy at `localhost:3020`; when that isn't running it returns a generic template that looks like AI output |
| **Verify & Complete** | 🔴 **Fake.** Every check starts as passed: a missing repo folder means "all passed" without running anything. Tests = the build result copied over. A contract-scan error counts as a pass. The task is marked 100 % complete and pushed to the platform even when checks fail, and the banner always says "Build: PASS, SAST: PASS, Contracts: PASS" |

## 5. Confirmed real (tested)

- **Approvals:** agent tool calls go through the decider → approval inbox → audit (in-app, GitCopilot, editor bridge).
- **Conflict engine:**
  - simulation and history use real git merge-tree data
  - classification looks at the conflict hunks
  - the resolver only applies implemented techniques and checks for markers before staging
  - cherry-pick works with `-x`
- **Policy migration:** old override names are cleaned up, and the default level is Suggest.

## 7. Status after fixes (2026-10-23)

All of F1–F9 are done. The build passes and the scratch-repo harness passes 44/44 checks.

| # | Done |
|---|---|
| F1 | No seed data at all (empty store); old demo ids are removed from any existing cache on load. GitManager only activates a task whose project folder or repo URL matches the open repo |
| F2 | Verify & Complete runs SAST, build, tests and contracts for real, with a status per gate ("passed", "failed", "not run: why"). Errors are failures. The task is only completed if nothing failed and the build or the tests actually ran. The banner shows the real results |
| F3 | Team changes and file-tree sync report whether the platform accepted them; the team list shows "stored on this computer only" when it didn't come from the platform. Fixed a crash when there are no projects |
| F4 | Methodology fallback is titled "Generic template (AI unavailable)" and says why |
| F5 | Override chips show your saved overrides; the floors list matches the decider; level and band text is corrected; removed the fake "Hard Block" score band |
| F6 | YAML rule input is parsed with YamlDotNet and validated against known types, techniques and gates; errors are shown. The never-executed "Script" mode was removed |
| F7 | Technique catalog and dropdown show whether each technique runs (T2, T8, T10, T13 today) |
| F8 | Simulator, Cherry-pick and Insights text match what they do; the export is labelled export-only |
| F9 | Sandbox folders editor added to Guardrails |

## 6. Fix list (proposed)

| # | Fix | Size |
|---|---|---|
| F1 | Stop auto-activating seeded tasks; only activate a task whose repo matches the open repo; never seed invented teammates — show an empty state when offline | M |
| F2 | AI Tasks Verify: start gates as not run, run tests for real, treat errors as failures, only mark complete when all run gates pass, build the banner from real results | M |
| F3 | Mark team roster and file-tree sync as "not available" until the endpoints exist (or add them in pixel-platform) | S |
| F4 | Label the methodology fallback "AI unavailable — generic template" | S |
| F5 | Guardrails: replace hard-coded override chips with the real saved overrides; rewrite hard floors and level text to match the decider | S |
| F6 | YAML rule input: parse it for real (YamlDotNet) or remove the mode | S |
| F7 | Technique catalog: add an "implemented" column; mark custom Script techniques as not executed yet | S |
| F8 | Fix overclaiming text: Simulator tab summary, Insights text, Export modal title | S |
| F9 | Add a sandbox-folders editor, or hide "Full Auto Sandbox" | S |
