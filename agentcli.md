# Agent ⇄ CLI — Centralised DB & Loopable Execution

> A single safe `syncro` entry point, one **central DB** (`.syncro_db`) that is the **contract
> between the agent and the CLI**, and a **loopable task model** where *one status change* drives
> the next action — run a script, classify an error, edit code, or call the LLM (if available) —
> until success or a bounded human-handoff.
>
> Extends `cli.md` (LlmGateway, RagEngine, safety gate) and `ast.md` (cognitive memory / agents).

---

## 0. What you can type (after `syncro install` adds it to PATH)

```bash
syncro git clone https://github.com/user/repo.git   # safe child-process clone, recorded in DB
syncro analyse [path]                                # AST scan + tokenise → writes AST + Vectors
syncro generate report                               # reads DB → PDF/JSON report (no re-scan)
syncro show project [id] [--json]                    # prints project FROM DB in vector/token form
```

All four read/write the **same central DB** — never the filesystem directly — so the agent and
the CLI always see one consistent state.

---

## 1. Centralised dispatcher

```
syncro <cmd> <args> --flags
        │
        ▼
   CliDispatcher           (parse argv → CliInvocation, global flags from cli.md)
        │
        ▼
   ┌──────────────────────────────────────────────┐
   │  SyncroDb  (the ONE facade — atomic, locked)   │ ◀── single source of truth
   └──────────────────────────────────────────────┘
        │            │             │            │
   GitCommand   AnalyseCommand  ReportCommand  ShowCommand   …  (ICliCommandV2)
        │            │             │            │
   LlmSafetyGate ── LlmGateway ── RagEngine  (only when needed; safe by default)
```

**Rule:** no command touches `.syncro_db` files directly. Everything goes through `SyncroDb`.
This is what makes it *centralised* and safe to share between agent and CLI.

---

## 2. The four commands (DB-backed)

| Command | Reads | Writes | Notes |
|---|---|---|---|
| `syncro git clone <url>` | — | `Tasks` (clone task), `Projects` | Runs in a **child process** (crash-isolated); streams progress; records a `TaskRecord` |
| `syncro analyse [path]` | `Projects` | `AST/<id>.json`, `Vectors/<id>/index.json`, `Projects` (astNodeCount, vectorCount) | AST scan → **tokenise** via `IEmbeddingProvider` (lexical default, offline) |
| `syncro generate report` | `AST`, `Vectors`, `Endpoints`, `Tasks` | `report.pdf` / `report.json` | Pure read of DB → `PdfReporter`/`JsonReporter`; **no re-scan** |
| `syncro show project [id]` | `Projects`, `Vectors`, `AST` | — | Renders the project **in vector/tokenised form** (below) |

**`syncro show project` output (the "vector tokenised form"):**
```
Project: viking-api  (csharp / aspnet)   path: D:\...\viking-api
AST:     142 nodes · 18 endpoints · 9 DTOs
Vectors: 142 records · dim 256 · embedder=lexical-v1
Top tokens (by tf-idf weight):  controller(0.91) tender(0.88) repository(0.79) jwt(0.71) ...
Entities:  TenderController →[uses]→ TenderService →[writes]→ TenderTable
Recent tasks: 3 done · 1 failed · 0 running
```
`--json` returns the structured `VectorRecord[]` + project meta for programmatic callers.

---

## 3. `SyncroDb` — the central facade + store layout

```
.syncro_db/                         (one per project/group root — never duplicated)
├── Projects/projects.json          # registry (exists)
├── Groups/groups.json              # group manifests (exists)
├── AST/<projectId>.json            # AST maps
├── Graphs/<projectId>.json         # dependency/call graphs
├── Vectors/<projectId>/index.json  # vector records (exists)
├── Tasks/tasks.jsonl               # ◀ THE LOOP — append-only event log
├── Scripts/scripts.json + bodies/  # registered scripts (id → body, shell, safe-flag)
├── Errors/errors.jsonl             # error records (linked to tasks)
├── Memory/                         # agent memory + custom to-do (jsonl per kind)
└── Agents/audit.jsonl              # append-only audit (every mutation + LLM call)
```

