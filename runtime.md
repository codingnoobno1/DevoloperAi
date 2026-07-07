# Universe Runtime Engine — Engine-First, LLM-as-Consumer

> The inversion: today's flow is `User → LLM → Planner → Engine`. Flip it. A **continuously-running
> Runtime Engine** observes the whole ecosystem — processes, ports, APIs, tests, schemas, containers,
> dependencies — publishes everything to the live Universe graph, and drives the IDE dashboard
> **directly, with zero LLM tokens**. The LLM becomes *one consumer* that queries the graph, not the
> thing that discovers state. If the AI is offline, the IDE still knows Flutter is running on port
> 51234, the backend is healthy on 5000, 42 of 52 APIs are mapped, and SQL's schema is missing.

---

## 1. Why this is the missing piece (and it fits what's already built)

The Universe graph (U1/U2) is already a **persistent store that anything can write to**. Nothing says
only an indexer may write. The Runtime Engine is just *more writers* — background workers — plus a
scheduler, an event bus, and a dashboard read-model over the same graph. The LLM-facing retrieval
(universe.md U6) then reads runtime nodes for free.

This **subsumes and re-prioritizes** two things already planned: orchestrator.md **G2 (StackRunManager)**
and **G3 (mapper)**, and universe.md **U4 (event stream)** + **U7 (runtime graph)**. The user's insight
is correct: those shouldn't be late phases — the live engine is the spine.

---

## 2. Capability audit — which workers can we build from existing code *today*

| Worker | Status | Existing substrate | Net-new work |
|---|---|---|---|
| **Run / Process** | 🟡 Prototype exists | `FlutterService` (spawn, `OnOutput`, hot-reload) — but Flutter-only; `CliScaffolder` heartbeat/timeout pattern | Generalize to any stack via `stacks.json` `run:` → `StackRunManager` |
| **Health** | 🟡 Primitives exist | `EnvironmentManager.IsPortInUse`, `GetSystemResources`; `MobilePreview.CheckUrl` (HTTP probe) | Loop it per process → status/cpu/mem nodes |
| **API / Mapping** | ✅ Exists | `WorkspaceIndexer`, Connector resolvers, `ContractMatcher`, `PathNormalizer` | Run continuously; N-consumer mapping (G3) |
| **AST** | ✅ Exists | `AstService.ScanProjectAsync` | Trigger on file-change, not on prompt |
| **Dependency** | 🟡 Partial | `AstDependencyInfo` (declared deps), `AstNodeType.Import` (used imports), `EnvironmentManager` | Diff used-vs-declared → auto-install (see §7) |
| **Build** | ❌ Missing | `stacks.json` `install:`; CliWrap streaming | Run build cmd, parse success/errors |
| **Test** | ❌ Missing | CliWrap streaming | Run test cmd, parse pass/fail counts per framework |
| **Docker** | ❌ Missing | `mongo.md`/`postgres.md` emit compose; CliWrap | `docker compose ps/up`, container health |
| **Schema** | ❌ Missing | Connector swagger parse (analogous) | Introspect Mongo collections / SQL schema |
| **Git** | 🟡 Exists elsewhere | `GitAutomationService`, `GitCommandService` | Publish branch/dirty/ahead-behind as nodes |
| **Event Bus / Scheduler** | ❌ Missing | — | The genuinely new core |
| **Live graph** | ✅ Exists | `UniverseGraphStore` (LiteDB) | Add runtime node kinds + status |
| **Dashboard feed** | ❌ Missing | Blazor + `MobilePreview` tile patterns | Read-model + push to UI |

**Verdict:** ~half the workers are a thin wrapper over services that already exist; the new *core* is
small and well-bounded — a coordinator, a scheduler, an event bus, and runtime node kinds.

---

## 3. Architecture

```
                    UNIVERSE RUNTIME ENGINE  (RuntimeCoordinator — never sleeps)
                                     │
            ┌────────────────────────┼────────────────────────┐
            ▼                        ▼                         ▼
     Worker Scheduler          Event Bus              Health Monitor
     (tiers: continuous /   (workers publish       (port probes, cpu/mem,
      on-change / on-demand)  RuntimeEvent)          container status)
            │                        │
   ┌────────┴─────── parallel workers (Task.Run, no LLM) ───────┴────────┐
   │  Run · Health · ApiMap · AST · Dependency · Build · Test · Docker · │
   │  Schema · Git                                                        │
   └────────────────────────────┬───────────────────────────────────────┘
                                 ▼
                        Graph Updater  ──▶  UniverseGraphStore (LiteDB)
                                 │            + Process/Test/Container/Metric nodes
                 ┌───────────────┴───────────────┐
                 ▼                               ▼
          Dashboard Feed                    LLM / MCP / Planner
          (live tiles, 0 tokens)            (query the graph — a CONSUMER)
```

The rule: **workers write facts; the graph holds them; consumers read.** The LLM never blocks the
dashboard, and the dashboard never waits for the LLM.

