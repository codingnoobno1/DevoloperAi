# idegap.md — IDE Subsystem: Deep Analysis, Gaps & Fix List

> Scope: the Syncro IDE (`Components/IDE/*`, `Services/Ide/*`, `vscode-main/`), measured against
> what a modern AI IDE (VS Code / Cursor / Antigravity) provides: **project opening**,
> **file reading**, **autocompletion**, **error finding (diagnostics)**, **scope definition
> (go-to-definition / references)**, and the **MCP server** bridge.
> Severity: 🔴 broken/missing · 🟠 incomplete · 🟡 polish. Verified against current source.

---

## 0. Architecture — how the IDE actually works today

Syncro's IDE is a **host-abstraction seam** (`IIdeHost`, `IdeHostResolver`) with **two interchangeable backends**:

| Backend | Class | What it is | Language intelligence |
|---|---|---|---|
| **Monaco** (default) | `MonacoIdeHost` + `MonacoEditorEngine` | In-process Blazor + Monaco editor | **Roslyn** (C# only) via `RoslynLanguageService` |
| **Code-OSS** | `CodeOssIdeHost` | Real **VS Code OSS** (`vscode-main`) served by `code-server.js` on `127.0.0.1`, shown in an `<iframe>` | VS Code's own TS/JS/etc. + `syncro-bridge` extension |

`IdeShell.razor` is the shell. It renders Monaco by default; toggling "Default editor: VS Code OSS"
(`:359`) calls `CodeOssIdeHost.StartAsync`, which boots serve-web and drops the URL into an
`<iframe>` (`:53`). The bridge (`McpHttpEndpoint`) is started and its port/token injected into the
Code-OSS process env (`CodeOssIdeHost.cs:66`), so the `syncro-bridge` extension can reach the engine.

### Is it really using the Code-OSS repo? — **Yes.**
- `vscode-main/` is a full microsoft/vscode checkout, **compiled** (`out/server-main.js` present),
  with the server node runtime downloaded (`.build/node/v24.17.0/win32-x64/node.exe`).
- `vscode-main/extensions/syncro-bridge/` exists **and is compiled** (`out/*.js`). It contributes an
  MCP server definition, a Groq language-model provider, a `@syncro` chat participant, and a tree view.
- So the Code-OSS path is genuinely runnable, not just scaffolding. **But** it is an **opt-in toggle**,
  not the default, and Syncro's own Monaco path is what most users hit.

---

## 1. Project opening

**Works:** `IdeLaunchContext` hands a `WorkspacePath` to `IdeShell`, which calls
`State.OpenWorkspaceAsync(path)` (`IdeShell.razor:392`); `FileService.ListAsync` walks the tree for
the explorer; Code-OSS opens the same folder via `?folder=` (`CodeOssIdeHost.WithFolder`).

- 🟠 **Two disjoint "open project" notions.** `ProjectContextBuilder` builds a *rich* `ProjectContext`
  (tasks, AST-index presence, language/framework from `.syncro_db`), but `IdeShell` opens a workspace
  purely by **path** and never calls `ProjectContextBuilder` — the metadata/AST/task context is
  computed but not fed into the editor session. The IDE doesn't know a project's detected stack.
- 🟠 **No multi-root / recent-workspaces.** `IdeWorkspaceState` tracks a single `Current` root; there's
  no recent list, no multi-folder workspace, no reopen-last.
- 🟡 **No "trust" or size guard** before walking a huge tree in `FileService.ListAsync` (it's lazy per
  directory, which helps, but there's no cap or warning on massive folders).

## 2. File reading system

**Works:** `FileService.ReadAsync/WriteAsync/ListAsync` with an ignore-set (`bin/obj/node_modules/…`),
`DetectLanguage` maps 20+ extensions, `FileTree`/`FileTreeNode` render lazily.

- 🟠 **No file watching.** Nothing subscribes to filesystem changes — edits made outside the editor
  (git checkout, another tool, the AI writing files) don't refresh the tree or open buffers. Every
  real IDE has a watcher; Syncro's Monaco path has none.
- 🟠 **Read errors are swallowed into the buffer.** `ReadAsync` returns a `// could not read` **string
  as file content** (`FileService.cs:48`) — the editor shows the error *as if it were the file*, and a
  subsequent save would overwrite the real file with that comment. Should surface an error state instead.
- 🟠 **No binary/large-file detection.** Opening a large binary loads it fully into a Monaco buffer.
- 🟡 **No encoding detection** (assumes UTF-8 via `File.ReadAllTextAsync`).

## 3. Autocompletion

**Works (C# only):** `monaco.js:164` registers a completion provider for `csharp` → `GetCompletionsJS`
→ `RoslynLanguageService.GetCompletionsAsync` using Roslyn's `Recommender`. Hover is wired the same way
(`monaco.js:184`). Buffizedits feed `UpdateDocumentAsync` and re-run diagnostics (debounced 500 ms).

- 🔴 **C# is the only language with intelligence in the Monaco path.** TS/JS/Python/Go/Java/Dart get
  Monaco's built-in syntax highlighting but **no semantic completion, no hover, no diagnostics** — the
  provider is registered for `'csharp'` only. In a project that is mostly Flutter/Next.js (Syncro's own
  paired platform!), the editor is effectively "notepad with colors".
- 🟠 **Completions are shallow.** No `documentation`, no `insertTextRules` (snippets), no
  `commitCharacters`, no resolve step — labels + detail only (`RoslynLanguageService.cs:120`).
- 🟠 **Buildalyzer load is silent + all-or-nothing.** `LoadWorkspaceAsync` runs `AnalyzerManager` over
  every `.csproj` and swallows failures into an empty `catch` (`:43`) — if the design-time build fails,
  the user gets zero completions with **no indication why**. No progress/loading state either.
- 🟡 **No cancellation** of an in-flight completion when the user keeps typing (each call is independent
  and lock-serialized, so fast typing queues behind a slow semantic model).

## 4. Error finding (diagnostics)

**Works (C# only):** `DebounceDiagnosticsAsync` (`IdeShell.razor:633`) calls
`RoslynLanguageService.GetDiagnosticsAsync`, pushes markers to Monaco (`setModelMarkers`) and fills the
Problems panel.

- 🔴 **Single-file, semantic-only, C#-only.** `GetDiagnosticsAsync` uses
  `semanticModel.GetDiagnostics()` for **one document** — it misses compiler diagnostics that arise
  from other files, and there's no workspace-wide problem list. Non-C# files get nothing.
- 🟠 **No analyzer/build diagnostics.** Only Roslyn's in-memory semantic diagnostics; no `dotnet build`
  errors, no Roslyn analyzers, no ESLint/tsc/pylint for other stacks.
- 🟠 **Diagnostics don't survive tab switches** — `_problems` is cleared and refilled per edited `uri`
  (`:641`), so the Problems panel only ever reflects the last-edited file, not the project.

## 5. Scope definition (go-to-definition / references / symbols)

- 🔴 **No go-to-definition, no find-references, no document/workspace symbols, no rename** anywhere in
  the Monaco path. `IRoslynLanguageService` exposes only `LoadWorkspace/UpdateDocument/Completions/
  Hover/Diagnostics` — F12, Shift-F12, Ctrl-T, and rename simply do nothing. This is the biggest
  "feels unfinished vs. VS Code/Cursor" gap. **(A first fix is implemented below — §Fixes.)**
- Note: the AST subsystem (`Services/AST/*`, `AstGraph`/`CallGraph`) and `WorkspaceIndexer` already
  compute symbol/definition-like data, but none of it is wired to editor navigation.

## 6. MCP server (the bridge)

**Works:** `McpHttpEndpoint` (`Services/Engine/Mcp/Transport/`) is a localhost JSON-RPC 2.0 server
(`initialize`/`ping`/`tools/list`/`tools/call` + token-free `GET /health`), per-session bearer token,
ports 9877–9897. `syncro-bridge`'s `mcpProvider.ts` registers it via
`vscode.lm.registerMcpServerDefinitionProvider` so all 17 engine MCP servers appear in VS Code chat.

- 🔴 **The MCP bridge only serves the Code-OSS path.** In the **Monaco** (default) editor there is no
  MCP surface at all — the engine's tools are reachable from VS Code chat but not from Syncro's own
  built-in editor. The two editors have very different capability sets.
- 🟠 **Write/exec tools are hard-refused over the bridge.** `McpHttpEndpoint` blocks any
  `RequiresAdminApproval` tool (no in-app approval flow yet), so `WritePipeline`, `RunBatchScript`,
  `GitPush`, `GitRollback`, `CloneRepository` are **unusable from the editor** — the agent can read but
  not act. (This is intentional-safe, but it caps the "agentic" story until an approval UI exists.)
- 🟠 **Orchestrator doesn't enforce approval for in-process calls.** `McpOrchestrator.ExecuteToolAsync`
  has a `// In production, validate AdminApproval…` TODO (`McpOrchestrator.cs:55`) — the gate exists only
  on the HTTP path, so an in-app agent-loop call can invoke a write tool ungated.
- 🟠 **Registration is fragile.** Servers register sequentially with `.GetAwaiter().GetResult()` in one
  try/catch (`MauiProgram.cs:202`); a single server's `InitializeAsync` throwing can abort the rest.

## 7. Host-integration robustness (`CodeOssIdeHost` / shell)

- 🟠 **Iframe-hosted Code-OSS is second-class.** It's shown in a plain `<iframe>` (`IdeShell.razor:53`),
  so Syncro's shell (activity bar, Problems, terminal, AI panel) and the Code-OSS workbench are two
  disconnected UIs stacked in the same window — no shared theme, command palette, or state.
- 🟠 **No health re-check / auto-recovery.** If the serve-web process dies after start, the iframe just
  goes blank; `StartAsync` reuses `_proc.HasExited==false` but nothing re-probes `/health` or restarts.
- 🟠 **Startup is heavy and opaque.** First-run requires `npm install && npm run compile && npm run gulp
  node` (the host detects and reports this well, `CodeOssIdeHost.cs:48–59`), but there's no in-app
  build trigger or progress — the user must run npm by hand.
- 🟡 **Fallback isn't automatic.** `IdeHostResolver.Fallback` (Monaco) exists but the shell doesn't
  auto-switch when Code-OSS reports `Unavailable`; it shows remedy text and the user must toggle back.
- 🟡 **`_codeOssUrl` iframe has no sandbox attributes / loading indicator.**

---

## Prioritized fix list

| # | Priority | Area | Fix |
|---|---|---|---|
| 1 | **P0** | §5 scope | Add **go-to-definition** to `IRoslynLanguageService` + Monaco provider. *(done below — first increment)* |
| 2 | **P0** | §3/§4 langs | Make non-C# languages usable: either **default the IDE to the Code-OSS host** (it already has full multi-language LSP) or add a JS/TS/Python language service. Recommend Code-OSS-as-default. |
| 3 | **P0** | §6 bridge | Build the **in-app admin-approval flow** so write/exec MCP tools (`GitPush`, `WritePipeline`, `RunBatchScript`) work from the editor, not just read tools. |
| 4 | P1 | §5 scope | Add **find-references** + **document symbols** (Ctrl-T outline) to the Roslyn service; wire the existing `AstGraph`/`CallGraph` for cross-language navigation. |
| 5 | P1 | §2 files | Add a **filesystem watcher** that refreshes the tree + open buffers (esp. after AI/git writes). |
| 6 | P1 | §2 files | Fix `FileService.ReadAsync` to return an **error state**, not error-text-as-content (prevents overwrite-with-comment on save). |
| 7 | P1 | §4 diag | Make diagnostics **workspace-wide and persistent** across tab switches; add build/analyzer diagnostics. |
| 8 | P1 | §1 open | Feed `ProjectContextBuilder` output (stack/AST/tasks) into the editor session so the IDE knows the project's detected stack. |
| 9 | P2 | §6 mcp | Move the `RequiresAdminApproval` gate into `McpOrchestrator` so in-app agent-loop calls are gated too; harden server registration against one-server failure. |
| 10 | P2 | §3 compl | Enrich completions (docs, snippets, commit chars, resolve) + cancellation on keystroke. |
| 11 | P2 | §7 host | Auto-fallback to Monaco on `Unavailable`; add `/health` re-probe + restart; add an in-app "build Code-OSS" trigger with progress. |
| 12 | P3 | §6 mcp | Expose an MCP surface (or the engine tools) to the **Monaco** editor so both hosts have parity. |

---

## Fixes applied in this pass

**#1 — Go-to-definition (the "scope definer") for the C#/Monaco path.** Additive, no change to existing
completion/hover/diagnostics flow:

- `IRoslynLanguageService` — new `GetDefinitionAsync(uri, line, column)` + `DefinitionDto`.
- `RoslynLanguageService` — resolves the symbol at the caret (same pattern as hover), returns the
  symbol's first in-source location as `{uri, line, column}`.
- `IdeShell.razor` — new `[JSInvokable] GetDefinitionJS`.
- `wwwroot/js/ide/monaco.js` — registers a `csharp` definition provider (enables **F12 / Ctrl-Click**).

> Remaining items in the list above are documented but **not** implemented — several (Code-OSS-as-default,
> approval flow, multi-language LSP) are design decisions worth confirming before building.