```csharp
public class SyncroDb                       // injected everywhere; the ONLY writer
{
    // Atomic, file-locked, crash-safe (write-temp + rename; jsonl append for logs)
    Task<TaskRecord> EnqueueTaskAsync(TaskRecord t);
    Task UpdateTaskStatusAsync(string taskId, TaskStatus to, string? note = null);  // ← drives the loop
    Task<IReadOnlyList<TaskRecord>> QueryTasksAsync(string projectId, TaskStatus? status = null);
    Task<ScriptRecord?> GetScriptAsync(string scriptId);
    Task AppendErrorAsync(ErrorRecord e);
    Task UpsertVectorsAsync(string projectId, IEnumerable<VectorRecord> v);
    Task<ProjectRecord?> GetProjectAsync(string id);
    Task AppendAuditAsync(AuditEntry a);
    Task<IReadOnlyList<MemoryNote>> GetTodoAsync(string projectId);   // agent's custom to-do
    Task AddTodoAsync(MemoryNote note);
}
```

Because both the **agent** (in-process / desktop) and the **CLI** (in-process or standalone exe)
use `SyncroDb`, the DB *is* the coordination channel. A Named Pipe (cli.md §2) becomes an
*optional* live-notify optimisation — the DB stays the source of truth, so a CLI crash loses nothing
(resume from last `tasks.jsonl` status).

---

## 4. The shared task model (your exact fields)

`Tasks/tasks.jsonl` — one JSON object per line (event-sourced, replayable):

```jsonc
{
  "task_id": "tsk_8f2a",
  "project_id": "viking-api",
  "title": "Install backend dependencies",
  "intent": "run_script",          // run_script | edit_code | analyse | generate | llm_fix
  "script_id": "scr_setup_bat",    // → Scripts/ registry
  "template_script_executed": false, //  ← executed: yes / no
  "status": "pending",             //  ← THE ONE FIELD that triggers actions
  "error": false,                  //  ← error: yes / no
  "error_count": 0,                //  ← number of errors
  "source": null,                  //  ← where it failed (file:line | stderr tail | command)
  "error_type": null,              //  ← if error → type (missing_dep|compile|runtime|port|perm|unknown)
  "solution": null,                //  ← edit_code | run_another | rerun | llm_fix | none
  "next_action": null,             //  computed next step
  "llm_involved": false,           //  did the LLM participate?
  "attempts": 0,
  "max_attempts": 4,               //  loop bound (no infinite loops)
  "needs_approval": false,         //  mutating/unsafe → wait for human/--yes
  "created_at": "2026-06-06T...",
  "updated_at": "2026-06-06T...",
  "history": [ { "status": "pending", "at": "...", "note": "enqueued by syncro analyse" } ]
}
```

Supporting records:
```csharp
public record ScriptRecord(string Id, string Shell, string Body, bool Safe, string[] Tags); // Safe=auto-rerunnable
public record ErrorRecord(string Id, string TaskId, string Type, string Message, string? Source, int Line);
public record MemoryNote(string Id, string ProjectId, string Kind, string Text, double Salience); // Kind: todo|decision|bug|risk|failure
public record AuditEntry(DateTime At, string Actor, string Action, string[] Files, string? LlmState);
```

---

## 5. The loop — one status change → one action

A **single status field** is the trigger. `TaskLoopEngine` is a reducer: it reacts to a status and
performs exactly one action, then transitions. This is the "1 status change could take action,
execute the script, and even involve the LLM if on."

```
            ┌──────────┐
            │ pending  │── pick up ─────────────┐
            └──────────┘                        ▼
                                          ┌──────────┐  execute script (sandboxed, child process)
                                          │ running  │  via ScriptRunner / ProcessRunner
                                          └────┬─────┘
                              exit==0 ──────────┴────────── exit!=0
                                  ▼                              ▼
                            ┌───────────┐                 ┌──────────┐  error=true, error_count++,
                            │ succeeded │                 │  failed  │  capture source + classify type
                            └────┬──────┘                 └────┬─────┘
                                 ▼                              ▼
                            ┌────────┐                   ┌─────────────┐  ErrorClassifier + ResolutionPolicy
                            │  done  │                   │ diagnosing  │  (deterministic rule, or LLM if on)
                            └────────┘                   └────┬────────┘
                                                              ▼
                                          solution = rerun | run_another | edit_code | llm_fix
                                                              ▼
                                ┌──────────────────────────────────────────────┐
                                │ resolving                                      │
                                │  • rerun / run_another  → safe → auto          │
                                │  • edit_code / llm_fix  → needs_approval (gate) │
                                └────┬──────────────────────────────────┬────────┘
                       attempts<max  │                                   │ attempts≥max OR unsafe
                                     ▼                                   ▼
                                ┌──────────┐                       ┌──────────────┐
                                │ patched  │── attempts++ ──▶ pending │ needs_human  │ (terminal until user acts)
                                └──────────┘   (LOOP BACK)         └──────────────┘
```

