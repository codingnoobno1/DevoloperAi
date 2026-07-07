# Viking IDE — In-Depth Plan

> A Blazor-native, AI-first IDE built **inside Syncro.Desktop** (MAUI + Blazor Hybrid).
> Not a VS Code wrapper — it replaces the "Open in VS Code" escape hatch with a first-class
> editor that is wired directly into Syncro's existing intelligence: **AST engine, Hindsight
> vector DB, Knowledge Graph, Agent loop, NLP error→fix mappings, script reuse, and 3030 telemetry**.
>
> Positioning: **VS Code's ergonomics + Claude-Code's agency + Syncro's project memory.**
> The differentiator is that the editor sits on top of a project that already *understands itself*.

---

## 0. Why this is feasible (reuse map)

Syncro.Desktop already ships most of the "brain." Viking IDE is mostly **surface + editor + terminal**
over services that exist:

| IDE subsystem | Already in repo | Action |
|---|---|---|
| AST Engine | `Services/AST/*` (`AstService`, parsers, `AstProjectMap`) | **reuse** |
| Vector DB / RAG | `Services/AST/Hindsight/*` (`HindsightVectorStore`, `HindsightQueryService`) | **reuse + upgrade embeddings** |
| Knowledge Graph | planned in `ast.md` (`KnowledgeGraph`, `RelationshipIndexer`) | **build (Phase 4)** |
| Memory | `Services/AgentCli/SyncroDb` + cognitive memory (`agentcli.md`) | **reuse** |
| Agent Runtime | `Services/AgentCli/Loop/TaskLoopEngine` + `Services/SyncroCLI/AgentOrchestrator` | **reuse + unify** |
| NLP error→fix | `Services/AgentCli/Nlp/*` (`ErrorMatcher`, `ErrorMappingStore`) | **reuse** |
| Script reuse | `Services/AgentCli/Scripts/*` (`ScriptStore`, `ScriptMatcher`) | **reuse** |
| Telemetry | `Services/SyncroCLI/AgentMonitorServer` (:3030) + `DbStatusService` | **reuse, embed panel** |
| AI / LLM | `BusinessLogic/AIClient` (:3020) + `AgentCli/Llm/ILlmGateway` | **reuse + abstract** |
| Git | `Services/SyncroCLI/Providers/Git/GitProvider` (CLI) | **reuse + add LibGit2Sharp** |
| Terminal (UI) | `Components/Shared/SyncroTerminal.razor` + `ProcessRunner` | **replace with Xterm.js + ConPTY** |
| Project gen / templates | `Services/projectgenerator/*`, `ProjectTemplates/*` | **reuse (New Project)** |
| UI kit | MudBlazor 9.4 | **reuse as base** |
| Charts | OxyPlot (already referenced) | reuse, or add LiveCharts2 |
| Docs | QuestPDF (already referenced) | **reuse** |
| Graph algos | QuikGraph (already referenced) | **reuse (DAG)** |

**Net new** to add: **BlazorMonaco** (editor), **Xterm.js + ConPTY** (terminal), **Cytoscape.js**
(graph render), a **docking/split** layout, **real embeddings** (OllamaSharp/Semantic Kernel),
and a **persistence DB** (SQLite/LiteDB) for editor/workspace state.

---

## 1. Guiding principles

1. **Blazor-native, single process.** The IDE is a set of Razor components in the existing MAUI
   `BlazorWebView`. JS libraries (Monaco, Xterm, Cytoscape) are integrated via **JS interop modules**.
2. **Intelligence is the product.** Every panel is wired to Syncro's AST/RAG/agent services. The
   editor isn't just text — it knows endpoints, DTOs, the dependency graph, and past fixes.
3. **Offline-first, AI-optional.** Everything works without the LLM (deterministic AST/RAG); the
   model (:3020 or local Ollama) *enriches*. Mirrors the Hindsight with/without-LLM design.
4. **Safe agency.** Agent edits flow through the existing `LlmSafetyGate`/approval model — preview
   diffs, never auto-apply, audited.
