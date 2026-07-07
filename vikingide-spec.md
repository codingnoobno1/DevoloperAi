# Viking IDE — Engineering Spec (build-level)

> Companion to `vikingide.md` (architecture overview). This is the **implementation contract**:
> exact files, C# interface signatures, JS-interop APIs, data models, SQLite schema, DI wiring,
> sequence flows, and a granular phase checklist with acceptance criteria.
> Target: `Syncro.Desktop` (MAUI net9 + Blazor Hybrid, MudBlazor, `BlazorWebView`).

---

## 0. Integration into Syncro.Desktop

- **Route:** `IdeShell.razor` at `@page "/ide"` and `@page "/ide/{WorkspaceB64}"` (base64 of the
  workspace path, same pattern the team added on `Hindsight.razor`).
- **Entry points:** NavMenu item "Viking IDE"; "Open in Viking IDE" buttons on `AllProjects.razor`,
  `MyProjects.razor`, `Hindsight.razor` (replacing the VS Code launcher per §12).
- **DI:** one extension `AddVikingIde()` called from `MauiProgram` (§6).
- **Assets:** vendored JS/CSS/wasm under `wwwroot/js/ide/vendor/` (Monaco, Xterm, Cytoscape) — no CDN.
- **Packages:** see `vikingide.md` §11; add to `Syncro.Desktop.csproj`.
- **Window:** the IDE is a full-page Blazor route in the existing MAUI window (no second process).

---

## 1. Solution-wide models (`Services/Ide/Models/`)

```csharp
namespace Syncro.Desktop.Services.Ide.Models;

public class Workspace {
    public string Id { get; set; } = "";          // hash of RootPath
    public string Name { get; set; } = "";
    public string RootPath { get; set; } = "";
    public DateTime LastOpened { get; set; }
}

public class EditorTab {
    public string Uri { get; set; } = "";          // "file:///abs/path"
    public string FilePath { get; set; } = "";
    public string Language { get; set; } = "plaintext";
    public bool IsDirty { get; set; }
    public bool IsActive { get; set; }
    public string? ViewStateJson { get; set; }      // Monaco saveViewState() blob
    public bool IsDiff { get; set; }                // diff tab (git/agent patch)
    public string? OriginalContent { get; set; }    // for diff tabs
}

public class FileNode {
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool HasChildren { get; set; }           // lazy expand
    public string? GitStatus { get; set; }          // M | A | D | U | null
    public bool Indexed { get; set; }               // present in hindsight vectors
}

public enum SidePanelView { Explorer, Search, Git, Ast, Agents }
public enum BottomTab { Terminal, Problems, Output, Telemetry, Graph }

public record IdeCommand(string Id, string Title, string? Shortcut, Func<Task> Run);
public record FileChange(string Path, FileChangeKind Kind);
public enum FileChangeKind { Created, Modified, Deleted, Renamed }
```

---

## 2. Workspace state (`Services/Ide/IdeWorkspaceState.cs`)

Scoped service; the single source of UI truth. Components subscribe to `Changed`.

```csharp
public class IdeWorkspaceState
{
    public Workspace? Current { get; private set; }
    public List<EditorTab> Tabs { get; } = new();
    public EditorTab? ActiveTab => Tabs.FirstOrDefault(t => t.IsActive);
    public SidePanelView SidePanel { get; set; } = SidePanelView.Explorer;
    public BottomTab BottomPanel { get; set; } = BottomTab.Terminal;
    public bool BottomVisible { get; set; } = true;

    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();

    public Task OpenWorkspaceAsync(string rootPath);     // sets Current, restores session (IdeStateStore)
    public EditorTab OpenFile(string filePath);          // dedupes by Uri, activates
    public void CloseTab(string uri);
    public void Activate(string uri);
    public void MarkDirty(string uri, bool dirty);
}
```

---

## 3. JS interop contracts (`wwwroot/js/ide/`)

Each widget = one ES module imported via `IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/ide/<x>.js")`.
Callbacks use `DotNetObjectReference<T>` with `[JSInvokable]` methods.

### 3.1 `monaco-interop.js`
| JS function | Returns | Notes |
|---|---|---|
| `init(el, theme)` | `editorId` | create editor instance |
| `openModel(editorId, uri, language, value, viewStateJson)` | void | create/switch model, restore view state |
| `getValue(editorId)` | string | |
| `getViewState(editorId)` | string | persist cursor/scroll |
| `setMarkers(editorId, markers)` | void | diagnostics `{startLine,startCol,endLine,endCol,message,severity}` |
| `revealPosition(editorId, line, col)` | void | go-to-def |
| `decorate(editorId, decorations)` | void | AST overlays (endpoint/DTO gutter) |
| `registerProvider(language, dotnetRef)` | void | completion+hover via `dotnetRef` |
| `openDiff(el, original, modified, language)` | `diffId` | git/agent patch preview |
| `dispose(editorId)` | void | on component dispose |

