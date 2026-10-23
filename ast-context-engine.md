# AST Context Engine — Plan

> A Syncro feature (and MCP-exposed extension surface) that runs the **deep AST engine across many
> projects**, connects them, **tokenizes** each project map into LLM-budgeted context packs, and
> maintains a **project AST cache** — so the in-editor LLM (via the E1 MCP bridge) can reason about
> a whole multi-repo universe within a fixed token budget.
>
> Companion to `extension-system.md` (the bridge) and `syncro-codeoss-mcp.md` (the architecture).
> Status: **PLAN** — grounded in a recon of the real engine (2026-07-16). Phase gates below.

---

## 0. What exists (facts, from the code)

| Piece | Reality in the repo | Reuse? |
|---|---|---|
| **Deep AST engine** | `Services/AST/Core/AstEngine.RunAsync(path)` → `AstProjectMap`: `Nodes` (symbols: Type/Name/File/Line/Params/ReturnType/HttpMethods/Route/Summary), `Endpoints`, `Dtos`, `Dependencies`, `Ports`, `SwaggerSpecs`, `DependencyGraphDot`, `CallGraphDot`, `DagFlowchartMermaid`. Parsers: Python/TS/JS/C# + JSON. | ✅ core |
| **Per-project cache** | `AstStorageService` → `<workspaceRoot>/ast_cache/<safeKey>.json`; `AstCacheManager.ComputeDirectorySignature` = SHA-256 over (relPath+size+mtimeTicks). `AstService.ScanProjectAsync` cache-checks the signature (stored in `ConfigFiles["__cache_signature__"]`), else runs + saves. **Real caches on disk** (pgxplore 412 KB / 438 nodes, a 3 MB one). | ✅ extend |
| **Multiproject scan** | `Services/Connector/Scan/MultiProjectScanService` + `WorkspaceDiscoveryService` + `DataLayerScanner`. Uses the **shallow connector resolvers** (regex/swagger/thunder), **not** the AST engine. | ⚠️ parallel to us |
| **MCP → LLM bridge** | `McpHttpEndpoint` (E1) exposes every registered `IMcpTool` at `POST /mcp`; appears natively in code-oss chat. New tools = zero wiring. | ✅ the on-ramp |
| **Tokenizer / budgeter** | **Does not exist.** No token counting anywhere. The 412 KB map is ~100k+ tokens — un-feedable as-is. | ❌ **new** |
| **Universe graph** | `UniverseGraphStore` (LiteDB) + `WorkspaceIndexer` already model cross-workspace nodes/edges. | ✅ sink |

**The gap in one line:** the AST engine is deep but single-project and un-tokenized; the multiproject
scanner is broad but shallow; nothing turns either into an LLM-sized context. This feature is the
bridge: **deep + multiproject + token-budgeted + LLM-reachable.**

---

## 1. Architecture

```
 root folder ──► AstUniverseScanner
                   │  (WorkspaceDiscoveryService finds N projects)
                   ▼
        ┌─ per project (parallel, cache-gated) ─────────────────────────┐
        │  AstService.ScanProjectAsync(path)  ──►  AstProjectMap         │
        │     • hash signature unchanged → load ast_cache/<key>.json     │
        │     • changed → AstEngine.RunAsync → save                      │
        └──────────────────────────┬────────────────────────────────────┘
                                    ▼
             AstTokenizer ──► ContextPack  (tiered: L0 skeleton → L3 full)
                                    │        + per-tier token cost, cached as <key>.pack.json
                                    ▼
   AstUniverseManifest  (_universe.json): projects, token costs, cross-connections
                                    │
        ┌───────────────────────────┼───────────────────────────────────┐
        ▼                           ▼                                     ▼
   Universe graph            MCP tools (registered → E1 bridge)     Syncro UI (Multi-Scan)
   (Consumes edges)          AstScanUniverse / AstContextPack /      AST tab, token meter
                             AstQuery / AstConnections / AstSymbol
                                    │
                                    ▼
                     in-editor LLM chat  &  Syncro's own Groq loop
                     "explain the orders flow"  →  budgeted pack  →  answer w/ file:line
```

Three genuinely new components (`Services/AST/Context/`): **AstTokenizer**, **ContextPack builder**,
**AstUniverseScanner**. Everything else is reuse.

---

## 2. Tokenization — the core new piece

**Why:** an `AstProjectMap` is 100k+ tokens; a model's window is a hard ceiling. We must (a) *count*
tokens and (b) *fit* a useful projection under a budget.

### 2.1 Counting (`AstTokenizer`)
- **Default:** fast heuristic — `ceil(chars / 4)` (well-calibrated for code/JSON), zero dependency.
- **Optional exact:** pluggable `ITokenCounter`; a BPE backend (SharpToken / Tiktoken nuget, `o200k_base`)
  when precision matters. The heuristic is the floor everything is budgeted against; exact refines it.
