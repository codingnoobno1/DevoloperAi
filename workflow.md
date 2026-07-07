# Project Generator — Robust UI Workflow

> Design for a **robust** new-project creation flow. The backend is already well-structured
> (`IProjectCreationService` → `StackRegistry` → scaffolders → `GroupOrchestrator` + `PortAllocator`
> + `ProjectWiringService`, plus `DatabaseScaffolder`/`ComposeGenerator`). This doc designs the **UI
> workflow** that drives it so creation is **never-hangs, never-lies, validated-early,
> all-or-nothing, and resumable**.
>
> Grounded in: `Components/Shared/CreateProjectDialog.razor` (the 3-step wizard today),
> `Services/projectgenerator/IProjectCreationService.cs`, `Models/ProjectCreationRequest.cs`,
> `Orchestration/*`, `Registry/*`, `Services/Database/*`, `BusinessLogic/EnvironmentManager`.
> Sister docs: [`safeupgrade.md`](safeupgrade.md), [`projectgenerator.md`](projectgenerator.md),
> [`engine/optimisation server cli.md`](engine/optimisation%20server%20cli.md) (§3 parallel runner + DB).

---

## 0. Current state & robustness gaps

The dialog is a 3-step MudBlazor wizard (**type → config → location**) that builds a
`ProjectCreationRequest` and calls `CreationService.CreateAsync(request, progress)`
([CreateProjectDialog.razor:389](Components/Shared/CreateProjectDialog.razor:389)).

| # | Gap | Where | Effect |
|---|---|---|---|
| G1 | **No cancellation.** `CreateAsync` accepts a `CancellationToken` but the UI never passes one | [CreateProjectDialog.razor:389](Components/Shared/CreateProjectDialog.razor:389) | a hung `flutter create` / `npm create` (interactive prompt) freezes the dialog forever |
| G2 | **No environment pre-flight.** `EnvironmentManager` is injected but the chosen stack isn't validated against installed toolchains before creating | inject at [:6](Components/Shared/CreateProjectDialog.razor:6) | runs a CLI that may not exist → catch → silent template fallback |
| G3 | **No database step.** `ProjectCreationRequest` has no DB field; `DatabaseScaffolder`/`ComposeGenerator` are unreachable from the UI | `Models/ProjectCreationRequest.cs` | bug B7 — the "…+ Postgres" flow can't be expressed |
| G4 | **No review/confirm gate.** Creation fires immediately on the last Next | step 3 → Create | the proposal's "User Review Required" is missing; commands run unseen |
| G5 | **Log-only progress.** `Progress<string>` appends lines; no phase/percent/per-member structure | [:388](Components/Shared/CreateProjectDialog.razor:388) | group creation is opaque; looks hung during long installs |
| G6 | **No post-create actions** (open in IDE / run / open compose) or **rollback surfacing** for partial group failure | after result | dead-end on success; ambiguous on partial failure |
| G7 | **No idempotency** on an existing target path | request build | silent overwrite / clobber |

---

## 1. Design goals (what "robust" means here)

1. **Never hangs** — every long step is cancellable + timeout-bounded (fixes G1).
2. **Never lies** — validate the toolchain *before* committing; show real progress, not a scroll
   that might be stuck (G2, G5).
3. **Validated early** — each step gates Next on valid input; the env probe decides which stacks are
   even offered (G2).
4. **All-or-nothing** — a group either fully succeeds or rolls back; no half-made stacks (G6).
5. **Resumable / re-entrant** — safe to retry after a failure; existing-path handled (G7).
6. **One request, one behavior** — the UI only ever fills a `ProjectCreationRequest`; all creation
   logic stays behind `IProjectCreationService` (the existing seam).

---

## 2. The workflow state machine

```
        ┌─────────────┐   probe ok    ┌──────────┐   valid   ┌──────────┐
  open →│ S0 Preflight │─────────────▶│ S1 Type  │─────────▶│ S2 Config │
        └─────────────┘               └──────────┘◀────Back──└──────────┘
              │ probe fail                                        │ valid
              ▼ (warn, allow template-only)                       ▼
        (offer installable stacks only)                     ┌──────────┐
                                                            │ S3 Location│
                                                            └──────────┘
                                                                 │ valid
                                                                 ▼
                                    ┌───────────────┐  confirm  ┌──────────┐
                        (retry) ◀───│ S6 Result     │◀──────────│ S4 Review │
                                    └───────────────┘           └──────────┘
                                          ▲                          │ Create
                                          │ done/failed              ▼
                                          └──────────────────  ┌──────────┐
                                             (cancel → S4)     │ S5 Create │──cancel──┐
                                                               └──────────┘◀──────────┘
```

