# IDE & UI Development — Hybrid Blazor + JS Architecture

> **Thesis:** Syncro IDE is not a CRUD app. It is in the class of **VS Code / Rider / Android
> Studio / Unity Editor** — docking, split editors, thousands of UI updates/sec, virtualized
> trees, canvas/WebGL graphs, an integrated terminal, Monaco. **MudBlazor is the wrong primitive
> for that surface.** The right architecture is **Hybrid Blazor + JS**: C# owns business logic and
> shell chrome; JavaScript owns the high-frequency, canvas, and editor surfaces.
>
> This isn't a greenfield bet — **the seam already exists in this repo.** `CodeEditor.razor`
> already imports a JS module (`./js/ide/editor.js`) and calls `attach`/`refresh` over interop
> ([CodeEditor.razor:66](Components/IDE/CodeEditor.razor:66)). We formalize and extend that seam
> instead of fighting Blazor's diffing for things the DOM/JS world already does at 10/10.
>
> Sister docs: [`engine/optimisation server cli.md`](engine/optimisation%20server%20cli.md)
> (runtime/servers/DB runner), [`mappertodo.md`](mappertodo.md) (NLP router),
> [`vikingide.md`](vikingide.md) (full vision), [`ui_architecture.md`](ui_architecture.md).

---

## 0. The decision (and why)

| Layer | Technology | Rating for IDEs | Role in Syncro |
|---|---|---|---|
| Dashboards, settings, auth, dialogs, forms, snackbars, the **main app** outside the IDE | **MudBlazor** | 10/10 admin · **6/10 IDE** | keep — it's already the app shell and theme |
| Window/shell, routing, DI, services, state | **Blazor (C#)** | 7.5/10 — no docking, no IDE widgets | keep — business logic stays in C# |
| **Editor, docking, terminal, graphs, canvas, virtualization** | **JS libraries** (Monaco, Golden Layout, Xterm.js, Cytoscape, D3, Split.js) | **10/10** — what VS Code / Cursor / Codespaces actually use | **adopt** — the IDE surface lives here |

**Rule of the seam:** *Keep business logic in C#. Keep complex, high-frequency UI in JavaScript.*
Everything below is the contract that makes that split clean, debuggable, and reversible. The
**open-source sourcing, licensing, and VS Code/Electron source decomposition** — which projects to
reuse, which to only steal ideas from, and why none of it embeds an IDE wholesale — are in
§3.2–§3.5.

### 0.1 Why not push MudBlazor harder

MudBlazor is excellent at what it's for (the main app's Dashboard/Projects/Settings/Logs/Social
pages all use it well). For the IDE surface you would end up **re-implementing docking, a
virtualized scroll engine, and a text-editing core** on top of Blazor's render diff — the exact
work the JS ecosystem already shipped and battle-tested. The editor is the proof: today it is a
`<textarea>` + a `<pre>` gutter ([CodeEditor.razor:30](Components/IDE/CodeEditor.razor:30)) with
no IntelliSense, folding, multi-cursor, minimap, or large-file virtualization — because doing
those in Blazor is a research project; in Monaco it's a `<script>` tag.

### 0.2 Why Blazor stays (don't rewrite the app in JS)

Syncro's value is the **brain** — AST, Hindsight RAG, agent loop, NLP, the file DB. That is all
C# and stays C#. The JS layer is **dumb surface**: it renders, captures input, and calls back
into C# for anything that touches the project. No business logic crosses into JS.

---