C# side `MonacoBridge` mirrors each; callbacks the JS invokes on the .NET ref:
`OnContentChanged(string uri)`, `Task<object> ProvideCompletions(string uri, int line, int col, string text)`,
`Task<object?> ProvideHover(string uri, int line, int col)`.

### 3.2 `xterm-interop.js`
`init(el, dotnetRef, cols, rows) -> termId` (JS calls `dotnetRef.OnInput(termId, data)`),
`write(termId, data)`, `fit(termId) -> {cols,rows}`, `clear(termId)`, `dispose(termId)`.

### 3.3 `cytoscape-interop.js`
`render(el, nodesJson, edgesJson, dotnetRef)` (calls `dotnetRef.OnNodeClick(id)`),
`runLayout(el, name)`, `fit(el)`, `dispose(el)`.

### 3.4 `split-interop.js`
`init(selectorArray, sizesArray, direction)`, `destroy(id)` — resizable panes (or BlazorSplit instead).

---

## 4. Subsystem specs

### 4.1 Editor — `Services/Ide/Editor/`
```csharp
public class MonacoBridge : IAsyncDisposable {
    Task<string> InitAsync(ElementReference el, string theme);
    Task OpenAsync(string editorId, EditorTab tab, string content);
    Task<string> GetValueAsync(string editorId);
    Task SetMarkersAsync(string editorId, IEnumerable<EditorMarker> markers);
    Task DecorateAsync(string editorId, IEnumerable<EditorDecoration> decos);
    Task RevealAsync(string editorId, int line, int col);
    Task<string> OpenDiffAsync(ElementReference el, string original, string modified, string lang);
}

public class RoslynCompletionService {
    Task LoadAsync(string workspaceRoot);                       // AdhocWorkspace / Buildalyzer (already referenced)
    Task<IReadOnlyList<CompletionItem>> CompleteAsync(string file, string text, int line, int col);
    Task<IReadOnlyList<EditorMarker>> DiagnoseAsync(string file, string text);
    Task<HoverInfo?> HoverAsync(string file, string text, int line, int col);
}

public class LanguageSymbolProvider {   // non-C# langs via Syncro AST parsers (TS/JS/Py)
    Task<IReadOnlyList<DocumentSymbol>> OutlineAsync(string file);   // feeds AST outline + breadcrumbs
    Task<SymbolLocation?> DefinitionAsync(string file, string symbol);
}
```
- `MonacoEditor.razor` owns one `editorId`; subscribes to `IdeWorkspaceState`; debounces change →
  `RoslynCompletionService.DiagnoseAsync` → `SetMarkersAsync`.
- AST overlays: on file open, `AstService.ActiveProjectMap` endpoints/DTOs for that file → `DecorateAsync`.
- Inline AI: selection → `ChatPanel.AskAboutSelection` → `PatchGenerator` → `OpenDiffAsync` → approve.

### 4.2 Explorer — `Services/Ide/FileService.cs`
```csharp
public class FileService {
    Task<IReadOnlyList<FileNode>> ListAsync(string dir);     // honors AST ignore set
    Task<string> ReadAsync(string path);
    Task WriteAsync(string path, string content);
    Task<string> CreateFileAsync(string dir, string name);
    Task DeleteAsync(string path);
    Task RenameAsync(string from, string to);
    IDisposable Watch(string root, Func<FileChange, Task> onChange);   // FileSystemWatcher, debounced
    string DetectLanguage(string path);                      // ext → monaco language id
}
```
- `FileTree.razor`: lazy nodes (`HasChildren`), git status badges (from `GitWorkspaceService`),
  "indexed" badge (from `HindsightVectorStore.StatsAsync`), context menu (open / template / ask AI).

