# Syncro Connector Engine — Full Implementation Plan

## Overview

The **Connector Engine** is a new first-class service in `Services/Connector/`. It takes a
**frontend repo** (Flutter mobile app first, then Next.js / Blazor / Vite) and a **backend repo**
(Express / FastAPI / Spring / ASP.NET) and makes them *understand each other*:

1. **Run the frontend live** — reuse the existing `MobilePreview` (Flutter web-server + hot reload)
   as the **right-hand side window** while the user works on the left.
2. **Understand the frontend's API surface** — statically parse every HTTP call the frontend makes
   (Dio/http in Flutter, `fetch`/axios in Next, `HttpClient` in Blazor) into a normalized
   `ApiCallSite` list.
3. **Detect / generate the backend contract** — read `swagger.json`/`openapi.yaml` if present
   (reuse `ParseSwaggerTool`), otherwise scan routes, otherwise **AI-generate** the backend in
   parallel from the frontend's observed calls.
4. **Map frontend call → backend route** — produce a live **Connection Graph**: green = matched,
   amber = shape mismatch, red = frontend calls an endpoint the backend doesn't expose.
5. **Drive both with AI** — a prompt sets the tech stack + folder structure for the missing side and
   keeps the two contracts in sync. Exposed as a **`ConnectorMcp` server** so the agent loop can
   call it like any other tool.

This is **not a new IDE** — it is a new *layout mode* and a new *service* that compose existing parts.

**Slot in the architecture:**

```
UI (Blazor)
  └── Components/Pages/Connector/ConnectorWorkbench.razor   ← new "Connector" layout mode
        ├── (left)   editor      → reuse Components/IDE/CodeEditor.razor (Monaco)
        ├── (center) ConnectionGraph.razor                  ← new: FE call ↔ BE route edges
        ├── (right)  MobilePreview / WebPreview              ← reuse Components/Shared/MobilePreview.razor
        └── (bottom) backend terminal + AI composer          ← reuse IdeShell panels
        │
Services/Connector/ConnectorEngine.cs        ← DI singleton, registered in MauiProgram.cs
  ├── FrontendApiExtractor                    ← parse FE source → ApiCallSite[]
  ├── BackendContractResolver                 ← swagger | route-scan | AI-generate → BackendRoute[]
  ├── ContractMatcher                         ← ApiCallSite ↔ BackendRoute → ConnectionEdge[]
  ├── BackendGenerator                        ← AI parallel scaffold from unmatched calls
  ├── ConnectorProjectStore                   ← reads/writes connector.json (the .md's runtime twin)
  └── Mcp/ConnectorMcp.cs                     ← MCP server exposing the engine to the agent loop

Services/SyncroCLI/Commands/ConnectorCommand.cs   ← `syncro connect <fe> <be>` CLI command
```

---

## Reuse map — what already exists (do NOT rebuild)

| Need | Existing asset | How the Connector uses it |
|---|---|---|
| Live mobile output side-window | `Components/Shared/MobilePreview.razor` + `Services/FlutterService.cs` (`RunProject`, `HotReload`, `SmartReload`, `OnOutput`) | Mount as the **right pane**. Its AI sidebar already does prompt → JSON changes → `SmartReload`. We add a `WebPreview.razor` twin for Next/Blazor (same iframe shell, `npm run dev`). |
| Create wizard | `Components/Shared/CreateProjectDialog.razor` (3-step: type → config → location) + `Services/projectgenerator/ProjectGenerator.cs` (`ScaffoldProjectAsync`, `ScaffoldGroupProjectAsync`) | Add a **"Connected Stack"** project type that scaffolds FE+BE and writes `connector.json`. The wizard's `Group Stack` path is the template. |
| Code editor | `Components/IDE/CodeEditor.razor` + `Services/Ide/Engines/IEditorEngine.cs` (Monaco via JS interop, `window.SyncroIde`) | Reuse verbatim as the left editor pane. See **Monaco reuse** below. |
| Multi-pane layout | `Components/IDE/IdeShell.razor` GoldenLayout (`window.SyncroIde.initLayout`, portal pattern) | The Connector layout is a **new GoldenLayout config** with the same portal trick. |
| MCP tooling | `Services/Engine/Mcp/McpOrchestrator.cs`, `IMcpTool`, `IMcpServer`, `Servers/ApiMcp.cs` | `ConnectorMcp` implements `IMcpServer`; register via `orchestrator.RegisterServerAsync`. |
| Swagger parsing | `Services/Engine/Tools/Api/ParseSwaggerTool.cs` (paths → method/operationId, components → schemas) | `BackendContractResolver` calls it first; falls back to route-scan/AI. |
| Code understanding | `Services/AST/` (`AstService.ScanProjectAsync`, parsers, endpoint scanners) | `FrontendApiExtractor` and route-scan reuse the TS/JS/C# parsers instead of raw regex. |
| LLM + context | `Services/Engine/Core/ILLMProvider`, `Context/ContextBuilder`, `Session/SessionManager`, `BusinessLogic/AIClient.cs` (Groq) | `BackendGenerator` and prompt-to-stack use these, same as `MobilePreview.ProcessAiRequest`. |

