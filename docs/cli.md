# Syncro CLI v2 — Deep Architecture Reference

> **Standalone executable · Win32 API · AI-Powered · Named Pipe IPC**  
> Built as a .NET 9 Console application — fully isolated from the MAUI host process.

---

## Table of Contents

1. [Overview & Design Goals](#1-overview--design-goals)
2. [Project Directory Tree](#2-project-directory-tree)
3. [Full Command Reference](#3-full-command-reference)
4. [Win32 PATH Installer](#4-win32-path-installer)
5. [Shell Runner Abstraction](#5-shell-runner-abstraction)
6. [Build Pipeline (MSBuild)](#6-build-pipeline-msbuild)
7. [AI Command Pipeline](#7-ai-command-pipeline)
8. [Named Pipe IPC Bridge](#8-named-pipe-ipc-bridge)
9. [Pre-Written Scripts](#9-pre-written-scripts)
10. [Security & UAC Model](#10-security--uac-model)
11. [Roadmap](#11-roadmap)

---

## 1. Overview & Design Goals

`syncro-cli.exe` is a **completely independent .NET 9 console application** spawned in its own
native Windows Terminal / PowerShell window. It communicates back to the Syncro Desktop agent
via a **Named Pipe IPC bridge**, making it safe to crash, restart, and extend without ever
risking the MAUI host process.

### Core Principles

| Principle | Implementation |
|---|---|
| **Process Isolation** | Spawned via `Process.Start` with `UseShellExecute = true`. Crashes never propagate to Desktop. |
| **Win32 Integration** | `SendMessageTimeout` P/Invoke broadcasts `WM_SETTINGCHANGE` after PATH updates — no reboot. |
| **AI-Aware** | `syncro ai *` subcommands call LLM with AST + Vector DB context injected as RAG. |
| **Live Status** | Named Pipe pushes real-time JSON status packets to the Desktop sidebar. |
| **Extendable Scripts** | Pre-written PS1/Bash scripts bundled. AI can register new scripts at runtime via RAG. |
| **Least Privilege** | Admin elevation requested only for `syncro install`. All other commands run as standard user. |

---

## 2. Project Directory Tree

```
Syncro.Desktop/
├── Syncro.CLI/                         # Standalone console project
│   ├── Syncro.CLI.csproj               # net9.0 Exe, AssemblyName=syncro-cli
│   ├── Program.cs                      # Entry point, interactive REPL loop
│   ├── Win32Api.cs                     # P/Invoke: SendMessageTimeout, Broadcast
│   ├── ShellRunner.cs                  # PowerShell & Bash execution abstraction
│   │
│   ├── Commands/
│   │   ├── InitCommand.cs              # syncro init — workspace bootstrapper
│   │   ├── DoctorCommand.cs            # syncro doctor — env health check
│   │   ├── GitCommand.cs               # syncro git clone/pull/push (safe child process)
│   │   ├── RunCommand.cs               # syncro run — auto-detect and launch dev server
│   │   ├── ScanCommand.cs              # syncro scan — trigger AST analysis
│   │   ├── GraphCommand.cs             # syncro graph — render dependency graph
│   │   ├── AstCommand.cs               # syncro ast — inspect AST nodes
│   │   ├── KbCommand.cs                # syncro kb — knowledge base operations
│   │   ├── MemoryCommand.cs            # syncro memory — view agent memory store
│   │   ├── AiCommand.cs                # syncro ai — LLM queries with RAG context
│   │   ├── ProjectCommand.cs           # syncro project — register/list/group projects
│   │   ├── TemplateCommand.cs          # syncro template — list/apply code templates
│   │   ├── GenerateCommand.cs          # syncro generate — scaffold boilerplate
│   │   ├── PatchCommand.cs             # syncro patch — AI auto-fix compiler errors
│   │   ├── DeployCommand.cs            # syncro deploy — push to cloud targets
│   │   ├── StatusCommand.cs            # syncro status — push status to Desktop UI
│   │   ├── AgentCommand.cs             # syncro agent — multi-step autonomous AI task
│   │   └── InstallCommand.cs           # syncro install — PATH registration (UAC)
│   │
│   ├── AI/
│   │   ├── AgentBridge.cs              # Named Pipe client → sends to Desktop UI
│   │   ├── RagEngine.cs                # RAG retrieval from SyncroDB Vectors + AST
│   │   └── LlmClient.cs               # Local LLM or API call abstraction
│   │
│   └── Scripts/
│       ├── setup-venv.sh               # Python venv bootstrapper
│       ├── install-node.ps1            # Node.js + npm installer (winget/nvm-windows)
│       ├── check-db.sh                 # DB connectivity checker (SQL/NoSQL)
│       ├── clone-repo.ps1              # Safe repo cloner with Named Pipe progress report
│       └── add-to-path.ps1             # Elevated PATH registration script
│
└── Syncro.Desktop.csproj               # Post-build target: BuildCLI
```

---

## 3. Full Command Reference

### `syncro init`
**Category:** Dev  
Bootstraps a new Syncro workspace. Creates `.syncro_db/`, registers the project, and auto-detects
the tech stack via the language/framework fingerprinting engine (`FrameworkKnowledgeStore`).

```bash
syncro init .
syncro init ./myproject --name "Viking API"
syncro init . --force   # Re-initialise existing workspace
```

---

### `syncro doctor`
**Category:** Info  
Runs a complete environment health check. Verifies Node.js, Python, .NET SDK, Git, Docker,
database connectivity, and venv presence. Outputs a color-coded report.

```bash
syncro doctor
syncro doctor --fix       # Attempt to auto-install missing tools
syncro doctor --json      # Machine-readable output for agent use
```

**Checks performed:**

| Tool | Detection Method | Auto-Fix |
|---|---|---|
| Node.js | `node --version` | Install via `winget install OpenJS.NodeJS` |
| Python | `python --version` | Install via `winget install Python.Python.3` |
| .NET SDK | `dotnet --version` | Link to download page |
| Git | `git --version` | Install via `winget install Git.Git` |
| Docker | `docker info` | Link to Docker Desktop |
| MongoDB | `mongosh --eval "db.version()"` | Report only |
| PostgreSQL | `psql --version` | Report only |
| Redis | `redis-cli ping` | Report only |

---

### `syncro git`
**Category:** Dev  
Safe Git wrapper that runs clone, pull, push, and status operations **in a child process**.
Reports progress back to Desktop via Named Pipe. Clone failures never crash the host.

```bash
syncro git clone https://github.com/user/repo.git
syncro git clone https://github.com/user/repo.git --dest D:/workspace/repo
syncro git pull --rebase
syncro git push origin main
syncro git status
```

> **Safety guarantee:** The actual `git` process runs as a grandchild subprocess of the CLI.
> If git crashes or hangs, the CLI detects it via a watchdog timeout and reports the failure
> to the Desktop without impacting either process.

---

### `syncro run`
**Category:** Dev  
Auto-detects the project type using `FrameworkKnowledgeStore` and runs the appropriate dev server.

```bash
syncro run
syncro run --port 5000
syncro run --env .env.staging
syncro run --project viking-backend-002
```

**Auto-detection mapping:**

| Detected Framework | Run Command |
|---|---|
| Next.js / NestJS / Vite | `npm run dev` |
| React (CRA) | `npm start` |
| Django | `python manage.py runserver` |
| FastAPI | `uvicorn main:app --reload` |
| Flask | `flask run` |
| Flutter | `flutter run` |
| ASP.NET Core | `dotnet run` |
| Spring Boot | `./mvnw spring-boot:run` |
| Go | `go run .` |
| Rust (Axum) | `cargo run` |

---

### `syncro scan`
**Category:** System  
Triggers a full AST scan of the current project. Nodes are parsed, context-filtered
(build artifacts separated from source), and stored in `SyncroDB/AST/`.

```bash
syncro scan
syncro scan --depth 5
syncro scan --filter source          # Exclude build artifact nodes
syncro scan --incremental            # Re-scan only changed files
syncro scan --project viking-backend-002
```

**Context Filtering Rules:**

Artifact directories excluded from source nodes:
- `.next/`, `node_modules/`, `bin/`, `obj/`, `.git/`, `dist/`, `build/`, `.vercel/`, `__pycache__/`

Nodes from artifact paths are stored in `AST/filtered.json` (separated from main AST index)
so they never pollute RAG context while remaining available for debugging.

---

### `syncro graph`
**Category:** Info  
Renders the dependency graph from the AST. Supports Mermaid and DOT output formats.
Supports cross-project group graphs.

```bash
syncro graph
syncro graph --format mermaid
syncro graph --format dot --output graph.dot
syncro graph --group grp-mern-001    # Cross-project combo graph
syncro graph --symbol TenderController  # Sub-graph for one symbol
```

---

### `syncro ast`
**Category:** System  
Directly queries the AST node store. Inspect specific symbols, list all classes/routes,
or find all usages of a method.

```bash
syncro ast find TenderController
syncro ast usages loginUser
syncro ast list --type Route
syncro ast list --type Class --framework Express
syncro ast diff                      # Compare last two scan snapshots
```

---

### `syncro kb`
**Category:** DB  
Manages the Knowledge Base within SyncroDB. Add, search, and list architectural notes,
design decisions, and framework documentation snippets.

```bash
syncro kb search "JWT auth flow"
syncro kb add --tag auth --file notes.md
syncro kb add --type framework --file custom-fastapi.json
syncro kb list
syncro kb export --format markdown
```

---

### `syncro memory`
**Category:** AI  
Views and manages the Agent Memory Store — past code generations, success/failure status,
and iteration counts.

```bash
syncro memory list
syncro memory list --status failed
syncro memory list --framework Express --last 10
syncro memory view mem-gen-2047
syncro memory purge --older-than 30d
syncro memory purge --status failed
```

---

### `syncro ai`
**Category:** AI  
The primary AI command. Integrates AST context, Vector DB search, and LLM inference.

```bash
# Explain a symbol using AST + vector context
syncro ai explain TenderController

# Find the auth flow across the project
syncro ai find-auth-flow

# Generate code matching project conventions
syncro ai generate controller Tender
syncro ai generate dto Tender --lang csharp
syncro ai generate service UserAuth

# Review a file for issues
syncro ai review src/controllers/auth.ts

# Ask a natural language question
syncro ai ask "Where is JWT verified in this project?"
```

**Pipeline:**
```
Parse CLI Query
    → AST Symbol Lookup     (O(1) from ast_index.json)
    → Metadata Filter       (project, framework, language)
    → Vector Similarity     (cosine match in embeddings.vec)
    → Memory RAG Injection  (few-shot: past success records)
    → Prompt Assembly       (AST + Vectors + Memory + System prompt)
    → LLM Inference         (local or cloud endpoint)
    → Save to Memory        (append result to memory.jsonl)
```

---

### `syncro generate`
**Category:** AI  
Scaffolds full boilerplate matching detected language and framework conventions.
Prefers generation patterns with `success=true` from Memory Store.

```bash
syncro generate controller Tender
syncro generate service UserAuth --lang csharp
syncro generate dto TenderRequest --framework nestjs
syncro generate model Product --db mongodb
syncro generate page Dashboard --framework nextjs
```

---

### `syncro patch`
**Category:** AI  
Reads the last compiler error from `SyncroDB/Errors/` and applies an AI-assisted auto-fix.
Runs the build again and updates Memory on result.

```bash
syncro patch --last-error
syncro patch --file src/controllers/auth.ts
syncro patch --error "TS2345: Argument of type 'string' is not assignable..."
```

**Auto-fix loop:**
```
Read last error from Errors/build_errors.jsonl
    → Query AI for fix (with file context injected)
    → Apply patch to file
    → Re-run build command
    → If success: update Memory (success=true), done
    → If fail (< 3 iterations): retry with new error context
    → If fail (≥ 3 iterations): update Memory (success=false), report
```

---

### `syncro template`
**Category:** Dev  
Lists and applies micro-architecture templates stored in `SyncroDB/Templates/`.

```bash
syncro template list
syncro template search "jwt"
syncro template apply jwt-auth ./src/auth
syncro template apply mongodb-connector ./src/db
syncro template apply stripe-checkout ./src/payments
```

---

### `syncro project`
**Category:** System  
Registers, lists, and removes projects from the SyncroDB Projects index.
Supports grouping into combo stacks.

```bash
syncro project list
syncro project add ./backend --name "Viking API"
syncro project remove viking-backend-002
syncro project group "MERN Stack" api frontend db
syncro project info viking-backend-002
```

---

### `syncro deploy`
**Category:** System  
Pushes the project to configured cloud targets.

```bash
syncro deploy --target cloudrun
syncro deploy --target azure
syncro deploy --target vercel
syncro deploy --preview          # Dry-run: show what would be deployed
```

---

### `syncro status`
**Category:** Info  
Pushes the current CLI task status to the Syncro Desktop UI via Named Pipe.

```bash
syncro status --msg "AST scan complete" --level success
syncro status --msg "Build failed" --level error --task scan-001
syncro status --task current
```

---

### `syncro install`
**Category:** System  
Auto-elevates via UAC and registers `syncro-cli.exe` in the Windows Machine-level PATH.
Broadcasts `WM_SETTINGCHANGE` so all open shells pick it up immediately.

```bash
syncro install
syncro install --uninstall
syncro install --check           # Verify if already in PATH
```

---

### `syncro agent`
**Category:** AI  
Invokes a multi-step autonomous AI agent task. Chains AST scans, code generation,
build verification, and auto-patching in a single session.

```bash
syncro agent "Refactor auth to use JWT refresh tokens"
syncro agent "Add MongoDB connection pooling to the API"
syncro agent --plan-only "Migrate from REST to GraphQL"
```

---

## 4. Win32 PATH Installer

Setting the Machine-level `PATH` requires Administrator rights. The installer flow:

```
syncro install
    → Check WindowsIdentity.GetCurrent() + IsInRole(WindowsBuiltInRole.Administrator)
    → If NOT admin: Process.Start(exe, "install", verb="runas") → UAC Dialog → re-enter as admin
    → If admin:
        → Read current Machine PATH from Environment.GetEnvironmentVariable("PATH", Machine)
        → Append CLI directory if not already present
        → Environment.SetEnvironmentVariable("PATH", newPath, Machine)
        → Win32Api.BroadcastEnvironmentChange()  ← WM_SETTINGCHANGE broadcast
```

### `Win32Api.cs` — Full Implementation

```csharp
public static class Win32Api
{
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint   Msg,
        IntPtr wParam,
        string lParam,
        uint   fuFlags,
        uint   uTimeout,
        out IntPtr lpdwResult);

    public static void BroadcastEnvironmentChange()
    {
        IntPtr result;
        SendMessageTimeout(
            HWND_BROADCAST,
            WM_SETTINGCHANGE,
            IntPtr.Zero,
            "Environment",
            SMTO_ABORTIFHUNG,
            5000,
            out result);
    }

    public static bool RegisterInSystemPath(string cliDirectory)
    {
        var current = Environment.GetEnvironmentVariable(
            "PATH", EnvironmentVariableTarget.Machine) ?? "";

        if (current.Contains(cliDirectory, StringComparison.OrdinalIgnoreCase))
            return false; // Already registered

        Environment.SetEnvironmentVariable(
            "PATH",
            current + ";" + cliDirectory,
            EnvironmentVariableTarget.Machine);

        BroadcastEnvironmentChange();
        return true;
    }
}
```

---

## 5. Shell Runner Abstraction

`ShellRunner.cs` wraps PowerShell and Bash invocations, captures stdout/stderr,
and optionally streams output to the Named Pipe bridge in real-time.

| Method | Shell | Use Case | Return |
|---|---|---|---|
| `RunPowershell(script)` | PowerShell 7+ | Node install, PATH scripts, Windows automation | `ShellResult { ExitCode, Stdout, Stderr }` |
| `RunBash(script)` | Git Bash / WSL | Python venv, Linux-compatible setup | `ShellResult` |
| `RunAndStream(cmd, pipe)` | Any | Real-time output streaming to Desktop UI | `IAsyncEnumerable<string>` |
| `RunElevated(exe, args)` | OS Shell | Admin-required operations | `Process` handle |

```csharp
public record ShellResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
}

public static class ShellRunner
{
    public static async Task<ShellResult> RunPowershell(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pwsh.exe",  // PowerShell 7; fallback: powershell.exe
            Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
            CreateNoWindow  = true
        };
        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        var stderr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return new ShellResult(proc.ExitCode, stdout, stderr);
    }
}
```

---

## 6. Build Pipeline (MSBuild)

The CLI is compiled automatically on every `Syncro.Desktop` build via a post-build MSBuild target.
The output single-file binary is copied directly into the Desktop's output directory.

```xml
<!-- In Syncro.Desktop.csproj -->
<Target Name="BuildCLI" AfterTargets="Build">

  <!-- Publish as single-file, self-contained for win-x64 -->
  <MSBuild
    Projects="Syncro.CLI/Syncro.CLI.csproj"
    Targets="Publish"
    Properties="PublishSingleFile=true;SelfContained=false;RuntimeIdentifier=win-x64;Configuration=$(Configuration)" />

  <!-- Copy the published exe into Desktop's output directory -->
  <Copy
    SourceFiles="Syncro.CLI/bin/$(Configuration)/net9.0/win-x64/publish/syncro-cli.exe"
    DestinationFolder="$(OutputPath)"
    SkipUnchangedFiles="true"
    ContinueOnError="true" />

</Target>
```

> **Note:** `PublishSingleFile=true` bundles all DLLs into one `.exe`.
> No runtime DLLs needed alongside it — just copy the exe, add to PATH, and `syncro` works globally.

---

## 7. AI Command Pipeline

Every `syncro ai *` subcommand follows this multi-stage retrieval-augmented generation pipeline:

```
1. Parse CLI Query
       ↓
2. AST Symbol Lookup          ← O(1) from ast_index.json
       ↓
3. Metadata Filter            ← project_id, framework, language, node_type
       ↓
4. Vector Cosine Search       ← top-K from embeddings.vec
       ↓
5. Memory RAG Injection       ← fetch past success records matching prompt hash
       ↓
6. Prompt Assembly
   System prompt
   + AST node definitions
   + Vector-matched code snippets
   + Few-shot examples from Memory (success=true only)
   + User query
       ↓
7. LLM Inference              ← local (Ollama) or cloud (Gemini/GPT)
       ↓
8. Stream output to terminal
       ↓
9. Save to Memory.jsonl       ← success=null (pending build verification)
       ↓
10. Run build command
       ↓
    ┌─────────────────────┬────────────────────┐
    ↓ Build success       ↓ Build failure
    Update memory         → Run syncro patch
    (success=true)          → Retry loop (max 3)
                            → Update memory (success=false)
```

---

## 8. Named Pipe IPC Bridge

The CLI and Desktop communicate via a local Windows Named Pipe.
- **Desktop** hosts a `NamedPipeServerStream` on a background thread
- **CLI** connects as `NamedPipeClientStream` and sends JSON packets

### Packet Types

```json
// status packet
{ "type": "status", "level": "success", "message": "AST scan complete", "taskId": "scan-001" }

// progress packet
{ "type": "progress", "percent": 72, "step": 18, "total": 25, "taskId": "scan-001" }

// error packet
{ "type": "error", "code": "TS2345", "message": "Argument not assignable", "file": "auth.ts", "line": 42 }

// result packet
{ "type": "result", "resultType": "graph", "data": "...(mermaid string)...", "durationMs": 1240 }

// heartbeat (every 5 seconds)
{ "type": "heartbeat", "pid": 18420, "timestamp": "2026-06-05T23:00:00Z" }
```

### Bridge Client (`AgentBridge.cs`)

```csharp
public class AgentBridge : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private StreamWriter? _writer;
    private const string PipeName = "syncro-agent-bridge";

    public async Task ConnectAsync()
    {
        _pipe = new NamedPipeClientStream(".", PipeName,
            PipeDirection.Out, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(timeout: 2000); // ms; fail silently if Desktop not running
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
    }

    public async Task SendAsync(object packet)
    {
        if (_writer is null) return;
        try { await _writer.WriteLineAsync(JsonSerializer.Serialize(packet)); }
        catch { /* Desktop disconnected — CLI continues independently */ }
    }

    public void Dispose() { _writer?.Dispose(); _pipe?.Dispose(); }
}
```

---

## 9. Pre-Written Scripts

Scripts bundled in `Syncro.CLI/Scripts/`:

| Script | Shell | Description |
|---|---|---|
| `setup-venv.sh` | Bash | Detects Python version, creates `.venv`, installs `requirements.txt` |
| `install-node.ps1` | PowerShell | Checks Node.js; installs via `winget` or `nvm-windows` |
| `check-db.sh` | Bash | Pings SQL/NoSQL hosts, reports latency and connection status |
| `clone-repo.ps1` | PowerShell | Safe git clone in child process; reports progress to Named Pipe |
| `add-to-path.ps1` | PowerShell | Admin-elevated PATH registration + `WM_SETTINGCHANGE` broadcast |

### AI-Extendable Script Registry

When `syncro ai generate script` creates a new helper script, it is saved to
`SyncroDB/Memory/` with `type: "script"` and registered in a script index file.
On subsequent runs, the RAG engine can suggest it as a reusable asset.

```json
{
  "script_id": "scr-042",
  "name": "setup-redis.sh",
  "description": "Install and start Redis on WSL2",
  "tags": ["redis", "cache", "wsl"],
  "path": ".syncro_db/Memory/scripts/setup-redis.sh",
  "generated_by": "syncro ai generate script",
  "success_count": 3,
  "created": "2026-06-05T22:00:00Z"
}
```

---

## 10. Security & UAC Model

| Concern | Approach |
|---|---|
| **Principle of Least Privilege** | Elevation only for `syncro install`. All other commands are standard user. |
| **Process Isolation** | Scripts run in child `Process` objects. CLI memory is isolated from MAUI app. |
| **Credential Safety** | API keys and DB URLs stored in `.syncro_db/` with filesystem ACL protections. Never logged to stdout or Named Pipe. |
| **Audit Logging** | Every elevated command logged to `SyncroDB/Agents/audit.jsonl` with timestamp, user, and operation. |
| **Script Sandboxing** | PS1/Bash scripts are run with explicit `-ExecutionPolicy Bypass` scoped to the single script invocation, not the system policy. |
| **Input Sanitisation** | All user-supplied paths and symbols are sanitised before being interpolated into shell commands to prevent injection. |

---

## 11. Roadmap

| Phase | Feature | Status |
|---|---|---|
| v2.0 | All 18 commands + Named Pipe bridge + Win32 installer | 🔧 In Progress |
| v2.1 | AI-extendable script registry via RAG hindsight | 📋 Planned |
| v2.2 | Multi-agent orchestration (chain of CLI agents) | 📋 Backlog |
| v2.3 | Mobile push: CLI status → Syncro Mobile app | 📋 Backlog |
| v3.0 | WSL2 integration + Linux agent support | 📋 Backlog |
