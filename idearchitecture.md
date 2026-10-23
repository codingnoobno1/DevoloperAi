# idearchitecture.md — Syncro Target Architecture (Code-OSS-first, Workspace-centric)

> Decision record + engine catalog + ownership map + migration plan.
> v2 — decomposes the Workspace facade into focused engines and adds the missing layers
> (Build, Dependency, Contract, Knowledge Graph, Verification, Domain, Cross-Language, Parallel AI).
> Companion: [idegap.md](idegap.md) (gap inventory).

---

## Decision 0 — One editor: Code-OSS is primary. Monaco is a viewer. *(unchanged)*

VS Code OSS (`vscode-main`, compiled, product `code-oss-dev` v1.128.0) is **the** editor, driven by
`CodeOssIdeHost` + the `syncro-bridge` extension. Monaco is demoted to viewer/diff/markdown/logs +
offline fallback. **Phase 0 applied:** `IdeHostOptions.Kind` defaults to `CodeOss`; the shell
auto-falls back to Monaco when Code-OSS is unavailable.

## Decision 1 — The Workspace Engine is a **thin orchestrator**, not a facade.

The v1 draft made `IWorkspaceEngine` own the graph, index, language, AI, git and diagnostics — a God
Object. Corrected: **`WorkspaceOrchestrator` owns only workspace *lifecycle* and an *event bus*.** All
capability lives in focused, independently-testable engines it wires together. Nothing calls another
engine's internals; they communicate through the orchestrator's events.

```
                      WorkspaceOrchestrator
                (lifecycle + EventBus + registry — NO logic)
                              │  emits/routes events
   ┌──────────┬──────────┬───┴──────┬──────────┬──────────┐
   ▼          ▼          ▼          ▼          ▼          ▼
 Project    Language    Build     Search    Dependency  Index
 Graph      Engine      Engine    Engine    Engine      Engine
   │          │                                │          │
   ▼          ▼                                ▼          ▼
 Knowledge   AST/LSP                        Contract    AST Cache
 Graph       Adapters                        Engine
   └───────────────┬───────────────┬───────────┘
                   ▼               ▼
             Context Engine   Diagnostic Engine (Problems Hub)
                   └──────┬────────┘
                          ▼
                   AI Orchestrator
          ┌───────────────┼───────────────┐
          ▼               ▼               ▼
   Backend Agent    Flutter Agent    Git/Test/Review Agents
          └───────────────┼───────────────┘
                          ▼
                 Verification Engine  (compile · test · analyze · AST-validate)
                          ▼
                   Code-OSS + MCP (event bus)
```

**The rule holds:** every engine depends on the **orchestrator's events + the graph**, never on the
editor. Swapping Monaco↔Code-OSS touches nothing below the UI line.

---

## Engine catalog — responsibility, status, seed

Status: 🟢 exists (reuse) · 🟡 partial (extend) · 🔴 missing (build). "Seed" = the code to grow from.

