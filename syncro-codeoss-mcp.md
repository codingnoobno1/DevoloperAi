# Syncro × Code-OSS × MCP — Architecture Reference

> How Syncro Desktop hosts a real VS Code OSS editor behind an abstraction layer, and the full
> catalog of its MCP servers/tools. Everything here reflects **verified, working state**
> (build 0 errors; code-oss served end-to-end on 2026-07-16).

---

## 1. Syncro Desktop in one picture

```
┌────────────────────────────  Syncro.Desktop (MAUI + Blazor Hybrid)  ───────────────────────────┐
│                                                                                                 │
│  Main window (MudBlazor shell)                 IDE window (IdePage → BlazorWebView)             │
│  ├─ Connector / Multi-Scan / Runtime / AST     └─ IdeShell.razor (GoldenLayout workbench)       │
│  │                                                    │                                         │
│  │                                                    ▼                                         │
│  │                                            IdeHostResolver ──► IIdeHost (the seam)           │
│  │                                                    │      ┌──────────────┴──────────────┐    │
│  │                                                    │  MonacoIdeHost              CodeOssIdeHost
│  │                                                    │  (in-process,               (child process:
│  │                                                    │   always ready)              VS Code OSS server)
│  │                                                    │                                   │    │
│  ▼                                                    ▼                                   ▼    │
│  Engines (Connector, Universe graph,          iframe / Blazor portal            http://127.0.0.1:9888
│  Reverse-AST bridge, Runtime, AgentCli)                                          ?folder=<workspace>
│                                                                                                 │
│  McpOrchestrator ──► 14 MCP servers / ~50 tools (deterministic engine surface for the AI agent) │
└─────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Two windows, one principle: **the shell never knows which editor backend is running**, and the AI
agent never touches the engines directly — everything goes through typed seams (`IIdeHost`, MCP tools).

---

## 2. The IDE host abstraction (`Services/Ide/Host/`)

The seam that lets Syncro swap its custom Monaco editor for real VS Code OSS without touching the shell.

| File | Role |
|---|---|
| `IIdeHost.cs` | The whole-workbench contract: `StartAsync(workspacePath) → IdeHostSession`, `StopAsync()` |
| `IdeHostModels.cs` | `IdeHostKind` (Monaco \| CodeOss), `IdeHostMode` (InProcess \| WebUrl \| Unavailable), `IdeHostSession` (Url/Status/**Remedy**), `IdeHostOptions` (Kind, CodeOssPath, Port=9888, StartTimeoutSeconds) |
| `MonacoIdeHost.cs` | The built-in Blazor+Monaco shell. In-process, zero startup, **always-available fallback** |
| `CodeOssIdeHost.cs` | Launches the real VS Code OSS server as a child process (details in §3). **Never throws** — every failure returns `Unavailable` + an actionable `Remedy` string |
| `IdeHostResolver.cs` | Picks `Active` from `IdeHostOptions.Kind`; exposes `.Fallback` (Monaco). DI singletons via `SyncroIdeServiceCollectionExtensions.AddSyncroIde()` |

**Session contract** — the shell renders whatever mode comes back:

| `Mode` | Shell behavior |
|---|---|
| `InProcess` | Render the Blazor/Monaco workbench (no URL) |
| `WebUrl` | Load `session.Url` in the iframe portal |
| `Unavailable` | Show `Status` + `Remedy` + a **Start / retry** button; fall back to Monaco |

**Shell wiring** (`Components/IDE/IdeShell.razor`):
- The `blazor-codeoss-portal` iframe binds to `_codeOssUrl` (was a dead hardcoded `:8080` — the original "can't connect").
- **Start / retry** → `Hosts.Get(IdeHostKind.CodeOss).StartAsync(State.Current?.RootPath)`.
- **Make default editor** toggles `IdeHostOptions.Kind`; when CodeOss is default it auto-boots on IDE open (fire-and-forget, never blocks init).
- `Dispose()` → `StopAsync()` kills the server process tree with the window.

**Related, lower-level seams** (predate the host layer; still used by the Monaco path):
`Engines/IEditorEngine` (open/get/set/diff via `window.SyncroMonaco`), `ITerminalEngine`, `IRoslynLanguageService`.
`IIdeHost` sits **above** these: it swaps the whole workbench, not just the text editor.

---

## 3. Code-OSS components (`vscode-main/`)

A full **VS Code OSS source checkout** (own git repo) with a custom patch:
`AgentHostChannel` — registers an `agentHostProxy` IPC channel, enabled by
`--agent-host-bridge-port` / `--agent-host-bridge-path`. This is Syncro's hook for the C#
`NodeAgentBridge` (copilot-engine sidecar, Phase 2).

`product.json`: `applicationName: code-oss`, `serverApplicationName: code-server-oss`.

### 3.1 What CodeOssIdeHost actually launches

```
<vscode-main>/.build/node/v24.17.0/win32-x64/node.exe        ← the checkout's own node runtime
    scripts/code-server.js --host 127.0.0.1 --port 9888 --without-connection-token
    env: NODE_ENV=development  VSCODE_DEV=1  VSCODE_SKIP_PRELAUNCH=1
        → runs out/server-main.js  →  Web UI at http://127.0.0.1:9888/?folder=<workspace>