- Never block on counting: budgeting is additive over pre-computed per-item costs.

### 2.2 Fitting — tiered context packs (`ContextPack`)
Each project map is projected into nested tiers, each a token-costed, LLM-ready markdown/JSON blob:

| Tier | Contents | ~tokens (pgxplore) |
|---|---|---|
| **L0 Skeleton** | name, language, framework, counts, endpoint list (`METHOD /path`), top modules | ~300 |
| **L1 Symbols** | class/method **signatures** (name, params, returnType, `file:line`), DTOs, deps | ~3–6k |
| **L2 Detail** | L1 + summaries/docstrings, call-graph edges, per-module grouping | ~15–30k |
| **L3 Full** | the raw map (rarely handed to a model; for tools that stream) | ~100k+ |

**Budget algorithm** (`ContextPackBuilder.Fit(maps, budget, focus?)`):
1. Emit **L0 for every project** (always fits — the universe overview).
2. Spend the remainder on higher tiers for the **most relevant** projects/symbols first
   (relevance in §5), degrading tier-by-tier until the budget is full.
3. Hard ceiling: the builder **never returns more than `budget`** tokens; it emits a truncation note
   (`… +N symbols omitted`) rather than silently dropping — same honesty rule as the scanner.

### 2.3 Cache the packs
Alongside `ast_cache/<key>.json`, write `ast_cache/<key>.pack.json` (the L0–L2 tiers pre-rendered
with token costs), keyed by the **same signature**. Tokenizing 400 KB once per change, not per query.

---

## 3. Project AST cache — extend, don't replace

- **Reuse** `AstStorageService` + `AstCacheManager` verbatim (signature-gated per-project maps).
- **Add** `<key>.pack.json` (tiered packs) — invalidated by the same signature.
- **Add** `ast_cache/_universe.json` — the manifest: `{ root, projects:[{path, key, lang, framework,
  signature, nodeCount, endpointCount, tokens:{L0,L1,L2} }], connections:[…], generatedAt }`.
- **Incremental:** on rescan, only projects whose signature changed re-run the engine + re-tokenize;
  the manifest is rebuilt from cached maps (cheap). This is what makes "scan 5 projects" fast on repeat.

---

## 4. Multiproject connection — AST-powered

Deeper than the regex connector: the connection is drawn from **AST endpoints + call sites**, with the
full symbol behind each side.

- **Provides** side: `AstProjectMap.Endpoints` per backend (real routes, incl. the engine's Next.js
  `app/`+`pages/` scanners — which closes the Next.js gap the connector still has).
- **Consumes** side: call sites. Reuse the existing frontend extractors + thunder `api.json` for
  Flutter/web, keyed the same way; where the AST parser already captures invocations, prefer those.
- **Match:** `ContractMatcher` (already param-agnostic, suffix-tolerant) → `Matched / Missing /
  MethodMismatch`, plus host/base-URL correlation for cross-repo (the `remote:{host}` seed pattern).
- **Sink:** emit into the **Universe graph** (`Consumes` edges) and the manifest, each edge carrying
  the AST symbol (`file:line`) on both ends — so the LLM can jump from "this call" to "this handler."

Net: same connection report the Multi-Scan page shows today, but every node is an AST symbol, and the
whole thing is tokenizable.

---

## 5. LLM embedding — MCP tools (auto-surface via E1)

New deterministic `IMcpTool`s registered in `McpOrchestrator` → they appear in code-oss chat **and**
are callable by Syncro's own Groq loop. All accept a `budgetTokens` and honor it.

| Tool | Input | Returns |
|---|---|---|
| `AstScanUniverse` | `{ root, refresh? }` | manifest summary: projects, per-project token cost, connection counts (triggers cache-gated (re)scan) |
| `AstContextPack` | `{ project, budgetTokens, tier? }` | the richest tier of one project that fits the budget |
| `AstUniversePack` | `{ root, budgetTokens, focus? }` | budgeted **cross-project** pack (L0 all + higher tiers for focus) — the "explain this system" context |
| `AstConnections` | `{ root }` | AST connection graph (matched/missing/orphan) with symbol provenance |
| `AstSymbol` | `{ project, name }` | one symbol + neighbors (callers/callees/DTOs), token-costed |
| `AstQuery` | `{ question, budgetTokens }` | relevance-ranked cross-project pack for a NL question (retrieval, not generation) |

**Relevance for `AstQuery`/`focus`** (fill budget with what matters):
1. Lexical: BM25-ish over symbol names, routes, DTO fields, summaries.
2. Structural: connection-graph proximity (a matched endpoint pulls in its handler + the calling symbol).
3. (A5, optional) Semantic: embed symbols into the existing **Hindsight vector store** for
   meaning-based recall.