```csharp
public class TaskLoopEngine
{
    // Process one task one step. Called on-demand (CLI drains queue) or by a watcher (agent).
    public async Task<TaskRecord> StepAsync(TaskRecord t, CancellationToken ct)
    {
        switch (t.Status)
        {
            case TaskStatus.Pending:    return await Run(t, ct);            // execute script
            case TaskStatus.Running:    return await Collect(t, ct);        // exit code → succeeded/failed
            case TaskStatus.Failed:     return await Diagnose(t, ct);       // classify + decide solution
            case TaskStatus.Resolving:  return await Resolve(t, ct);        // apply (gated if code edit)
            case TaskStatus.Patched:    return Reenqueue(t);                // loop back to Pending
            case TaskStatus.Succeeded:  return await Finish(t, ct);         // write hindsight memory
            default:                    return t;                           // Done / NeedsHuman terminal
        }
    }
}
```

**`Diagnose` decision (the agent's logic):**
```
classify(stderr) → error_type
match error_type:
  missing_dep   → solution = run_another (install script)        [SAFE → auto]
  port_conflict → solution = rerun with new port                 [SAFE → auto]
  compile/runtime:
     if LlmGateway.Available → solution = llm_fix (propose patch) [NEEDS APPROVAL]
     else                    → solution = edit_code (heuristic)   [NEEDS APPROVAL]
  perm/unknown  → needs_human
```

A `--auto` / autonomous mode lets SAFE solutions loop without prompting; code edits **always**
go through the approval gate unless `--yes` is set. `max_attempts` bounds the loop; on exhaustion → `needs_human`.

---

## 6. Agent logic + file memory + custom to-do

The agent is not just a runner — it has policy and persistent memory in the DB:

- **`ResolutionPolicy`** — the rule table above + LLM escalation; pluggable, learns from outcomes.
- **File memory** (`Memory/`): typed notes (decision / bug / risk / **failure** / todo) — reuses the
  `ast.md` Ext-III memory kinds. On every `succeeded`/`needs_human`, `Finish` writes what happened
  (a `FailureFact` on repeated errors, a `DecisionFact` on a chosen fix).
- **Custom to-do** (`Memory/todo.jsonl`): the agent appends its own follow-ups — e.g. after `analyse`
  it notices "no tests" → `AddTodoAsync(todo)`. `syncro memory list --todo` shows them; the agent can
  later promote a to-do into a `TaskRecord`.
- **Hindsight feedback:** before diagnosing a new failure, the agent queries past `failure`/`bug`
  memory + RAG (`RagEngine`) — "have we hit this `error_type` before? what fixed it?" → seeds the solution.

This closes the loop: **run → fail → recall hindsight → resolve (LLM if on) → record outcome → improve next time.**

---

## 7. Safety (centralised)

- **All writes via `SyncroDb`** (atomic temp+rename; `tasks.jsonl`/`audit.jsonl` append-only) → no torn state, crash-resumable.
- **LLM is opt-in & gated** (`LlmSafetyGate` from cli.md): context redaction, file allowlist, prompt-injection delimiters, timeouts. LLM output is **data**, never auto-run.
- **Auto vs approval:** only `ScriptRecord.Safe == true` re-runs auto-loop; **code edits / new LLM scripts require approval** (`needs_approval` → wait for `--yes`/UI).
- **Bounds:** `max_attempts`, terminal `needs_human`, per-step timeout + cancellation.
- **Audit:** every status change, script exec, and LLM call → `Agents/audit.jsonl` (timestamp, actor=cli|agent, files, llm-state).
- **Sandboxed exec:** scripts run in child processes via `ProcessRunner` with minimal env; elevation only for `install`.

---

## 8. Architecture improvement (before → after)

| Concern | Today | After |
|---|---|---|
| State | scattered: `AIClient`, `SyncroCLIService.CreateProject`, `ProjectGenerator` each write their own files | **one `SyncroDb` facade** |
| Agent↔CLI link | none / ad-hoc; named pipe only in docs | **DB-as-contract** (pipe = optional live notify) |
| Failure handling | manual; errors logged, not acted on | **loopable state machine** auto-resolves SAFE cases, escalates rest |
| LLM | blind POST to 3020, no probe/timeout | **gated, probed, optional**; degrades offline |
| Crash recovery | none | **event-sourced `tasks.jsonl`** → resume from last status |
| Memory | none persistent | **file memory + custom to-do + hindsight reuse** |

---

## 9. Module layout (new)

```
Services/AgentCli/
├── SyncroDb.cs                  # central facade (atomic, locked)
├── Dispatcher/
│   └── CliDispatcher.cs         # syncro <cmd> → ICliCommandV2 (+ global flags)
├── Commands/
│   ├── GitCloneCommand.cs       # syncro git clone
│   ├── AnalyseCommand.cs        # syncro analyse  (AST + tokenise)
│   ├── ReportCommand.cs         # syncro generate report (DB → PDF/JSON)
│   └── ShowProjectCommand.cs    # syncro show project (vector/token form)
├── Loop/
│   ├── TaskRecord.cs            # + TaskStatus / TaskIntent / ErrorType / Solution enums
│   ├── TaskLoopEngine.cs        # the reducer (1 status → 1 action)
│   ├── ErrorClassifier.cs       # stderr → error_type + source
│   ├── ResolutionPolicy.cs      # error_type → solution (+ LLM escalation)
│   └── ScriptRunnerAdapter.cs   # sandboxed exec via ProcessRunner
├── Memory/
│   ├── AgentMemoryStore.cs      # decision/bug/risk/failure notes
│   └── TodoStore.cs             # custom to-do
└── Models/
    ├── ScriptRecord.cs  ├── ErrorRecord.cs  ├── ProjectRecord.cs
    ├── VectorRecord.cs  ├── MemoryNote.cs   └── AuditEntry.cs
```
DI: register `SyncroDb`, `TaskLoopEngine`, `ResolutionPolicy`, `CliDispatcher`, the 4 commands, and
reuse `LlmGateway` + `RagEngine` + `LlmSafetyGate` from cli.md.

---

## 10. Build order

1. **`SyncroDb` facade** + record models + `tasks.jsonl` (atomic/append) — the central contract.
2. **4 commands** wired through `SyncroDb`: `git clone`, `analyse`, `generate report`, `show project`.
3. **`TaskLoopEngine`** + `ErrorClassifier` + `ResolutionPolicy` (deterministic rules only) — loop works **without** LLM.
4. **LLM escalation** (`llm_fix`) behind `LlmGateway` + approval gate — loop gets smarter when 3020 is up.
5. **Memory + to-do + hindsight reuse** — outcomes feed future resolutions.
6. **Named-Pipe live notify** (optional) — UI sees status changes instantly; DB remains source of truth.

---

## 11. Verification

- **PATH:** after `syncro install`, all four commands run from any directory.
- **Centralised:** grep shows only `SyncroDb` writes under `.syncro_db` (no command writes files directly).
- **Offline loop:** with 3020 down, enqueue a `run_script` task that fails with a missing-dep error →
  loop auto-runs the install script (SAFE) → re-runs → `succeeded`, all from deterministic rules.
- **LLM loop:** with 3020 up, a compile error → `diagnosing` → `llm_fix` proposes a patch →
  `needs_approval` (no `--yes`) → after `--yes`, applies, rebuilds, records outcome to memory.
- **Bounds:** force a persistent failure → loop stops at `max_attempts` → `needs_human` (no infinite loop).
- **Crash-resume:** kill mid-`running` → restart → engine resumes from `tasks.jsonl` last status.
- **show project:** `syncro show project --json` returns vector records + token stats straight from DB (no re-scan).
```
