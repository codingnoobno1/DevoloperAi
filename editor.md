# Syncro Editor Architecture: Embedded Minimal Code-OSS

> The core insight: Re-implementing a full IDE (search, terminal, LSP client, debugger, file watchers, peek, codelens, problems panel, quick fixes) from scratch on top of the Monaco widget is a monumental undertaking that essentially boils down to rewriting VS Code itself.
> 
> Instead, we adopt the Cursor Playbook: we take **Code-OSS** (the open-source core of VS Code) and embed it inside the Syncro IDE. 

---

## 1. The Architectural Strategy

We want the full power of the VS Code engine (Debugger, Search, Git, Workspace, Extension Host) without giving up the bespoke Blazor Syncro UI, and without forcing the user into a generic VS Code desktop window.

The strategy: **Run Code-OSS as a background local web server, strip its UI down to a "blank canvas", embed it seamlessly inside the Syncro Blazor shell via an iframe, and bridge them using a custom TypeScript extension.**

This gives us 100% of VS Code's features, but they are fully subservient to the Syncro master UI.

---

## 2. Component Roles

### The Backend Engine (Syncro.Server / C#)
Stays exactly as it is. It runs the AST parsing, LiteDB Universe Graph, AI orchestration, and the `StackRunManager`. It remains the ultimate source of truth for your multi-project ecosystem.

### The Cockpit (Syncro.Desktop / Blazor)
Remains the primary UI shell. The user looks at visual Connector graphs, monitors live server health, orchestrates multi-project builds, and manages agents here. When it comes time to edit a file, the Blazor shell displays the Code-OSS iframe.

### The Daemon Orchestrator (Syncro.CLI)
Rather than the Blazor UI spawning background processes directly, `syncro-cli` acts as the daemon manager. 
- A command like `syncro-cli daemon start` spins up the Code-OSS web server in the background.
- It handles downloading the Code-OSS server binaries, managing the extension host, and spinning up the web server process.

### The Editor Engine (Code-OSS + Web Server Mode)
Code-OSS runs in Web Server Mode (`code serve-web` or `openvscode-server`) as a background daemon.
- A custom `settings.json` is injected to aggressively hide the VS Code chrome:
  - `"workbench.activityBar.visible": false`
  - `"workbench.statusBar.visible": false`
  - `"workbench.layoutControl.enabled": false`
  - `"window.titleBarStyle": "custom"` (or completely hidden)
- To the user, the iframe looks like a totally blank, clean Monaco editor canvas that perfectly blends into the Syncro UI. 
- But under the hood, the entire VS Code engine is running.

### The API Bridge (Syncro TypeScript Extension)
We build a native TypeScript VS Code Extension (`extensions/syncro`) that runs inside the hidden Code-OSS process.
- **Access:** It has full access to `vscode.debug`, `vscode.workspace.findFiles` (Ripgrep Search), `vscode.extensions` (Git), and `vscode.languages` (LSP).
- **Communication:** The extension opens a WebSocket back to `Syncro.Server` (port 3030) or communicates via `window.postMessage` to the parent Blazor iframe.
- **Orchestration:** When the user clicks "Search" or "Debug" in the **Syncro Blazor UI**, Blazor sends a message to the TypeScript extension, which translates it into the native VS Code API call, executing the action invisibly in the background engine.

---

## 3. How the Features Map

| Feature | Implementation |
|---|---|
| Editor core | Handled by Code-OSS (which wraps Monaco). |
| Diff viewer | Code-OSS native diff editor (`vscode.commands.executeCommand('vscode.diff', ...)`). |
| Git / SCM | Code-OSS native SCM provider, driven by the Syncro extension. |
| Language Intelligence | Native VS Code language servers (LSP) running inside the Code-OSS extension host. |
| Terminal | Code-OSS native PTY terminal. |
| Project-wide Search | `vscode.workspace.findFiles` (Ripgrep) called by the Syncro extension. |
| Problems / Diagnostics | Native VS Code diagnostic collections. |
| Debugger | Native VS Code Debug Adapter Protocol (DAP). |
| Multi-root Workspace | Code-OSS native multi-root `.code-workspace` files managed by the CLI. |
| AI Inline Edits / Chat | Syncro Extension uses VS Code's Proposed APIs for Inline Chat and Inline Completions (the same ones Copilot and Cursor use) to provide ghost-text and `Cmd+K` diff editing, powered by the `.NET` Agent/AI infrastructure. |
| Missing Route Drafting | Syncro Extension registers a `CodeLensProvider` to inject `[Draft missing route]` above backend controllers. Clicking it triggers the AI over WebSocket. |

---

## 4. Roadmap

1. **Daemon Infrastructure:** Update `Syncro.CLI` to support spawning `code serve-web` with the minimal `settings.json` payload.
2. **Embedded Editor:** Update `Syncro.Desktop` to mount the iframe pointing to the CLI-managed local port.
3. **Extension Skeleton:** Scaffold the `extensions/syncro` TypeScript extension.
4. **Bridge Communication:** Establish the `window.postMessage` or WebSocket bridge between the Blazor UI and the TypeScript extension.
5. **Feature Migration:** Begin piping features (Search, Debug, Git) from Blazor buttons into the extension's VS Code API hooks.