5. **One state spine.** `.syncro_db` per workspace (AST, vectors, tasks, scripts, memory) + a small
   SQLite for editor state (open tabs, layout). The 3030 monitor reflects it live.
6. **Don't fork logic.** The IDE consumes services; it does not re-implement scanning, retrieval,
   or the agent loop.

---

## 2. High-level architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│  Viking IDE Shell  (Components/IDE/IdeShell.razor)                     │
│  ┌─────────┬───────────────────────────────────┬───────────────────┐  │
│  │ Activity│  Editor Group (Monaco tabs)        │  AI / Intelligence │  │
│  │  Bar    │  ┌──────────────┐ ┌──────────────┐ │  Side Panel        │  │
│  │ (icons) │  │ file.cs (Mon)│ │ diff (Mon)   │ │  • Chat (RAG)      │  │
│  │ Explorer│  └──────────────┘ └──────────────┘ │  • Agent runs      │  │
│  │ Search  │                                    │  • Hindsight       │  │
│  │ Git     │  ┌────────────────────────────────┐│  • Knowledge graph │  │
│  │ AST     │  │ Panel: Terminal | Problems |    ││  • Telemetry       │  │
│  │ Agents  │  │ Output | Graph | Telemetry      ││                    │  │
│  └─────────┴──┴────────────────────────────────┴┴───────────────────┘  │
│  Status bar: workspace · git branch · LLM(:3020) · vectors · agent task │
└──────────────────────────────────────────────────────────────────────┘
        │ JS interop (Monaco, Xterm, Cytoscape)   │ DI services (C#)
        ▼                                          ▼
   wwwroot/js/ide/*.js                     AST · Hindsight · AgentCli · Git · Telemetry
```

Layout = a **docking/splitter** grid (resizable). Panels are Razor components; the heavy widgets
(editor, terminal, graph) are JS-backed and bridged.

---

## 3. Recommended stack (decisions, not the whole menu)

Pick a tight set; avoid mixing 8 UI kits (bundle size + WebView perf). Status: ✅ already referenced · ➕ add.

| Layer | Choice | Status | Why this one |
|---|---|---|---|
| UI base | **MudBlazor** | ✅ | already the app's kit; consistent theme |
| Docking/split | **BlazorSplit** (or custom CSS‑grid splitters) | ➕ | lightweight; full docking kits are heavy in a WebView |
| Dialogs | MudBlazor dialogs (already used) | ✅ | no extra dep |
| Editor | **BlazorMonaco** | ➕ | the VS Code editor; mature Blazor wrapper |
| C# IntelliSense | **Roslyn** (`Microsoft.CodeAnalysis`) | ✅ | completions/diagnostics; already referenced |
| Multi-lang parse | **Syncro AST parsers** + optional **Tree-sitter (WASM)** | ✅/➕ | reuse existing TS/JS/Py parsers; Tree-sitter for highlight/folding breadth |
| Terminal | **Xterm.js** + **ConPTY** (Pty.Net or custom) | ➕ | real interactive shell, not line-buffered |
| Process exec | existing `ProcessRunner` + **CliWrap** (optional) | ✅/➕ | reuse; CliWrap for ergonomic piping |
| Git | **GitProvider** (CLI) + **LibGit2Sharp** | ✅/➕ | status/diff/stage without spawning git each time |
| GitHub | **Octokit.NET** | ➕ | PRs/issues (ties to `GitHubPRService` plan) |
| Graph render | **Cytoscape.js** | ➕ | interactive dependency/KG graph in the WebView |
| Graph algos | **QuikGraph** + optional **MSAGL** | ✅/➕ | DAG/topo (have) + auto-layout (add) |
| Charts | **OxyPlot** (have) or **LiveCharts2** | ✅/➕ | telemetry; OxyPlot already in, LiveCharts2 nicer for Blazor |
| Vector DB | **Hindsight** now → **SQLite + MathNet** → optional **Qdrant** | ✅/➕ | start file-based; scale later |
| Embeddings | **OllamaSharp** or **Semantic Kernel** | ➕ | real local embeddings (replace mock vectors) |
| Agents | **AgentCli loop** + optional **AutoGen** + **MCP** | ✅/➕ | reuse loop; AutoGen for multi-agent; MCP for tools |
| Local DB | **SQLite** (Microsoft.Data.Sqlite) or **LiteDB** | ➕ | editor/workspace state, recents, settings |
| Logging | **Serilog** | ➕ | structured logs → Output panel + telemetry |
| Resilience | **Polly** | ➕ | retry LLM/network calls |
| Docs | **QuestPDF** | ✅ | reports already wired |

> **Replace** `Services/VSTools/VscodeLauncherService` usage (the "Open in VS Code" button on
> `Hindsight.razor`) with "Open in Viking IDE" once Phase 1 lands. Keep the launcher as a fallback
> behind a setting.

---

## 4. Shell & docking

`Components/IDE/`
```
IdeShell.razor            // root: activity bar + side panel + editor group + bottom panel + status bar
IdeLayout.razor           // resizable split regions (BlazorSplit / custom)
ActivityBar.razor         // left icon rail → toggles side views
SidePanel.razor           // hosts Explorer/Search/Git/AST/Agents views (tabbed)
EditorGroup.razor         // Monaco tab host (open files, diff, split)
BottomPanel.razor         // Terminal/Problems/Output/Telemetry tabs
StatusBar.razor           // workspace, branch, LLM state, vector count, active agent task
CommandPalette.razor      // Ctrl+Shift+P — runs IDE commands + syncro CLI verbs
```
State: `IdeWorkspaceState` (scoped service) — open files, active tab, layout, dirty buffers,
selected workspace. Persisted to SQLite so sessions restore.

---

## 5. Subsystem deep-dives

### 5.1 Monaco Editor
- **`wwwroot/js/ide/monaco-interop.js`** + `Components/IDE/Editor/MonacoEditor.razor` (or BlazorMonaco).
- Models per file (URI-keyed), tabs, dirty tracking, save → disk via a C# `FileService`.
- **Diff editor** reused for Git diffs and agent patch previews (`PatchGenerator` output).
- **IntelliSense:**
  - C#: `RoslynCompletionService` (AdhocWorkspace over the project) → completion/hover/diagnostics
    pushed into Monaco via `registerCompletionItemProvider`.
  - Other langs: Monaco's built-in language workers for highlight; **Syncro AST parsers** feed a
    custom symbol provider (go-to-def, outline) for TS/JS/Py.
- **AST overlays:** gutter markers for endpoints (`AstEndpoint`), DTOs, complexity hotspots — pulled
  from `AstProjectMap`. Hovering a symbol shows its Knowledge-Graph relationships.
- **Inline AI:** selection → "Explain / Refactor / Fix" → RAG-grounded prompt → diff preview → approve.

### 5.2 Project Explorer
- `Components/IDE/Explorer/FileTree.razor` + `FileService` (enumerate, watch via `FileSystemWatcher`).
- Respects the AST ignore set (`bin`, `obj`, `node_modules`, `.git`, `.syncro_db`).
- Context actions: open, rename, delete, "Scan into Hindsight," "Generate from template" (reuses
  `ProjectGenerator` + `ProjectTemplates/*`), "Ask AI about this file."
- Badges: indexed/not, endpoint count, last agent touch.

### 5.3 Terminal
- **Xterm.js** (`wwwroot/js/ide/xterm-interop.js`) + a **ConPTY** backend (`PtyService` via Pty.Net
  or a thin P/Invoke over `CreatePseudoConsole`). Bi-directional stream over a C# channel.
- Wire the existing **SyncroCLI** so `syncro …` verbs run in-terminal; the command palette can
  inject them. Replaces the line-buffered `SyncroTerminal.razor`.
- Multiple terminals, per-workspace cwd, env from `EnvironmentManager`.

### 5.4 Git
- **LibGit2Sharp** for fast status/staging/branch/log + **GitProvider** (CLI) for push/pull/clone
  (crash-isolated child process, already built).
- `Components/IDE/Git/SourceControl.razor`: changes list, stage/unstage, commit, branch switcher;
  click a file → **Monaco diff**.
- Octokit for PR creation (ties into `GitHubPRService` from `agentcli.md`).

### 5.5 AST Engine (reuse)
- `AstService.ScanProjectAsync` / `TokenizeProjectAsync` already exist.
- IDE adds an **AST view** (outline + endpoints + DTOs + complexity) in the side panel, and a
  "Rescan" action; results drive editor overlays and the graph.

### 5.6 Knowledge Graph
- Build per `ast.md` (`EntityResolver`, `RelationshipIndexer`, `KnowledgeGraph`) → entities +
  typed edges (`uses`, `writes`, `exposes`).
- Render with **Cytoscape.js** (`wwwroot/js/ide/cytoscape-interop.js`); click node → open file at
  symbol; filter by type. QuikGraph for DAG/topo + MSAGL for auto-layout export.

### 5.7 Vector DB / RAG (upgrade path)
- **Now:** `HindsightVectorStore` (file `.syncro_db/Vectors`) + lexical retrieval (`HindsightQueryService`).
- **Step 1:** replace mock 768-float vectors with **real embeddings** via `OllamaSharp` (e.g.
  `nomic-embed-text`) behind an `IEmbeddingProvider`; cosine via **MathNet.Numerics**.
- **Step 2 (scale):** move vectors to **SQLite** (or **Qdrant** for large/multi-repo).
- Keep the deterministic lexical path as the offline fallback (the with/without-LLM story holds).

### 5.8 Memory (reuse)
- `AgentCli/SyncroDb` is the spine (tasks, mappings, scripts, audit, todo). Cognitive memory
  (`Decision/Bug/Risk/Failure/Preference/FollowUp` from `agentcli.md`) surfaces in an IDE
  "Memory" view; the agent recalls it before acting.

### 5.9 Agent Runtime
- **Unify** the two agent systems: keep `AgentCli/TaskLoopEngine` (DB-loop, NLP, script reuse) as
  the executor; `SyncroCLI/AgentOrchestrator` becomes the session/telemetry front. Optional
  **AutoGen** for multi-agent (Architect/Generator/Validator/Repair) and **MCP** to expose Syncro
  tools (AST query, RAG search, run script) to the model safely.
- `Components/IDE/Agents/AgentPanel.razor`: enter a goal → live 7-stage loop (from `agentcli.md`)
  → diffs → approve → apply. All gated by `LlmSafetyGate`.

### 5.10 Telemetry
- The **:3030 AgentMonitorServer** + `DbStatusService` already expose `/api/status` + `/api/db`.
- IDE embeds a **Telemetry panel** that reads `/api/db` (tasks, scripts, NLP, tokens, audit) and
  renders with **LiveCharts2/OxyPlot** — no new server needed. **Serilog** sink → Output panel.

### 5.11 AI Panel
- Chat grounded in `HindsightQueryService` (RAG) with a **with/without-LLM** toggle (reuse the
  Hindsight comparison). Inline code actions produce **patches** (`PatchGenerator`) → diff → approve.
- Model routing via `ILlmGateway`: local Ollama / :3020 Gemini / cloud (OpenAI/Azure) — Polly retries.

### 5.12 RAG
- `HindsightQueryService.RetrieveAsync` is the retrieval primitive; upgrade to Semantic Kernel /
  **Kernel Memory** if you want chunking + citations + connectors. Chunks already carry file:line
  provenance for citations in the editor.

### 5.13 Documentation
- **QuestPDF** project reports (already wired via `AstService.GeneratePdfAsync`) + an in-IDE
  Markdown preview pane (render `*.md`, the `ProjectTemplates/*` literate templates, and generated docs).

### 5.14 Multi-Workspace
- `WorkspaceManager`: open multiple project roots; each gets its own `.syncro_db`,
  AST map, vectors, git repo. Switch via the status bar; recents in SQLite.
- `DbStatusService.RegisterProjectRoot()` is called on workspace open (so telemetry tokens populate).

---

## 6. Module / folder layout (new)

```
Components/IDE/
├── IdeShell.razor  IdeLayout.razor  ActivityBar.razor  SidePanel.razor
├── StatusBar.razor  CommandPalette.razor  BottomPanel.razor
├── Editor/        MonacoEditor.razor  DiffView.razor  EditorTabs.razor
├── Explorer/      FileTree.razor  FileNode.razor
├── Terminal/      TerminalView.razor
├── Git/           SourceControl.razor  BranchMenu.razor
├── Ast/           AstOutline.razor  EndpointList.razor
├── Graph/         GraphView.razor
├── Agents/        AgentPanel.razor  PatchReview.razor
├── Ai/            ChatPanel.razor  InlineActions.razor
├── Telemetry/     TelemetryPanel.razor
└── Docs/          MarkdownPreview.razor

Services/Ide/
├── IdeWorkspaceState.cs   WorkspaceManager.cs   FileService.cs
├── Editor/    RoslynCompletionService.cs  MonacoBridge.cs  LanguageSymbolProvider.cs
├── Terminal/  PtyService.cs
├── Git/       GitWorkspaceService.cs        (LibGit2Sharp wrapper)
├── Persistence/ IdeStateStore.cs            (SQLite/LiteDB)
└── Embeddings/  IEmbeddingProvider.cs  OllamaEmbeddingProvider.cs  LexicalEmbeddingProvider.cs

wwwroot/js/ide/
├── monaco-interop.js   xterm-interop.js   cytoscape-interop.js   split-interop.js
```

---

## 7. JS interop in the MAUI BlazorWebView (the key technical detail)

- Load Monaco/Xterm/Cytoscape as **ES modules** via `IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/ide/monaco-interop.js")` (one module per widget). Ship the JS/wasm under `wwwroot` (bundled as MAUI content).
- **Dispose discipline:** each component disposes its JS module + editor instance in
  `IAsyncDisposable.DisposeAsync` to avoid WebView leaks.
- **Big payloads:** stream file content and terminal data over `DotNetObjectReference` callbacks,
  not giant JSON strings.
- **ConPTY:** the PTY runs in C#; bytes are pumped to Xterm via a JS callback; input events come
  back via `[JSInvokable]`.
- **Offline assets:** vendor Monaco/Xterm/Cytoscape locally (no CDN) so the IDE works offline.

---

## 8. Persistence

| What | Where |
|---|---|
| Project intelligence (AST, vectors, tasks, scripts, mappings, audit, memory) | `.syncro_db` (existing) |
| Editor/workspace state (open tabs, layout, recents, settings) | **SQLite/LiteDB** (`IdeStateStore`) |
| Cross-project learning (root causes, patterns) | `%LOCALAPPDATA%/SyncroDesktop/intelligence` (existing) |

---

## 9. Phased roadmap

**Phase 0 — Shell (foundation)**
`IdeShell` + docking layout + activity bar + status bar + command palette. Static panels.
*Deliverable: navigable IDE chrome inside the app.*

**Phase 1 — Editor + Explorer (the "no more VS Code" milestone)**
BlazorMonaco interop, file tree + `FileService`, open/edit/save, tabs, dirty state, SQLite state restore.
Replace the "Open in VS Code" button with "Open in Viking IDE."
*Deliverable: edit real files in-app.*

**Phase 2 — Terminal + Git**
Xterm + ConPTY `PtyService`; SourceControl via LibGit2Sharp + Monaco diff; wire `syncro` CLI.
*Deliverable: run commands + commit without leaving the IDE.*

**Phase 3 — Intelligence panels (reuse)**
AST outline + endpoint overlays; Hindsight chat (with/without LLM); Telemetry panel (`/api/db`).
*Deliverable: the editor "knows" the project.*

**Phase 4 — Knowledge Graph + real embeddings**
`RelationshipIndexer` → Cytoscape graph; OllamaSharp embeddings replace mock vectors; SQLite vector store.
*Deliverable: real semantic search + clickable graph.*

**Phase 5 — Agents (Claude-Code competitor)**
Agent panel over `TaskLoopEngine`; patch preview/approve; AutoGen multi-agent; MCP tools; safety gate.
*Deliverable: "add JWT auth" → plan → diff → approve → apply, grounded in the project.*

**Phase 6 — Polish**
Multi-workspace, themes, settings, search-in-files, problems panel, docs preview, performance pass.

Maps to their tiers: **Tier 1** = Phases 0–3, **Tier 2** = Phase 4, **Tier 3** = Phase 5.

---

## 10. Risks & mitigations

| Risk | Mitigation |
|---|---|
| **WebView JS perf** (Monaco + Xterm + Cytoscape in one WebView) | lazy-load modules per visible panel; dispose on hide; virtualize trees/lists |
| **ConPTY complexity** on Windows | use a maintained Pty.Net binding; fall back to `ProcessRunner` line mode |
| **Roslyn memory** for IntelliSense | one `AdhocWorkspace` per workspace; throttle/cancel completion requests |
| **Mock vectors mislead** | clearly lexical until Phase 4; swap to real embeddings behind `IEmbeddingProvider` |
| **Two agent systems drift** | unify on `TaskLoopEngine`; orchestrator becomes the front-end only |
| **Disk pressure** (already hit 0 B free on D:) | gitignore `bin/obj/.syncro_db`; vendor JS not node_modules; cap vector store size |
| **Bundle bloat** from many UI kits | MudBlazor only; add BlazorSplit, not Radzen/Telerik/Syncfusion together |
| **LLM unsafe edits** | reuse `LlmSafetyGate`: preview diffs, approval, sandbox, audit |

---

## 11. Library install list

**NuGet (add):** `BlazorMonaco`, `LibGit2Sharp`, `Octokit`, `Microsoft.Data.Sqlite` (or `LiteDB`),
`OllamaSharp` (and/or `Microsoft.SemanticKernel`, `Microsoft.KernelMemory`), `Serilog` +
`Serilog.Extensions.Logging`, `Polly`, optional `Pty.Net`, `CliWrap`, `LiveChartsCore.SkiaSharpView.Blazor`,
optional `Microsoft.Msagl`, optional `AutoGen`, `ModelContextProtocol`.
**Already referenced (reuse):** MudBlazor, Roslyn (`Microsoft.CodeAnalysis.CSharp`), QuikGraph,
QuestPDF, OxyPlot, YamlDotNet, MathNet.Numerics, Newtonsoft.Json.
**JS (vendor under `wwwroot/js/ide/vendor`):** `monaco-editor`, `xterm` (+ `xterm-addon-fit`),
`cytoscape`, `split.js` (if not using BlazorSplit).

---

## 12. Verification per phase

- **P0:** shell renders, panels toggle, palette runs a no-op command.
- **P1:** open/edit/save a file; reopen app → tabs/layout restored; "Open in Viking IDE" works.
- **P2:** interactive `python`/`node` REPL in terminal; stage+commit a change; diff renders.
- **P3:** endpoint overlays match `AstProjectMap`; Hindsight chat returns grounded answers offline.
- **P4:** semantic search beats lexical on a paraphrased query; graph node click opens the file.
- **P5:** an agent goal produces a previewed patch; rejecting it changes nothing; accepting builds + audits.
- **Build:** `dotnet build` clean (run with the app **closed** — a running instance locks the output DLL).

---

## 13. Migration: retire "Open in VS Code"

`Hindsight.razor` currently injects `VscodeLauncherService` and shows an "Open in VS Code" button.
Once Phase 1 lands:
1. Add "Open in Viking IDE" (navigates to `IdeShell` with the workspace path).
2. Demote the VS Code button behind a setting (`Settings → External editor`), default off.
3. Keep `VscodeLauncherService` only as that optional fallback.
```