**"Smoothly embedded with the LLM":** the model never sees 400 KB. It asks `AstQuery("how does an
order get created?", budget)` and gets a tight, cited pack; or the editor agent lists the tools and
calls `AstContextPack` on demand. The token budget is the model's window — enforced in C#, not hoped for.

---

## 6. Repo layout

```
Services/AST/Context/
  ITokenCounter.cs / HeuristicTokenCounter.cs       # §2.1 (BpeTokenCounter optional, A5)
  ContextPack.cs                                     # tiers + token costs (model)
  ContextPackBuilder.cs                              # §2.2 Fit(maps, budget, focus)
  AstUniverseScanner.cs                              # §1 discover → AstService per project → manifest
  AstUniverseManifest.cs                             # §3 model, (de)serialized to _universe.json
  AstConnectionMapper.cs                             # §4 endpoints×calls over AST + ContractMatcher
Services/AST/Mcp/
  AstContextMcp.cs                                   # §5 registers the 6 tools with McpOrchestrator
```
(Extension side stays as-is — `syncro-bridge` already surfaces whatever the orchestrator registers.)

---

## 7. Phases (each ends with a runnable gate)

### A0 — Tokenizer + context packs
`HeuristicTokenCounter`, `ContextPack`, `ContextPackBuilder.Fit`. Load a **real cached map**
(`ast_cache/D__PG_Connectjlpl_pgxplore.json`) and project it.
- **Gate (headless):** L0 pack < 500 tokens and lists all 27 endpoints; L1 emits method signatures
  with `file:line`; `Fit(budget=2000)` returns ≤ 2000 tokens with a truncation note. No engine run needed.

### A1 — AST universe scan + manifest cache
`AstUniverseScanner`: discover (reuse `WorkspaceDiscoveryService`) → `AstService.ScanProjectAsync`
per project (parallel, cache-gated) → write `_universe.json` + per-project `.pack.json`.
- **Gate:** scan `D:\PG_Connectjlpl` → 7 projects; second run is near-instant (signatures unchanged);
  manifest shows per-project node/endpoint/token counts.

### A2 — AST-powered connections
`AstConnectionMapper`: AST endpoints × calls → `ContractMatcher` → connections into the manifest +
Universe graph, with symbol provenance. Closes Next.js server-route gap via the engine's route scanner.
- **Gate:** on PG_Connect, backend endpoints resolve; ≥ the current Multi-Scan matched count, each edge
  carrying `file:line` both ends.

### A3 — MCP tools (the LLM on-ramp)
`AstContextMcp` registers the 6 tools; they auto-appear via the E1 bridge.
- **Gate (headless, over `POST /mcp`):** `tools/list` shows them; `AstContextPack{project, budget:1500}`
  returns a ≤1500-token cited pack for pgxplore; `AstScanUniverse` returns the manifest.

### A4 — Relevance (AstQuery)
Lexical + structural ranking; fill budget best-first.
- **Gate:** `AstQuery("who creates an order?", 3000)` returns the order controller + the calling repo
  symbol (from student_app) within budget, with citations — verified against PG_Connect.

### A5 — Semantic recall (optional)
Embed symbols into the Hindsight vector store; blend into `AstQuery` ranking.
- **Gate:** a paraphrased query ("how is a booking placed?") still retrieves the order-creation symbols.

**First slice: A0 + A1 + A3** — after those, the editor LLM can pull budgeted AST context for any
project in the universe. A2/A4 deepen it; A5 is polish.

---

## 8. Design rules & risks

**Rules**
1. **Cache-first, signature-gated, incremental** — never re-parse an unchanged project; never
   re-tokenize an unchanged map. Reuse `AstCacheManager`/`AstStorageService` as-is.
2. **The budget is a hard ceiling** — `Fit` returns ≤ budget, always, with a visible truncation note.
3. **L0 always fits** — the universe overview is never dropped, however large the repos.
4. **Provenance on every symbol** — `file:line` flows into packs, connections, and chat citations.
5. **Deterministic tools, LLM on top** — the tools do no generation; the model composes over them.
   New capability = new `IMcpTool`, auto-surfaced by the E1 bridge.

**Risks**
| Risk | Mitigation |
|---|---|
| Heuristic token count drifts from the model's real tokenizer | Budget with a safety margin (e.g. target 90%); make `ITokenCounter` swappable for exact BPE (A5) |
| First scan of many/large repos is slow | Parallel + cache-gated; only first run pays; stream progress like the runtime scanner |
| Huge single project (3 MB map) blows L2 | Tiering + per-project token caps in the manifest; `Fit` degrades that project to L0/L1 |
| AST call-site extraction weaker than thunder for Flutter | Prefer thunder `api.json` where present (already wired); AST for backend/TS/C# depth |
| Cache staleness on external edits | Signature covers size+mtime; add an explicit `refresh` flag on `AstScanUniverse` |
| Pack cache invalidation bugs | Key `.pack.json` by the exact same signature string as the map; regenerate together |