```

**Why not `code-web.bat`:** it re-runs heavy first-run downloads on every launch (builtin extensions,
remote node, and — via preLaunch — electron) and serves the *browser-sandbox* build. The **server
flavor** (`code-server.js` → `server-main.js`) is what supports opening local folders.

**Readiness probes** (each failure = precise `Remedy`, no exception):
1. Checkout located? (`IdeHostOptions.CodeOssPath`, else walks up looking for a sibling `vscode-main`)
2. Compiled? → `out/server-main.js` exists
3. Server node downloaded? → `.build/node/**/node(.exe)` exists
4. Launch → poll TCP `127.0.0.1:<port>` until up (timeout → kill + `Unavailable`)

### 3.2 One-time build recipe (Windows, all pitfalls solved on this machine)

| Step | Command | Pitfall it clears |
|---|---|---|
| 0 | VS Installer → *MSVC v143 – VS 2022 C++ **x64/x86** Spectre-mitigated libs (Latest)* | `MSB8040` from every native module. ARM variants don't count. Install into the instance node-gyp uses |
| 1 | `set npm_config_msbuild_path=…\Community\MSBuild\Current\Bin\MSBuild.exe`<br>`set GYP_MSVS_OVERRIDE_PATH=…\Community` · `set GYP_MSVS_VERSION=2022` | node-gyp defaults to the **BuildTools** instance, which lacked Spectre libs |
| 2 | Node **≥ 24.17** on PATH (portable zip is fine; run system npm via `node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" <cmd>`) | `build/npm/preinstall.ts` hard-rejects older Node (system had 24.14) |
| 3 | `npm install` | → 1560 packages, all native modules compile |
| 4 | `npm run compile` | → `out/` incl. `server-main.js` (~4 min) |
| 5 | `npm run download-builtin-extensions` · `npm run gulp node` | Builtin extensions + the `.build/node` server runtime |

### 3.3 Runtime gotchas (encoded in `CodeOssIdeHost`)

- **Drain redirected pipes.** The dev server logs heavily; an unread stdout/stderr pipe fills and
  **blocks node mid-response** (port opens, HTTP hangs forever). Fixed with `BeginOutputReadLine()`
  + no-op handlers.
- **First page load ≈ 30 s** in dev mode (`CSS_DEV` compiles ~400 css modules). Don't use short
  HTTP timeouts; subsequent loads are fast.
- `@vscode/spdlog` binding errors in the log are **non-fatal** — logging falls back, server runs.
- Server data dir: `~/.vscode-server-oss-dev/`.

---

## 4. MCP layer — servers & tools

`Services/Engine/Mcp/` — `IMcpServer` + `IMcpTool` contracts, dispatched by `McpOrchestrator`.
Tools are deterministic engine calls (no AI inside); each declares `InputSchema` and
`RequiresAdminApproval`. This is the agent's only surface onto the engines.

### 4.1 Engine servers (`Services/Engine/Mcp/Servers/`)