## 1. The layered architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  .NET MAUI  (native window, lifecycle, OpenWindow)                            │
│   └─ BlazorWebView (one per IDE window)                                        │
│       └─ Blazor / Razor  ── shell, routing, DI, MudBlazor chrome (C#)          │
│           │  state: IdeWorkspaceState (scoped)   services: FileService, …      │
│           │                                                                    │
│           ▼  JS interop boundary  (IJSObjectReference ⇄ DotNetObjectReference) │
│       ┌────────────────────── wwwroot/js/ide/* (ES modules) ──────────────────┐│
│       │  monaco.js   docking.js   terminal.js   graph.js   tree.js   editor.js ││
│       │   Monaco      GoldenLayout  Xterm+ConPTY  Cytoscape  virtual   (seed)  ││
│       └─────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────────────┘
   Business logic NEVER lives in JS. JS calls back to C# for project-aware work.
```

- **One BlazorWebView per IDE window** (already true via `IdeWindowService.Open` →
  `new Window(new IdePage())`, [IdeWindowService.cs:20](Services/Ide/IdeWindowService.cs:20)).
- **One scoped `IdeWorkspaceState` per window** ([SyncroIdeServiceCollectionExtensions.cs:14](Services/Ide/SyncroIdeServiceCollectionExtensions.cs:14)).
- **JS modules are lazy-imported** per component (`JS.InvokeAsync<IJSObjectReference>("import", …)`),
  exactly as `CodeEditor` does today ([CodeEditor.razor:66](Components/IDE/CodeEditor.razor:66)).

---

## 2. The JS interop contract (the heart of the hybrid)

Every JS surface follows one disciplined pattern, generalized from the working `editor.js` seam.

### 2.1 Module shape

```js
// wwwroot/js/ide/<surface>.js   — ES module, one per surface
let instances = new Map();                 // elementId -> { widget, dispose }

export function attach(el, dotnet, options) {
    const widget = createWidget(el, options);          // Monaco / Xterm / Cytoscape / GoldenLayout
    widget.onChange(v => dotnet.invokeMethodAsync('OnChanged', v));   // JS → C# callback
    instances.set(el.id, { widget, dispose: () => widget.dispose() });
    return el.id;                                       // handle
}
export function call(id, method, arg) { return instances.get(id)?.widget[method]?.(arg); }
export function dispose(id) { instances.get(id)?.dispose(); instances.delete(id); }
```

### 2.2 C# side (mirror of CodeEditor's lifecycle)

```csharp
_mod  ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/ide/monaco.js");
_self ??= DotNetObjectReference.Create(this);
await _mod.InvokeVoidAsync("attach", _el, _self, options);
// …
[JSInvokable] public Task OnChanged(string value) { /* update buffer, mark dirty */ }
public async ValueTask DisposeAsync() { await _mod.DisposeAsync(); _self?.Dispose(); }   // already done today
```

### 2.3 Non-negotiable interop rules

1. **Data crosses the boundary, not behavior.** JS gets text + options; C# gets events. No
   project knowledge (paths, AST, RAG) is computed in JS.
2. **Always `DotNetObjectReference` + `IAsyncDisposable`.** The current editor leaks nothing
   ([CodeEditor.razor:106](Components/IDE/CodeEditor.razor:106)) — keep that for every surface.
3. **Batch chatty calls.** A keystroke must not be one interop round-trip per char. Monaco owns
   its buffer; C# pulls on save / debounced sync (the textarea today already keeps the buffer in
   `IdeWorkspaceState.Buffers` and only writes on Save — [CodeEditor.razor:88,99](Components/IDE/CodeEditor.razor:88)).
4. **Degrade gracefully.** If a module fails to import, the feature is cosmetic-optional — the
   editor already wraps interop in `try/catch` and notes "gutter is cosmetic; editing still works"
   ([CodeEditor.razor:80](Components/IDE/CodeEditor.razor:80)). Same posture for every surface.
5. **Theme via CSS variables** shared between MudBlazor and the JS widgets, so Monaco/Xterm match
   the app theme without duplicating color constants.

### 2.4 Every external dependency behind a C# interface (provider model)

The single most important durability rule, and the one refinement on top of "Golden Layout now,
Lumino later": **C# never names a JavaScript library.** It talks to an interface; a JS-backed
implementation fulfills it over interop. Swapping the underlying library (Golden Layout → Lumino →
a future native MAUI dock) is then *one implementation class*, with **zero** changes to panels,
state, or services.

```csharp
public interface IEditorEngine   { Task OpenAsync(string uri, string content, string lang);
                                   Task<string> GetContentAsync(string uri);
                                   Task ShowDiffAsync(string uri, string original, string modified);
                                   event Action<string /*uri*/, string /*content*/> Changed; }

public interface ITerminalEngine { Task<string> CreateAsync(string cwd, string shell);
                                   Task WriteAsync(string id, string bytes);   // C# → PTY
                                   event Action<string /*id*/, string /*bytes*/> Output; }

public interface IDockEngine     { Task<string> AddPanelAsync(PanelSpec spec);
                                   Task<string> SaveLayoutAsync();             // serialize
                                   Task LoadLayoutAsync(string json);          // restore
                                   event Action<string /*panelId*/> PanelClosed; }

public interface IGraphRenderer  { Task RenderAsync(string elementId, GraphModel nodesEdges);
                                   event Action<string /*nodeId*/> NodeActivated; }

