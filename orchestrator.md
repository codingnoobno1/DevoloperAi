# Smart Orchestrator — Parallel Develop · Parallel Run · N-Project Mapping · LLM Planner

> The evolution plan that fuses the two engines built so far — the **Generator engine**
> (`Services/projectgenerator/`) and the **Connector engine** (`Services/Connector/`) — into one
> smart orchestrator that scaffolds stacks **in parallel**, **auto-runs** them in parallel with
> health checks, maps **how every project calls every other**, and puts an **LLM planner on top
> that knows exactly what engines it commands** ("unique engine" self-knowledge) via compact
> structured context ("smart input tokens"), not raw code dumps.

---

## 1. Answering the question first: do we have a "project connector generator engine"?

**Yes — both halves exist and work, but they are separate and both are sequential/pairwise:**

| Engine | What it does today | Where |
|---|---|---|
| **Generator** | stacks.json registry → CLI or markdown-template scaffold, port allocation, FE→API env wiring | `ProjectGenerator`, `GroupOrchestrator`, `MarkdownTemplateEngine`, `PortAllocator`, `ProjectWiringService` |
| **Connector** | backend routes (swagger/scan) + frontend calls (extract) → match graph → AI-draft missing routes → atomic apply → connector.json | `BackendContractResolver`, `FrontendContractResolver`, `ContractMatcher`, `BackendGenerator`, `ConnectorProjectStore`, `ConnectorMcp` (6 MCP tools) |

**What the vision needs that neither has:**

| Vision requirement | Current reality | Gap |
|---|---|---|
| Develop projects **in parallel** | `GroupOrchestrator.OrchestrateAsync` is a sequential `foreach … await` ([GroupOrchestrator.cs:57](Services/projectgenerator/Orchestration/GroupOrchestrator.cs)) | **G1** — wave-parallel scaffolding |
| **Auto-run in parallel** | Nothing runs generated projects. `stacks.json` has `run:` commands and templates have `run:` front-matter, but no code executes them. Only `FlutterService` runs anything, and only Flutter | **G2** — a StackRunManager |
| **Mapping between multi-projects** | `ContractMatcher.Match(calls, routes)` is strictly **one FE ↔ one BE** | **G3** — N-way service graph |
| **LLM as smart input token** | `ContextBuilder` builds prompt context from workspace summary + hindsight; connector contracts are never fed to the LLM | **G4** — token-budgeted contract digests |
| **LLM must know the unique engine** | `McpOrchestrator` holds 12 servers / ~40 tools but exposes no self-description; the LLM is never told what Syncro itself can do | **G5** — EngineManifest |
| **LLM must plan** | `IProjectPlanner` was designed in safeupgrade.md Phase 4 but never built | **G6** — OrchestrationPlanner |

Everything below closes G1–G6 **by composing existing pieces**, not rebuilding them.

---

## 2. Target architecture

```
                       ┌─────────────────────────────────────────────┐
 user prompt ────────▶ │  OrchestrationPlanner (LLM)                  │
 "flutter app + node   │  input  = EngineManifest + SmartContext      │
  api + postgres,      │  output = OrchestrationPlan (strict JSON)    │
  run it all"          └───────────────┬─────────────────────────────┘
                                       │ plan (validated, human-approved)
                 ┌─────────────────────┼──────────────────────┐
                 ▼                     ▼                      ▼
   ┌──────────────────────┐ ┌───────────────────┐ ┌────────────────────────┐
   │ ParallelGroup        │ │ StackRunManager   │ │ MultiProjectMapper     │
   │ Orchestrator (G1)    │ │ (G2)              │ │ (G3)                   │
   │ waves: DB ∥ → BE ∥ → │ │ run cmds ∥, port  │ │ every member extracted │
   │ FE ∥  (Task.WhenAll) │ │ health, logs,     │ │ + resolved → service    │
   │                      │ │ stop/restart      │ │ graph in connector.json │
   └──────────┬───────────┘ └────────┬──────────┘ └───────────┬────────────┘
              │  reuses             │  reuses               │  reuses
   Generator engine        stacks.json run:,        Connector engine
   (scaffolders, ports,    PortAllocator,           (resolvers, matcher,
    wiring, templates)     CliWrap patterns          PathNormalizer)
                           from FlutterService
              └──────────────── all exposed as OrchestratorMcp tools ─────────┘
                                       ▲
                     SmartContext (G4) + EngineManifest (G5) feed back up
```