**Net new code is small**: the extractor, the matcher, the resolver/generator glue, one MCP server,
one CLI command, and the workbench/graph Razor. Everything heavy is already in the repo.

---

## Monaco editor reuse — analysis

`Components/IDE/CodeEditor.razor` already drives Monaco through `IEditorEngine` and the
`window.SyncroIde` JS bridge (`initLayout`, `mountTerminal`, `Editor.OpenAsync(uri, content, lang, name)`,
plus `OnEditorChangedJS` / `OnEditorSaveJS` callbacks). Three concrete reuse moves:

1. **Same engine, two workspaces.** The Connector opens *two* roots (FE and BE). `IdeWorkspaceState`
   is currently single-workspace; introduce `ConnectorWorkspaceState` holding `Frontend` and `Backend`
   `WorkspaceModel`s and a `Side` enum on each tab. Monaco URIs become `fe:///…` and `be:///…` so the
   one editor instance can show files from both repos without collision.
2. **Decorations = the connection overlay.** Monaco supports gutter/inline decorations. When the
   matcher finds an `ApiCallSite` at `fe:///lib/api/user_api.dart:42`, add a clickable gutter glyph
   that jumps to the matched `be:///routes/user.js:18`. This is the editor-level twin of the center graph
   — wire it via a new `window.SyncroConnector.decorate(uri, ranges)` JS call alongside the existing bridge.
3. **Diff editor for generated backend.** Monaco's `createDiffEditor` shows AI-proposed backend files
   as before/after. Reuse the same JS module; add `window.SyncroConnector.openDiff(uri, original, modified)`.
   The user approves → write to disk → `SmartReload`/restart, mirroring `MobilePreview`'s transactional write.

No new editor library. We extend the existing JS bridge with a `SyncroConnector` namespace that sits
next to `SyncroIde`.

---

## The new layout plan ("Connector mode")

A 4-region GoldenLayout, same portal pattern as `IdeShell.razor`:

```
┌───────────────────────────────────────────────────────────────────────────┐
│  Connector title bar — [Frontend ▸ Flutter]  ⇄  [Backend ▸ Express]  ● LIVE │
├──────────────────────┬───────────────────────┬────────────────────────────┤
│  LEFT                 │  CENTER                │  RIGHT                      │
│  Monaco editor        │  Connection Graph      │  Live Preview              │
│  (FE + BE files,      │  ┌FE call┐   ┌BE route┐│  ┌──────────────┐          │
│   fe:/// & be:///)    │  │GET     │──▶│GET     ││  │  device frame │ (mobile) │
│  + gutter glyphs that │  │/users  │ ✓ │/users  ││  │  iframe →     │   or      │
│  link call → route    │  │POST    │──▶│  (none)││  │  flutter web  │  WebPreview│
│                       │  │/orders │ ✗ │ 🔴 gen ││  │  hot reload   │  (Next)   │
├──────────────────────┴───────────────────────┴────────────────────────────┤
│  BOTTOM — tabbed: [Backend Terminal] [Build Output] [AI Composer] [Hindsight]│
│  AI: "add an orders endpoint that matches the Flutter call" → parallel gen    │
└───────────────────────────────────────────────────────────────────────────┘
```

- **Right pane** is literally `MobilePreview.razor` for Flutter, or the new `WebPreview.razor` for
  Next/Blazor/Vite — same hot-reload UX the user already has ("a side window of output running like now").
- **Center** is the headline feature: every frontend call rendered as a node, every backend route as a
  node, edges colored by match state. Clicking an edge focuses both files in the left editor.
- **Bottom** reuses the IdeShell panels (terminal, output, AI composer) — the backend runs here while the
  frontend runs in the right pane, so **both sides run in parallel**.

Add it as a new entry in `IdeShell`'s activity bar (`new() { Id = "connector", Icon = "bi bi-diagram-3" }`)
and a `/connector` route, so it lives beside Explorer/AST/AI rather than replacing them.

---

## Data model (`Services/Connector/Models/`)

