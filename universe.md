# Universe — A Persistent Cross-Workspace Intelligence Layer

> The strategic reframe: stop thinking **one workspace + one AI chat** (VS Code / Cursor / Claude
> Code) and think **one Universe of many workspaces + one AI brain** that holds a *persistent*
> relationship graph and edits across workspaces only as far as a change's blast radius actually
> reaches. This document audits, honestly, how far the current codebase already is, and lays out the
> depth plan to get the rest of the way.

---

## 0. The honest headline

**~65% of the primitives already exist — but every one of them is (a) single-workspace-scoped and
(b) scan-on-demand.** The Universe is not a rewrite; it's three moves applied to what's built:

1. **Lift** the per-workspace graphs into one **persistent, cross-workspace graph store**.
2. **Event-drive** the indexers so the graph updates on file-change, not on prompt.
3. Put an **impact-traversal + retrieval brain** on top that queries the graph, decides scope
   (auto-mode level), and edits N workspaces through the batch-write actuator that already exists.

The single biggest missing thing is exactly the one you named: **a persistent Knowledge Graph above
all workspaces.** Everything else is plumbing to feed and consume it.

---

## 1. Capability audit — where each Universe layer stands today

| Universe layer | Status | What exists (real code) | Scope today | Gap to Universe |
|---|---|---|---|---|
| **Workspace Registry** | 🟡 Partial | `ProjectService` (flat `projects.json` in AppData), group `groups.json`, `IdeWorkspaceState` (single open folder) | Flat list, **no relationships** | Promote to a Universe registry: workspaces + org + cross-workspace edges |
| **File / Symbol Graph** | ✅ Exists | `Services/AST/` — `AstEngine`, `AstProjectMap`, `AstNode` (Class/Method/Interface/Route…), parsers for C#/TS/JS/Py | **Per project**, cached in `SyncroWorkspace/ast_cache/{key}.json` | Cross-workspace symbol resolution + persistent store |
| **Dependency Graph** | ✅ Exists | `Graph/DependencyGraph`, `CallGraph`, `DagBuilder` (QuikGraph — topo sort, cycles), `GraphExporter` | Per project | Inter-workspace package/module edges |
| **Service Graph** | 🟡 Partial | Connector: `ContractMatcher`, `Backend/FrontendContractResolver`, `PathNormalizer`, `ConnectorProjectStore` | **Pairwise** FE↔BE, per connector.json | N-way (see `orchestrator.md` G3) + persisted globally |
| **Knowledge Graph / Memory** | 🟡 Stub + vectors | `IKnowledgeEngine` → **`KnowledgeEngineStub`** (!), `HindsightVectorStore`/`QueryService`, AgentCli `MemoryNote` + `SyncroDb` (**LiteDB**) | Vectors per `.syncro_db`; stub does nothing | **This is the layer to build.** Anchor on LiteDB (already referenced) |
| **Runtime Graph** | ❌ Missing | Only `FlutterService` runs anything; `AgentMonitorServer` (port 3030 browser monitor); `PortAllocator` | — | `StackRunManager` (orchestrator.md G2) → live process/port/health nodes |
| **Build Graph** | ❌ Missing | `stacks.json` has `install`/`run`; nothing models build outputs/artifacts | — | Derive from registry + run manager |
| **Event Stream / Continuous Intelligence** | ❌ Missing | **No `FileSystemWatcher` anywhere.** All indexing is on-demand (`CreateProjectDialog` scans once; IDE scans on open) | — | The core new subsystem: watch → dirty-set → incremental re-index → graph delta |
| **Planner** | 🟡 Partial | AgentCli `TaskRouter` + `TaskLoopEngine` + `ResolutionPolicy`; `IProjectPlanner` (designed, safeupgrade P4); `OrchestrationPlanner` (orchestrator.md G6) | Single-task, single-project | Impact/risk/rollback planner over the graph |
| **Auto Mode (levels 0–4)** | ❌ Missing | Substrate exists: MCP batch write + `PathGuard` + agent loop | — | Level = blast-radius from graph traversal |
| **Smart Retrieval / Compression** | 🟡 Partial | `Engine/Context/ContextBuilder`, AST RAG indexer, `SmartContext` (orchestrator.md G4) | Per workspace | Graph-query → compact relationship digest |
| **The single AI brain** | 🟡 Partial | `ILLMProvider` (Groq), `McpOrchestrator` (12 servers, ~40 tools, now wired) | One session, one workspace context | One brain over the Universe graph |