### 4.3 Terminal — `Services/Ide/Terminal/`
```csharp
public interface IPtyService {
    Task<string> StartAsync(string shell, string cwd, int cols, int rows);  // returns termId
    event Action<string, string> Output;                                    // (termId, data)
    event Action<string, int> Exited;                                       // (termId, code)
    Task WriteAsync(string termId, string data);
    Task ResizeAsync(string termId, int cols, int rows);
    Task KillAsync(string termId);
}
public sealed class ConPtyService : IPtyService { /* Pty.Net or CreatePseudoConsole P/Invoke */ }
public sealed class ProcessFallbackPtyService : IPtyService { /* wraps existing ProcessRunner, line mode */ }
```
- `TerminalView.razor` binds `xterm-interop`: JS `OnInput` → `IPtyService.WriteAsync`; service
  `Output` → `xterm.write`. Default shell from `EnvironmentManager`/platform; cwd = workspace root.
- Command palette + buttons can inject `syncro …` (via existing `SyncroCLIService`).

### 4.4 Git — `Services/Ide/Git/GitWorkspaceService.cs`
```csharp
public class GitWorkspaceService {
    Task<bool> IsRepoAsync(string root);
    Task<IReadOnlyList<GitChange>> StatusAsync(string root);        // LibGit2Sharp
    Task<(string Original, string Modified)> DiffAsync(string root, string path);
    Task StageAsync(string root, string path);
    Task UnstageAsync(string root, string path);
    Task CommitAsync(string root, string message);
    Task<IReadOnlyList<string>> BranchesAsync(string root);
    Task SwitchAsync(string root, string branch);
    Task PushAsync(string root);                                    // delegate to GitProvider (CLI, crash-safe)
    Task PullAsync(string root);
}
```
- `SourceControl.razor`: change list → click → `MonacoBridge.OpenDiffAsync`. PR via Octokit (later).

### 4.5 AST + Graph
- Reuse `AstService`. `AstOutline.razor` shows classes/methods/endpoints/DTOs from `AstProjectMap`.
- `GraphView.razor` → builds nodes/edges from the Knowledge Graph (`ast.md` `RelationshipIndexer`,
  Phase 4) → `cytoscape-interop.render`; node click → `IdeWorkspaceState.OpenFile` + `RevealAsync`.

### 4.6 AI / RAG / Agents
- `ChatPanel.razor`: reuses `HindsightQueryService` (`AnswerWithoutLlmAsync` / `AnswerWithLlmAsync`,
  `IsLlmAvailableAsync`) → with/without-LLM toggle, cited chunks open files.
- `AgentPanel.razor`: drives `AgentCli/TaskLoopEngine` (`StepAsync`/`RunToCompletionAsync`); shows the
  7-stage loop; patch diffs via `OpenDiffAsync`; approve → apply → `SyncroDb` audit.
- `InlineActions.razor`: editor selection → Explain/Refactor/Fix; mutations gated by `LlmSafetyGate`.

### 4.7 Telemetry — `TelemetryPanel.razor`
- Polls `http://localhost:3030/api/db` (the `DbStatusService` snapshot) every ~2s → renders tasks,
  scripts, NLP mappings, tokens, audit with **LiveCharts2/OxyPlot**. No new server.

### 4.8 Embeddings — `Services/Ide/Embeddings/`
```csharp
public interface IEmbeddingProvider { string Id { get; } Task<float[]> EmbedAsync(string text); }
public sealed class LexicalEmbeddingProvider : IEmbeddingProvider { /* deterministic, offline (today's path) */ }
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider { /* OllamaSharp nomic-embed-text; Phase 4 */ }
```
- `HindsightVectorStore`/`TokenizeProjectAsync` switch from mock vectors → `IEmbeddingProvider`;
  cosine via MathNet; store in SQLite (Phase 4). Lexical stays as offline fallback.

---

## 5. Persistence — `Services/Ide/Persistence/IdeStateStore.cs` (SQLite)

```sql
CREATE TABLE workspaces (id TEXT PRIMARY KEY, name TEXT, root_path TEXT, last_opened TEXT);
CREATE TABLE open_tabs (workspace_id TEXT, uri TEXT, file_path TEXT, language TEXT,
                        is_active INTEGER, view_state TEXT, ord INTEGER,
                        PRIMARY KEY(workspace_id, uri));
CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE recents (path TEXT PRIMARY KEY, opened_at TEXT);
```
```csharp
public class IdeStateStore {
    Task InitAsync();
    Task SaveSessionAsync(Workspace ws, IEnumerable<EditorTab> tabs);
    Task<(Workspace?, List<EditorTab>)> LoadSessionAsync(string workspaceId);
    Task<string?> GetSettingAsync(string key);  Task SetSettingAsync(string key, string value);
    Task AddRecentAsync(string path);  Task<IReadOnlyList<string>> RecentsAsync();
}
```
(Use `Microsoft.Data.Sqlite`, or `LiteDB` if you prefer document storage.)