Loop closure: after a run, `MultiProjectMapper` re-maps, the mapping (missing edges!) goes back
into `SmartContext`, and the planner can propose the next action ("orders endpoint missing —
draft it?") — a develop → run → observe → plan cycle.

---

## 3. Components (all under `Services/Orchestrator/` — additive, nothing existing is modified except `GroupOrchestrator` gaining a parallel path)

### 3.1 G1 — `ParallelGroupOrchestrator`
Wave-based parallelism. Scaffolding members of one wave have **no inter-dependency** (ports are
pre-allocated up front — `PortAllocator` is already lock-protected and safe for this):

```
Wave 1: all Databases   — Task.WhenAll(mongo, postgres, …)
Wave 2: all Backends    — Task.WhenAll(fastapi, express, …)   (DATABASE_URL known)
Wave 3: all Frontends   — Task.WhenAll(next, flutter, …)      (API_BASE_URL known)
```

- Per-member log streams multiplex into the existing `onLog` with a `[stack:{id}]` prefix so the
  create-dialog terminal stays readable.
- Failure policy: a wave member failing does **not** cancel siblings; it's reported per-member and
  the group result lists `succeeded[] / failed[]` (fixes the old "partial failure reports success" bug B9).
- `ProjectWiringService` is extended to wire **backends too** (`DATABASE_URL` from wave-1 results —
  today it only wires frontends, see [ProjectWiringService.cs:13](Services/projectgenerator/Orchestration/ProjectWiringService.cs)).
- `GroupOrchestrator` keeps its sequential path; the parallel one is opt-in
  (`ProjectCreationRequest.Parallel = true`) until proven — safeupgrade discipline.

### 3.2 G2 — `StackRunManager` (auto-run in parallel)
Generalizes what `FlutterService.RunProject` does for Flutter to **every** stack:

```csharp
public interface IStackRunManager
{
    Task<RunHandle> StartAsync(RunSpec spec, CancellationToken ct);   // spec = path, command, port, env
    Task StopAsync(string handleId);
    IReadOnlyList<RunStatus> Snapshot();                              // per-stack: Starting|Healthy|Crashed|Stopped
    event Action<string, string> OnOutput;                            // (handleId, line)
}
```

- `RunSpec` comes straight from `stacks.json` `run:` / template front-matter `run:` with `{PORT}`
  substituted from the allocated port (already recorded at scaffold time).
- Processes via CliWrap with the **heartbeat + timeout pattern already proven in `CliScaffolder`**;
  kill-tree on stop so `npm run dev` children die too.
- **Health = TCP/HTTP probe on the assigned port** (reuse the polling idea from
  `MobilePreview.CheckUrl`), flipping status to `Healthy` when the port answers.
- Start order = same waves as scaffolding (DB before API before FE), but *within* a wave all start
  in parallel; `Task.WhenAll` on health gates the next wave.
- This is also what the Connector workbench's missing "live preview pane" plugs into later — the
  right-hand pane just iframes `http://localhost:{port}` of a `Healthy` frontend.

### 3.3 G3 — `MultiProjectMapper` (who calls whom, N-way)
Today's matcher is FE↔BE pairwise. The mapper generalizes:

1. For every group member, run **both** resolvers (`FrontendContractResolver` *and*
   `BackendContractResolver`) — a Next.js app has API routes of its own (`app/api/*`), an Express
   service both exposes routes *and* calls other services. Every member gets
   `{ calls[], routes[] }`.
2. Match every member's `calls[]` against **all other members' `routes[]`** (same
   `ContractMatcher` + `PathNormalizer`, run per ordered pair; disambiguate multi-candidate hits by
   port/env hints from wiring — the `.env.local` `API_URL=http://localhost:{port}` written at
   scaffold time tells us which backend a frontend was wired to).
3. Output a **ServiceGraph**: nodes = projects (stack, path, port, role), edges = classified call
   routes (`Matched/MethodMismatch/ShapeMismatch/Missing` — same enum), plus cross-project orphan
   routes.
4. Persist in `connector.json` v2: `members[]` + `graph.edges[]` (the store's current
   frontend/backend pair becomes the degenerate 2-member case; version bump keeps old files loadable).

### 3.4 G4 — `SmartContext` (LLM smart input tokens)
The LLM never reads raw source. It reads **budgeted digests** of what the engines already computed:

```json
{ "graph": { "members": [ {"id":"mobile","stack":"Flutter","port":null,"calls":12},
                          {"id":"server","stack":"Express","port":5000,"routes":9} ],
             "edges":  [ {"from":"mobile","verb":"POST","path":"/api/orders","to":null,"state":"Missing"} ] },
  "runs":   [ {"id":"server","status":"Healthy","port":5000} ],
  "issues": [ "mobile → POST /api/orders has no provider", "server exposes 3 orphan routes" ] }
```

- Hard token budget (default ~2k tokens): edges sorted `Missing > Mismatch > Matched`, matched
  edges collapse to counts first, paths truncated last. Deterministic serialization so prompts are
  cache-friendly and reproducible.
- Built by `SmartContextBuilder` sitting **beside** the existing `Engine/Context/ContextBuilder`
  (which stays for workspace/hindsight context; the planner prompt concatenates both).

### 3.5 G5 — `EngineManifest` (the LLM knows its unique engine)
A self-description the planner receives in its system prompt, generated **from live registries,
never hand-written** (so it can't drift):

- **Stacks**: from `StackRegistry.GetAllStacks()` — id, kind, ports, provides/needs, cli-or-template.
- **Templates**: from `TemplateLocator` — which archetypes have rich templates and which architectures (`flat/ntier/clean`) each supports (parsed from front-matter).
- **Tools**: from `McpOrchestrator` — add `IEnumerable<(string name, string description, string schema, bool requiresApproval)> DescribeTools()` (the registry dictionary already holds all of this; it's a 10-line addition).
- **Capabilities**: parallel scaffold, parallel run, mapping, AI route drafting, atomic batch write.

Rendered as a compact system-prompt block: *"You orchestrate Syncro. You can scaffold these 10
stacks (…), run them (…), map inter-project calls, and call these tools (…). You cannot do X."*
This is the difference between an LLM guessing and an LLM **planning against a known machine**.

### 3.6 G6 — `OrchestrationPlanner` (the LLM plans)
The `IProjectPlanner` from safeupgrade.md Phase 4, upgraded from "pick stacks" to "plan the whole
lifecycle":

```
input :  user prompt + EngineManifest + SmartContext (+ EnvironmentManager toolchain report)
output:  OrchestrationPlan (strict JSON, schema-validated like MobilePreview/BackendGenerator):
         { "summary": "...",
           "create":  [ {"stack":"flutter","folder":"mobile"}, {"stack":"express-node","folder":"server","architecture":"ntier"}, {"stack":"postgres","folder":"database"} ],
           "wire":    [ {"from":"mobile","needs":"API_BASE_URL","of":"server"} ],
           "run":     { "parallel": true, "waves": [["database"],["server"],["mobile"]] },
           "verify":  [ "map contracts", "report missing edges" ] }
```

- **Plan ≠ execute.** The plan renders as editable chips/steps (the safeupgrade "User Review
  Required" gate); on approval each step dispatches to the engines — `create` →
  `ParallelGroupOrchestrator`, `run` → `StackRunManager`, `verify` → `MultiProjectMapper`.
- Validation rejects plans referencing stacks/tools not in the manifest — the manifest is both the
  LLM's knowledge **and** the executor's allowlist.

### 3.7 MCP surface — `OrchestratorMcp` (13th server)
So the agent loop (and CliTalk / AgentCli) can drive all of it:

| Tool | Wraps | Writes? |
|---|---|---|
| `PlanOrchestration` | planner (returns plan JSON, executes nothing) | no |
| `ScaffoldGroupParallel` | ParallelGroupOrchestrator | **yes** |
| `StartStacks` / `StopStacks` / `RunStatus` | StackRunManager | yes / yes / no |
| `MapServiceGraph` | MultiProjectMapper (+ persists connector.json v2) | writes json only |
| `DescribeEngine` | EngineManifest | no |

---

## 4. Build order (each step shippable, verified, reversible)

| # | Step | Proves |
|---|---|---|
| 1 | `EngineManifest` + `McpOrchestrator.DescribeTools()` + `DescribeEngine` tool | LLM self-knowledge exists (also immediately useful in CliTalk) |
| 2 | `MultiProjectMapper` + connector.json v2 + `MapServiceGraph` tool | N-way "who calls whom" — pure read-only, zero risk |
| 3 | `SmartContextBuilder` (digests from #2 + run snapshots) | smart tokens, measurable prompt size |
| 4 | `StackRunManager` + workbench "Run all" panel (status chips per stack) | parallel auto-run with health |
| 5 | `ParallelGroupOrchestrator` behind `Parallel=true` + backend wiring fix | parallel develop; characterization harness re-run proves scaffold output unchanged (only timing) |
| 6 | `OrchestrationPlanner` + plan-approval UI + `OrchestratorMcp` complete | prompt → plan → parallel create → parallel run → mapped graph, end to end |

Order rationale: read-only intelligence first (1–3), process management next (4), then the
parallel write path (5), and the LLM conductor last (6) — the planner is only trustworthy once
the things it commands already work deterministically.

## 5. Open questions before step 1

1. **Run safety** — auto-starting stacks spawns real processes (npm/uvicorn/docker). Default to
   requiring explicit user click per run wave, or one approval for the whole plan? (Recommend: one
   approval per plan, stop-all always one click.)
2. **Docker** — DB stacks run via docker-compose; if Docker Desktop is absent, plan degrades to
   "scaffold but skip run" with a note, or hard-fail the wave? (Recommend: degrade + note.)
3. **connector.json v2** — migrate old pair-format files on load, or keep reading both formats? (Recommend: read both, write v2.)