**Reading the table:** the graph *content* (symbols, deps, services) is largely computed already —
it's just trapped per-workspace and recomputed each time. The three ❌ rows (Runtime, Event Stream,
Auto-Mode) plus lifting the 🟡 rows to global scope *are* the Universe.

---

## 2. Target architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              UNIVERSE                                      │
│                                                                            │
│  Workspace Registry ──┐                                                    │
│                       │        ┌──────────────────────────────────────┐   │
│  Event Stream ────────┼──────▶ │  KNOWLEDGE GRAPH  (LiteDB)            │   │
│  (FileWatchers per    │        │  nodes: Workspace, File, Symbol,      │   │
│   workspace)          │        │         Endpoint, Service, Package,   │   │
│                       │        │         Process, Env                  │   │
│  Indexers ────────────┘        │  edges: calls, exposes, imports,      │   │
│   (AST · Connector ·           │         depends_on, consumes,         │   │
│    Runtime · Package)          │         shares_auth, runs_on, …       │   │
│                                └──────────────┬───────────────────────┘   │
│                                               │ query (not scan)           │
│                    ┌──────────────────────────┼─────────────────────┐      │
│                    ▼                          ▼                     ▼      │
│         Impact Engine            Retrieval / Compression      Runtime      │
│         (blast radius →          (relevant edges → ~2k tok    Awareness    │
│          auto-mode level)         digest, not 500 files)      (health)     │
│                    └──────────────┬───────────────────────────┘            │
│                                   ▼                                        │
│                   Planner (task → impact → risk → plan → rollback)         │
│                                   ▼                                        │
│                   Single AI Brain  ── edits N workspaces via ──▶ MCP       │
│                                                                  batch     │
│                                                                  + PathGuard│
└──────────────────────────────────────────────────────────────────────────┘
```

**The rule that makes it different:** the LLM never *discovers* relationships per request. Indexers
keep the graph current via events; the brain **queries** the graph, gets a compact impact set, and
decides — file, project, workspace, connected-workspaces, or whole-universe.

---

## 3. The Knowledge Graph (the strategic missing layer) — concrete design

**Substrate:** LiteDB (already a dependency, already used by AgentCli's `SyncroDb`). One embedded DB
at the Universe root (`%AppData%/Syncro/universe.db`), not inside any workspace — that's what makes
it survive across sessions and span workspaces.

**Node schema** (one collection, typed):
```
Node { Id, Kind, WorkspaceId, Path?, Name, Language?, Signature?, Hash, Meta{}, UpdatedAt }
  Kind ∈ { Workspace, File, Symbol, Endpoint, ApiCall, Service, Package, Process, EnvVar, Migration }
```
**Edge schema:**
```
Edge { FromId, ToId, Kind, Confidence, Source, UpdatedAt }
  Kind ∈ { imports, calls (symbol→symbol), exposes (service→endpoint),
           consumes (apiCall→endpoint), depends_on (workspace→workspace),
           shares (workspace→package/dto/auth), runs_on (process→port), tests (test→symbol) }
```
- **Populated by existing indexers, not new scanners:** AST nodes → Symbol/File nodes + `imports`/`calls`;
  Connector resolvers → Endpoint/ApiCall nodes + `exposes`/`consumes`; the N-way mapper → `depends_on`;
  package manifests → Package + `shares`.
- **Cross-workspace edges** are the whole point: `consumes` from Workspace-A's `ApiCall` to
  Workspace-B's `Endpoint` (resolved via `PathNormalizer` + the wiring env `API_URL:port`) is what no
  IDE has.
- **Reverse index** (LiteDB index on `ToId`) gives O(log n) "who depends on X" — the primitive
  auto-mode and impact analysis are built on.
- Replaces `KnowledgeEngineStub` behind the existing `IKnowledgeEngine` interface — so the wiring
  seam is already there.

---

## 4. Event Stream — continuous, incremental (no rescanning per prompt)

The subsystem the codebase completely lacks today. Per registered workspace:

```
FileSystemWatcher (debounced ~300ms, respects SourceWalker skip-dirs)
   └─ changed paths → DirtySet
        └─ IncrementalIndexer:
             re-parse ONLY dirty files (AstEngine already works per-file)
             diff new symbols/edges vs graph
             apply node/edge upserts + deletes to LiteDB
             recompute edges that touched dirty nodes (calls, consumes)
             emit GraphDelta event