States: **S0 Preflight · S1 Type · S2 Config · S3 Location · S4 Review · S5 Create · S6 Result.**
`Back` is allowed S1↔S3. `Cancel` during S5 aborts the token and returns to S4 (inputs preserved).

---

## 3. Step-by-step design

### S0 — Environment pre-flight (new)
- On open, run `EnvironmentManager` probe → `{ node, npm, python, pip, dotnet, java, flutter, docker,
  git }`. Cache for the dialog's lifetime.
- Render a compact capability strip: `Detected: Node 20 ✓ · Python 3.12 ✓ · Docker ✓ · Flutter ✗`.
- **Gate the registry:** a stack whose required toolchain is missing is shown **disabled** with
  "install Node to enable", *or* offered in **template-only** mode (no native CLI). This kills G2 at
  the source — the user can't pick a CLI path that will fail.

### S1 — Project type
- **Single vs Group** toggle. Options come from `StackRegistry.GetAllStacks()`
  ([CreateProjectDialog.razor:128](Components/Shared/CreateProjectDialog.razor:128)) filtered by S0.
- Group: multi-select archetypes (Frontend + Backend + …), each annotated with its `StackKind`.
- **Validation:** ≥1 stack selected; group needs ≥1 frontend/backend pairing to be meaningful.

### S2 — Configuration
- Per-stack options (language/variant), and the **new DB archetype selector**:
  - Engine: **None · Postgres · MySQL · Mongo · Redis · SQLite** (`DatabaseScaffolder.DbEngine`).
  - Strategy auto-picked from S0: Docker present → compose; else local binary; else **SQLite
    fallback** (always offered, needs nothing).
  - DB name / user (defaults filled).
- **Port preview:** `PortAllocator` proposes free ports per member + DB; show them, let advanced
  users override. No two members share a port (fixes bug B5).
