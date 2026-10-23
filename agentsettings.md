# Agent Settings — Proposal (v0.2, 2026-10-23)

> v0.2: added **Part D — gap review & build checklist** (40 items), 5 new tabs, P0 phase, extra data model
> and decisions. Changes from v0.1 are marked **(v0.2)**.

> Status: **proposal only — nothing built yet.** Covers three things you asked for:
> 1. Is Syncro ready for SRE (Site/System Reliability Engineering) work?
> 2. Do project connectors work across many projects?
> 3. A full, dedicated **Agent Settings** page: AI config, conflict types, resolution order,
>    cherry-pick techniques, manual rule entry, and a policy for **when the agent may bypass approval**.

---

## Part A — SRE readiness

### A.1 What an SRE actually does

| SRE job | What it means day to day |
|---|---|
| **SLIs / SLOs / error budgets** | Define "good" (e.g. 99.9% of requests < 300 ms), measure it, spend the budget on risk |
| **Observability** | Metrics, logs, traces from *running* systems (Prometheus, Grafana, Loki, OpenTelemetry, Datadog) |
| **Alerting & on-call** | Alerts tied to SLO burn rate, paging, escalation, rotations |
| **Incident response** | Detect → triage → mitigate (rollback / flag off / scale) → resolve → timeline |
| **Postmortems / RCA** | Blameless write-up, contributing causes, action items tracked to done |
| **Change management** | Safe deploys: canary, blue/green, feature flags, freeze windows, fast rollback |
| **Toil reduction** | Turn runbooks into automation; measure toil hours |
| **Capacity & performance** | Load tests, saturation tracking, cost |
| **Resilience** | Chaos tests, dependency maps, failure-mode analysis |
| **Config / IaC** | Terraform, Helm, k8s manifests, Docker Compose, drift detection |

### A.2 What Syncro has today (mapped to SRE)

| SRE need | Syncro today | Where | Grade |
|---|---|---|---|
| Process health | TCP port probe + real CPU/mem per managed process, every 4 s | `Services/Universe/Runtime/HealthWorker.cs` | ✅ local only |
| Event stream | In-proc event bus, runtime events | `Services/Universe/Runtime/EventBus.cs`, `RuntimeEvent.cs` | ✅ seed |
| Service dependency map | Multi-project scan, cross-workspace "who consumes this endpoint" | `Connector/Scan/MultiProjectScanService.cs`, `Universe/Indexing/CrossWorkspaceMapper.cs` | ✅ strong |
| Contract drift (breaking API change) | Baseline + diff + re-verify on change | `Connector/Contract/*` | ✅ strong (pair only, see Part B) |
| Change safety (git) | Typed engine, guarded auto-sync (branch allowlist, ff-only), revert | `Services/Git/*` | 🟡 no merge/rebase/cherry-pick API |
| CI/CD | Pipeline builder | `Services/Cicd/PipelineBuilder.cs` | 🟡 generates, doesn't watch |
| Security | Native SAST | `Services/Security` | ✅ |
| Build verification | Build/verify engine | `Services/Verification`, `BuildMcp` | ✅ |
| Runbooks | PowerShell script runner + 15 git scripts | `Services/AgentCli/Scripts`, `scripts/git` | 🟡 seed for runbook automation |
| Deploy status | Netlify status only | `Services/NetlifyService.cs` | 🟡 one provider |
| Containers | Compose generator | `Services/Database/ComposeGenerator.cs` | 🟡 generate only |
| Remote metrics/logs/traces | — | — | ❌ none |
| SLO definitions / error budgets | — | — | ❌ none |
| Alerts / on-call / paging | — | — | ❌ none |
| Incident object, timeline, postmortem | — | — | ❌ none |
| k8s / cloud (AWS/GCP/Azure) | — | — | ❌ none |
| Agent approval enforcement | `RequiresAdminApproval` flag exists but the **in-app agent loop ignores it** (see A.4) | `Engine/Mcp/McpOrchestrator.cs:51` | ❌ blocker |

### A.3 Verdict

**Not ready for SRE yet.** Syncro is a strong *developer-local* reliability tool (it knows how your
projects connect, whether contracts broke, whether processes are up, whether code is safe). It has
no view of **production** — no remote telemetry, no SLOs, no incidents. Rough readiness: **~25%**.

The good news: the hard, unique part (a live multi-project dependency graph + contract drift) is
already built. Most SRE tools *don't* have that. What's missing is mostly integration plumbing.

### A.4 Blocker found while analysing: approval is not enforced in-app

- `IMcpTool.RequiresAdminApproval` is set to `true` on Shell, WriteFileBatch, WriteBackendFiles,
  Pipeline, Clone, FetchEndpoint and the write git tools.
- It is **only enforced** in `Engine/Mcp/Transport/McpHttpEndpoint.cs:220` (the external editor
  bridge) and shown as a lock icon in `Components/IDE/AgentMissionControl.razor:309`.
- `McpOrchestrator.ExecuteToolAsync` (used by `RunAgentAsync`, the in-app agent loop) calls
  `tool.ExecuteAsync` directly. **Inside Syncro the agent can already run shell and write tools
  with no approval.**

Any SRE automation (rollback, restart, deploy) makes this dangerous. The Bypass Decision Engine in
Part C §C8 replaces this boolean with a real policy check — it should be Phase 1 of the build.

### A.5 SRE roadmap (after Agent Settings lands)

| Phase | Adds | Builds on |
|---|---|---|
| SRE-1 Telemetry in | Prometheus / OTel / Loki / generic HTTP JSON pull; `TelemetryWorker : IRuntimeWorker` | EventBus, RuntimeDashboard |
| SRE-2 SLOs | `slo.yaml` per service (SLI query, target, window); burn-rate calc; error budget tile | Universe graph nodes = services |
| SRE-3 Incidents | Incident entity, auto-timeline from EventBus + git + deploys, severity, owner | EventBus, Git log |
| SRE-4 Blast radius | "This failing endpoint is consumed by Flutter, Next, Desktop" — from the graph | CrossWorkspaceMapper |
| SRE-5 Mitigation runbooks | Rollback / restart / flag-off as runbook steps, **gated by the bypass policy** | Script runner, GitEngine.Revert, C8 |
| SRE-6 Postmortem | AI drafts postmortem from incident timeline; action items → tasks | Groq agent, AgentTaskDashboard |
| SRE-7 Change risk | Pre-deploy risk score: contract drift + SAST + diff size + touched-service fan-out | ContractEngine, SAST, graph |

---

## Part B — Do project connectors work across many projects?

**Partly. Scanning is multi-project; drift tracking is one pair.**

| Capability | Multi-project? | Evidence |
|---|---|---|
| Discover every project under a root | ✅ yes | `WorkspaceDiscoveryService.Discover` |
| Resolve each as producer **and** consumer (dual-role) | ✅ yes, concurrently | `MultiProjectScanService.ResolveNodeAsync` |
| Match every consumer against **pooled** routes of all producers | ✅ yes | `MultiProjectScanService.ScanAsync` |
| N-consumer graph ("endpoint X used by A, B, C") | ✅ yes | `CrossWorkspaceMapper` |
| Contract drift baseline + re-verify | ❌ **one backend + one frontend** | `connector.json` has single `Frontend`/`Backend`; `ConnectorProjectLocator.LocateAsync` returns one pair |
| Same path in two backends (e.g. `/api/health` in 3 services) | ❌ ambiguous | owner picked with `FirstOrDefault` — first backend wins |
| Base URL / host / port disambiguation | ❌ not used | matcher keys on method + path (exact, then suffix) |
| Cross-repo git operations (multi-repo branch/merge) | ❌ none | GitEngine is per-repo |

### Fixes needed for true multi-project connectors

1. **`connector.json` v2** — `projects: [{ id, path, role: producer|consumer|both, baseUrls[], env }]`
   plus `links: [{ consumer, producer, baseUrlSymbol }]`. Keep v1 readable (auto-migrate).
2. **Route ownership by base URL** — resolve the consumer's base-URL symbol (`API_URL`, `dio.options.baseUrl`)
   to a producer project before path matching; fall back to path-only only when unresolved, and flag
   as `Ambiguous` instead of picking the first.
