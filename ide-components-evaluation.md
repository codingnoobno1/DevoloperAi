# Syncro IDE — Component-by-Component Evaluation

> Deeper than the polish pass ([`ide-professional-polish.md`](ide-professional-polish.md)): this
> grades **each IDE subsystem** against the professional standard (VS Code / Antigravity), states
> what Syncro has **today** (grounded in the code), and gives the **reuse-or-build** call + effort.
>
> Grades: 🟢 pro-grade · 🟡 partial / stub · 🔴 missing.

---

## 0. Runtime — is Electron used? **No.**

Verified in the project files:

- `Microsoft.AspNetCore.Components.WebView.Maui` (csproj), `<BlazorWebView HostPage="wwwroot/index.html">`
  ([IdePage.xaml](IdePage.xaml), [MainPage.xaml](MainPage.xaml)), target `net9.0-windows10.0.19041.0`.
- **No** `electron`, `electron.NET`, `photino`, `CEF`, or bundled Chromium anywhere.

| | Antigravity (VS Code fork) | Syncro |
|---|---|---|
| App shell | **Electron** | **.NET MAUI** |
| Renderer | Chromium **bundled** (~150–200 MB) | **WebView2** — the OS Edge/Chromium runtime, *not* bundled (app stays small) |
| Backend / main process | **Node.js** | **C# / .NET** |
| JS ecosystem | full npm + extension host | npm libs vendored into the WebView only; **no Node at runtime** |
| Multi-window | Electron `BrowserWindow` | **native MAUI `Window`** (`IdeWindowService`) |
| Language intelligence | LSP servers as child processes | **Roslyn in-process** (latency edge for C#) |

**Implications of WebView2 vs Electron (be aware):**
- ✅ Smaller installer, C# backend (your AST/agent brain is native), real native windows.
- ⚠️ No Node extension host → no VS Code extension ecosystem; everything is build-it-yourself.
- ⚠️ WebView2 has **one shared web context per window**; heavy multi-process isolation that
  Electron gives is not there. Web **workers** still work (Monaco needs them — see §3).
- ⚠️ Some Electron niceties (custom title bar integration, native menus, `webContents` APIs) need
  MAUI/Win32 equivalents.

**Verdict:** the runtime choice is fine and even advantageous for Syncro's C#-centric design. It is
**not** the reason Syncro looks unprofessional — that's the component gaps below.

---

## 1. Component scorecard (the whole picture)

| # | Component | Pro standard | Syncro today | Grade | Reuse / Build | Effort |
|---|---|---|---|---|---|---|
| 1 | **Code editor** | Monaco | Monaco via `monaco.js` (provider model) | 🟢 | reuse (have) | — |
| 2 | **Syntax highlighting** | TextMate grammars | Monaco basic-languages (all vendored) | 🟢 | reuse (have) | — |
| 3 | **IntelliSense (web langs)** | TS/JSON/CSS/HTML services | workers vendored; **unverified at runtime** | 🟡 | reuse + verify worker URL | S |
| 4 | **IntelliSense (C#/Py)** | LSP servers | none (highlight + word-complete only) | 🔴 | **build: Roslyn→Monaco (W10)** | L |
| 5 | **Terminal** | xterm + PTY | xterm **echo only**, "pending (W3)" | 🔴 | reuse xterm + **build ConPTY** | M |
| 6 | **File explorer / tree** | virtualized + icons | `FileTree.razor` recursive, no virt, no icons | 🟡 | build virt + reuse icon set | M |
| 7 | **File-type icons** | Seti/Material set | **none** | 🔴 | reuse icon set (MIT) | S–M |
| 8 | **Tabs / editor groups** | icon+name, dirty, split | GoldenLayout tabs, no icons | 🟡 | enhance | S |
| 9 | **Docking / layout** | custom grid | GoldenLayout | 🟢 | reuse (have) | — |
| 10 | **Minimap** | Monaco | Monaco (on) | 🟢 | reuse (have) | — |
| 11 | **Breadcrumbs** | path + symbol | **none** | 🔴 | build (AST gives symbols) | M |
| 12 | **Status bar** | dense, real info | custom, leaks dev strings | 🟡 | rebuild content | S |
| 13 | **Activity bar** | full icon set | custom, looks cut-off | 🟡 | fix + extend | S |
| 14 | **Command palette** | Ctrl+Shift+P | **none** | 🔴 | build over a command registry (W16) | M |
| 15 | **Keybindings** | full, rebindable | Ctrl+S only | 🔴 | build registry | M |
| 16 | **Search (find in files)** | ripgrep | **none** | 🔴 | reuse ripgrep via CliWrap | M |
| 17 | **Source control / Git** | SCM view | `GitManager` in main app, **not in IDE** | 🟡 | reuse LibGit2Sharp/CliWrap + build panel | M |
| 18 | **Diff / merge** | Monaco diff | `showDiff` exists, **not surfaced** | 🟡 | wire up (have engine) | S |
| 19 | **Problems / diagnostics** | from LSP | **static / always empty** | 🔴 | build (feeds from #4) | S after #4 |
| 20 | **Debugger** | DAP | **none** | 🔴 | build (DAP: netcoredbg/debugpy) | XL |
| 21 | **Themes** | refined, neutral | `syncro-dark`, too colorful | 🟡 | re-spec (polish doc §6) | S |
| 22 | **Settings UI** | full | **none** | 🔴 | build Blazor settings | M |
| 23 | **Notifications/toasts** | native | MudBlazor `Snackbar` | 🟡 | reuse (have) | — |
| 24 | **AI panel** | rich chat | task-loop wired, thin UI | 🟡 | enhance | M |
| 25 | **Multi-window** | Electron windows | native MAUI windows | 🟢 | reuse (have) | — |
| 26 | **Extensions** | marketplace | **none** (no Node host) | 🔴 | out of scope / MCP-as-extensions | XL |

**Headline:** the **rendering layer is pro-grade** (Monaco, docking, minimap, multi-window). The
gaps are the **developer-workflow components** — IntelliSense, terminal, search, git view, command
palette, problems, debugger, icons — plus the **theme/chrome** reading as a demo.

---

## 2. The critical components, in depth

### 2.1 IntelliSense (#3, #4) — the biggest credibility gap
- **Web langs (TS/JS/JSON/CSS/HTML):** Monaco bundles real language services; the workers are
  vendored (`lib/monaco/vs/language/**`, `base/worker/workerMain.js`). They *should* work, but in
  WebView2 the worker bootstrap can fail silently → **add `MonacoEnvironment.getWorkerUrl`** so
  it's guaranteed. (Effort S.)
- **C#/Python (the languages that matter here):** no semantic intelligence. The right answer is
  **W10: Roslyn → Monaco** — register Monaco completion/hover/diagnostics providers whose handlers
  `[JSInvokable]`-call C# backed by the **in-process Roslyn** Syncro already runs
  (`CSharpAstParser`). No LSP server, lower latency than VS Code's own pipeline. For Python/TS as
  first-class langs later, bridge a real LSP behind the **same** provider surface. (Effort L.)
- **Why it matters most:** #4 also feeds #19 (Problems) and the status-bar error counts (#12). One
  build unlocks three "this is a real IDE" signals.

### 2.2 Terminal (#5) — the most damning stub
- xterm renders + echoes but there's **no shell**; the banner literally says *"ConPTY shell binding
  pending (W3)"* — the single most unprofessional element on screen.
- **Fix:** keep xterm; add a **ConPTY** (Windows pseudo-console) host in C#. Options: a small P/Invoke
  wrapper around `CreatePseudoConsole`, or the **Pty.Net**-style NuGet. Stream bytes ⇄ xterm over
  interop; spawn the user's shell (pwsh/bash/wsl). (Effort M.)
- Until then: **show a blank prompt, not the "pending" text** (polish doc §3).

### 2.3 File tree + icons (#6, #7) — instant perceived quality
- Antigravity's explorer reads instantly because of **file-type icons** + density. Syncro shows
  plain text → looks empty/amateur.
- **Icons:** vendor an MIT icon set — **Seti UI** or **Material Icon Theme** (vscode-icons) — and map
  by extension/filename. (Effort S–M.)
- **Virtualization:** the current `FileTree.razor` renders every node. Blazor's `<Virtualize>` is
  already available (`_Imports.razor`) — wrap the tree for 10k+ files. (Effort M.)

### 2.4 Command palette + keybindings (#14, #15) — defines "an IDE"
- A custom Blazor overlay (or Monaco's quick-input) over a **C# command registry**
  (`ICommandRegistry`: id, title, keybinding, handler). Ctrl+Shift+P lists commands; commands are
  the same ones the activity bar / menus invoke. (Effort M.) This is W16.

### 2.5 Source control (#17) + diff (#18) — you already have the pieces
- A `GitManager` exists in the main app; the IDE has **no SCM panel**. Add a Source Control view
  (changes list, stage, commit) backed by **LibGit2Sharp** or git-via-CliWrap.
- The **Monaco diff editor is already wired** (`SyncroMonaco.showDiff` / `IEditorEngine.ShowDiffAsync`)
  — surface it for "compare with HEAD" and for the versioning rail (W11). (Effort: SCM M, diff S.)

### 2.6 Status bar (#12) + breadcrumbs (#11) — density = credibility
- Status bar must show **real** info: git branch, ⛔/⚠ counts (from #4), Ln/Col, indentation,
  encoding, EOL, language, interpreter — and **drop** "layout ready"/"Roslyn"/phase strings.
- Breadcrumbs (file path + current symbol) above the editor — Syncro's AST already yields the
  symbol list. (Effort: status S, breadcrumbs M.)

### 2.7 Debugger (#20) — the one genuinely large gap
- Real debugging needs the **Debug Adapter Protocol** (DAP) + an adapter per language (netcoredbg
  for .NET, debugpy for Python). This is **XL** and arguably post-MVP. Antigravity gets it free from
  VS Code; Syncro would build it. Defer unless debugging is a headline requirement.

### 2.8 Extensions (#26) — accept the tradeoff
- No Node host → no VS Code extension marketplace. Don't fight this. Syncro's equivalent is its
  **MCP tools + agents** ("extensions" that are C#/MCP, not VSIX). Frame it that way; don't try to
  emulate the marketplace.

---

## 3. What "professional" actually requires here (priority order)

The rendering layer is done. To *feel* professional, in order of impact-per-effort:

1. **Polish P0** (cut self-promo + neutral VS Code theme) — hours, free. ([polish doc](ide-professional-polish.md) §3, §6)
2. **File-type icons** (#7) + **real status bar** (#12) — a day; huge perceived-quality jump.
3. **Real terminal / ConPTY** (#5) — removes the worst stub.
4. **Roslyn → Monaco IntelliSense** (#4) → unlocks **Problems** (#19) + error counts.
5. **Command palette** (#14) + **search** (#16) — power-user table stakes.
6. **SCM panel** (#17) + **diff** (#18, mostly wired).
7. (Later) Settings UI (#22), AI panel depth (#24), Debugger (#20).

---

## 4. Reuse map (libraries, all MIT unless noted)

| Need | Library / approach | Status |
|---|---|---|
| Editor / diff / minimap | **Monaco** | vendored ✅ |
| Terminal UI | **xterm.js** (+ addon-fit) | vendored ✅ |
| Terminal backend | **ConPTY** (P/Invoke) or Pty.Net-style NuGet | build |
| Docking | **GoldenLayout** | vendored ✅ |
| Graphs | **Cytoscape.js** | vendored ✅ (unused) |
| Diagrams | **Mermaid** | vendored ✅ (unused) |
| File icons | **Seti UI** / **Material Icon Theme** | add |
| Virtualized tree | Blazor `<Virtualize>` | available ✅ |
| Search | **ripgrep** via CliWrap | add |
| Git | **LibGit2Sharp** or git-CliWrap | add (LibGit2Sharp = build) |
| C# intel | **Roslyn** in-process | have ✅ → wire (W10) |
| Other-lang intel | LSP via stdio behind the provider | later |
| Debug | **DAP** + netcoredbg/debugpy | later (XL) |

**Bottom line:** Syncro isn't behind because of Electron (it doesn't use it) or because of bad
foundations (Monaco/docking are pro-grade). It's behind on **workflow components and chrome
restraint** — every one of which is a known, bounded build on top of what's already here.