| Engine | Single responsibility | Status | Seed / where |
|---|---|---|---|
| **WorkspaceOrchestrator** | Open/close workspace, hold engine registry, own the event bus. No domain logic. | 🔴 | new; wraps `IdeWorkspaceState` + `DomainDetector` |
| **EventBus** | Typed workspace events (`WorkspaceOpened`, `FileChanged`, `Indexed`, `LspReady`, `BuildDone`, `ContractDrift`…). | 🔴 | new (see Decision 2) |
| **ProjectGraph** | Workspace→solution→projects→modules topology. | 🟡 | `UniverseGraphStore`, `WorkspaceDiscoveryService`, `MultiProjectScanService` |
| **RepositoryIntelligence** | Repo topology: monorepo/microservices/Flutter/backend/docker/CI/infra/db. | 🟡 | `DomainDetector`, `WorkspaceDiscoveryService` (extend to infra/docker/CI) |
| **KnowledgeGraph** | Semantic nodes (Project/Class/Method/Interface/API/DTO/DB/Screen/Bloc) + edges + embeddings. | 🟡 | `UniverseGraphStore` (nodes/edges) + `HindsightVectorStore` (embeddings) — **unify these two** |
| **IndexEngine** | Incremental, watcher-driven index feeding search/diag/AI/nav. | 🟡 | `WorkspaceIndexer`, `AstCacheManager` (add FS watcher, Decision below) |
| **LanguageEngine** | Editor language intelligence. **Code-OSS LSP is primary** for all languages. | 🟡 | Code-OSS LSP; Roslyn/AST become engine-side providers over MCP |
| **LanguageAdapters** | Language-independent AST behind one interface. | 🟡 | `IAstParser` (C#/JS/TS/Py); **add Tree-sitter** for the rest, **Dart via Thunder** (Decision 4) |
| **BuildEngine** | `IBuildEngine` over dotnet/flutter/npm/python/rust — incremental, structured output. | 🟢 | `Services/Build/` — `BuildEngine` + `BuildOutputParser`, emits `BuildDone` |
| **DependencyEngine** | package→version→license→security→build per project. | 🟡 | `DependencyAnalyzer`, `DependencyGraph` (add version/license/CVE via `SecurityScanner`) |
| **ContractEngine** | Backend DTO ↔ OpenAPI ↔ Frontend DTO ↔ Repository ↔ Bloc, with **drift verification** + codegen. | 🟡 | `ContractMatcher`, `IBackend/IFrontendContractResolver`, `MapContractsTool`, `ThunderScanFrontendResolver` (Decision 3) |
| **SearchEngine** | text/regex + symbol/type/method/refs/impls/call-graph/dep/arch/route/widget. | 🟡 | AST `FindClasses/FindFunctions/FindImports` tools (broaden over the graph) |
| **DiagnosticEngine (Problems Hub)** | Unify Roslyn + LSP + build + analyzer + git + security + AI into one stream. | 🟢 | `Services/Workspace/Diagnostics/` — `IProblemsHub` + `DiagnosticsSubscriber` |
| **ContextEngine** | **Semantic** context packs: symbols/deps/types/tests/errors/imports/usage/arch — never raw files. | 🟡 | `ContextPackBuilder` + `HeuristicTokenCounter` (re-source from KnowledgeGraph, Decision 5) |
| **CrossLanguageMapper** | `ProductController` → `ProductRepository`/`ProductBloc`/`ProductState`/`ProductScreen`. | 🟢 | `Services/Universe/Indexing/CrossLanguageMapper.cs` — concept clusters + `Mirrors` edges |
| **FlutterEngine / FeatureMapper** | AI feature → Thunder → Flutter+Bloc+Repository+DTO wired to backend API. | 🔴 | `thunder` reverse-AST exists; **generation is still vanilla `flutter create`** (Decision 6) |
| **DomainEngine** | Reusable domain models (ERP/CRM/Marketplace/Hospital/Finance/Education). | 🔴 | none |
| **VerificationEngine** | patch→compile→test→static-analysis→AST-validate→apply→undo. Nothing applies unverified. | 🟡 | `Services/Verification/` — compile **+ test** gated, auto-rollback; `ApplyPatch` delegates to it. Static-analysis/AST stages still missing |
| **AI Orchestrator + agents** | Planner routes to specialized Backend/Flutter/Git/Test/Review agents in parallel. | 🔴 | single `RunAgentLoopAsync`; `GitCopilotService` (one loop) |
| **MCP Layer** | Capability discovery + planning + approval + progress + retry + verify — the event/orchestration substrate. | 🟡 | `McpOrchestrator`, `McpHttpEndpoint` (today: RPC only, Decision 7) |

> Headline: **~13 of 19 engines already have working seeds.** The work is decomposition + wiring +
> filling 4 genuinely-missing engines (Build, Domain, Verification, parallel-AI), not a rewrite.

---

## Key decisions (the "better way" for each of your 15 points)

**Decision 2 — Build & events are infrastructure, not logic.**
`IBuildEngine.BuildAsync(project) → {ok, diagnostics[]}` for .NET/Flutter/Node/Python/Rust. Emits
`BuildDone` on the EventBus. Diagnostics, AI-repair, CI generation, and incremental builds all consume
it instead of shelling out. (Your #2, #8.)

**Decision 3 — Contract Engine is first-class and *verifying*.** (Your #7 — biggest omission.)
Matching already exists (`ContractMatcher`). Promote it: on `BuildDone`/`Indexed`, re-resolve backend
DTOs+routes and frontend calls+DTOs, diff them, and emit **`ContractDrift`** when `UserDto` changes but
the Flutter side still compiles against the old shape. Add codegen (backend DTO → OpenAPI → Dart DTO →
repository → Bloc scaffolding). This makes Syncro *contract-centric*.

**Decision 4 — AST is language-independent.** (Your #4.)
One `ILanguageAdapter { Parse, Symbols, Definition, References }`. Roslyn adapter (C#), Tree-sitter
adapter (JS/TS/Py/Go/Rust/…), Thunder adapter (Dart). No feature ever branches `if csharp / if dart`.

**Decision 5 — Context is semantic, from the Knowledge Graph.** (Your #5, #6.)
`ContextEngine` emits symbols/types/deps/tests/errors/imports/usage/architecture pulled from the
**KnowledgeGraph** (unified `UniverseGraphStore` + embeddings), not file blobs. `@syncro` and the AI
agents consume packs, never `File.ReadAllText`.

**Decision 6 — Thunder is the default Flutter generation path.** (Your #11, P0.)
`FlutterEngine`: AI feature spec → `FeatureMapper` → Thunder templates → Flutter project with Bloc +
Repository + DTO already wired to the backend API (via ContractEngine). Replace the vanilla
`flutter create` in `FlutterProvider` as the default; keep vanilla as an escape hatch.

**Decision 7 — MCP becomes an orchestration/event substrate.** (Your #8, #15.)
`Event → Planner → Executor → Observer → Verifier`. Add capability discovery, the in-app **approval
flow** (unblocks write/exec tools), progress, retry, and a verify pass. Move the `RequiresAdminApproval`
gate into `McpOrchestrator`. Open-project becomes an event chain: `Opened → Indexed → LspReady →
ContractsResolved → AIReady → GitReady → TestsReady`; every engine subscribes.

**Decision 8 — AI is a team, gated by verification.** (Your #12, #15.)
An `AIOrchestrator` planner fans work to specialized agents (Backend/Flutter/Git/Test/Review) that run
in parallel, then **everything** routes through `VerificationEngine` (compile+test+analyze+AST-validate)
before `PatchMcp` applies. No direct `Write File`.

---

## Ownership map (the five questions — refined)

| Question | Owner | Not |
|---|---|---|
| Workspace **lifecycle/state** | `WorkspaceOrchestrator` | not any engine, not the editor |
| **Semantic model** | `KnowledgeGraph` (+ `ProjectGraph`) | not the file tree |
| **Language intelligence** | `LanguageEngine` (Code-OSS LSP) + `LanguageAdapters` | not Roslyn-in-Monaco |
| **Build** | `BuildEngine` | not scattered `Process.Start("dotnet"…)` |
| **Contracts (FE↔BE)** | `ContractEngine` | not implicit/at-runtime |
| **AI context** | `ContextEngine` (from KnowledgeGraph) | not raw files |
| **Editor state** | active `IIdeHost` (Code-OSS) | Syncro mirrors nothing |
| **Verification** | `VerificationEngine` | not "trust the LLM" |
| **Indexing** | `IndexEngine` (watcher-driven) | not per-request rescans |

---

## Revised roadmap (maps your priority table)

### Phase 0 — Editor pivot ✅ *(done)*
Code-OSS default + Monaco auto-fallback.

### Phase 1 — Orchestrator + EventBus (thin) — **P0**  *(kernel built ✅)*
`WorkspaceOrchestrator` + typed `EventBus`; register the existing engines; emit
`WorkspaceOpened/FileChanged`. **No God Object** — orchestrator holds only lifecycle + events.
*Delivers your "split IWorkspaceEngine" P0.*
**Built:** `Services/Workspace/` — `IEventBus`/`InMemoryEventBus` (typed pub/sub, subscriber-isolated,
unit-verified), `WorkspaceEvents` (Opened/Closed/FileChanged/Indexed/**ContractDriftDetected**),
`IWorkspaceOrchestrator`/`WorkspaceOrchestrator` (lifecycle only), `AddSyncroWorkspace()` DI.
**Contract Engine now publishes `ContractDriftDetected`** on breaking drift (optional bus, so it stays
standalone). **Remaining:** call `AddSyncroWorkspace()` in MauiProgram; have IdeShell drive
`orchestrator.Open()`; subscribe the Problems Hub + Indexer.

### Phase 2 — Watcher-driven IndexEngine + Problems Hub — **P1/P2**  *(complete ✅)*
FS watcher → incremental `AstCacheManager`/`WorkspaceIndexer` → `Indexed` events. DiagnosticEngine
aggregates Roslyn+LSP+security into one Problems Hub. *(your #6, #8, #9 foundation.)*
**Built:** `Services/Workspace/Watch/` — `WorkspacePathFilter`, `ChangeCoalescer`,
`FileSystemWorkspaceWatcher`/`IWorkspaceWatcher`; `ContractReverifySubscriber` (FileChanged → debounced
→ ContractEngine.VerifyAsync); **`IndexEngineSubscriber` ✅** (`Services/Workspace/Index/`) — subscribes
to `WorkspaceOpened`/`FileChanged`, 2-s debounce per workspace, drives `WorkspaceIndexer.IndexAsync`,
publishes `WorkspaceIndexed`. AstCacheManager's SHA-256 signature handles cache-invalidation
automatically on any file mtime change. Registered in `AddSyncroWorkspace()`, resolved at startup.
**Problems Hub ✅** (`Services/Workspace/Diagnostics/`) — `Problem`/`ProblemSeverity`/`ProblemSource`
(Language·Security·Contract·Build·Agent), `IProblemsHub`/`ProblemsHub` (bucketed by source+scope so one
producer refreshing never drops another's findings; `Changed` event for the UI), `DiagnosticsSubscriber`
(`WorkspaceIndexed` → SAST scan → Security problems; `ContractDriftDetected` → Contract problems;
`WorkspaceClosed` → clears). IdeShell's PROBLEMS panel + status bar now render the unified stream and
push Roslyn diagnostics in per-document.

### Phase 3 — Contract Engine + Cross-Language Mapper — **P0**  *(complete ✅)*
Promote `ContractMatcher` to a verifying `ContractEngine` emitting `ContractDrift`; add codegen and the
`Controller→Repository→Bloc→Screen` mapper over the KnowledgeGraph. *(your #7, #14.)*
**Built:** `Services/Connector/Contract/` — `IContractEngine`/`ContractEngine`, `ContractDiffer`,
`ContractSnapshotStore`, MCP `VerifyContract` tool; **`DartBlocCodegen` ✅** + **`ContractDriftCodegenSubscriber` ✅**
(`Services/Connector/Codegen/`) — `ContractDriftDetected` → locate FE/BE → resolve `BackendContract` →
generate 4 Dart files per feature (`*_dto.dart`, `*_repository.dart`, `*_state.dart`, `*_bloc.dart`)
under `{frontendRoot}/lib/features/{feature}/`. Registered in `AddSyncroConnector()`.
**Cross-Language Mapper ✅** (`Services/Universe/Indexing/CrossLanguageMapper.cs`) — clusters graph
Symbol nodes by *concept* (name minus architectural suffix: Controller·Service·Repository·Bloc·Cubit·
State·Event·Screen·Page·View·Widget·Dto·Model·Entity·ViewModel, longest-suffix-first, `I`-prefix and
`_`-prefix aware, **24/24 unit-verified**), requiring ≥2 distinct roles per cluster, and writes `EdgeKind.Mirrors` edges
(confidence 0.9 same-language / 0.75 cross-language). Runs from `IndexEngineSubscriber` immediately
after each index pass. Gives navigation/AI/impact-analysis one language-independent "the Product
feature" notion. Registered in `AddSyncroUniverse()`.

### Phase 4 — BuildEngine + VerificationEngine — **P1**  *(both built ✅)*
`IBuildEngine` over all stacks; `VerificationEngine` gates every AI patch (compile+test+analyze) before
`PatchMcp` applies. *(your #2, #12.)*
**Built:** `Services/Build/` — `IBuildEngine`/`BuildEngine` (marker-file stack detection: pubspec→Flutter,
sln/csproj→DotNet, package.json→Node, requirements/pyproject→Python; runs `dotnet build` / `flutter analyze`
/ `tsc --noEmit` or `npm run build` / `compileall`; 5-min timeout, event-driven stream reads so a full pipe
buffer can't deadlock; publishes `BuildDone`), `BuildOutputParser` (MSBuild·tsc·dart-analyze·flake8·py-traceback
→ structured `BuildDiagnostic`, per-TFM de-dup, **19/19 unit-verified against real captured output**).
`Services/Verification/` — `IVerificationEngine`/`VerificationEngine`: structural gate (anchor found?
**unique?** actually changes anything?) → baseline build → apply → rebuild → **roll back on any newly-introduced
error**. Errors are compared by `file|code|message` signature *excluding line numbers*, since patching shifts
lines and would otherwise flag every surviving downstream error as "introduced". No build stack → structural
pass only, and the result says so. MCP `BuildProject` + `VerifiedPatch` (`BuildMcp`) put both on the agent's
path; `VerifiedPatch` is described to the model as preferred over raw `ApplyPatch`.
**Tests are now in the gate.** `Services/Build/ITestEngine`/`TestEngine` runs `dotnet test` /
`flutter test -r expanded` / `npm test` / `pytest -q`, gated by `HasTests()` so projects without a suite
pay nothing. `TestOutputParser` extracts failing test *names* (not counts, so a patch that fixes one test
and breaks another is still caught) — **27/27 unit-verified** across VSTest, xunit, flutter, jest and pytest
formats. The baseline test run is **lazy**: if the patched suite is green there is nothing to attribute, so
the common case costs one test run; only a red suite pays for the second (restore → re-run → diff) to
separate "this patch broke it" from "already broken". Skips are never reported as passes —
`TestsSkippedReason` says why, including the ambiguous case where the suite was already red *and* names
could not be parsed.

**Raw `ApplyPatch` is retired.** Rather than removing the tool the model is trained to call,
`ApplyPatchTool` now *delegates* to `IVerificationEngine`, so the guardrail holds regardless of which tool
name the agent picks. This also fixed a latent hazard: the old implementation used `string.Replace`, which
silently patched **every** occurrence of the search target; the engine rejects a non-unique anchor instead.
`RunTests` is exposed as a standalone MCP tool for checking the suite without patching.

**UI:** `Components/Pages/Build/BuildVerify.razor` at `/build` (sidebar → DEVELOPMENT → Build & Verify) —
project picker with live stack + suite detection, Build / Run tests / Build+test, diagnostics table grouped
by file, failing-test list, raw output tab, and a "Verify a patch" panel that drives the real gate.
That panel **defaults to a dry run**; writing to disk is an explicit opt-in toggle, since it can overwrite
source. Stack detection runs on a background thread — `HasTests()` walks the filesystem and doing it inline
stuttered the debounced text field on every keystroke.
**Remaining:** static-analysis and AST-validate stages of Decision 8 are still not in the gate.

### Phase 5 — Flutter/Thunder generation + KnowledgeGraph unification — **P0/P1**
Thunder becomes the default generation path via `FeatureMapper`; unify `UniverseGraphStore` +
`HindsightVectorStore` into one queryable KnowledgeGraph. *(your #11, #6.)*

### Phase 6 — MCP orchestration + parallel AI — **P1**
MCP gains discovery/plan/approve/progress/retry/verify; `AIOrchestrator` runs specialized agents in
parallel through the VerificationEngine. *(your #5, #8, #15.)*

### Phase 7 — DomainEngine + LanguageAdapters(Tree-sitter) — **P2/P3**
Reusable domain models; Tree-sitter/Dart adapters so AST is fully language-independent. *(your #4, #13.)*

---

## Guardrails to keep it from re-monolithizing
1. **Each engine is an interface with its own tests.** No engine references another's concrete type.
2. **Cross-engine communication is events + the graph only** — no direct calls into internals.
3. **The orchestrator has no domain logic** — if a method there does real work, it belongs in an engine.
4. **AI/context never touch the filesystem** — they go through ContextEngine + KnowledgeGraph.
5. **Nothing applies AI output unverified** — VerificationEngine is on the only write path.

## Applied so far
- **Phase 0 ✅** `IdeHostModels.cs` defaults to `CodeOss`; `IdeShell.razor` auto-falls back to Monaco.
- **Phase 1 ✅** `Services/Workspace/` kernel wired: `AddSyncroWorkspace()` is called in `MauiProgram.cs`
  and `IdeShell.OpenWorkspace()` drives `orchestrator.Open(path)`, so `WorkspaceOpened` fires for real.
- **Phase 2 ✅** watcher → `IndexEngineSubscriber` → `WorkspaceIndexed`; Problems Hub aggregates
  compiler + SAST + contract drift into the IDE panel and status bar.
- **Phase 3 ✅** Contract Engine → `ContractDriftDetected` → `DartBlocCodegen` regenerates the Flutter
  layer; `CrossLanguageMapper` writes `Mirrors` edges after every index pass.
- **Phase 4 ✅** `BuildEngine` gives every stack one build call with structured diagnostics;
  `VerificationEngine` is the gated write path — AI patches that break the build *or the test suite*
  roll themselves back, and `ApplyPatch` routes through it so no unverified write path remains.
- **Agent reach:** `WorkspaceIntelligenceMcp` (`GetProblems`, `MapFeature`) is registered with
  `McpOrchestrator`, and `GroqProvider.RunAgentLoopAsync` now runs `openai/gpt-oss-120b` — so the LLM
  can query the unified problem stream and the cross-language feature map over the Groq tool loop.

### The live event chain
```
IdeShell.OpenWorkspace(path)
  └─ WorkspaceOrchestrator.Open → WorkspaceOpened
       ├─ FileSystemWorkspaceWatcher.Start ──(edit+save)──► FileChanged
       │     ├─ ContractReverifySubscriber (800ms debounce) → ContractEngine.VerifyAsync
       │     │     └─ breaking? → ContractDriftDetected
       │     │           ├─ ContractDriftCodegenSubscriber → DartBlocCodegen → *.dart written
       │     │           └─ DiagnosticsSubscriber → ProblemsHub[Contract]
       │     └─ IndexEngineSubscriber (2s debounce) ──┐
       └─ IndexEngineSubscriber (immediate) ──────────┴─► WorkspaceIndexer.IndexAsync
             └─ CrossLanguageMapper.Map → Mirrors edges
             └─ WorkspaceIndexed
                   └─ DiagnosticsSubscriber → SecurityScanner → ProblemsHub[Security]

AI agent edit (Groq tool loop)
  └─ VerifiedPatch │ ApplyPatch  →  VerificationEngine
       ├─ anchor unique? changes anything?          ─ no → REJECT (file untouched)
       ├─ BuildEngine.BuildAsync (baseline) ─┐
       ├─ write patched bytes                │      ─► BuildDone → ProblemsHub[Build]
       ├─ BuildEngine.BuildAsync (after)  ───┘
       ├─ new error signatures? ─ yes → ROLL BACK + return the introduced errors
       └─ TestEngine.TestAsync (patched)
            ├─ green ─────────────────────────────────────────────→ KEEP   (1 test run)
            └─ red → restore original → TestAsync (baseline) → re-apply
                 ├─ failures absent from baseline → ROLL BACK + name the broken tests
                 └─ same failures as baseline     → KEEP (pre-existing, not this patch)
```

**Next:** Phase 5 — Thunder as the default Flutter generation path + unifying `UniverseGraphStore`
with `HindsightVectorStore` into one queryable KnowledgeGraph.