```csharp
// One HTTP call discovered in frontend source.
public sealed class ApiCallSite {
    public string Id;                 // stable hash of file+line+method+path
    public string FilePath;           // fe:///lib/api/user_api.dart
    public int Line;
    public HttpVerb Method;           // GET/POST/PUT/PATCH/DELETE
    public string PathTemplate;       // /api/users/{id}  (placeholders normalized)
    public string? BaseUrlSymbol;     // e.g. ApiConfig.baseUrl  (so we can resolve env)
    public List<ApiField> RequestShape;   // inferred body fields
    public List<ApiField> ResponseShape;  // inferred from model/fromJson
    public string Client;             // dio | http | fetch | axios | HttpClient
}

// One route exposed by the backend.
public sealed class BackendRoute {
    public string Id;
    public HttpVerb Method;
    public string PathTemplate;
    public string? FilePath;          // be:///routes/user.js (null if only in swagger)
    public string Source;             // swagger | routescan | generated
    public List<ApiField> RequestSchema;
    public List<ApiField> ResponseSchema;
}

// One edge in the Connection Graph.
public sealed class ConnectionEdge {
    public ApiCallSite Call;
    public BackendRoute? Route;       // null = unmatched (frontend orphan)
    public MatchState State;          // Matched | ShapeMismatch | MethodMismatch | Missing
    public List<string> Diffs;        // human-readable shape differences
}

public enum MatchState { Matched, ShapeMismatch, MethodMismatch, Missing }
public enum HttpVerb { Get, Post, Put, Patch, Delete, Head, Options }
public sealed class ApiField { public string Name; public string Type; public bool Required; }
```

---

## `connector.json` — the project contract (runtime twin of this .md)

Written by the wizard / engine into the connected project root. This is the **"project connector.md"**
the request asks for, in machine-readable form (a `connector.md` human summary is generated alongside it):

```json
{
  "version": 1,
  "frontend": {
    "path": "../mobile-app",
    "stack": "flutter",
    "runCommand": "flutter run -d web-server",
    "apiClient": "dio",
    "baseUrlSymbol": "ApiConfig.baseUrl",
    "previewUrl": "http://localhost:5000"
  },
  "backend": {
    "path": "../api",
    "stack": "express",
    "runCommand": "npm run dev",
    "port": 5000,
    "swaggerPath": "openapi.yaml"
  },
  "mappings": [
    { "call": "GET /api/users",      "route": "GET /api/users",  "state": "Matched" },
    { "call": "POST /api/orders",    "route": null,              "state": "Missing", "action": "generate" }
  ],
  "ai": { "provider": "groq", "lastPrompt": "..." }
}
```

`ConnectorProjectStore` loads/saves this; the workbench renders from it; the MCP tools mutate it.

---

## File map (≈ 18 C# files + 4 Razor + 1 JS module + 1 CLI)

```
Services/Connector/
├── ConnectorEngine.cs                 [1]  DI singleton; orchestrates a connect() pass
├── Models/
│   ├── ApiCallSite.cs                 [2]
│   ├── BackendRoute.cs                [3]
│   ├── ConnectionEdge.cs              [4]
│   ├── ConnectorProject.cs            [5]  maps connector.json
│   └── Enums.cs                       [6]  HttpVerb, MatchState, FrontendStack, BackendStack
├── Frontend/
│   ├── IFrontendApiExtractor.cs       [7]  contract
│   ├── FlutterApiExtractor.cs         [8]  Dio/http calls in .dart (reuse AST where possible)
│   ├── WebApiExtractor.cs             [9]  fetch/axios in .ts/.tsx/.js (reuse TS parser)
│   └── BlazorApiExtractor.cs          [10] HttpClient calls in .cs/.razor (Roslyn via AstService)
├── Backend/
│   ├── BackendContractResolver.cs     [11] swagger → routescan → AI, in that order
│   ├── RouteScanner.cs                [12] Express/FastAPI/Spring/ASP.NET route extraction
│   └── BackendGenerator.cs            [13] AI scaffold from unmatched ApiCallSites (parallel)
├── Matching/
│   └── ContractMatcher.cs            [14]  ApiCallSite[] × BackendRoute[] → ConnectionEdge[]
├── Storage/
│   └── ConnectorProjectStore.cs       [15] read/write connector.json + connector.md summary
├── Mcp/
│   └── ConnectorMcp.cs                [16] IMcpServer exposing tools [17]–[21] below
└── ConnectorServiceCollectionExtensions.cs  [22] AddSyncroConnector(this IServiceCollection)

Components/Pages/Connector/
├── ConnectorWorkbench.razor           GoldenLayout shell (clone of IdeShell pattern)
├── ConnectionGraph.razor              center pane — nodes + colored edges
├── WebPreview.razor                   right pane twin of MobilePreview for Next/Blazor/Vite
└── ConnectorWizard.razor              "Connected Stack" creation flow (extends CreateProjectDialog)

wwwroot/js/syncro-connector.js         window.SyncroConnector: decorate(), openDiff()
Services/SyncroCLI/Commands/ConnectorCommand.cs   `syncro connect <fe> <be>`
```