---

## 6. DI registration — `Services/Ide/VikingIdeServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddVikingIde(this IServiceCollection s)
{
    s.AddScoped<IdeWorkspaceState>();           // per BlazorWebView circuit
    s.AddSingleton<FileService>();
    s.AddSingleton<IdeStateStore>();
    s.AddSingleton<MonacoBridge>();             // (or transient per editor; see §10)
    s.AddSingleton<RoslynCompletionService>();
    s.AddSingleton<LanguageSymbolProvider>();
    s.AddSingleton<IPtyService, ConPtyService>();
    s.AddSingleton<GitWorkspaceService>();
    s.AddSingleton<IEmbeddingProvider, LexicalEmbeddingProvider>();  // swap to Ollama in Phase 4
    s.AddSingleton<IdeCommandRegistry>();
    return s;
}
```
Called in `MauiProgram` after `AddAgentCli()`. Reuses already-registered `AstService`,
`HindsightQueryService`, `HindsightVectorStore`, `AgentCli` services, `ProjectService`, `AIClient`.

---

## 7. Key sequence flows

**Open file**
`FileTree click → IdeWorkspaceState.OpenFile(path) → MonacoEditor renders → MonacoBridge.OpenAsync
→ FileService.ReadAsync → AstService overlays (DecorateAsync) → IdeStateStore.SaveSessionAsync (debounced)`

**Edit + diagnostics (C#)**
`Monaco onChange → MarkDirty → debounce 400ms → RoslynCompletionService.DiagnoseAsync → SetMarkersAsync`

**Save**
`Ctrl+S → MonacoBridge.GetValueAsync → FileService.WriteAsync → clear dirty → (optional) re-tokenize`

**Terminal command**
`Xterm OnInput → IPtyService.WriteAsync → ConPTY → Output event → xterm.write`

**Agent edit**
`AgentPanel goal → TaskLoopEngine → GeneratorAgent → PatchGenerator → OpenDiffAsync(preview)
→ user Approve (--yes) → PatchApplier → build/validate → SyncroDb audit → ScriptStore/mapping update`

**Semantic search (Phase 4)**
`query → IEmbeddingProvider.EmbedAsync → cosine over SQLite vectors → ranked chunks → editor reveal`

---

## 8. Settings & keybindings
- `settings` table + `SettingsPanel.razor`: theme, font size, default shell, external-editor toggle,
  LLM endpoint (`:3020`/Ollama/cloud), embedding model, autosave.
- `IdeCommandRegistry` maps shortcuts → `IdeCommand`: `Ctrl+P` quick-open, `Ctrl+Shift+P` palette,
  `Ctrl+S` save, `Ctrl+`` terminal, `Ctrl+Shift+F` search, `F12` go-to-def (via `LanguageSymbolProvider`).

## 9. Theming
- MudBlazor dark theme as base; Monaco `vs-dark`; Xterm theme tokens matched to CSS vars
  (`--accent-cyan`, etc., reused from the 3030 monitor / docs styling for brand consistency).

---

## 10. Performance & disposal rules
- One Monaco/Xterm/Cytoscape instance per visible panel; **dispose JS instance + `IJSObjectReference`
  in `DisposeAsync`** (leaks crash the WebView over time).
- Lazy-load each interop module on first panel use; don't import all upfront.
- Virtualize file tree and large lists (`MudVirtualize`/`Virtualize`).
- Throttle/cancel Roslyn completion+diagnostics (CancellationToken per keystroke).
- Stream large file/terminal payloads via callbacks, not multi-MB JSON.
- Cap vector store; gitignore `bin/obj/.syncro_db` (disk hit 0 B earlier).

---

## 11. Phase checklist (acceptance criteria)

**Phase 0 — Shell** ☐ `IdeShell` route ☐ activity bar toggles side views ☐ resizable split layout
☐ status bar (workspace/branch/LLM/vectors) ☐ command palette runs a registered command.
*Accept: navigate IDE chrome; layout persists.*

**Phase 1 — Editor + Explorer** ☐ vendor Monaco ☐ `monaco-interop.js` + `MonacoBridge` ☐ open/edit/save
☐ tabs + dirty + close ☐ `FileService` tree + watcher ☐ `IdeStateStore` session restore ☐ language
detection ☐ "Open in Viking IDE" buttons.
*Accept: edit & save real files; reopen app → tabs restored.*