```
- **Hash-gated:** skip files whose content hash is unchanged (Node already stores `Hash`).
- **Cascade is bounded:** a changed endpoint re-evaluates only `consumes` edges pointing at it (via
  the reverse index), not the world.
- `GraphDelta` events feed the runtime/UI ("3 workspaces now affected by this change") and let the
  brain react without a full scan — the "event-based intelligence" you described, made concrete.

---

## 5. Auto-Mode levels = graph blast-radius, decided by the engine

Levels 0–4 are not a user toggle over guesswork; each is a **traversal depth** on the Knowledge
Graph. Given a change target (symbol/file/endpoint):

| Level | Traversal | Example: rename `Customer` |
|---|---|---|
| 0 File | the node only | rename in one file |
| 1 Project | intra-workspace `calls`/`imports` closure | + all refs in that repo |
| 2 Workspace | + tests, migrations, config in same workspace | + DTO, DB migration, unit tests |
| 3 Connected | follow cross-workspace `consumes`/`shares` edges 1–2 hops | + frontend API client, mobile model, SDK |
| 4 Universe | full transitive closure until fixpoint | + docs, swagger, deploy manifests, every consumer |

**Algorithm:** `ImpactEngine.Radius(target, maxLevel)` = BFS over reverse edges, tagging each reached
node with the hop-type; the planner picks `maxLevel` from the user's intent ("fix auth" → allow L4;
"tweak this function" → L1) and the **user approves the computed impact set before any write**. This
is the "the engine decides scope, not prompt engineering" property — and it's auditable because the
impact set is a concrete node list, not a vibe.

---

## 6. Runtime Graph & awareness

From `StackRunManager` (orchestrator.md G2) + existing monitors:
- Each running stack → a `Process` node with `runs_on`→`Port`, plus live `status` (health probe),
  cpu/mem (from the process handle), and tail-of-logs in `Meta`.
- Lets the brain answer "why is login slow?" by joining: `Endpoint(/login)` ← `consumes` ← frontend,
  `exposes` ← `Service(auth)` → `Process` → recent error logs — **a graph query, not a code search.**
- Browser/network signal already has a foothold in `AgentMonitorServer` (port 3030) — promote its
  events into `ApiCall` runtime nodes for real request-level awareness.

---

## 7. Retrieval & compression — what the LLM actually receives

The brain's prompt is assembled by `UniverseContextBuilder` (evolves `SmartContextBuilder`):
1. Resolve the intent's anchor nodes (fuzzy/vector match via existing Hindsight).
2. `ImpactEngine.Radius` → the relevant subgraph.
3. Serialize **edges, not files**, under a hard token budget: e.g. *"18 relationships, 12 endpoints,
   4 changed services, 7 dependent workspaces"* — deterministic, cache-friendly.
4. Only fetch file *bodies* for the handful of nodes actually being edited (via `ReadFile`), lazily.

This is the difference between shipping 500 files and shipping the 18 relationships that matter.

---

## 8. The single AI brain over the Universe

- **One chat, many workspaces.** The brain's context is the Universe graph, not an open folder. It
  edits across workspaces via the **already-built** `WriteFileBatchTool` (atomic, rollback) — each
  batch scoped per-workspace by `PathGuard`, so a Universe-wide change is N guarded batches in one
  transaction-of-transactions.
- **Planner upgrade** (over AgentCli's `TaskLoopEngine`): `task → impact analysis → risk score →
  workspace selection → execution plan → verification plan → rollback plan → execute`. Rollback plan
  = the pre-change snapshots the batch engine already captures, elevated to a Universe checkpoint.
- **Engine self-knowledge:** the `EngineManifest` (orchestrator.md G5) tells the brain what it is and
  what it can do; the Universe graph tells it what *exists*. Together: an LLM that plans against a
  known machine over a known ecosystem.

---

## 9. Depth roadmap — phased, each shippable, each grounded in existing code

| Phase | Deliverable | Built on | Proves |
|---|---|---|---|
| **U1** | **Universe Registry + `universe.db`** (LiteDB): register workspaces, org grouping; replace `KnowledgeEngineStub` with a real `IKnowledgeEngine` writing nodes | ProjectService, LiteDB, IKnowledgeEngine seam | Persistence above workspaces exists |
| **U2** | **Graph population from AST + Connector** on demand (`IndexWorkspace` MCP tool): symbols, imports, calls, endpoints, apiCalls into the graph | AstEngine, Connector resolvers, PathNormalizer | The graph holds real, queryable structure |
| **U3** | **Cross-workspace edge resolver** (`consumes`/`depends_on`) + reverse index + graph-query API | ContractMatcher, LiteDB indexes | "Who calls whom" across the whole Universe |
| **U4** | **Event Stream**: per-workspace watcher → incremental re-index → GraphDelta | AstEngine per-file, SourceWalker skip-dirs | Graph stays live without rescanning |
| **U5** | **ImpactEngine + Auto-Mode levels 0–4** with approval UI (impact set preview) | LiteDB reverse index, QuikGraph traversal | Scope decided by graph, not guesswork |
| **U6** | **UniverseContextBuilder** (compressed retrieval) + one-brain chat that reads the graph | ContextBuilder, Hindsight, ILLMProvider | LLM reasons over relationships, budgeted |
| **U7** | **Runtime Graph** (StackRunManager nodes) + runtime-aware queries | orchestrator.md G2, AgentMonitorServer | "Why is X slow" answerable from the graph |
| **U8** | **Impact/Risk/Rollback Planner** + Universe checkpoint (multi-workspace rollback) | AgentCli TaskLoopEngine, batch snapshots | Enterprise-trust: plan+verify+rollback |

Ordering logic: **persist → populate → connect → keep-live → traverse → retrieve → runtime → plan.**
Read-only graph value (U1–U3) ships before any autonomous multi-workspace write (U5+), matching the
safeupgrade discipline — prove the intelligence before you let it act at Universe scope.

Each of `orchestrator.md`'s G1–G6 slots in: the parallel scaffold/run and N-way mapper *feed* the
Universe graph; this doc is the persistent layer they were missing.

---

## 10. Can the current features get there? — verdict

**Yes, evolutionarily, not as a rewrite.** Concretely:
- The **hard parts are largely done**: multi-language AST + graphs (`Services/AST`), service matching
  (`Services/Connector`), an embedded graph-capable DB (LiteDB), a graph-algorithms lib (QuikGraph),
  the actuator (MCP batch write + PathGuard), an LLM provider, a task loop, and a vector store for
  fuzzy anchoring. That's most of a knowledge-graph platform's raw material.
- The **missing parts are well-bounded and additive**: one persistent graph store (U1–U3), one event
  subsystem (U4), one traversal engine (U5), one retrieval builder (U6), plus runtime (U7) and the
  planner upgrade (U8). None require touching the working IDE/generator/connector — they sit *above*.
- The **realistic caveat:** the depth is real. U4 (correct incremental invalidation) and U3
  (cross-language, cross-workspace symbol/endpoint resolution) are genuinely hard — false edges and
  stale nodes are the failure modes. Confidence-scored edges + hash-gated re-index + human-approved
  impact sets are the mitigations baked into the design above.

## 11. Hard problems to respect (so we don't oversell)

1. **Cross-language symbol identity** — matching a Dart model to a C# DTO to a TS interface is
   heuristic; keep it confidence-scored, never silently authoritative.
2. **Blast-radius precision** — L3/L4 traversal must avoid "everything depends on everything"; cap by
   edge kind + confidence + hop count, and always preview the set.
3. **Staleness vs cost** — full re-index is expensive; the whole event/hash design exists to make
   updates incremental. Watcher storms (git checkout, npm install) must be debounced/batched.
4. **Write safety at scale** — a Universe-level auto-edit is high blast radius; the checkpoint +
   per-workspace `PathGuard` batches + mandatory approval on L3+ are non-negotiable.

## 12. Competitive framing (one line)

VS Code edits files; Cursor edits a workspace; Claude Code agents over a repo; **Universe maintains a
continuously-updated model of an organization's entire software ecosystem and edits exactly as far as
a change actually reaches.**

## 13. Decisions to make before U1

1. **Universe DB location** — `%AppData%/Syncro/universe.db` (roams with user) vs a user-chosen
   "org root". Recommend AppData with an export/attach command.
2. **Graph population trigger** — index-on-register (eager, slower onboarding) vs index-on-first-open
   (lazy). Recommend lazy + background catch-up.
3. **Auto-mode default ceiling** — cap autonomous edits at L2 (workspace) by default, requiring
   explicit opt-in per prompt for L3/L4. Recommend yes — trust is earned upward.