| Server | Tools | Purpose |
|---|---|---|
| **File System MCP** | `ReadFile` · `WriteFile` · `WriteFileBatch` · `DeleteFile` · `GlobSearch` | Workspace-guarded file ops (`PathGuard` boundary; batch = atomic multi-file) |
| **Repository MCP** | `ScanRepository` · `RepositoryTree` · `DetectProjectType` | Repo shape & stack detection |
| **Workspace MCP** | `ListProjects` · `WorkspaceSummary` | Project registry views |
| **AST MCP** | `FindClasses` · `FindFunctions` · `FindImports` | Symbol queries over the deep-AST engine (`Services/AST`: Python/TS/JS/C# parsers) |
| **API MCP** | `ParseSwagger` · `FetchEndpoint` | OpenAPI contracts + live endpoint probes |
| **Git MCP** | `GitStatus` · `Commit` · `CloneRepository` | Version control |
| **Patch MCP** | `ValidatePatch` · `ApplyPatch` · `RollbackPatch` | Safe, reversible edits |
| **Planning MCP** | `CreateTaskGraph` · `UpdateTaskStatus` | Agent task graphs |
| **Knowledge MCP** | `StoreHindsight` · `QueryHindsight` · `UpdateArchitecture` | Decision memory (Hindsight vector store) |
| **RepairKnowledge MCP** | `QueryRepairKnowledge` · `ApplyKnownRepair` · `StoreRepairArtifact` | Known-fix reuse |
| **JSON Intelligence MCP** | `ExtractSchema` · `ValidatePayload` | Payload shape work |

### 4.2 Connector MCP (`Services/Connector/Mcp/`)

The frontend↔backend contract engine. Resolvers are deterministic (Swagger → source scan → AI-draft last).

| Tool | What it does |
|---|---|
| `ResolveBackendContract` | Backend repo → routes (Swagger preferred, else regex route scan: Express/FastAPI/Flask/Spring/ASP.NET) |
| `ExtractFrontendApi` | Frontend repo → API call sites. **AST-first**: `ThunderScanFrontendResolver` consumes `.thunder/reverse/connector/api.json` when a `thunder scan` exists, else regex heuristics |
| `MapContracts` | Calls × routes → connection report: `Matched / MethodMismatch / ShapeMismatch / Missing` + orphan routes |
| `GenerateBackendForCall` | AI-drafts the backend route for a `Missing` call (Groq) |
| `WriteBackendFiles` | Applies generated files (PathGuard + batch ops) |
| `SyncConnectorContract` | Persists `connector.json` / `connector.md` into the repo |

Sibling non-MCP service: **`MultiProjectScanService`** (`Services/Connector/Scan/`) — root folder →
discover N projects → resolve each → cross-map. Powers the `/universe-scan` page; also extracts the
data layer (`DataLayerScanner`: EF `DbSet<>` tables, Mongo collections, migrations, models/DTOs).

### 4.3 Universe & Runtime MCP (`Services/Universe/Mcp/`)

| Server | Tools | Purpose |
|---|---|---|
| **UniverseMcp** | `IndexWorkspace` · `UniverseGraphStats` · `MapUniverse` · `ReconcileDependencies` | Populate/query the persistent cross-workspace graph (LiteDB, `%LocalAppData%/SyncroDesktop/universe.db`). `ThunderScanGraphContributor` enriches from reverse-AST scans (service classes, remote-server seeds) |
| **RuntimeMcp** | `StartStack` · `StopStack` · `RunStatus` · `RunGroup` · `StopGroup` · `BootstrapWorkspace` | Run projects (wave-ordered groups: db → backend → frontend), health-watched, env-bootstrapped |

### 4.4 Related engine (not MCP, feeds the tools)

**Reverse-AST / Thunder** (`Services/tool/`, Dart, analyzer 7.7.1): `thunder scan <flutter-app>` →
16 JSON files under `.thunder/reverse/` (screens, widgets, models, dtos, bloc, theme, datasource,
`connector/api.json`, `master_ir.json`). Resolves the ApiClient + Repository + ApiConstants
indirection to real endpoints — consumed by `ExtractFrontendApi`, `MapContracts`, the Universe graph
and Multi-Scan.

---

## 5. Decision log (why it's shaped this way)

1. **Abstraction over reimplementation** — a custom Monaco workbench can't chase VS Code's feature
   surface. `IIdeHost` makes real VS Code OSS a *backend*, with Monaco as the guaranteed fallback.
2. **Server flavor, not web sandbox** — local-folder editing requires `server-main.js`
   (`code-server-oss`), not the `code-web` browser build.
3. **Fail with a remedy, never a blank frame** — every unavailable state carries the exact command
   to fix it (the old UI showed a dead iframe; the new one shows what to run).
4. **MCP as the only engine surface** — the AI agent operates the engines exclusively through typed,
   schema'd, approval-gated tools; engines stay deterministic and testable in isolation.
5. **AgentHostChannel** — the code-oss patch that will let the C# side bridge into the editor's
   agent host (`--agent-host-bridge-port`), unifying the IDE and the copilot-engine sidecar.