public interface IDiagramRenderer { Task RenderAsync(string elementId, string mermaidSource); }
```

| Interface | Default impl (today) | JS module | Swap target later |
|---|---|---|---|
| `IEditorEngine` | `MonacoEditorEngine` | `monaco.js` | CodeMirror, etc. |
| `ITerminalEngine` | `XtermEngine` (+ ConPTY via `ICliRunner`) | `terminal.js` | — |
| `IDockEngine` | `GoldenLayoutEngine` | `docking.js` | `LuminoEngine` / native MAUI dock |
| `IGraphRenderer` | `CytoscapeRenderer` | `graph.js` | D3, Sigma |
| `IDiagramRenderer` | `MermaidRenderer` | `diagram.js` | — |

The interface is the contract from §2.1–§2.3 made explicit and named. A panel asks the DI container
for `IDockEngine`, not `GoldenLayoutEngine`; it never imports `docking.js` itself. **This is what
keeps Syncro independent of any specific JS library** and makes every box in the stack replaceable
without a rewrite — see the work-package roadmap in §11.

---

## 3. Surface-by-surface library selection

| Surface | Today | Adopt | Interop module | Notes |
|---|---|---|---|---|
| **Code editor** | `<textarea>` + `<pre>` gutter ([CodeEditor.razor:30](Components/IDE/CodeEditor.razor:30)) | **Monaco** (BlazorMonaco wrapper or raw) | `monaco.js` (replaces `editor.js`) | IntelliSense, folding, multi-cursor, minimap, large-file virtualization, diff view (feeds §6 versioning) |
| **Docking / layout** | fixed flex panes ([IdeShell.razor:25](Components/IDE/IdeShell.razor:25)) | **Golden Layout** (or DockSpawn) | `docking.js` | drag-drop panels, split editors, save/restore layout per workspace |
| **Split panes** | none | **Split.js** | folded into `docking.js` | lightweight resizable splits where full docking is overkill |
| **Drag/resize** | none | **Interact.js** | `interact.js` | panel drag, gutter drag, reorder tabs |
| **Integrated terminal** | "Phase 2" placeholder ([IdeShell.razor:121](Components/IDE/IdeShell.razor:121)) | **Xterm.js + ConPTY** | `terminal.js` | real PTY; C# spawns the shell via the engine's `ICliRunner`, streams bytes over interop |
| **File tree** | Blazor recursive `FileTreeNode` | **virtualized tree** (Clusterize/custom) over `FileService.ListAsync` | `tree.js` | thousands of nodes without DOM blowup; C# still owns ignore-set + lazy children ([FileService.cs:18](Services/Ide/FileService.cs:18)) |
| **Dependency / AST graph** | none (Analysis window is read-only) | **Cytoscape.js** (graph) + **D3** (charts) | `graph.js` | render AST/knowledge graph from C#-computed nodes/edges |
| **Markdown / docs** | none in IDE | **markdown-it** + highlight.js | `markdown.js` | preview generated docs, agent plans |
| **Diagrams** | none | **Mermaid** (JointJS only if interactive editing needed) | `diagram.js` | architecture & sequence diagrams, task graphs, pipeline views |
| **AI chat** | main-app pages | Blazor + Monaco-rendered code blocks | reuse `monaco.js` | chat is low-frequency → Blazor is fine; code blocks reuse Monaco read-only |
| **Chrome: dialogs, snackbars, menus, settings** | **MudBlazor** ([CodeEditor.razor:8](Components/IDE/CodeEditor.razor:8) uses `ISnackbar`) | **keep MudBlazor** | — | this is MudBlazor's 10/10 zone |

### 3.1 What stays pure Blazor (deliberately)

Activity bar, status bar, tab strip chrome, command palette, settings panes, the welcome screen
([IdeShell.razor:78](Components/IDE/IdeShell.razor:78)) — all **low-frequency** and project-aware.
Blazor + MudBlazor is the right tool; pushing them into JS would scatter business logic across the
boundary for no rendering benefit.

### 3.2 Open-source provenance & license posture (ship-safe)

Syncro is a **shipped, closed-source desktop binary**. License is therefore a hard gate, not a
footnote: a strong-copyleft (GPL) dependency linked into the app would force source disclosure, and
a weak-copyleft framework (EPL) pulled in wholesale brings file-level obligations. The rule:
**reuse permissive (MIT/BSD/Apache-2.0) render-layer libraries; never embed a GPL app; never pull a
copyleft *framework* wholesale.**

| Component | Project | License | Ship in closed desktop? | Why we use it |
|---|---|---|---|---|
| **Monaco Editor** | `microsoft/monaco-editor` (extracted from VS Code `src/vs/editor`) | **MIT** | ✅ yes | the actual VS Code editor core |
| **Xterm.js** | `xtermjs/xterm.js` (`@xterm/xterm`) | **MIT** | ✅ yes | VS Code's terminal renderer; industry standard |
| **Golden Layout** | `golden-layout/golden-layout` | **MIT** | ✅ yes | mature docking (tabs/split/float/drag) |
| **Lumino** | `jupyterlab/lumino` (ex-PhosphorJS) | **BSD-3** | ✅ yes | docking behind JupyterLab **and Theia** — alt to Golden Layout |
| **Split.js** | `nathancahill/split` | **MIT** | ✅ yes | lightweight resizable panes |
| **Interact.js** | `taye/interact.js` | **MIT** | ✅ yes | drag/resize/reorder |
| **Cytoscape.js** | `cytoscape/cytoscape.js` | **MIT** | ✅ yes | AST / dependency / call graphs |
| **Mermaid** | `mermaid-js/mermaid` | **MIT** | ✅ yes | architecture & sequence diagrams |
| **monaco-languageclient** | `TypeFox/monaco-languageclient` | **MIT** | ✅ yes (optional) | wire Monaco to LSP — *but see §3.4, we may skip it* |
| VS Code **source** | `microsoft/vscode` | **MIT** (source) | ✅ source is MIT | extract patterns/components; the *branded binary* is proprietary, the repo is not |
| Eclipse **Theia** | `eclipse-theia/theia` | **EPL-2.0 / GPL-2.0-cpe** | ⚠️ framework copyleft | steal ideas + its Lumino usage; **don't** embed the framework |
| **Zed** | `zed-industries/zed` | **GPL-3.0** | ❌ no | strong copyleft + native Rust; ideas only |
| **Lapce** | `lapce/lapce` | Apache-2.0 | ❌ not embeddable | permissive but native Rust UI; ideas only |
| **Judge0 API** | `judge0/judge0` | **GPL-3.0** | ⚠️ as a service only | run as a **self-hosted network service** (no linking), not in-process |
| Judge0 **IDE** | `judge0/ide` | MIT | ✅ reference | clean Monaco+exec layout to learn from |
| OpenVSCode / code-server | gitpod / coder | MIT | ⚠️ don't embed | full VS Code web server; black-box, not MAUI-friendly |
| Pulsar | `pulsar-edit/pulsar` | MIT | ⚠️ ideas | Electron/Atom successor; tree-sitter ideas |

**Net:** every box in Syncro's chosen stack (Monaco, Xterm, Golden Layout/Lumino, Split.js,
Cytoscape, Mermaid) is **MIT/BSD** — clean to ship. The copyleft and native projects (Theia, Zed,
Lapce, Judge0-core) contribute **ideas and architecture**, not linked code.

### 3.3 VS Code / Electron source — what is actually reusable

VS Code is the richest source to mine, but most of it is **not extractable** because it is welded to
two things Syncro does not have: VS Code's internal **service/DI layer** and an **Electron + Node**
backend. The honest decomposition:

| VS Code subsystem | Source path | Reusable here? | Syncro's path |
|---|---|---|---|
| **Editor core** | `src/vs/editor` → published as `monaco-editor` | ✅ **reuse as-is** | Monaco via `monaco.js` interop |
| **Terminal renderer** | uses external `xterm.js` | ✅ **reuse as-is** | Xterm via `terminal.js`; PTY from C# |
| **Tree widget** (async, virtualized) | `src/vs/base/browser/ui/tree` + `…/list` | ⚠️ **pattern only** (not published standalone) | custom virtualized Blazor/JS tree over `FileService.ListAsync` ([FileService.cs:18](Services/Ide/FileService.cs:18)) |
| **Split view / Sash / Grid** (the real "docking") | `src/vs/base/browser/ui/{splitview,sash,grid}` | ⚠️ **pattern only** (coupled to workbench) | Golden Layout / Lumino instead |
| **Command + keybinding registry** (palette) | `src/vs/platform/{commands,keybinding}` | ⚠️ **pattern only** | C# command registry + MudBlazor palette (stays Blazor, §3.1) |
| **Search** | bundles `@vscode/ripgrep` (child process) | ✅ **the tool**, not the code | C# shells ripgrep via `ICliRunner`, or reuse Syncro's own AST/Grep |
| **Language intelligence** | LSP via `vscode-languageserver-*` | ⚠️ optional | **Syncro skips LSP** — feeds Monaco from in-process Roslyn (see below) |
| **FileSystemProvider** abstraction | `vscode` API | ⚠️ **pattern only** | `FileService` already is this abstraction |
| **Workbench** (parts, editor groups, viewlets) | `src/vs/workbench` | ❌ **do not reuse** | this is "embedding VS Code" — our custom shell replaces it |
| **Extension host** | Node process | ❌ no Node in MAUI | Syncro's MCP/agent layer is the equivalent |

#### The Electron-vs-MAUI fault line (the crux)

```
VS Code        =  Electron  =  Chromium (renderer)  +  Node.js (main/extension host)
Syncro IDE     =  MAUI      =  WebView2 (Chromium)   +  ❌ no Node  →  C# is the backend
```

Everything VS Code does in **Node** — file watching, ripgrep search, LSP servers, the extension
host — Syncro must do in **C#**. The good news: **Syncro already has those backends** —
`FileService` (fs), `AstService`/Roslyn (language intel), `CliWrap` (`ICliRunner`, child
processes), `SyncroDb` (state), the MCP servers (tools). So the reuse strategy is precise:

> **Reuse VS Code's render-layer libraries (Monaco, Xterm) through interop; replace its Node
> backend with Syncro's existing C# services.** Never try to lift the workbench.

#### Syncro's edge over the generic VS Code model

VS Code talks to language servers over LSP because it has **no language knowledge in-process**.
Syncro **does**: `CSharpAstParser` runs **Roslyn in-process**, and the AST engine already produces
symbols, imports, and a project map. So Monaco can be fed **diagnostics, hovers, go-to-def, and
completions directly from C#** via interop — **no LSP child process, no `monaco-languageclient`
round-trip** for first-party languages. That is faster and simpler than VS Code's own pipeline for
the languages Syncro parses natively, and it's a genuine differentiator, not just parity.

### 3.4 OSS IDE landscape — embed / steal / avoid (verdicts)

| Project | Verdict | One-line reason |
|---|---|---|
| **Monaco** | **REUSE** | the editor; don't write your own |
| **Xterm.js** | **REUSE** | the terminal; PTY from C# (ConPTY) |
| **Golden Layout / Lumino** | **REUSE (pick one)** | docking VS Code's grid isn't published; these are |
| **Cytoscape / Mermaid / Split.js** | **REUSE** | graphs, diagrams, panes — all MIT |
| **Eclipse Theia** | **STEAL IDEAS** | best reference for "IDE as composable widgets + services + AI/MCP", but EPL framework + Node backend → don't embed |
| **Judge0 (IDE)** | **STEAL IDEAS** | clean editor→compile→output pipeline; run Judge0 *API* only as a self-hosted service if multi-language sandboxed exec is wanted |
| **OpenVSCode Server / code-server** | **AVOID embedding** | full VS Code web server in an iframe = a black box that defeats the C# integration; consider only as a future "remote workspace" mode |
| **Zed** | **STEAL IDEAS** | GPU UI, rope/sum-tree buffers, CRDT collab — inspiration; GPL-3.0 + native Rust → not reusable |
| **Lapce / Pulsar** | **STEAL IDEAS** | rope editor (Lapce), tree-sitter (Pulsar) — not embeddable in MAUI |
| **Eclipse Che** | **AVOID** | Kubernetes cloud-IDE platform; overkill; ideas (devfile/workspace-as-container) only |

**Decision: build a custom shell, reuse mature subsystems.** Do **not** embed any IDE wholesale —
not VS Code, not Theia, not IntelliJ/Rider — they assume a Node or JVM backend and their own window
manager, neither of which fits a single-process MAUI app with a C# brain.

### 3.5 Settled stack with provenance

```
Syncro IDE shell (custom: Blazor + MudBlazor chrome, C# services)
│
├── Code Editor ........ Monaco            (MIT, ex-VS Code)        monaco.js
├── Terminal ........... Xterm.js + ConPTY (MIT + C# PTY)           terminal.js
├── Docking ............ Golden Layout     (MIT)  [or Lumino BSD]   docking.js
├── Split panes ........ Split.js          (MIT)                    (in docking.js)
├── Drag/resize ........ Interact.js       (MIT)                    interact.js
├── File Tree .......... custom virtualized (over FileService)      tree.js
├── AST / Dep Graph .... Cytoscape.js      (MIT)                    graph.js
├── Diagrams ........... Mermaid           (MIT)                    diagram.js
├── Git Panel .......... custom Blazor      (over Git services)     —
├── AI Chat ............ custom Blazor + Monaco code blocks         (reuse monaco.js)
├── Agent Monitor ...... existing :3030 dashboard / Blazor          —
└── Language intel ..... in-process Roslyn → Monaco (no LSP)        (C# → interop)

Every panel is independent and replaceable — exactly the VS Code model, minus the Node backend.
```

**Golden Layout vs Lumino:** both are mature and MIT/BSD. **Golden Layout** is simpler to drop in
and is purpose-built for app docking. **Lumino** is heavier but is what **Theia and JupyterLab**
ship, so it's proven at IDE scale and has richer dock semantics. Recommendation: **start with Golden
Layout** (faster to integrate, smaller surface), keep the docking behind our own `docking.js`
facade so swapping to Lumino later is a one-module change — the interop contract (§2.1) makes the
docking engine itself replaceable.

---

## 4. Parallel project handling — opening with context

The robustness concern from the original plan survives the architecture change; the docking layer
makes it *more* important (each window now restores a full layout, not just tabs).

### 4.1 The launch hand-off (partially built — finish it)

`IdeShell` already takes a `Token` and claims its payload via
`LaunchCtx.TryClaim(Token, out var req)` ([IdeShell.razor:237](Components/IDE/IdeShell.razor:237)),
and `IdePage` passes the token in. **This is the one-shot mailbox pattern — keep it.** Verify the
launcher side mints a token per `Open()` and that `IdeLaunchContext` is a one-shot registry (claim
removes the entry) so two windows opened back-to-back never read each other's payload. If
`IdeLaunchContext` is still a single-slot `{ WorkspacePath; AnalysisPath }`
([IdeLaunchContext.cs:8](Services/Ide/IdeLaunchContext.cs:8)), upgrade it to:

```csharp
public sealed class IdeLaunchContext
{
    private readonly ConcurrentDictionary<string, IdeLaunchRequest> _pending = new();
    public string Stage(IdeLaunchRequest r) { var t = Guid.NewGuid().ToString("n"); _pending[t] = r; return t; }
    public bool TryClaim(string token, out IdeLaunchRequest r) => _pending.TryRemove(token, out r!);
}
public record IdeLaunchRequest(string? WorkspacePath, string? AnalysisPath, string Mode);
```

### 4.2 De-dupe: focus an open project instead of cloning

`IdeWindowService` (singleton, [IdeWindowService.cs:7](Services/Ide/IdeWindowService.cs:7)) tracks
open workspaces by id (`IdeWorkspaceState.Hash`, [IdeWorkspaceState.cs:92](Services/Ide/IdeWorkspaceState.cs:92),
promoted to a public `static WorkspaceId(path)`). Opening the same path focuses the existing
window via `Application.Current.ActivateWindow` instead of spawning a duplicate that would
clobber the shared `session_{id}.json`.

### 4.3 Per-window layout + context restore

Each window restores **two** things on open:

1. **Layout** — Golden Layout serialized JSON, persisted per workspace alongside tabs in
   `IdeStateStore` ([IdeStateStore.cs:30](Services/Ide/IdeStateStore.cs:30)). Phase-1 stores tabs
   only; extend the same JSON store with `layout` (no caller changes — the store already abstracts
   persistence and the comment plans the SQLite upgrade, [IdeStateStore.cs:11](Services/Ide/IdeStateStore.cs:11)).
2. **Project context** — assembled off-thread so the shell paints instantly:

```csharp
public async Task OpenWorkspaceAsync(string rootPath)
{
    Current = new Workspace { Id = WorkspaceId(rootPath), Name = …, RootPath = rootPath };
    foreach (var t in await _store.LoadSessionAsync(Current.Id)) Tabs.Add(t);
    Notify();                                          // paint tabs + tree NOW
    _ = Task.Run(async () => {                          // fill context off the UI thread
        Context = await _contextBuilder.BuildAsync(rootPath, Current.Id);
        await MainThread.InvokeOnMainThreadAsync(Notify);
    });
}
```

`ProjectContext` sources already exist: `.syncro_db/Projects/projects.json` (stack/lang/pkg-mgr,
written by the generator), `Groups/groups.json` (sibling services), AST map, Hindsight memory,
and pending tasks via `SyncroDb.QueryTasksAsync(projectId)`. The shell renders a **context strip**
above the editor; if any source throws, the strip shows "context unavailable" and the editor still
works (offline-first).

### 4.4 Parallel-build safety

Two windows building context touch the shared global `.syncro_db`. `SyncroDb` already serializes
writes with `SemaphoreSlim(1,1)` and writes atomically (tmp→move,
[SyncroDb.cs:68](Services/AgentCli/SyncroDb.cs:68)); **reads** add a retry-on-transient helper for
the sub-ms swap window. Per-project context reads from the **project's own** `.syncro_db`
(`SyncroDb.BaseDir` = project path), so different projects never contend.

---

## 5. High-frequency rendering — keeping the UI at 60fps

The user named the real risk: *thousands of UI updates, background indexing, large file trees.*
Blazor's render-diff is not built for that cadence. The hybrid model is precisely the mitigation:

| Hot path | Wrong (Blazor diff) | Right (JS owns it) |
|---|---|---|
| Typing in the editor | re-render component per keystroke | Monaco owns the buffer; C# pulls debounced/on-save |
| Terminal output (1000s lines/sec) | append to a Blazor list | Xterm renders to its own canvas; C# streams bytes |
| File tree of 50k nodes | render every node | virtualized tree, lazy children from `FileService` |
| Live AST/dep graph | Blazor SVG | Cytoscape/WebGL canvas |
| Background indexing progress | `StateHasChanged` flood | coalesced `IProgress` ticks, throttled to ~10/s |

**Discipline:** C# emits *coarse* state changes (file saved, indexing 40%, tab opened). JS handles
the *fine* ones (every keystroke, every scroll row, every animation frame). Never route a 60fps
signal through `StateHasChanged`.

---

## 6. File editor → versioning, LLM-controlled, self-triggered

The headline ask, now expressed in the hybrid model. Monaco's diff engine becomes the UI; C# owns
the history.

### 6.1 Today's gap

`FileService.WriteAsync` is a raw `File.WriteAllTextAsync`
([FileService.cs:51](Services/Ide/FileService.cs:51)) — Save, agent edits, and LLM patches all
overwrite with **no per-file history** beyond the textarea's in-memory state.

### 6.2 Three-tier version model

| Tier | Mechanism | Granularity | Trigger | UI (Monaco) |
|---|---|---|---|---|
| **T0** buffer undo | Monaco's own undo stack | keystroke | user | native |
| **T1** shadow snapshots | copy-on-write → `.syncro_db/ide/history/{relpath}/{ts}.snap` | per save / agent edit | save, agent, LLM | diff vs snapshot in Monaco's `DiffEditor` |
| **T2** git checkpoints | `git` via **CliWrap** (existing dep) on a `syncro/checkpoints` ref | per task / session | task boundary, user | timeline rail |

### 6.3 The versioning service (C#)

```csharp
public interface IFileHistory
{
    Task<string> SnapshotAsync(string path, SnapshotReason reason, string? note = null);
    Task<IReadOnlyList<SnapshotInfo>> ListAsync(string path);
    Task RevertAsync(string path, string snapshotId);        // "revert back"
    Task<string> DiffAsync(string path, string snapshotId);  // -> Monaco DiffEditor
}
public enum SnapshotReason { ManualSave, AgentEdit, LlmPatch, PreBuild, PreRevert }
```

`FileService.WriteAsync` is wrapped to snapshot prior content (content-addressed, deduped) before
writing, with an ambient `SnapshotReason` so history is **attributed** (agent vs LLM vs user).
Index is a `history.jsonl` (mirrors `SyncroDb.AppendJsonlAsync`).

### 6.4 LLM-controlled revert (gated through the existing loop safety)

Expose history as **MCP tools** in the patch family, registered via `McpOrchestrator.RegisterTool`
([McpOrchestrator.cs:20](Services/Engine/Mcp/McpOrchestrator.cs:20)). The `IMcpTool` contract
already carries `RequiresAdminApproval` ([McpOrchestrator.cs:52](Services/Engine/Mcp/McpOrchestrator.cs:52)):

| Tool | Approval | Purpose |
|---|---|---|
| `ListFileHistory` | read | LLM inspects restore points |
| `DiffSnapshot` | read | LLM reads what a revert changes (Monaco renders it for the user too) |
| `RevertToSnapshot` | **mutating** | gated by `TaskLoopEngine.Autonomous` ([TaskLoopEngine.cs:29](Services/AgentCli/Loop/TaskLoopEngine.cs:29)) |

Same trust model as scripts: the LLM may revert autonomously **only** when autonomous mode is on;
otherwise it proposes and the user gates ([TaskLoopEngine.cs:87](Services/AgentCli/Loop/TaskLoopEngine.cs:87)).
A revert snapshots `PreRevert` first, so it is itself undoable.

### 6.5 Self-optimised / edge-case triggers

| Trigger | Action |
|---|---|
| Before any agent/LLM write or `ApplyPatchTool` | T1 snapshot, attributed |
| Before `dotnet build` / native CLI | T2 git checkpoint `PreBuild` |
| File changed on disk while open (mtime drift) | T1 snapshot both sides + Monaco conflict diff |
| Crash/exit with dirty buffers | flush → T1 on next boot (mirrors loop crash-resume, [TaskLoopEngine.cs:52](Services/AgentCli/Loop/TaskLoopEngine.cs:52)) |
| N consecutive failed agent attempts (`t.ErrorCount`, [TaskLoopEngine.cs:113](Services/AgentCli/Loop/TaskLoopEngine.cs:113)) | auto-revert to last `PreBuild`, escalate `NeedsHuman` |
| History store over budget | prune: keep all checkpoints + dense-recent, drop mid dupes (logarithmic backbone) |

### 6.6 UI surface (Monaco-native)

Gutter change dots (diff vs last snapshot), `DiffEditor` side-by-side on hover, a **History rail**
(`SidePanelView.History`) with attribution badges (AgentEdit/LlmPatch/ManualSave/PreBuild),
one-click revert + "undo the revert" toast.

---

## 7. UX robustness — the cross-cutting sweep

The recurring anti-pattern is **fire-and-forget async with empty catches** that leaves the UI
lying. Concrete fixes (unchanged by the architecture, still required):

| Location | Today | Fix |
|---|---|---|
| `IdeStateStore.SaveSessionAsync` | `catch { /* best effort */ }` ([IdeStateStore.cs:37](Services/Ide/IdeStateStore.cs:37)) | keep best-effort, surface a one-time "session not saved" indicator on repeated failure |
| `IdeWorkspaceState.Persist` | fire-and-forget `_ =` ([IdeWorkspaceState.cs:89](Services/Ide/IdeWorkspaceState.cs:89)) | debounce tab churn + observe the task; surface on fault |
| `FileService.ReadAsync` | returns an error **string as file content** ([FileService.cs:47](Services/Ide/FileService.cs:47)) | return `Result<string>`; Monaco shows an error state, not a fake file the user might save back |
| `ProjectGenerator` progress | `onLog` lines only | structured `IProgress<>` → real progress bar (see [`engine/optimisation server cli.md §3.2`](engine/optimisation%20server%20cli.md)) |
| Long ops | no cancel/timeout | `CancellationToken` tied to window-close + per-step timeout |

**Optimistic UI + reconcile:** every action paints immediately (tab ops already do —
[IdeWorkspaceState.cs:57](Services/Ide/IdeWorkspaceState.cs:57)) and reverts on async fault with a
toast. **Empty/loading/error states everywhere** — the monitor dashboard already models these well
([AgentMonitorServer.cs](Services/SyncroCLI/AgentMonitorServer.cs)); bring the same skeletons into
the Blazor IDE so a pane is never blank.

---

## 8. Build & asset pipeline for the JS layer

The hybrid model adds real JS deps — treat them as first-class:

- **Vendoring:** Monaco/Xterm/Cytoscape live under `wwwroot/lib/` (or an npm build that outputs
  there). ES modules imported lazily via `JS.InvokeAsync("import", "./js/ide/<m>.js")` — the
  pattern `CodeEditor` already uses ([CodeEditor.razor:66](Components/IDE/CodeEditor.razor:66)).
- **No bundler required for v1:** start with `<script type="module">` + CDN-or-vendored libs.
  Introduce a Vite/esbuild step only when module count justifies it.
- **CSP / offline:** vendor everything locally (no CDN at runtime) — the app is offline-first and
  ships as a desktop binary.
- **Theme bridge:** a single `:root` CSS-variable set consumed by both MudBlazor and the JS
  widgets so the dark theme ([IdeShell.razor:140](Components/IDE/IdeShell.razor:140)) is one
  source of truth.

---

## 9. Phased delivery

| Phase | Deliverable | Why now |
|---|---|---|
| **U0** | Finish the launch mailbox + open-window de-dupe (§4.1–4.2) | fixes a parallel-open data-loss bug; partly built already |
| **U1** | **Monaco** replaces the textarea via `monaco.js` (§3) — same interop seam as `editor.js` | the single biggest IDE-quality jump |
| **U2** | **Golden Layout** docking + per-workspace layout restore (§4.3) | "dockable windows / split editors" |
| **U3** | **Xterm.js + ConPTY** terminal over `ICliRunner` (§3, replaces the Phase-2 placeholder) | real integrated terminal |
| **U4** | Virtualized file tree (§3, §5) | large repos without jank |
| **U5** | `IFileHistory` + Monaco diff/History rail (§6) | revertable, attributed edits |
| **U6** | Versioning MCP tools, gated by loop autonomy (§6.4) | LLM-controlled revert |
| **U7** | Cytoscape AST/dep graph + D3 charts (§3) | "live graphs / scene graph" |
| **U8** | UX hardening sweep (§7) + auto-checkpoint policy (§6.5) | overall robustness |
| **U9** | In-process **Roslyn → Monaco** diagnostics/hover/completion (§3.3) — no LSP | language intel without a server; the differentiator. Lands any time after U1 |

U0 first (data loss), then U1 (Monaco) — that one swap moves the IDE rating from the textarea era
toward the 10/10 surface the user is targeting, and it rides the interop seam that already exists.
Every adopted library (§3.2) is MIT/BSD, so none of this creates a licensing obligation in the
shipped binary; the copyleft/native projects (Theia, Zed, Judge0-core) inform the design but ship
no code.

> This table is the **sequencing** view (what order). For the **work-breakdown** view — each piece
> as a discrete package with build-vs-reuse, the C# service it binds to, and effort — see
> **§11**. The two map 1:1 (U0=W6, U1=W2, …, U9=W10).

---

## 10. Test matrix

| Scenario | Expected |
|---|---|
| Open A then B within 50 ms | two windows, distinct workspaces + layouts, no shared session file |
| Open A twice | second call focuses the existing window |
| Monaco module fails to import | editor falls back to textarea, no crash (graceful interop) |
| Type 5k chars fast | no per-keystroke `StateHasChanged`; buffer synced on debounce/save |
| Terminal emits 100k lines | Xterm canvas stays smooth; Blazor untouched |
| File tree, 50k nodes | virtualized; lazy children honor the ignore set |
| Agent edits → build regresses, autonomous off | revert tool proposed, gated, Monaco shows the diff, user approves |
| Same, autonomous on | auto-reverted, toast, still undoable (`PreRevert`) |
| External edit to open file | Monaco conflict diff + snapshots both sides |
| Crash with 3 dirty tabs | next boot restores tabs + layout + offers `ManualSave` snapshots |
| Reopen workspace | docking layout restored byte-for-byte |

---

## 11. Implementation work packages (the build sheet)

Everything above is **what + why**. This section is **what you actually build** — the
hardening/architecture prose translated into discrete engineering packages, each tagged
**Reuse** (embed a mature MIT library), **Wrap** (thin interop around a library), or **Build**
(Syncro-specific, no library exists), with the C# backend it binds to and a rough effort.

### 11.1 The one rule that splits the work

> **Reuse the editor, terminal, dock, graph, and diagram engines. Build everything that knows
> about *your project* — explorer, workspace, agents, AST, Hindsight, approvals, commands.**

Reuse / Wrap (mature libraries — do **not** reinvent):

- **Monaco** — editor, diff, search, folding, minimap, multi-cursor, decorations, undo/redo.
- **Xterm.js** — terminal renderer, ANSI, scrollback (PTY comes from C#).
- **Golden Layout** — docking: panels, tabs, split, drag, float.
- **Split.js / Interact.js** — resizable panes, drag/resize.
- **Cytoscape.js** — AST / dependency / call / memory graphs.
- **Mermaid** — architecture / sequence / ER / flow diagrams (no AST data — authored diagrams).

Build (Syncro-specific — no library does this):

- Explorer / **virtualized file tree**, Workspace manager, Agent panel, Task queue, AI chat,
  Hindsight timeline, AST explorer, Project-context strip, Syncro command palette, Patch/Diff
  review, Approval UI, Autonomous-loop controls, Memory panel.

### 11.2 Work packages

Effort is rough order-of-magnitude for one developer; packages above the line unblock everything
below. "Binds to" names the existing C# service the package wires into — most backends already
exist, which is why the IDE is mostly *surface*.

| # | Package | Type | Provider (§2.4) | JS module | Binds to (C#) | Effort |
|---|---|---|---|---|---|---|
| W1 | **JS interop layer** (module loader, `DotNetObjectReference` lifecycle, theme bridge) | Build | — | `ide/core.js` | extends the seam in [CodeEditor.razor:66](Components/IDE/CodeEditor.razor:66) | ~1 wk |
| W2 | **Monaco engine + `MonacoEditor.razor`** (replaces the textarea) | Wrap | `IEditorEngine` | `monaco.js` | `FileService`, `IdeWorkspaceState.Buffers` | 2–4 d |
| W3 | **Terminal + `Terminal.razor`** (Xterm + ConPTY) | Wrap | `ITerminalEngine` | `terminal.js` | `ICliRunner` / CliWrap (see [`engine/optimisation server cli.md §5.1`](engine/optimisation%20server%20cli.md)) | 2–3 d |
| W4 | **Dock service + `DockManager.razor`** (panels move/split/float, layout persist) | Wrap | `IDockEngine` | `docking.js` | `IdeStateStore` (layout JSON, [IdeStateStore.cs:30](Services/Ide/IdeStateStore.cs:30)) | ~1 wk |
| W5 | **Virtualized file tree** (lazy load, virtualization, filter, context menu, 100k files) | Build | — | `tree.js` (virtualization only) | `FileService.ListAsync` + ignore set ([FileService.cs:18](Services/Ide/FileService.cs:18)) | ~2 wk |
| W6 | **Launch mailbox + open-window de-dupe** (parallel projects, §4.1–4.2) | Build | — | — | `IdeLaunchContext`, `IdeWindowService` | 2–3 d |
| W7 | **Workspace manager** (open-with-context, per-window layout/tabs restore, §4.3) | Build | — | — | `IdeWorkspaceState`, `SyncroDb`, `ProjectContext` (new) | ~2 wk |
| W8 | **Graph viewer + `Graph.razor`** (AST / dep / call graph) | Wrap | `IGraphRenderer` | `graph.js` | `AstService` map → nodes/edges | ~1 wk |
| W9 | **Diagram viewer + `Diagram.razor`** | Wrap | `IDiagramRenderer` | `diagram.js` | agent/plan output → Mermaid source | ~1 d |
| W10 | **Roslyn → Monaco language provider** (diagnostics/hover/completion, **no LSP**, §11.4) | Build | feeds `IEditorEngine` | (via `monaco.js`) | `CSharpAstParser` / Roslyn, `AstService` | ~2 wk |
| W11 | **File history / versioning** (T1 snapshots, Monaco diff, History rail, §6) | Build | uses `IEditorEngine.ShowDiffAsync` | — | wraps `FileService.WriteAsync`, `SyncroDb` jsonl | ~1.5 wk |
| W12 | **Versioning MCP tools** (LLM-controlled revert, gated, §6.4) | Build | — | — | `McpOrchestrator`, `TaskLoopEngine.Autonomous` | 3–4 d |
| W13 | **AI panels** (chat, task queue, agent monitor, memory, approval/patch review) | Build | reuse `IEditorEngine` for code blocks | — | `AgentOrchestrator`, `TaskLoopEngine`, `SyncroDb`, :3030 | 3–4 wk |
| W14 | **AST explorer panel** | Build | — | (reuse `graph.js`) | `AstService`, `AstProjectMap` | ~2 wk |
| W15 | **Hindsight timeline panel** | Build | — | — | `AgentOrchestrator.OfflineHindsightMemory`, Hindsight store | ~2 wk |
| W16 | **Syncro command palette** (over Monaco's command system + C# command registry) | Build | — | (Monaco command API) | new `ICommandRegistry` | ~1 wk |
| W17 | **UX hardening sweep** (structured progress, cancel/timeout, error states, §7) | Build | — | — | `IdeStateStore`, `FileService`, `ProjectGenerator` | ~1 wk |

### 11.3 Critical path

```
W1 (interop layer)
 ├─▶ W2 Monaco ──▶ W10 Roslyn provider ──▶ W11 versioning ──▶ W12 revert tools
 ├─▶ W3 Terminal
 ├─▶ W4 Dock ─────▶ (hosts every panel below)
 └─▶ W5 Tree
W6 mailbox (independent, do FIRST — fixes data loss) ──▶ W7 Workspace manager
W8 Graph / W9 Diagram / W13 AI panels / W14 AST / W15 Hindsight  ── all dock into W4
W16 palette, W17 hardening ── continuous
```

**Do W6 first** (parallel-open data loss is a real bug, partly built already), then **W1 → W2**
(the interop layer + Monaco is the single biggest quality jump and unblocks W10/W11). W4 (dock) is
the second backbone — once it exists, every Build panel (W13–W15) just registers itself. The
provider interfaces (§2.4) mean W2/W3/W4/W8/W9 can each be stubbed and swapped independently.

### 11.4 The Roslyn → Monaco language provider (W10) — why no LSP

Monaco doesn't care whether its backend is Node or C#. It needs a handful of callbacks:

```
Monaco asks:   onOpen(content) · getDiagnostics() · getHover(pos) · getCompletions(pos)
               · getDefinition(pos) · getReferences(pos) · format()
```

VS Code answers these by shelling out to a **language server over JSON-RPC/LSP**
(`Editor → JSON-RPC → LSP → Roslyn server → compiler`). Syncro already hosts **Roslyn
in-process** (`CSharpAstParser`), so it answers them **directly**
(`Editor → interop → Roslyn → compiler`) — **no RPC, no LSP server, no `monaco-languageclient`**,
lower latency. The work package is: register Monaco language providers whose handlers call
`[JSInvokable]` C# methods backed by Roslyn (`GetDiagnosticsAsync`, `GetHoverAsync`,
`GetCompletionsAsync`, …). For non-first-party languages (TS/Py), fall back to a real LSP later
behind the **same** provider surface — Monaco never knows the difference.

### 11.5 What this section changes about §9

§9 (Phased delivery, U0–U9) is the **sequencing** view; §11 is the **work-breakdown** view. They
map: U0=W6, U1=W2(+W1), U2=W4, U3=W3, U4=W5, U5=W11, U6=W12, U7=W8, U8=W17, U9=W10. Use §9 to
decide *order*, §11 to scope *each piece* and its build-vs-reuse + backing C# service.