---

## MCP server — `ConnectorMcp` (the "MCP server engine with AI")

Implements `IMcpServer`; each tool implements the existing `IMcpTool` contract
(`Name`, `Description`, `InputSchema`, `RequiresAdminApproval`, `ExecuteAsync(json)`), registered through
`McpOrchestrator.RegisterServerAsync`. The agent loop (and the AI composer in the workbench) call these:

| Tool | Input | Output | Admin? |
|---|---|---|---|
| `ExtractFrontendApi` | `{ frontendPath, stack }` | `ApiCallSite[]` JSON | no |
| `ResolveBackendContract` | `{ backendPath }` | `BackendRoute[]` (swagger\|scan\|empty) | no |
| `MapContracts` | `{ frontendPath, backendPath }` | `ConnectionEdge[]` + summary counts | no |
| `GenerateBackendForCall` | `{ callId, backendStack, architecture }` | proposed files (diff) | **yes** (writes) |
| `SyncConnectorContract` | `{ }` | rewrites `connector.json` + `connector.md` | no |

`GenerateBackendForCall` is where **parallel backend AI generation** lives: given N unmatched
`ApiCallSite`s, fan out N generation calls (one per endpoint) using `ILLMProvider`, each prompted with
the call's request/response shape + the chosen backend template (`ProjectTemplates/express.md` etc.) so
generated routes match the FE's expectations. Results return as Monaco diffs for approval, then write +
hot-restart the backend — exactly the transactional pattern in `MobilePreview.ProcessAiRequest`.

---

## "AI sets tech stack + folder structure" flow

The prompt-to-stack path (in `ConnectorWizard` and the AI composer):

1. User picks/clones a **frontend repo** in the wizard (reuse `CloneGitProjectDialog.razor` / `PickFolderAsync`).
2. `FrontendApiExtractor` runs → we now know the FE stack and its API calls.
3. User types a prompt: *"Node + Postgres backend, clean architecture."*
4. `ContextBuilder` builds a system prompt with the extracted `ApiCallSite[]` + the matching
   `ProjectTemplates/*.md` (which already encode `architectures: [flat, ntier, clean]`, folder layout,
   `dependencies`, `run`).
5. `BackendGenerator` emits the folder structure + files for every endpoint, in parallel, honoring the
   chosen architecture from the template front-matter.
6. `ConnectorProjectStore` writes `connector.json` + a generated `connector.md` describing the wiring.
7. Both sides launch: FE in the right preview pane (`FlutterService.RunProject`), BE in the bottom terminal.

---

## Build order (incremental, each step shippable)

1. **Models + store** `[2]–[6,15]` — `connector.json` round-trips; no UI yet.
2. **Frontend extractor (Flutter first)** `[7,8]` — `syncro connect` prints `ApiCallSite[]` to console.
3. **Backend resolver** `[11,12]` — reuse `ParseSwaggerTool`; route-scan Express/FastAPI.
4. **Matcher** `[14]` — produce `ConnectionEdge[]`; CLI prints the match table.
5. **Workbench shell + graph** (`ConnectorWorkbench`, `ConnectionGraph`) — wire the right pane to
   existing `MobilePreview`; render edges from the matcher.
6. **MCP server** `[16]` + register in `MauiProgram.cs` — agent can now call the engine.
7. **Backend generator (parallel AI)** `[13]` + Monaco diff approval — close the loop.
8. **Web extractors + WebPreview** `[9,10]` — extend beyond Flutter to Next/Blazor.

---

## Registration (MauiProgram.cs)

```csharp
builder.Services.AddSyncroConnector();          // [22] — engine, extractors, store
// after the existing McpOrchestrator setup:
await orchestrator.RegisterServerAsync(new ConnectorMcp(engine));   // [16]
```

---

## Open questions to confirm before coding

- **First frontend target**: Flutter only for v1 (matches `MobilePreview`), web stacks in step 8 — OK?
- **Backend generation default**: scaffold into a *new* sibling repo, or into an existing backend the
  user points at? (`connector.json.backend.path` supports both.)
- **Matching strictness**: should a method/path match with a body-shape diff be amber (warn) or red (block)?