---

## 4. Event Bus + runtime nodes in the graph

New `NodeKind`s (extend the enum built in U1): `Process`, `Container`, `TestRun`, `Metric`,
`SchemaObject`, `GitState`. New `EdgeKind`s: `RunsOn` (process→port), `Affects` (dependency→consumers),
`Covers` (testRun→symbol).

```csharp
public sealed record RuntimeEvent(string WorkerId, string WorkspaceId, RuntimeEventKind Kind,
                                  string TargetNodeId, IReadOnlyDictionary<string,string> Data);
// Kinds: ProcessStarted, ProcessHealthy, ProcessCrashed, PortOpened, ApiDiscovered,
//        MappingChanged, TestCompleted, BuildCompleted, ContainerStatus, SchemaChanged,
//        DependencyMissing, DependencyInstalled, GitChanged
```
`IEventBus` (in-proc `Channel<RuntimeEvent>`): workers `Publish`; the **Graph Updater** subscribes and
upserts nodes/edges (status in `Meta`); the **Dashboard Feed** subscribes and pushes to Blazor via an
`IObservable`/C# event. One event, two consumers, both cheap.

---

## 5. Worker Scheduler — CPU is the constraint, not correctness

Three tiers keep a live view without melting the machine:

| Tier | Cadence | Workers |
|---|---|---|
| **Continuous** | always-on loops | Run, Health (health probes ~2–5s, metrics ~5s) |
| **On-change** | debounced FileSystemWatcher (universe.md U4) | AST, ApiMap, Dependency, Schema, Git |
| **On-demand / periodic** | user click or slow interval | Build, Test, Docker refresh |

- A single `FileSystemWatcher` per workspace feeds a debounced dirty-set; on-change workers consume it
  (no rescanning per prompt). Watcher storms (git checkout, `npm install`) are batched.
- The scheduler enforces a **concurrency cap** (`min(cores-2, N)`, same discipline as the workflow
  runner) so a 5-workspace universe doesn't spawn 60 processes.
- Everything cancellable via linked `CancellationToken`s (the CliScaffolder timeout pattern).

---

## 6. The dashboard — a read-model over the graph (zero tokens)

The tiles you described are a **projection**, computed by `DashboardProjection.Build()` from graph
queries — no LLM, no scan:

```
Flutter   🟢 Running · port 51234 · hot-reload         ← Process node status + RunsOn edge
Backend   🟢 Running · port 5000 · cpu 12% · 210 MB    ← Process + Metric nodes
API       52 endpoints · 42 mapped · 10 missing         ← count Endpoint nodes, Consumes edges
Flutter   40 calls · 30 mapped · 10 missing             ← ApiCall nodes (ws) vs Consumes
Next.js   18 calls · 15 mapped · 3 missing
Mongo     🟢 connected · 12 collections                 ← SchemaObject nodes
SQL       ⚠ schema missing                              ← Schema worker found DB, no schema
Docker    🟢 4 containers                               ← Container nodes
Tests     unit 148/150 · integration 22/25              ← TestRun nodes
```

Because it's a projection over persisted nodes, it's **correct even before the LLM is ever invoked**,
and it survives restarts (last-known state is in LiteDB).

---

## 7. Pre-scripts & AST-driven dependency install (the "don't waste LLM tokens" asks)

Two deterministic subsystems that the LLM should **never** spend tokens driving:

### 7a. `EnvironmentBootstrapper` (pre-scripts)
Runs the boring, deterministic setup — `npm install`, `pip -m venv && pip install -r`, `flutter pub
get`, `dotnet restore` — from the stack's own `install:` command (already in `stacks.json` and template
front-matter). Triggered by the Run worker *before* start, or on manifest change. Reuses
`EnvironmentManager.ExecuteSetupScript` + the CliScaffolder heartbeat. **No LLM, ever** — these are
lookups, not decisions.

### 7b. `DependencyReconciler` (AST → env)
The `import cv2` / `import numpy` / `import 'lucide-react'` ask, done safely:

```
1. AST Import nodes  →  used modules (cv2, numpy, sklearn, PIL, lodash, lucide-react, @mui/material)
2. Map module → package via a CURATED alias table (NOT the raw import string):
     cv2→opencv-python, sklearn→scikit-learn, PIL→pillow, bs4→beautifulsoup4,
     yaml→pyyaml, lodash→lodash, "lucide-react"→lucide-react, "@mui/material"→@mui/material …
3. Diff mapped packages against DECLARED (AstDependencyInfo) + INSTALLED (node_modules / venv freeze)
4. For each genuinely-missing package: verify it EXISTS in the registry (npm view / pip index),
   then install via the stack's package manager, updating the lockfile.
```
**Safety is non-negotiable — this is a supply-chain surface:**
- Install **only** packages resolved through the curated alias map or an exact declared name — *never*
  an arbitrary parsed import string (typosquatting / hallucination risk).