3. **ContractEngine per link** — one baseline per (consumer, producer) link, so drift in service B
   doesn't hide behind service A.
4. **Workspace group** — a named set of repos ("Syncro ecosystem" = Desktop + pixel-platform + Flutter app)
   that Agent Settings scopes can target (see §C10).

---

## Part C — Agent Settings page (full design)

### C.1 Goals

- One place to configure **everything the agent is allowed to decide**, not just the model.
- Conflict resolution is **rule-driven and ordered**: the user decides *which technique runs first*
  for *which kind of conflict*, and what happens if it fails.
- Users can **add their own** conflict types, techniques and rules — by form, YAML, plain English, or import.
- The agent decides **when to bypass approval** using a transparent, explainable score, with
  **hard floors that can never be bypassed**.
- Works across **many projects**, with inheritance: Global → Workspace group → Project → Branch.
- Everything is **dry-runnable** and **audited**.

### C.2 Principles

1. **Deterministic first, AI last.** Prefer git-native and structural techniques; the LLM is a fallback
   resolver, never the first one for code.
2. **Every automatic action must be reversible** or it is not eligible for bypass.
3. **Explain every decision.** Each bypass/ask/block shows the factors and score that produced it.
4. **Repo policy is code.** Team rules live in `.syncro/agent-policy.yaml` and get reviewed like code.
5. **Safe defaults.** Out of the box the agent asks for anything that writes outward.

### C.3 Page layout

Route: `/settings/agent` · NavMenu entry "Agent Settings" (icon `Psychology`) · MudBlazor `MudTabs` vertical.
Top bar: **Scope picker** (Global ▸ Group ▸ Project ▸ Branch) · "Overridden here" badges · Save / Discard ·
**Export YAML** · **Simulate**.

Vertical nav is grouped into 5 sections **(v0.2)** so 16 tabs stay findable. A settings **search box**
sits above the nav.