- **Validation:** DB engine compatible with strategy (e.g. "Postgres needs Docker or local psql —
  none found → will use SQLite").

### S3 — Location & options
- Target path (browse), `UseNativeCli` toggle (auto-off if S0 lacks the CLI), `SyncWithAgent`
  (AST index after scaffold).
- **Idempotency (G7):** if `path/name` exists → offer *Use empty subfolder* / *Choose another* /
  *Cancel*; never silently overwrite.
- **Validation:** path writable; name is a valid package identifier (sanitize preview shown).

### S4 — Review & confirm (new — the gate)
The proposal's **"User Review Required"**. Show a **dry-run summary** before anything runs:
```
Create group "shop" at D:/dev/shop
  app/        Next.js      (create-next-app)     :3000
  backend/    FastAPI      (template)            :8000  → DATABASE_URL
  postgres    Postgres 16  (docker compose)      :5432
  docker-compose.yml + up.sh/up.bat will be written
  Wiring: app → backend (NEXT_PUBLIC_API_URL), backend → postgres (DATABASE_URL), CORS
```
Buttons: **Back** · **Create**. Nothing external has run yet — this is the last off-ramp.

### S5 — Create (parallel, live, cancellable)
- Build `ProjectCreationRequest` (+ DB/port extensions, §6) and call
  `CreateAsync(request, progress, cts.Token)` — **now passing the token** (fixes G1).
- **Cancel button** cancels the CTS; the runner aborts in-flight processes and rolls back (§4.3).
- **Structured progress** (§5), not a log scroll: a phase list with per-member state
  (`queued → scaffolding → installing → indexing → done/failed`) + an overall bar. Raw log is a
  collapsible drawer.
- Group members run in **parallel waves** via `GroupOrchestrator` (DB wave first, then services),
  bounded by cores.

### S6 — Result
- **Success:** summary + **post-create actions**: *Open in Syncro IDE* · *Run* (flutter/web/compose
  `up`) · *Reveal folder* · *Open compose*. Register the project (`ProjectService`).
- **Partial/failed:** show which member failed + its error tail; the group was **rolled back**
  (no orphan folders). Buttons: **Retry** (returns to S4 with inputs intact) · **View logs** · **Close**.

---

## 4. Robustness cross-cuts

### 4.1 Cancellation (G1)
- One `CancellationTokenSource` per creation; **Cancel** in S5 cancels it and any child in
  `MobilePreview`-style runs. Native CLI steps get a per-step timeout (e.g. 5 min) via a linked CTS →
  fall back to template or fail cleanly (never infinite).

### 4.2 Validation ladder
- Every state has a `CanAdvance()` predicate; **Next disabled** until valid (the pattern already at
  [CreateProjectDialog.razor:158](Components/Shared/CreateProjectDialog.razor:158)). S0 gates the whole
  registry; S4 is the human gate.

### 4.3 Rollback / all-or-nothing (G6)
- `GroupOrchestrator` records each created artifact (folder, DB volume, compose file). A failed wave
  triggers compensating deletes → the group is atomic. The UI shows "rolled back N partial artifacts".

### 4.4 Idempotency (G7)
- Existing target → explicit choice (§S3). Re-running after a failure reuses the same request; the
  scaffolder is safe to re-enter (skip-if-present where possible).

### 4.5 Error taxonomy (never a bare "failed")
| Class | Example | UI surface |
|---|---|---|
| Precondition | toolchain missing | blocked at S0/S2 with fix hint |
| Transient | network/registry timeout | Retry offered |
| Fatal | disk full / permission | clear message + logs |
| Cancelled | user cancelled | neutral "cancelled", inputs kept |

---

## 5. Progress event model (replaces log-only, G5)

Extend the seam from `IProgress<string>` to a structured event so the UI can show phases/percent:

```csharp
public enum CreationPhase { Preflight, Scaffold, Install, Database, Compose, Wire, Index, Done, Failed, RolledBack }

public sealed record CreationProgress(
    CreationPhase Phase,
    string Member,        // "app", "backend", "postgres", or "" for the whole group
    int Current, int Total,
    string Message,
    bool IsError = false);
```
- `IProjectCreationService.CreateAsync(request, IProgress<CreationProgress> progress, ct)` (keep a
  string overload for back-compat). The UI renders a **member × phase grid** + overall bar; the raw
  message stream stays available in a drawer.

---

## 6. `ProjectCreationRequest` extensions (to reach robust)

Add without breaking the existing seam:

```csharp
public DbEngine Database { get; set; } = DbEngine.None;   // S2
public string? DatabaseName { get; set; }
public Dictionary<string,int> PortOverrides { get; set; } = new();   // member → host port (S2)
public bool EmitCompose { get; set; } = true;             // write docker-compose + scripts
public EnvProbe? Environment { get; set; }                // S0 result, so the service re-validates
public OnExistingPath OnExisting { get; set; } = OnExistingPath.Prompt;  // S3 (G7)
```
`DefaultProjectCreationService` then routes DB nodes through `DatabaseScaffolder` + `ComposeGenerator`
and members through `PortAllocator`/`ProjectWiringService` — the pieces already exist; this just lets
the UI express them.

---

## 7. Wiring map (which service each step drives)

| Step | Service |
|---|---|
| S0 Preflight | `EnvironmentManager` |
| S1 Type | `StackRegistry.GetAllStacks()` |
| S2 Config | `StackRegistry`, `PortAllocator`, `DatabaseScaffolder` (strategy preview) |
| S3 Location | filesystem checks |
| S4 Review | pure UI (dry-run from the request) |
| S5 Create | `IProjectCreationService.CreateAsync` → `GroupOrchestrator` → scaffolders + `DatabaseScaffolder`/`ComposeGenerator` + `ProjectWiringService` |
| S6 Result | `ProjectService` (register), `IdeWindowService` (open), `FlutterService`/compose `up` (run) |

---

## 8. Phased build

| Phase | Work | Closes |
|---|---|---|
| **W-P0** | Pass a `CancellationToken` + add a Cancel button in S5 | G1 |
| **W-P1** | S0 env pre-flight gating the registry | G2 |
| **W-P2** | Structured `CreationProgress` + member×phase UI | G5 |
| **W-P3** | S4 Review/confirm gate | G4 |
| **W-P4** | DB step + request extensions → `DatabaseScaffolder`/`ComposeGenerator` | G3 |
| **W-P5** | Idempotency (S3) + rollback surfacing (S6) + post-create actions | G6, G7 |

**W-P0 first** — it's the smallest change (wire the token + a button) and removes the worst failure
mode: a permanently frozen creation dialog.

---

## 9. Acceptance criteria
- [ ] Creation can always be **cancelled**; a stuck CLI never freezes the dialog.
- [ ] Only stacks whose toolchain exists (or template-only) are selectable.
- [ ] A **Review** screen shows exactly what will run/write before anything external executes.
- [ ] DB engine selectable; Postgres/Mongo emit compose + wire `DATABASE_URL`; SQLite works with no Docker.
- [ ] Group creation shows per-member phase/percent, runs in parallel, and is **atomic** (rollback on failure).
- [ ] Existing target path is never silently overwritten.
- [ ] On success, the user can open the project in the IDE or run it in one click.