- **Registry-existence check** before install; skip unknowns and report them instead.
- **Approval gate by default** (auto-install opt-in per workspace); always lockfile-aware.
- Ambiguous aliases (`import api` — local? package?) are reported, not installed.

Result: opening a Python project that `import numpy` with no `requirements.txt` entry → the Reconciler
flags it, and (if approved) installs `numpy` + writes it to requirements.txt — with the LLM spending
**zero tokens**.

---

## 8. The LLM as consumer (what changes for the AI)

- "Why is login failing?" → the brain runs **one graph query**, not a repo scan: `Endpoint(/login)` is
  500, `Service(auth)` `RunsOn` 5000 🟢, but `depends_on` `Mongo` 🔴 offline, `TestRun` shows 2 failed,
  `SchemaObject` missing index. The answer is assembled from runtime nodes the workers already wrote.
- Token cost of "what's happening" drops to ~0 (it's the dashboard projection, already computed).
- The planner (orchestrator.md G6) gets a *live* world model to plan against, and can dispatch
  deterministic actions (run/build/test) to workers instead of narrating shell commands.

---

## 9. Upgrade roadmap (each phase shippable, engine runs without LLM at every step)

| Phase | Deliverable | Built on | Visible result |
|---|---|---|---|
| **R1** | `IEventBus` + `RuntimeCoordinator` + runtime `NodeKind`s + Graph Updater | UniverseGraphStore | events flow into the graph |
| **R2** | `StackRunManager` (Run worker) + Health worker (port/cpu/mem) | FlutterService, EnvironmentManager, stacks.json `run:` | dashboard shows processes 🟢/🔴 live |
| **R3** | Dashboard Feed + Blazor **Runtime Dashboard** page (tiles) | Blazor, MobilePreview patterns | the whole picture, 0 tokens |
| **R4** | `EnvironmentBootstrapper` (pre-scripts) wired before Run | EnvironmentManager, stacks.json `install:` | auto `npm install` / `pip install`, no LLM |
| **R5** | Continuous ApiMap + N-consumer mapping (G3) into graph | WorkspaceIndexer, ContractMatcher | live 52/42/10 API tiles + per-endpoint consumers |
| **R6** | `DependencyReconciler` (curated alias map + registry check + approval) | AST Import nodes, AstDependencyInfo | missing-lib detection + gated auto-install |
| **R7** | Test + Build workers (framework-aware parse) | CliWrap | live pass/fail tiles |
| **R8** | Docker + Schema workers | compose templates, DB drivers | container health + schema-missing warnings |
| **R9** | Worker Scheduler tiers + FileSystemWatcher (U4) unifying on-change | — | CPU-bounded, incremental, always-live |

Ordering: **bus → run+health → dashboard → pre-scripts → mapping → deps → test/build → docker/schema →
scheduler.** The dashboard is visible by R3; each later worker just adds tiles.

---

## 10. Can the current features get here? — verdict

**Yes, and faster than the Universe brain**, because observability is deterministic — no model quality
to tune. The Run/Health/ApiMap/AST/Dependency workers are wrappers over `FlutterService`,
`EnvironmentManager`, `WorkspaceIndexer`, and `AstService`; the graph store is already the live sink.
The genuinely new core (event bus + coordinator + scheduler) is a few hundred lines. Build/Test/Docker/
Schema are the real net-new effort, and they're independent workers that can land one at a time.

## 11. Hard problems / risks to respect

1. **Process lifecycle & kill-tree** — `npm run dev` spawns children; stop must kill the tree, or ports
   leak. Cross-platform (Windows job objects vs POSIX process groups).
2. **Auto-install supply-chain risk** (§7b) — the single most dangerous feature here; the curated map +
   registry check + approval gate are mandatory, not optional.
3. **Test/build output parsing** — every framework prints differently (jest vs pytest vs dotnet test);
   parsers are per-framework and brittle. Start with exit-code + last-line, refine per stack.
4. **CPU / battery** — always-on workers must respect the scheduler tiers and back off on
   battery/low-resource (use `GetSystemResources`).
5. **Watcher storms** — `git checkout` / `npm install` touch thousands of files; debounce + ignore
   generated dirs (SourceWalker skip-set already exists).
6. **Health false positives** — a port being open ≠ the app being ready; combine TCP + HTTP-200 probe.

## 12. Decisions before R1

1. **Event bus scope** — in-proc `Channel<T>` (simple, this app) vs a durable log (survives restart,
   enables the CLI/other processes to consume). Recommend in-proc now, durable later.
2. **Auto-install default** — off (detect + report) vs on (install on approval) vs fully auto.
   Recommend **detect+report by default, install on explicit approval** — never silent.
3. **Dashboard as a page vs a docked panel** — a `/runtime` page (like `/connector`) vs a persistent
   status strip in `MainLayout`. Recommend the page first, strip later.