**Phase 2 — Terminal + Git** ☐ Xterm + `ConPtyService` (fallback to ProcessRunner) ☐ interactive REPL
☐ `GitWorkspaceService` status/stage/commit/branch ☐ Monaco diff ☐ run `syncro` verbs in terminal.
*Accept: `python`/`node` REPL works; stage+commit+diff works.*

**Phase 3 — Intelligence panels** ☐ AST outline + endpoint/DTO overlays ☐ ChatPanel with/without-LLM
(reuse Hindsight) ☐ TelemetryPanel reads `/api/db` ☐ Problems panel from diagnostics.
*Accept: overlays match `AstProjectMap`; offline chat returns grounded answers.*

**Phase 4 — Knowledge Graph + real embeddings** ☐ `RelationshipIndexer` → Cytoscape ☐ node click→file
☐ `OllamaEmbeddingProvider` replaces mock vectors ☐ SQLite vector store ☐ semantic search.
*Accept: paraphrased query beats lexical; graph nodes open files.*

**Phase 5 — Agents** ☐ AgentPanel over `TaskLoopEngine` ☐ patch preview/approve/apply ☐ safety gate
☐ optional AutoGen multi-agent ☐ optional MCP tools.
*Accept: a goal → previewed patch; reject = no change; accept = build + audit.*

**Phase 6 — Polish** ☐ multi-workspace ☐ search-in-files ☐ settings/keybindings ☐ docs preview
☐ themes ☐ perf pass (dispose audit, virtualization).

---

## 12. Migrate off "Open in VS Code"
1. Phase 1: add "Open in Viking IDE" → `Nav.NavigateTo($"/ide/{Base64(path)}")`.
2. Demote `VscodeLauncherService` behind `settings.externalEditor` (default off).
3. Keep it only as an explicit fallback.

## 13. Decisions to confirm before coding
- **BlazorMonaco** package vs hand-rolled `monaco-interop.js`? (Recommend BlazorMonaco to start.)
- **SQLite (`Microsoft.Data.Sqlite`)** vs **LiteDB** for IDE state? (Recommend SQLite.)
- **ConPTY via Pty.Net** vs custom P/Invoke? (Recommend Pty.Net; fallback service ships first.)
- **Embedding model** (Ollama `nomic-embed-text` vs Semantic Kernel connector)?
- **Docking**: BlazorSplit vs custom CSS-grid splitters? (Custom is lightest for a WebView.)

## 14. Build note
Run `dotnet build` with the Syncro.Desktop app **closed** — a running instance locks
`bin/.../Syncro.Desktop.dll` (seen earlier as MSB3027). Code compiles; only the output copy is blocked while running.

---

## 15. CLI Talk tab (shipped early at `/clitalk`)

A standalone Blazor page now (`Components/Pages/Projects/CliTalk.razor`, nav: **CLI Talk**); it folds
into the IDE as `BottomTab.CliTalk` in Phase 2/3 by mounting the same component in `BottomPanel.razor`.

**Reuses (no new logic):** `SyncroCLIService` (`ExecuteCommand` + `OnLog` event) and `TaskStore`
(the canonical `TaskRecord`). Talks to the existing `syncro` engine (`help`, `doctor`, `ast`, `git`, `script`).

**Layout:**
- **Left — console:** terminal-styled log; type any `syncro` verb → `ExecuteCommand`; live `OnLog` stream; quick buttons + clear.
- **Right — Task Model inspector:** a task picker + a key/value table of **every shared field**:
  `task_id · project_id · title · intent · script_id · template_script_executed · status · error ·
  error_count · source · error_type · solution · next_action · llm_involved · attempts/max_attempts ·
  needs_approval · created_at · updated_at · history`.

**Reusable contract:** these exact fields now live in three places off **one model** —
1. `TaskRecord` (`Services/AgentCli/Models`) — canonical, used in-process by CLI Talk and the IDE.
2. `TaskRow` (`DbSnapshot`) — the wire projection, **extended** to carry all fields.
3. **3030 `/api/db`** + `/api/status.db.Tasks.Recent[]` — same fields exposed to the monitor dashboard.

So the CLI Talk tab, the 3030 telemetry, and future IDE panels (Agents, Problems) read the identical
task contract — change it once, everyone sees it.

**Phase 5 upgrade:** add an "Agent" mode that `EnqueueTask` → `TaskLoopEngine.RunToCompletionAsync`
and streams the status transitions live into the same inspector (the loop diagram in `agentcli.md`).
```