| Section | # | Tab | Purpose |
|---|---|---|---|
| **AI** | 1 | **Model & Provider** | Provider, model, temperature, token budget, fallback model, key status, custom instructions, cost budget |
| | 12 | **Privacy & Data** (v0.2) | What may be sent to the LLM: path deny-list, size cap, local-only projects, redaction |
| **Safety** | 2 | **Autonomy & Bypass** | Global autonomy level, risk thresholds, hard floors, per-tool overrides, approval behaviour |
| | 13 | **Permissions** (v0.2) | Shell allowlist/denylist, file-path allow/deny, network domains, repo-policy trust |
| | 14 | **Environments & Freezes** (v0.2) | Map branch/remote/deploy target → local/staging/prod; freeze calendar; break-glass |
| **Git & Conflicts** | 15 | **Git Operations** (v0.2) | Pull/rebase/stash/sync strategy (absorbs today's `GitSyncPolicy`), commit trailers, signing |
| | 3 | **Conflict Types** | Catalog of conflict kinds (built-in + custom), how each is detected |
| | 4 | **Techniques** | Catalog of resolution techniques (built-in + custom), with risk and params |
| | 5 | **Resolution Rules** | Ordered rules: *match → technique chain → verify → on-fail* |
| | 6 | **Cherry-pick & Backport** | Cherry-pick defaults, backport flows, dedupe rules |
| | 7 | **Verification Gates** | Build / test / contract / SAST / lint checks after a resolution |
| **Projects** | 8 | **Projects & Scope** | Workspace groups, per-project overrides, inheritance view, "effective policy for this file" |
| | 9 | **Rule Input & Import** | NL→rule, YAML editor, `.gitattributes` import, learn from history, templates |
| **Review** | 10 | **Simulator** | Replay a past merge/cherry-pick against current rules — no writes; shadow mode |
| | 11 | **Audit Log** | Every agent decision: what, why, score, outcome, undo link |
| | 16 | **Insights** (v0.2) | Auto-resolve rate, undo rate per rule, AI confidence calibration, approvals asked vs auto |

---

### C.4 Tab 1 — Model & Provider

| Setting | Type | Default | Notes |
|---|---|---|---|
| Provider | select | Groq | from `ILLMProvider` implementations |
| Agent model | select | `openai/gpt-oss-120b` | your tested choice for the loop |
| Resolver model | select | same | can be a cheaper model for conflict hunks |
| Fallback model | select | none | used on 429/5xx |
| Temperature (agent / resolver) | slider | 0.2 / 0.0 | resolver should be deterministic |
| Max tool calls per task | int | 25 | loop guard |
| Max tokens per conflict hunk | int | 4 000 | context pack size |
| Context sources for resolution | multi | both sides, base, AST neighbours, commit messages, linked contract | from AST context engine |
| Redact secrets before sending | toggle | on | SAST secret patterns reused |
| Offline mode | toggle | off | disables LLM techniques; rules skip to next |

---

### C.5 Tab 3 — Conflict Types (taxonomy)

Detection uses two layers: **git structural code** (from `git status --porcelain=v2`, already parsed in
`GitEngine.cs:130`) and **semantic class** (path/extension/content + AST).

#### C.5.1 Structural types (from git, fixed list)

| Code | Name | Meaning |
|---|---|---|
| `UU` | Both modified | Same region edited on both sides — the common case |
| `AA` | Both added | Same path created on both sides |
| `DU` | Deleted by us | We deleted, they modified |
| `UD` | Deleted by them | They deleted, we modified |
| `AU` / `UA` | Added by us / them | One side added, other has conflicting state |
| `DD` | Both deleted | Usually rename collision |
| `RR` | Rename/rename | Same file renamed differently on each side |
| `R/D` | Rename/delete | One side renamed, other deleted |
| `DIR` | File/directory | Path is a file on one side, a folder on the other |
| `MODE` | Mode change | Exec bit / symlink differs |
| `SUB` | Submodule pointer | Two different submodule commits |
| `BIN` | Binary | Non-text file, no markers possible |

#### C.5.2 Semantic types (built-in, extensible)

| Id | Detect by | Typical safe technique |
|---|---|---|
| `lockfile` | `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`, `pubspec.lock`, `packages.lock.json`, `Cargo.lock`, `poetry.lock` | Regenerate from merged manifest |
| `manifest-deps` | `package.json` deps, `pubspec.yaml`, `*.csproj` PackageReference | Key-level merge, take higher semver, then regenerate lock |
| `generated` | `*.g.dart`, `*.freezed.dart`, `*.Designer.cs`, `obj/`, `dist/`, header "auto-generated" | Regenerate (build_runner / codegen) |
| `changelog` | `CHANGELOG.md`, `RELEASE_NOTES*` | Union (both entries), sort by date |
| `imports` | Hunk only contains import/using lines | Union + sort + dedupe |
| `version-bump` | `version:` / `<Version>` / `"version"` only | Take max semver |
| `db-migration` | `migrations/`, `*_migration.*`, EF `Migrations/` | Renumber ours after theirs; never auto-merge content |
| `config-json-yaml` | `*.json`, `*.yaml`, `appsettings*.json` | Structural key merge; conflict only on same key |
| `api-contract` | OpenAPI/Swagger, route files linked in connector graph | Merge, then **ContractEngine verify** |
| `whitespace-only` | Diff disappears with `-X ignore-space-change` | Auto-take either |
| `formatting-only` | Diff disappears after formatter | Format both, re-merge |
| `code-logic` | Everything else in source code | Structural (AST) merge → AI → human |
| `secrets/env` | `.env*`, keys detected by SAST | **Never auto** — always human |
| `infra` | `*.tf`, Helm, k8s yaml, Dockerfile, CI yaml | Structural merge + human review |
| `binary-asset` | images, fonts, `.pptx` | Pick side by policy (newest, ours, theirs), never merge |

#### C.5.3 Custom conflict type (user-defined)

Fields: `id`, `label`, `match` (glob list, regex on content, AST node kind, connector role, file size),
`priority` (when several types match, highest wins), `severity` (low/med/high/critical — feeds bypass score),
`description`.

---

### C.6 Tab 4 — Techniques catalog (researched)

Each technique has: **risk** (feeds bypass score), **reversible?**, **needs tool**, **params**.

#### C.6.1 Git-native merge techniques

| # | Technique | What it does | Command / config | Risk | Best for |
|---|---|---|---|---|---|
| T1 | `ort` default 3-way | Standard recursive 3-way merge (default since git 2.34) | `git merge -s ort` | low | everything first |
| T2 | Better diff algorithm | Fewer false conflicts in moved/reordered code | `-X diff-algorithm=histogram` or `-X patience` | low | big refactors |
| T3 | Ignore whitespace | Treat whitespace-only changes as non-conflicting | `-X ignore-space-change` / `ignore-all-space` | low | formatting noise |
| T4 | Renormalize | Normalize line endings (CRLF/LF) before merge | `-X renormalize` | low | Windows ↔ Linux teams |
| T5 | Rename detection tuning | Catch renames with lower similarity | `-X find-renames=40%` | low | moved files |
| T6 | `zdiff3` markers | Shows base in markers — better context for AI/human | `merge.conflictStyle=zdiff3` | none | always on |
| T7 | Prefer side per hunk | Auto-pick ours/theirs **only in conflicting hunks**, keep clean merges | `-X ours` / `-X theirs` | medium | known-owner files |
| T8 | Take whole file | `git checkout --ours/--theirs -- <file>` | per-file | medium–high | generated, binary |
| T9 | Strategy `ours` | Record merge but **discard all their changes** | `git merge -s ours` | high | superseding a branch |
| T10 | Union driver | Keep both sides' lines | `.gitattributes: CHANGELOG.md merge=union` | low–med | changelogs, lists |
| T11 | Custom merge driver | Run a command `%O %A %B %L %P` | `merge.<name>.driver` | varies | lockfile regen, JSON merge |
| T12 | rerere | Re-apply a resolution you already made before | `rerere.enabled=true`, `rerere.autoUpdate` | low | repeated rebases |
| T13 | Single-file re-merge | Redo 3-way on one file with new options | `git merge-file`, `git checkout --conflict=zdiff3 <f>` | low | retry with T2/T3 |
| T14 | Octopus | Merge many branches at once (no conflicts allowed) | `git merge -s octopus` | low | integration branches |
| T15 | Subtree | Merge a repo into a subfolder | `-s subtree` / `-X subtree=<path>` | medium | vendored code |

#### C.6.2 Structural / semantic techniques (Syncro-specific advantage)

| # | Technique | What it does | Uses | Risk |
|---|---|---|---|---|
| S1 | **Regenerate lockfile** | Resolve manifest, then `npm install --package-lock-only` / `flutter pub get` / `dotnet restore --use-lock-file` | ProjectGenerator stack registry | low |
| S2 | **Regenerate codegen** | `dart run build_runner build`, re-run designer/codegen | Flutter service | low |
| S3 | **Key-level JSON/YAML merge** | Parse 3 versions, merge by key path; conflict only if same key changed differently | ConfigFileParser | low |
| S4 | **Import union** | Union, dedupe, sort imports/usings | AST engine | low |
| S5 | **Semver max** | Pick the higher version in bumps/deps | — | low |
| S6 | **AST structural merge** | Merge at declaration level (two methods added to same class ≠ conflict); like Mergiraf / SemanticMerge | `Services/AST` | low–med |
| S7 | **Format-then-merge** | Run formatter (dotnet format, prettier, dart format) on base/ours/theirs, re-merge | — | low |
| S8 | **Migration renumber** | Move our migration after theirs, regenerate snapshot | DB scaffolder | medium |
| S9 | **Contract-aware merge** | After merge, run ContractEngine; reject if a consumer breaks | `Connector/Contract` | low (it's a check) |

#### C.6.3 AI techniques

| # | Technique | What it does | Risk |
|---|---|---|---|
| A1 | **AI hunk resolve** | LLM gets base/ours/theirs (zdiff3) + AST neighbours + both commit messages, returns resolved hunk + confidence + rationale | medium |
| A2 | **AI intent merge** | LLM reads both *commit intents* and rewrites the region to satisfy both | high |
| A3 | **AI explain only** | No write — produces a summary for the human | none |
| A4 | **AI pick side** | LLM chooses ours/theirs with reason (for config/binary metadata) | medium |

AI output contract: `{ resolvedText, confidence 0–1, rationale, touchedSymbols[], needsHuman: bool }`.
Confidence < rule threshold ⇒ treated as failure ⇒ next technique in chain.

#### C.6.4 Terminal techniques

| # | Technique | Meaning |
|---|---|---|
| H1 | **Ask human** | Open 3-way editor in IDE with AI suggestion pre-filled |
| H2 | **Abort operation** | `merge --abort` / `rebase --abort` / `cherry-pick --abort` |
| H3 | **Skip commit** | `rebase --skip` / `cherry-pick --skip` (records in audit) |
| H4 | **Park on branch** | Commit conflict state to `syncro/conflict/<id>` for later |

#### C.6.5 Custom technique (user-defined)

Kinds: **command** (shell with `%BASE% %OURS% %THEIRS% %OUT% %PATH%` placeholders), **script** (from
your script store), **prompt** (custom LLM prompt template), **chain** (named reusable sub-chain).
Required fields: `id`, `kind`, `risk`, `reversible`, `timeoutSec`, `successWhen` (exit code 0 / no markers left / verify passes).

---

### C.7 Tab 5 — Resolution Rules & order

A rule = **when** (match) + **how** (ordered chain) + **then** (verify) + **else** (on-fail) + **approval**.

```
Rule "lockfiles"
  when   conflictType = lockfile
  chain  [S1 regenerate-lockfile]
  verify [build]
  else   H1 ask-human
  approval auto-if-score<=30
```

#### C.7.1 How the engine picks and runs rules

1. **Classify** every conflicted file → (structural code, semantic type). Custom types win by `priority`.
2. **Select rule**: rules are an ordered list per scope. Evaluation = **first match wins**, with scope
   precedence Branch > Project > Group > Global. A rule can be `final: true` to stop lower scopes overriding.
3. **Run chain** in order. Each step returns `Resolved | Failed | Partial`. Stop at first `Resolved`.
   `Partial` (some hunks done) passes remaining hunks to the next step.
4. **Verify** with the rule's gates (§C.9). Fail ⇒ revert that file's resolution, continue chain from next step.
5. **Chain exhausted** ⇒ `else` action (default H1 ask-human).
6. **Bypass decision** (§C.8) decides whether the final result is applied silently, applied + notified, or held for approval.

#### C.7.2 Global ordering controls

| Setting | Options | Default |
|---|---|---|
| File order | dependency order (AST graph) · path · smallest-first · risk-lowest-first | dependency order |
| Rule evaluation | first-match · best-score (all matching rules scored, best wins) | first-match |
| Per-chain max attempts | int | 1 per step |
| Global chain prefix | steps always run first (e.g. T6 zdiff3 + T12 rerere) | `[T6, T12]` |
| Global chain suffix | steps always run last | `[H1]` |
| Stop whole operation if any file is `critical` and unresolved | toggle | on |

#### C.7.3 Recommended default rule set (ships out of the box)

| Order | Match | Chain | Verify | Else |
|---|---|---|---|---|
| 1 | `secrets/env` | — | — | H1 (forced human) |
| 2 | `whitespace-only` | T3 | — | next rule |
| 3 | `lockfile` | S1 | build | H1 |
| 4 | `generated` | S2 → T8 theirs | build | H1 |
| 5 | `changelog` | T10 union | — | H1 |
| 6 | `imports` | S4 | build | A1 |
| 7 | `version-bump` | S5 | — | H1 |
| 8 | `manifest-deps` | S3 → S5 → S1 | build | H1 |
| 9 | `config-json-yaml` | S3 | — | A1 → H1 |
| 10 | `db-migration` | S8 | build | H1 |
| 11 | `api-contract` | S6 → A1 | contract + build | H1 |
| 12 | `infra` | S3 | — | H1 (always) |
| 13 | `binary-asset` | policy pick | — | H1 |
| 14 | `code-logic` | T13+T2 → S7 → S6 → A1 (conf ≥ 0.85) | build + tests | H1 |
| 15 | `*` (catch-all) | T13+T2 → A1 (conf ≥ 0.9) | build | H1 |

---

### C.8 Tab 2 — Autonomy & Bypass Decision Engine

"Bypass" = the agent applies an action **without waiting for your approval**. It also covers
**skipping a verification gate** (e.g. skip tests). Both go through the same decision.

#### C.8.1 Autonomy levels (global switch)

| Level | Behaviour |
|---|---|
| 0 Observe | Read-only. Suggests, never writes. |
| 1 Suggest | Prepares changes in a staging area; every write needs approval. **(default)** |
| 2 Guarded auto | Auto-applies when risk score ≤ threshold and action is reversible; asks otherwise. |
| 3 Auto + notify | Auto-applies up to a higher threshold; notifies after; hard floors still ask/block. |
| 4 Full auto (sandbox only) | Only inside a throwaway worktree/branch; never on protected branches or remotes. |

#### C.8.2 Risk score (0–100) — explainable

`score = Σ factor weights`, each factor shown in the decision card. Weights are editable in the tab.

| Factor | Values → points (default) |
|---|---|
| Action class | read 0 · local write 10 · local history rewrite 25 · outward (push/PR/deploy) 35 · destructive 60 |
| Reversibility | trivially undoable 0 · undo needs reflog 10 · not undoable +40 |
| Technique risk | none 0 · low 5 · medium 15 · high 30 |
| Conflict severity | low 0 · med 5 · high 15 · critical 30 |
| Confidence (AI) | ≥0.95: 0 · 0.85–0.95: 10 · 0.7–0.85: 20 · <0.7: block |
| Blast radius — files | 1–3: 0 · 4–20: 5 · >20: 15 |
| Blast radius — projects | 1: 0 · 2–3: 10 · >3: 20 (from connector graph consumers) |
| Branch | feature/personal 0 · shared 10 · protected (main/release/*) 30 |
| Verification result | all passed −10 · skipped +15 · failed → block |
| Novelty | rerere hit / identical past resolution −10 · never seen before +5 |
| Environment | local 0 · staging 10 · production 40 |
| Time | inside freeze window → ask always · off-hours +5 |
| Contract drift | none 0 · non-breaking 5 · breaking → block |
| Secrets touched | → block |

#### C.8.3 Decision bands (editable per scope)

| Score | Decision | UI |
|---|---|---|
| 0–20 | **Auto** (bypass) | logged silently |
| 21–40 | **Auto + notify** | toast + audit entry with Undo |
| 41–70 | **Ask** | approval card with diff + factor breakdown |
| 71–100 | **Ask with second confirm** | type branch name to confirm |
| block rule hit | **Block** | explained; user must do it themselves |

Bands are clamped by autonomy level: at Level 1 everything above 0 is "Ask".

#### C.8.4 Hard floors — never bypassable (cannot be lowered in UI)

- Force push, `reset --hard` on a branch with an upstream, deleting a remote branch
- Any write to a protected branch (`main`, `master`, `release/*`, plus user list)
- Any file classified `secrets/env`, or SAST finds a secret in the result
- Production environment actions
- Breaking contract drift on a link with ≥1 consumer
- AI confidence below the block threshold
- Shell commands not on the allowlist
- Any action during a declared freeze window

Users can **add** floors; they cannot remove these.

#### C.8.5 Per-tool overrides

Table of every registered `IMcpTool` (pulled from `McpOrchestrator.Tools`) with: default class, override
class, "always ask" / "never ask (only if reversible)" / "block". Replaces the static `RequiresAdminApproval` bool.

#### C.8.6 Decision examples

| Situation | Score breakdown | Result |
|---|---|---|
| Lockfile regen on `feature/x`, build passes | local write 10 + low 5 − verified 10 = 5 | Auto |
| Import union on `develop`, 2 projects | 10 + 5 + shared 10 + projects 10 − 10 = 25 | Auto + notify |
| AI hunk resolve, conf 0.88, `feature/y`, tests pass | 10 + med 15 + conf 10 − 10 + novel 5 = 30 | Auto + notify (Level 3) / Ask (Level 2 with threshold 20) |
| AI resolve on `main` | protected branch → floor | Block/Ask by floor |
| Cherry-pick hotfix to `release/2.1` and push | outward 35 + protected 30 + … | Ask with second confirm |
| `.env` conflict | secrets floor | Block (human only) |

#### C.8.7 Learning (optional, off by default)

If you approve the same kind of decision N times (default 5) with no undo, the agent **proposes**
lowering that factor or adding a rule. It never changes policy by itself.

---

### C.9 Tab 6 — Cherry-pick & Backport

#### C.9.1 Techniques (researched)

| # | Technique | Command | When |
|---|---|---|---|
| C1 | Traceable pick | `git cherry-pick -x <sha>` (adds "cherry picked from") | **always on** for shared branches |
| C2 | Range pick | `git cherry-pick A^..B` | a series of commits |
| C3 | Merge-commit pick | `git cherry-pick -m 1 <merge>` | picking a merged PR as one unit |
| C4 | No-commit pick | `git cherry-pick -n` then commit once | squash several picks |
| C5 | Partial pick | `git restore --source=<sha> --patch -- <paths>` | only some files/hunks |
| C6 | Strategy options | `git cherry-pick -X theirs / -X diff-algorithm=histogram` | reuse §C.6 rules |
| C7 | Empty handling | `--empty=drop|keep|stop` (git ≥ 2.45) | already-applied changes |
| C8 | Dedupe check | `git cherry -v <target> <source>` / `git log --cherry-pick --right-only A...B` (patch-id) | skip commits already on target |
| C9 | Compare series | `git range-diff` | verify backport matches original |
| C10 | Rebase-onto backport | `git rebase --onto release/2.1 main feature` | move a whole branch |
| C11 | Revert-of-revert aware | detect "Revert …" pairs, pick both or neither | avoid resurrecting reverted code |
| C12 | Backport via PR | pick onto `backport/<n>-<target>` branch, open PR | protected targets |

#### C.9.2 Settings

| Setting | Default |
|---|---|
| Always add `-x` trailer | on |
| Dedupe by patch-id before picking | on |
| Empty commit handling | drop |
| Merge-commit mainline | 1 |
| On conflict | run Resolution Rules (same engine as merge) |
| Multi-commit failure | stop at first unresolved · or skip & continue (logged) |
| Backport target branches | list, e.g. `release/*` |
| Backport mode | direct pick (non-protected) · backport branch + PR (protected) |
| Commit order | original author date · topological (default) |
| Cross-repo pick (same commit to many repos in a group) | off; when on, one decision per repo |

---

### C.10 Tab 7 — Verification gates

| Gate | Source in Syncro | Configurable |
|---|---|---|
| No conflict markers left | grep `<<<<<<<`/`>>>>>>>` | always on |
| Build | `Services/Verification` / BuildMcp | per project command |
| Tests | project test command | filter, timeout |
| Lint / format | stack formatter | on/off |
| Contract | ContractEngine.Verify | breaking = block |
| SAST | Services/Security | new high finding = block |
| AST sanity | parse succeeds for every touched file | always on |
| Custom | command or script | exit code |

Gate options: `required` / `advisory`, timeout, "skippable" (skipping adds +15 to score and needs C.8 approval).

---

### C.11 Tab 8 — Projects & scope (multi-project)

```
Global
 └─ Group "syncro-ecosystem"   (Desktop + pixel-platform + flutter-app)
     ├─ Project Syncro.Desktop
     │   └─ Branch main         (final: protected)
     └─ Project pixel-platform
```

- **Inheritance view**: each setting shows its effective value and *where it came from*.
- **Repo file** `.syncro/agent-policy.yaml` is the Project scope when present (team-shared, reviewed in PRs).
  Local UI edits to that scope write the file (with a diff preview).
- **Group rules** can reference the connector graph: e.g. "if a merge in a producer changes a route,
  run contract gate for **all** consumers in the group".
- **Multi-repo operation**: a group merge/cherry-pick runs per repo, in dependency order (producers first),
  stops the group if any repo blocks.

---

### C.12 Tab 9 — How users input rules (6 ways)

| # | Method | How it works | Validation |
|---|---|---|---|
| I1 | **Form builder** | Pick type → drag techniques into chain → pick gates → set approval. MudBlazor `MudDropContainer`. | live schema check |
| I2 | **YAML / JSON editor** | Monaco editor with JSON Schema autocomplete (`agent-policy.schema.json`) | schema + dry-run |
| I3 | **Plain English → rule** | "For Flutter generated files always take theirs and rebuild" → AI drafts rule → shown as form + YAML → you confirm | must pass schema; never saved without confirm |
| I4 | **Import** | From `.gitattributes` (merge drivers), git config (`rerere`, `conflictStyle`), another repo's policy, or a template | shows diff before applying |
| I5 | **Learn from history** | Scans `git log` merges + rerere cache: "you resolved `pubspec.lock` by regenerating 12 times" → suggests rule | suggestion only |
| I6 | **Templates / marketplace** | Packs: "Flutter app", "Next.js", ".NET", "Monorepo", "Strict (regulated)" — fits your scriptstore/marketplace | preview + simulate |

Every input path ends in the **same** normalized model (§C.14) and the same validation + simulator.

---

### C.13 Tabs 10–11 — Simulator & Audit

**Simulator**: pick a past merge commit (or two branches) → engine re-creates the merge in a temporary
worktree → shows per file: detected type, chosen rule, each chain step result, gate results, bypass
score and decision. **No writes to your branch.** Also "compare policies" (current vs draft).

**Audit log** (`%LOCALAPPDATA%\SyncroDesktop\agent\audit.jsonl`, one line per decision):
`time, scope, repo, branch, operation, file, conflictType, rule, steps[], gates[], score, factors[], decision, approver, undoRef`.
Undo button uses the recorded `undoRef` (pre-op commit / reflog entry / backup ref `refs/syncro/backup/<id>`).

---

### C.14 Data model (C# sketch)

```csharp
namespace Syncro.Desktop.Services.Agent.Policy;

public enum AutonomyLevel { Observe, Suggest, GuardedAuto, AutoNotify, FullAutoSandbox }
public enum ActionClass   { Read, LocalWrite, LocalHistoryRewrite, Outward, Destructive }
public enum Decision      { Auto, AutoNotify, Ask, AskDoubleConfirm, Block }
public enum StepOutcome   { Resolved, Partial, Failed }

public sealed class AgentPolicy            // one per scope
{
    public int Version { get; set; } = 1;
    public string Scope { get; set; } = "global";   // global | group:<id> | project:<path> | branch:<name>
    public ModelSettings? Model { get; set; }
    public BypassPolicy? Bypass { get; set; }
    public List<ConflictTypeDef> ConflictTypes { get; set; } = new();
    public List<TechniqueDef> Techniques { get; set; } = new();
    public List<ResolutionRule> Rules { get; set; } = new();
    public CherryPickPolicy? CherryPick { get; set; }
    public List<GateDef> Gates { get; set; } = new();
}

public sealed class ConflictTypeDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> Globs { get; set; } = new();
    public string? ContentRegex { get; set; }
    public string? AstNodeKind { get; set; }
    public int Priority { get; set; }
    public string Severity { get; set; } = "medium";
}

public sealed class TechniqueDef
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "builtin";   // builtin | command | script | prompt | chain
    public string Risk { get; set; } = "low";
    public bool Reversible { get; set; } = true;
    public Dictionary<string, string> Params { get; set; } = new();
    public int TimeoutSec { get; set; } = 120;
}

public sealed class ResolutionRule
{
    public string Id { get; set; } = "";
    public RuleMatch When { get; set; } = new();
    public List<string> Chain { get; set; } = new();          // technique ids, in order
    public List<string> Verify { get; set; } = new();         // gate ids
    public List<string> Else { get; set; } = new() { "ask-human" };
    public double? MinConfidence { get; set; }
    public int? MaxAutoScore { get; set; }                     // overrides band for this rule
    public bool Final { get; set; }
}

public sealed class RuleMatch
{
    public List<string> ConflictTypes { get; set; } = new();  // semantic ids
    public List<string> GitCodes { get; set; } = new();       // UU, AA, DU …
    public List<string> Branches { get; set; } = new();       // globs
    public List<string> Operations { get; set; } = new();     // merge | rebase | cherry-pick | pull
}

public sealed class BypassPolicy
{
    public AutonomyLevel Level { get; set; } = AutonomyLevel.Suggest;
    public Dictionary<string, int> Weights { get; set; } = new();
    public int AutoMax { get; set; } = 20, NotifyMax { get; set; } = 40, AskMax { get; set; } = 70;
    public List<string> ExtraFloors { get; set; } = new();
    public List<string> ProtectedBranches { get; set; } = new() { "main", "master", "release/*" };
    public List<FreezeWindow> Freezes { get; set; } = new();
    public Dictionary<string, string> ToolOverrides { get; set; } = new(); // toolName -> ask|auto|block
}

public sealed record DecisionResult(Decision Decision, int Score, IReadOnlyList<(string Factor, int Points, string Why)> Factors, string? FloorHit);
```

### C.15 Example `.syncro/agent-policy.yaml`

```yaml
version: 1
bypass:
  level: guarded-auto
  autoMax: 20
  notifyMax: 40
  protectedBranches: [main, release/*]
  freezes:
    - { cron: "0 18 * * FRI", durationHours: 64, reason: "weekend freeze" }
  toolOverrides:
    git_push: ask
    shell_exec: block
conflictTypes:
  - id: flutter-generated
    globs: ["**/*.g.dart", "**/*.freezed.dart"]
    priority: 50
    severity: low
techniques:
  - id: flutter-codegen
    kind: command
    params: { cmd: "dart run build_runner build --delete-conflicting-outputs" }
    risk: low
rules:
  - id: flutter-gen
    when: { conflictTypes: [flutter-generated] }
    chain: [flutter-codegen, take-theirs]
    verify: [build]
    else: [ask-human]
  - id: logic
    when: { conflictTypes: [code-logic], branches: ["feature/*"] }
    chain: [merge-file-histogram, format-then-merge, ast-merge, ai-hunk]
    minConfidence: 0.85
    verify: [build, tests]
    else: [ask-human]
cherryPick:
  traceable: true
  dedupeByPatchId: true
  empty: drop
  backport: { targets: ["release/*"], mode: pr }
```

---

### C.16 Services & wiring into existing code

| New piece | Location | Connects to |
|---|---|---|
| `AgentPolicyStore` (load/merge scopes, write YAML) | `Services/Agent/Policy/` | `%LOCALAPPDATA%\SyncroDesktop\agent\`, `.syncro/agent-policy.yaml` |
| `ConflictClassifier` | `Services/Agent/Conflicts/` | `GitEngine` porcelain parse, `Services/AST`, `ConfigFileParser` |
| `TechniqueRegistry` + built-ins T/S/A/H | `Services/Agent/Conflicts/Techniques/` | GitEngine.RawAsync, ProjectGenerator stacks, GroqProvider |
| `ConflictResolutionEngine` | `Services/Agent/Conflicts/` | rules, gates, decider; runs in temp worktree for simulate |
| `BypassDecider` | `Services/Agent/Policy/` | **called from `McpOrchestrator.ExecuteToolAsync` before every tool** (fixes A.4) |
| `GateRunner` | `Services/Agent/Gates/` | VerificationEngine, ContractEngine, Security scanner |
| `AuditLog` | `Services/Agent/Audit/` | jsonl + undo refs |
| GitEngine additions | `IGitEngine` | `MergeAsync`, `RebaseAsync`, `CherryPickAsync`, `AbortAsync`, `ContinueAsync`, `ConflictsAsync`, `BackupRefAsync` |
| MCP tools | `Engine/Mcp/Servers/ConflictMcp.cs` | `list_conflicts`, `classify_conflicts`, `resolve_conflicts(dryRun)`, `explain_decision`, `cherry_pick(plan)` |
| Approval UI | `Components/Shared/ApprovalCard.razor` | shows diff + factor table; Approve / Reject / Always-for-this-rule |
| Settings page | `Components/Pages/Settings/AgentSettings.razor` + one component per tab | NavMenu, Routes |
| GitManager | replace "dump error to snackbar" | opens conflict panel driven by the engine |

All registered as singletons in `MauiProgram.cs` via an `AddAgentPolicy()` extension (same pattern as
`ConnectorServiceCollectionExtensions`).

---

### C.17 Build phases

| Phase | Scope | Done when |
|---|---|---|
| **P0 Foundations** (v0.2) | One `AgentPolicyStore` shared by the IDE Settings panel and `/settings/agent` (D36); Groq key in SecureStorage (D8); move `GitSyncPolicy` into the store (D13) | one source of truth for settings; key not only in env var |
| **P1 Safety** | `BypassDecider` with floors + levels; enforce in `McpOrchestrator`; ApprovalCard; approval queue + timeout = deny (D6, D11); shell/path/network permissions (D3–D5); repo-policy trust (D1); audit log | in-app agent can no longer run a `RequiresAdminApproval` tool without a decision |
| **P2 Git ops** | merge/rebase/cherry-pick/stash/revert/abort/continue/backup-ref in GitEngine; conflict listing; operation journal + crash resume + repo lock (D16); 3-way merge editor (D37) | GitManager shows structured conflicts and a human can resolve in-app |
| **P3 Classifier + built-in techniques** | structural + semantic types, T1–T15, S1/S3/S4/S5/S7, H1–H4 | default rules resolve lockfile/import/changelog cases |
| **P4 Rules engine + gates** | ordered rules, scope precedence, gates | full chain runs with verify + else |
| **P5 Settings page** | tabs 1–8 with form builder and YAML editor | all settings editable and persisted |
| **P6 AI + AST merge** | A1–A4, S6 AST merge, S9 contract gate | code-logic conflicts get AI suggestions with confidence |
| **P7 Input & learning** | NL→rule, imports, learn-from-history, templates | 6 input paths produce valid rules |
| **P8 Simulator** | temp-worktree replay, policy compare | any past merge replays with no writes |
| **P9 Multi-project** | groups, connector.json v2, group merges, cross-repo cherry-pick, all-or-nothing group rollback (D34) | group rule triggers consumer contract checks |
| **P10 Insights & CLI** (v0.2) | Insights tab (D33), confidence calibration (D29), `syncro policy` / `syncro resolve` CLI commands (D38) | rules can be judged by undo rate; CLI and UI use the same policy |
| **P11 SRE hooks** (v0.2) | runbook action policies + incident mode (D40) | rollback/restart go through the same decider |

---

### C.18 Decisions for you

1. **Default autonomy level** — proposal: Level 1 (Suggest). Level 2 once P4 is proven.
2. **Where team policy lives** — proposal: `.syncro/agent-policy.yaml` committed in each repo.
3. **AST merge** — build our own on `Services/AST`, or shell out to Mergiraf as a merge driver first and
   replace later. Proposal: Mergiraf-style driver first (faster), own engine in P6.
4. **Learning** — keep "suggest only", or allow auto-lowering weights. Proposal: suggest only.
5. **Order of work vs SRE** — proposal: P0 + P1 first (P1 fixes a live safety gap), then P2–P5, then SRE-1.
6. **(v0.2) Repo policy trust** — may a repo's `.syncro/agent-policy.yaml` *loosen* settings? Proposal:
   no — repo files can only tighten; loosening needs a local "Trust this policy" approval (D1).
7. **(v0.2) One settings page or two** — proposal: `/settings/agent` is the real page; the IDE Settings
   panel's Permissions / Models / Notifications tabs render the same components (D36).
8. **(v0.2) Shadow mode before auto** — proposal: every new rule starts in shadow mode for 10 runs (D26).

---

## Part D — Gap review & build checklist (v0.2)

Reviewed every tab of Part C against the code. Each row is a missing feature or risk, with a fix and
the phase it lands in. **Must** = needed before the agent may auto-apply anything; **Should** = needed
for a complete page; **Later** = nice to have.

### D.1 What I checked in the code

| Finding | Evidence | Impact on this design |
|---|---|---|
| IDE Settings panel already has **Permissions, Models, Notifications** tabs — all empty, only Appearance is built | `Components/IDE/SettingsPanel.razor:104` (`_tabs`), only `ActiveTab == "Appearance"` branch at :44 | Risk of two settings pages with different data → D36 |
| No 3-way merge editor — Monaco only creates a 2-way diff | `wwwroot/js/ide/monaco.js:143` `createDiffEditor` | "H1 Ask human" has nowhere good to open → D37 |
| Groq key comes from `GROQ_API_KEY` env var or `Initialize(apiKey)` — no settings UI, no secure store | `Services/Engine/LLM/GroqProvider.cs:27,34` | Model tab needs key storage + test button → D8 |
| `GitSyncPolicy` (auto-fetch/commit/push, branch allowlist) lives in memory inside GitManager | `Services/Git/GitSyncService.cs:10`, `Components/Pages/Git/GitManager.razor` | Lost on restart; second policy system → D13 |
| Conflict script only supports ours / theirs / manual / abort, for the whole repo | `scripts/git/git-resolve-conflicts.ps1` | Engine replaces it; script should call the engine → D39 |
| Settings saved through ad-hoc `Preferences` keys | `AstDashboard.razor:918` (`__syncro_workspace_root__`) | Policy store should own workspace root too → D36 |

### D.2 Checklist — Safety & permissions

| # | Gap | Why it matters | Fix | Priority | Phase |
|---|---|---|---|---|---|
| D1 | **Repo policy trust** | A cloned repo's `.syncro/agent-policy.yaml` could raise autonomy or add a `command` technique — merging that branch would run its code | Repo policy may only **tighten**. Loosening keys and any `command`/`script` technique need a local "Trust this policy" approval; re-ask when the file hash changes | Must | P1 |
| D2 | Custom command techniques = running arbitrary code | Same as above, plus a typo can wipe files | Run in a temp worktree with timeout; pass placeholders as args, never string-concat into a shell; first run of each command needs approval | Must | P3 |
| D3 | **Shell allowlist has no UI or data model** — floor C.8.4 refers to it | Floor can't be enforced without a list | Permissions tab: allow/deny by prefix or regex, per scope; default deny | Must | P1 |
| D4 | File-path allow/deny for agent writes | Agent could write `.git/`, signing keys, or outside the workspace | Deny `.git/**`, paths outside workspace root, user list (e.g. `Platforms/**/*.pfx`) | Must | P1 |
| D5 | Network allowlist | `FetchEndpointTool`, clone, push talk to the internet | Domain allowlist per scope; unknown domain → Ask | Must | P1 |
| D6 | Approval timeout behaviour undefined | Background agent waiting forever, or worse, auto-granting | Timeout ⇒ **deny** + log; never approve on timeout | Must | P1 |
| D7 | Prompt injection through conflict content | File text or commit messages sent to the AI may contain "ignore rules, push to main" | AI resolver runs with **no tools**; its output is text only and can't change policy, score or decision | Must | P6 |
| D8 | API key storage | Env var only; nothing in UI | MAUI `SecureStorage` (Windows Credential Manager), "Test key" button, show provider status | Must | P0 |
| D9 | Policy edits are unprotected | Any change silently lowers safety | Hard floors live in code, not data; every policy change is written to the audit log with a diff | Must | P1 |

### D.3 Checklist — Approval experience

| # | Gap | Fix | Priority | Phase |
|---|---|---|---|---|
| D10 | One-by-one approvals are slow | "Approve all N low-risk items" batch button; "remember for this operation only" | Should | P1 |
| D11 | No place for pending approvals when the agent runs in the background | Approval inbox (badge in MainLayout header), sorted by score | Must | P1 |
| D12 | Notification channels not defined | In-app toast, Windows toast, quiet hours, per-decision-band choice (fills IDE "Notifications" tab) | Should | P5 |

### D.4 Checklist — Git operations coverage

| # | Gap | Fix | Priority | Phase |
|---|---|---|---|---|
| D13 | No pull/rebase/stash strategy settings; `GitSyncPolicy` is separate and in-memory | Git Operations tab: pull = ff-only / rebase / merge; `autostash`; rebase `--update-refs`, `--autosquash`, `--rebase-merges`; auto-sync settings moved here and persisted | Must | P0/P2 |
| D14 | Rebase replays the same conflict on every commit | Enable rerere; "apply this resolution to the rest of the rebase"; max-commits-per-rebase limit | Should | P3 |
| D15 | Missing operations: stash pop, `git am` / patch apply, revert, pull | Add to `RuleMatch.Operations`; each gets abort/continue support | Should | P2 |
| D16 | **No whole-operation rollback or crash recovery** | Backup ref before start; journal file (`.git/syncro-op.json`) so a crashed resolution resumes or rolls back on next launch; repo lock so the user and agent don't edit the same tree at once | Must | P2 |
| D17 | **Clean merges can still break** (git says "no conflict", build fails) | "Run gates after clean merges too" toggle, on by default for shared branches | Must | P4 |
| D18 | Delete/modify and rename defaults not set | Explicit defaults per code: DU/UD → Ask; RR → keep both names and Ask; R/D → keep rename | Should | P3 |
| D19 | LFS, encoding/BOM, large files | Detect LFS pointers (pick side, never merge); keep original encoding/BOM; files > N MB → Ask | Should | P3 |
| D20 | Resolution commits have no attribution | Commit message template + trailers `Resolved-by: syncro-agent`, `Syncro-Rule: <id>`; GPG/SSH signing setting | Should | P4 |
| D21 | Protected branches only from a local list | Also read GitHub branch protection via `gh api` when a GitHub remote exists | Later | P9 |

### D.5 Checklist — Conflict types to add

| # | Type | Detect by | Technique |
|---|---|---|---|
| D22a | `l10n` | `*.arb` (Flutter), `*.resx`, `i18n/*.json` | Key-level merge (S3), conflict only on same key |
| D22b | `solution-project` | `*.sln`, `*.csproj`, `*.slnx` | Structural XML/sln merge; dedupe project GUIDs; then `dotnet build` |
| D22c | `xcode-project` | `project.pbxproj` | Union + dedupe object ids, else Ask |
| D22d | `gradle` | `build.gradle(.kts)`, `gradle.properties` | Dependency-line union + semver max |
| D22e | `notebook` | `*.ipynb` | Cell-level merge (nbdime-style); drop output conflicts |
| D22f | `list-file` | `.gitignore`, `.dockerignore`, `CODEOWNERS` | Union + dedupe |
| D22g | `prose` | `*.md` docs (not changelog) | Paragraph-level merge → AI → human |
| D22h | `sql-schema` | `*.sql`, schema dumps | Never auto; Ask |
| D22i | `markup` | `*.razor`, `*.xaml`, `*.tsx` JSX | Format-then-merge (S7) → AST merge → AI |
| D22j | `snapshot-tests` | `__snapshots__/`, golden files | Regenerate by running tests with update flag |

### D.6 Checklist — Rule quality

| # | Gap | Fix | Priority | Phase |
|---|---|---|---|---|
| D23 | Bad rules go unnoticed | Rule linter: shadowed/unreachable rules, unknown technique ids, chains with no ending step, loops in `chain` techniques | Must | P4 |
| D24 | Rules can't be tested | Each rule can carry fixtures (base/ours/theirs + expected); "Run rule tests" button and CLI | Should | P8 |
| D25 | No policy history | Version every save; diff and revert; schema migration v1 → v2 | Should | P5 |
| D26 | No safe rollout for new rules | **Shadow mode** per rule: compute the decision, log "would have done X", don't apply | Must | P4 |

### D.7 Checklist — AI settings

| # | Gap | Fix | Priority | Phase |
|---|---|---|---|---|
| D27 | No custom instructions | Per-scope instructions (like `CLAUDE.md`) + editable resolver prompt template with preview | Should | P5 |
| D28 | No cost or rate-limit controls | Token budget per operation and per day; Groq 429 backoff/retry; model per conflict type; disable LLM per project | Must | P5 |
| D29 | AI confidence is self-reported and untested | Track confidence vs undo rate; show calibration; auto-raise `minConfidence` suggestion if undo rate is high | Should | P10 |
| D30 | Privacy rules scattered | Privacy & Data tab: path deny-list for LLM, max bytes sent, "local-only" projects, redaction preview | Must | P5 |

### D.8 Checklist — Environments, multi-project, UX, integration

| # | Gap | Fix | Priority | Phase |
|---|---|---|---|---|
| D31 | **"Production" factor has no source** — nothing says which branch/target is prod | Environment map: branch glob → env, remote → env, deploy target (Netlify site) → env | Must | P1 |
| D32 | Freeze windows are cron-only | Calendar UI with timezone, "freeze now" toggle, break-glass override that requires a reason and is logged | Should | P5 |
| D33 | No numbers to judge rules | Insights tab: auto-resolve %, undo % per rule/technique, approvals asked vs auto, time saved | Should | P10 |
| D34 | Group operations can half-succeed | All-or-nothing across repos: if repo 3 blocks, roll back repos 1–2 using their backup refs. Depends on Part B fixes | Should | P9 |
| D35 | Page usability | Settings search; reset section to defaults; unsaved-changes guard; inline validation; "effective policy for file X" viewer | Should | P5 |
| D36 | **Two settings UIs** | One `AgentPolicyStore`; IDE `SettingsPanel` tabs (Permissions, Models, Notifications) render the same components; workspace root moves into the store | Must | P0 |
| D37 | **No 3-way merge editor** | Monaco-based base / ours / theirs / result editor with per-hunk "accept AI suggestion" | Must | P2 |
| D38 | CLI ignores policy | `Syncro.CLI`: `syncro policy show|lint|test`, `syncro resolve --dry-run` use the same store | Should | P10 |
| D39 | Old script path | `git-resolve-conflicts.ps1` calls the engine (or is marked legacy) so behaviour matches the UI | Later | P4 |
| D40 | SRE actions not covered | "Runbook actions" section: restart / rollback / scale / flag-off through the same decider; **incident mode** temporarily allows rollback at Auto + notify | Later | P11 |

### D.9 Data model additions

```csharp
public sealed class PermissionsPolicy
{
    public List<string> ShellAllow { get; set; } = new();     // prefix or /regex/
    public List<string> ShellDeny  { get; set; } = new();
    public List<string> PathAllow  { get; set; } = new();     // globs, relative to workspace
    public List<string> PathDeny   { get; set; } = new() { ".git/**" };
    public List<string> NetworkAllow { get; set; } = new();   // domains
}

public sealed class EnvironmentMap
{
    public Dictionary<string, string> Branches { get; set; } = new();   // "main" -> "production"
    public Dictionary<string, string> Remotes  { get; set; } = new();
    public Dictionary<string, string> DeployTargets { get; set; } = new();
}

public sealed class ApprovalSettings
{
    public int TimeoutMinutes { get; set; } = 30;              // timeout always means deny
    public bool AllowBatchApprove { get; set; } = true;
    public List<string> NotifyChannels { get; set; } = new() { "in-app" };
    public string? QuietHours { get; set; }
}

public sealed class PolicyTrust                                 // stored locally, never in the repo
{
    public string RepoPath { get; set; } = "";
    public string TrustedFileHash { get; set; } = "";
    public DateTime TrustedAt { get; set; }
}

// ResolutionRule additions
//   public bool Shadow { get; set; } = true;     // D26: new rules start in shadow mode
//   public bool Enabled { get; set; } = true;
//   public List<RuleFixture> Fixtures { get; set; } = new();   // D24
```

`AgentPolicy` gains `Permissions`, `Environments`, `Approvals`, `Privacy`, `GitOps` sections.

### D.10 Is more needed after this?

With D1–D40 in place the panel covers model setup, safety, git operations, conflict rules, cherry-pick,
multi-project scope, input methods, review tools and metrics. Remaining open items are outside this panel:

- **Part B connector fixes** (connector.json v2, base-URL ownership) — needed before D34 and group rules.
- **Part A SRE phases** — telemetry, SLOs and incidents are their own pages; this panel only supplies the decider (D40).
- **Team features** (shared global policy, who may edit it) — not needed while Syncro is single-user.

---

## Part E — Mockup mapping & full tab UI plan (v0.3)

The Model & Provider mockup (shared 2026-10-23) is the visual standard for every tab: left sub-nav
grouped by section, top **scope bar** (breadcrumb · "Inherited with N overrides" chip · Export YAML ·
Simulate · Discard · Save changes), page title + one-line description, white **cards** with icon +
title + subtitle, 2-column grid on wide screens, and a footer strip ("Effective scope: … · All changes
are versioned and audited").

### E.1 How much the mockup maps

| Mockup element | Proposal section | Status |
|---|---|---|
| Sub-nav in 5 sections (AI / Safety / Git & Conflicts / Projects / Review) | C.3 | ✅ same grouping |
| Search box | D35 | ✅ |
| Scope breadcrumb + "Inherited with 3 overrides" | C.3, C.11 | ✅ (override count chip is a good addition) |
| Export YAML · Simulate · Discard · Save | C.3 | ✅ |
| Provider, API key, Change key, Test connection, Connected | C.4, D8 | ✅ |
| Agent / Resolver / Fallback model + two temperatures | C.4 | ✅ |
| Max tool calls, tokens per conflict hunk | C.4 | ✅ |
| Daily token budget + "42K used today" bar | D28 | ✅ (adds live usage — needs a usage counter in GroqProvider) |
| Context source chips | C.4 | ✅ |
| Redact secrets · Offline mode | C.4 | ✅ |
| Footer: effective scope · versioned & audited | C.11, D9, D25 | ✅ |
| Header pill "SRE Guard Active" | — | ⚠ hard-coded in `MainLayout.razor` today; must show real autonomy level + pending approvals |

**Tab-level coverage**

| Mockup tab | Proposal tabs it holds | Fit |
|---|---|---|
| Model & Provider | 1 | ✅ full |
| Tools & Capabilities | C.8.5 per-tool overrides | 🟡 partial — add action class + risk per tool |
| Context & Retrieval | C.4 context sources, D30 size caps | 🟢 new tab, good split |
| System Prompt | D27 | ✅ |
| Safety & Privacy | 12, D1, D7 | ✅ |
| Guardrails | 2 (autonomy, bands, floors) | ✅ |
| Merge & Conflict Resolution | 3 + 4 + 5 + 7 | 🟡 four tabs in one — needs inner tabs |
| Commit Style | D20 | 🟢 new tab |
| Pull Requests | C.9 C12, D21 | 🟢 new tab |
| Project Inheritance / Overrides | 8 | ✅ |
| Human Review | D10–D12 + approval inbox | ✅ |
| Automation | C.8.1 levels, D26 shadow mode | 🟡 unclear in mockup — defined below |

**Missing from the mockup** (8 of the proposal's 16 tabs): Cherry-pick & Backport (6), Rule Import (9),
Simulator page (10 — only a button), Audit Log (11), Permissions as its own page (13), Environments &
Freezes (14), Sync & Pull (15), Insights (16).

**Verdict:** the mockup covers about **60%** of the proposal's tabs and adds 3 good ones (Context &
Retrieval, Commit Style, Pull Requests). Final nav = mockup + the missing tabs = **21 tabs**.

### E.2 Final navigation

| Section | Tabs |
|---|---|
| **AI** | Model & Provider · Tools & Capabilities · Context & Retrieval · System Prompt |
| **Safety** | Guardrails · Permissions · Safety & Privacy · Environments & Freezes |
| **Git & Conflicts** | Merge & Conflict Resolution · Cherry-pick & Backport · Sync & Pull · Commit Style · Pull Requests |
| **Projects** | Project Inheritance · Project Overrides · Rule Import |
| **Review** | Human Review · Automation · Simulator · Audit Log · Insights |

Nav badges: red count on **Human Review** (pending approvals); grey "Planned" tag on tabs not built yet.

### E.3 Tab-by-tab UI plan

Cards are written `[Card] — fields`. **Built now** = part of this P0/P1 pass.

#### AI

**1. Model & Provider** — as mockup. *Built now:* provider, key (Change key / Test connection / Connected
pill), agent model, temperature, max tool calls, tokens-used-today counter. Resolver/fallback model,
daily budget enforcement and context chips are saved now but take effect in P5/P6.

**2. Tools & Capabilities** *(built now)*
- [Tool registry] tool · action class (Read / Internal / Local write / Execute / Outward / Destructive) ·
  old approval flag · **override** (Default / Always ask / Auto if safe / Block). Filter by class; search.

**3. Context & Retrieval** *(P6)*
- [Sources] mockup chips + AST depth, max files, include tests.
- [Retrieval] context-pack size, ranking (AST neighbours / recency / graph distance).
- [Preview] pick a file → exactly what would be sent + token count.

**4. System Prompt** *(P5)*
- [Global instructions] editor with variables (`{workspace}`, `{projects}`).
- [Per-scope instructions] inherited view with diff.
- [Resolver prompt template] with "Preview prompt" on a sample conflict.

#### Safety

**5. Guardrails** *(built now)*
- [Autonomy level] 5 option cards (Observe → Full auto sandbox) with one-line meaning.
- [Decision bands] Auto ≤ / Notify ≤ / Ask ≤ numbers drawn on a 0–100 coloured bar.
- [Protected branches] chip list.
- [Hard floors] read-only list with lock icons.
- [Factor weights] *(P4)* editable points for the 14 factors.

**6. Permissions** *(built now)*
- [Shell] allow + deny lists (prefix or `/regex/`), "Test command" → Allowed / Ask / Blocked.
- [File paths] allow/deny globs; `.git/**` denied by default.
- [Network] allowed domains; unknown domain → Ask.

**7. Safety & Privacy** *(P5)*
- [LLM data] redact secrets, offline mode, path deny-list for sending, max bytes, local-only projects.
- [Repo policy trust] repos with `.syncro/agent-policy.yaml`, trusted hash, Trust / Revoke.
- [Prompt injection] "AI resolver runs without tools" (locked on).

**8. Environments & Freezes** *(built now)*
- [Environment map] rows: branch glob → Local / Staging / Production.
- [Freeze] "Freeze now" toggle with reason.
- [Freeze windows / break-glass] *(P5)*.

#### Git & Conflicts

**9. Merge & Conflict Resolution** *(P3–P4)* — inner tabs:
- *Conflict types*: id, detect-by, severity, priority; built-ins locked; "Add type" drawer.
- *Techniques*: catalog grid (T/S/A/H) with risk badge, params drawer, "Add custom technique".
- *Rules*: drag-ordered rows = match chips → chain pills → gates → else; shadow toggle.
- *Gates*: build / tests / lint / contract / SAST / AST sanity, required vs advisory, timeout.

**10. Cherry-pick & Backport** *(P3)* — [Defaults] `-x`, patch-id dedupe, empty handling, mainline ·
[Backport targets] globs + mode · [On failure] stop / skip & continue.

**11. Sync & Pull** *(built now — replaces in-memory `GitSyncPolicy`)*
- [Registered repos] repo · auto-fetch · auto-commit · auto-push · branch allowlist · remove.
- [Interval] fetch interval.
- [Pull strategy] *(P2)* ff-only / rebase / merge, autostash.

**12. Commit Style** *(P4)* — [Message] template, Conventional Commits, subject length ·
[Trailers] `Resolved-by`, `Syncro-Rule`, `Co-authored-by` · [Signing] GPG / SSH / none.

**13. Pull Requests** *(P9)* — [Defaults] base, draft, reviewers, labels · [Body template] with AI
summary · [Protection] read GitHub branch protection.

#### Projects

**14. Project Inheritance** *(P5)* — tree Global → Group → Project → Branch; each setting shows
effective value + source chip.

**15. Project Overrides** *(P5)* — overrides at current scope with Reset; "Effective policy for file…".

**16. Rule Import** *(P7)* — six cards: Form · YAML · Plain English · Import · Learn from history ·
Templates, each ending in preview + Simulate.

#### Review

**17. Human Review** *(built now)*
- [Pending approvals] card per request: tool, target, score, factor table, input preview,
  Approve / Deny / "Allow this tool for this run"; "Approve all low-risk".
- [Settings] approval timeout (timeout = deny), batch approve on/off.

**18. Automation** *(P4)* — [Shadow mode] N runs · [Background triggers] on conflict / drift / failed
build · [Learning] suggest-only.

**19. Simulator** *(P8)* — past merge or two branches → per-file result table; policy compare.

**20. Audit Log** *(built now)* — time · origin · tool · decision · score · outcome · approver; filter by
decision/tool; row expands to factors + input preview; open the JSONL file.

**21. Insights** *(P10)* — auto-resolve %, undo %, approvals asked vs auto, top blocked tools.
