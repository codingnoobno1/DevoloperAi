# Engine Optimisation, Server CLI, MCP Management & Parallel Project Runner

> Scope: the **runtime spine** of Syncro.Desktop — the local servers (IPC bridge :pipe,
> monitor :3030, LLM :3020), the **MCP / agent-tool registry**, the **parallel project +
> database creation runner**, **environment detection/setup**, and **nginx provisioning** for
> Linux-like deploy targets.
>
> Sister docs: [`../ideuidevolopment.md`](../ideuidevolopment.md) (IDE/UX, editor versioning),
> [`../mappertodo.md`](../mappertodo.md) (NLP router). Generator bug catalogue lives in
> [`../projectgenerator.md`](../projectgenerator.md).
>
> Naming note: "MCP ADK" in the request = our **Model-Context-Protocol tool registry** that
> acts as the agent's **Agent Dev Kit** — `McpOrchestrator` + the `IMcpServer`/`IMcpTool`
> contracts in `Services/Engine/Mcp/*`. There is no third-party "ADK"; it is our own.

---

## 0. The runtime today (what's actually wired)

```
            ┌────────────────────────── Syncro.Desktop (MAUI, single process) ───────────────────────────┐
            │                                                                                             │
  CLI proc  │   AgentBridgeServer (named pipe "syncro-agent-bridge", PipeDirection.In)                    │
  ───JSONL──┼──▶  AgentBridgeServer.cs:36   ──OnPacketReceived──▶  AgentOrchestrator.cs:94                │
            │                                              │ telemetry, RAG repair (HandleCompileError)   │
            │   AgentMonitorServer (HttpListener :3030)  ◀─┘  AgentMonitorServer.cs:61                     │
            │     /api/status /api/db /ipc  → live dashboard HTML                                          │
            │                                                                                             │
            │   McpOrchestrator (in-proc tool registry)   McpOrchestrator.cs:10                           │
            │     ├ WorkspaceMcp FileSystemMcp RepositoryMcp GitMcp AstMcp ApiMcp                          │
            │     ├ JsonIntelligenceMcp KnowledgeMcp PatchMcp PlanningMcp RepairKnowledgeMcp               │
            │     └ each server → IMcpTool[]  (ReadFileTool, ApplyPatchTool, FindClassesTool, …)           │
            │                                                                                             │
            │   TaskLoopEngine (state machine)  TaskLoopEngine.cs:16   ⇄  SyncroDb (file DB)  SyncroDb.cs  │
            │   ProjectGenerator (CliWrap scaffolder)  ProjectGenerator.cs:159                             │
            └─────────────────────────────────────────────────────────────────────────────────────────────┘
  External: LLM service on http://localhost:3020 (Groq/Gemini)  — probed, optional (offline-first)
```

DI truth (`MauiProgram.cs:42–80`): bridge/orchestrator/monitor are singletons started at app
boot (`MauiProgram.cs:138`); the 11 MCP servers + `McpOrchestrator` are singletons; AgentCli
core via `AddAgentCli` (`MauiProgram.cs:73`); IDE via `AddSyncroIde` (`MauiProgram.cs:80`).

### 0.1 Honest assessment of robustness gaps

| # | Gap | Where | Impact |
|---|---|---|---|
| G1 | Monitor binds :3030, LLM probe hits :3020 — **no port-in-use handling**; `HttpListener.Start()` throws if taken | `AgentMonitorServer.cs:72` | a second app instance / stale process kills the monitor silently (caught, logged to nowhere) |
| G2 | MCP registry is **register-once, no lifecycle** — no health, no per-tool timeout, no concurrency cap, no de-register | `McpOrchestrator.cs:37` | one hung tool stalls a task; no recovery |
| G3 | MCP `RegisterServerAsync` awaits `InitializeAsync` **serially**; comment admits "validate AdminApproval … here" is a TODO | `McpOrchestrator.cs:28,41` | slow boot; approval gate not enforced at the registry |
| G4 | Project DB creation is **inline & sequential** inside `ScaffoldGroupProjectAsync`; group reports success on partial failure | `ProjectGenerator.cs:102,116` | no parallelism, no rollback (bug B9) |
| G5 | `.syncro_db` created **per sub-project AND at group root** | `ProjectGenerator.cs:48` per member | N+1 stores, ambiguous which the indexer reads (bug B4) |
| G6 | Environment detection (`EnvironmentManager`) is **not consulted** before choosing CLI vs template | `ProjectGenerator.cs:37` | runs `flutter`/`django-admin` that may not exist → catch → fallback (slow, noisy) |
| G7 | No deploy/runtime provisioning at all (nginx, systemd, reverse proxy) | — | "create + run a stack" stops at scaffold |
| G8 | RAG repair hardcodes :3020 URL & Gemini JSON shape | `AgentOrchestrator.cs:207,228` | brittle to provider change |

The rest of this doc is the plan to close G1–G8.

---

## 1. Server lifecycle & supervision (G1, G2)

### 1.1 A `LocalServerSupervisor`

Today each server is `Start()`/`Stop()` ad hoc from `MauiProgram` app lifecycle
(`MauiProgram.cs:138`). Introduce a supervisor that owns **all** long-lived listeners with a
uniform contract:

```csharp
public interface ILocalServer
{
    string Name { get; }
    Task<ServerHealth> StartAsync(CancellationToken ct);   // returns bound endpoint or failure
    Task StopAsync();
    ServerHealth Health { get; }                            // Starting|Healthy|Degraded|Stopped|PortConflict
}
```

`AgentBridgeServer`, `AgentMonitorServer` (and the future MCP transport, §2.4) implement it.
The supervisor:

- **Port acquisition with fallback** (fixes G1): try :3030; if `HttpListenerException`
  (address in use), probe :3031…:3039, then publish the *actual* port to `IdeLaunchContext`-style
  shared state and to the dashboard. The hardcoded `:3030`/`:3020` constants become
  `ServerOptions` (configurable, discoverable).
- **Health endpoint**: `/api/health` returns supervisor state for every server, so the IDE and
  the dashboard show "monitor: :3031 healthy, bridge: pipe healthy, llm: :3020 offline."
- **Restart-on-fault with backoff**: the bridge's `ListenLoopAsync` already retries with
  `Task.Delay(1000)` on stream error (`AgentBridgeServer.cs:85`); promote that into the
  supervisor so *all* servers get bounded exponential backoff and a circuit-breaker (stop
  retrying after K failures → `Degraded`, surface to user).
- **Graceful drain**: on app exit, `StopAsync` cancels the CTS, disconnects pipes, and `Close()`s
  the listener (current `Stop()` does this, `AgentMonitorServer.cs:236`) — but ordered, and
  awaited, so the process doesn't exit mid-write.

### 1.2 Bridge hardening

`AgentBridgeServer` accepts a **single** pipe instance (`maxNumberOfServerInstances: 1`,
`AgentBridgeServer.cs:39`). One stuck CLI client blocks the next. Upgrade:

- Allow a small instance pool (`NamedPipeServerStream` with `maxNumberOfServerInstances: 4`) and
  accept connections in a loop so concurrent CLI sessions (parallel projects each running
  `syncro …`) don't serialize.
- Per-connection CTS + idle timeout so a half-open client is reaped.
- Bound `OfflineHindsightMemory`/`LoggedActions`/`LoggedLlmRequests` growth — they grow
  unbounded (`AgentOrchestrator.cs:21`). The monitor already ring-buffers logs to 100
  (`AgentMonitorServer.cs:54`); apply the same cap to the orchestrator's lists.

---

## 2. MCP / Agent-Dev-Kit management (G2, G3)

### 2.1 What the registry must become

`McpOrchestrator` is a `Dictionary<string,IMcpTool>` + `ExecuteToolAsync`
(`McpOrchestrator.cs:12,37`). For robustness it needs to be a **managed registry**:

| Capability | Today | Target |
|---|---|---|
| Register tool | ✅ dedupe by name (`McpOrchestrator.cs:20`) | + capability tags, version, owning server |
| Register server | ✅ serial `InitializeAsync` (`McpOrchestrator.cs:28`) | parallel init, per-server health, re-init on fault |
| Execute tool | ✅ name lookup (`McpOrchestrator.cs:37`) | + **timeout**, **concurrency cap**, **approval gate**, **audit** |
| Approval | ❌ "in production validate…" comment (`McpOrchestrator.cs:41`) | enforce `RequiresAdminApproval` against loop autonomy |
| Health / de-register | ❌ none | heartbeat, quarantine a flapping server |
| Discovery | ❌ none | `ListTools()` schema dump for the LLM prompt |

### 2.2 Hardened `ExecuteToolAsync`

```csharp
public async Task<ToolResult> ExecuteToolAsync(string name, string json, ToolCallContext ctx)
{
    if (!_tools.TryGetValue(name, out var tool))
        return ToolResult.NotFound(name);

    // (G3) enforce the approval gate the comment promised
    if (tool.RequiresAdminApproval && !ctx.Autonomous && !ctx.Approved)
        return ToolResult.NeedsApproval(name);

    await _concurrency.WaitAsync(ctx.Ct);                 // global cap (e.g. 8 in-flight tools)
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Ct);
    timeoutCts.CancelAfter(tool.Timeout ?? DefaultTimeout); // per-tool timeout (G2)
    try
    {
        var sw = Stopwatch.StartNew();
        var output = await tool.ExecuteAsync(json).WaitAsync(timeoutCts.Token);
        await _db.AppendAuditAsync(new AuditEntry { Action = $"mcp.{name}", Note = $"{sw.ElapsedMilliseconds}ms ok" });
        return ToolResult.Ok(output);
    }
    catch (OperationCanceledException) { return ToolResult.TimedOut(name); }
    catch (Exception ex)              { return ToolResult.Faulted(name, ex.Message); }
    finally { _concurrency.Release(); }
}
```

This reuses the existing `SyncroDb.AppendAuditAsync` audit trail (`SyncroDb.cs:129`) — every MCP
call becomes an audit line, visible in the monitor's "Durable Audit Trail" panel
(`AgentMonitorServer.cs:520`). The approval gate aligns with `TaskLoopEngine.Autonomous`
(`TaskLoopEngine.cs:29`) so the **same** autonomy switch governs scripts *and* tools.

### 2.3 Server health & quarantine

`RegisterServerAsync` (`McpOrchestrator.cs:28`) gains:

- Parallel init: `await Task.WhenAll(servers.Select(s => InitWithGuard(s)))` — boot 11 servers
  concurrently, each guarded so one bad server doesn't abort the rest.
- Heartbeat: optional `IMcpServer.PingAsync()`; a server that throws/timeouts N times is
  **quarantined** (its tools return `Degraded`, the LLM is told they're unavailable) and a
  background task tries to re-init it with backoff.

### 2.4 Optional: expose MCP over a real transport

Today MCP is **in-proc only**. To let the external CLI / a remote agent use the same tools,
add a **stdio or local-socket MCP transport** behind `ILocalServer` (§1.1). The registry stays
the source of truth; the transport just frames JSON-RPC `tools/list` + `tools/call` over it.
This is what makes the "ADK" usable outside the desktop process without duplicating tool code.

---

## 3. Parallel project + database creation runner (G4, G5)

This is the core of *"parallel project database creation runner."* Replace the inline sequential
loop in `ProjectGenerator.ScaffoldGroupProjectAsync` (`ProjectGenerator.cs:102`) with a real
**job runner** that creates project services *and their databases* concurrently, with
dependency ordering, bounded parallelism, progress, and rollback.

### 3.1 Why the current loop is wrong

```csharp
foreach (var arch in selectedArchetypes)                 // ProjectGenerator.cs:102 — SEQUENTIAL
{
    bool subSuccess = await ScaffoldProjectAsync(...);    // each writes its own .syncro_db (G5)
    if (subSuccess) projectsConfig.Add(...);              // failures silently skipped (G4)
}
// returns true even if half failed; no DB provisioning at all (G7 for data tier)
```

### 3.2 The runner model

```csharp
public sealed class ProjectStackRunner
{
    // bounded fan-out — don't spawn 10 npm installs at once on a 4-core laptop
    private readonly SemaphoreSlim _gate = new(Math.Max(2, Environment.ProcessorCount - 1));

    public async Task<StackResult> RunAsync(StackPlan plan, IProgress<StackProgress> progress, CancellationToken ct)
    {
        // 1. ONE group .syncro_db at the root; members register INTO it (fixes G5)
        var db = new SyncroDb(Path.Combine(plan.RootPath, ".syncro_db"));

        // 2. topological order: databases & shared infra first, then services that depend on them
        var waves = TopoSort(plan.Nodes);   // [ [postgres, redis], [backend], [frontend] ]

        var done = new List<NodeResult>();
        foreach (var wave in waves)
        {
            var results = await Task.WhenAll(wave.Select(n => RunNodeGuarded(n, db, progress, ct)));
            if (results.Any(r => !r.Ok))                      // fail-fast within a wave
            {
                await RollbackAsync(done, ct);                // fixes G4 (no partial "success")
                return StackResult.Failed(results.First(r => !r.Ok));
            }
            done.AddRange(results);
        }
        return StackResult.Ok(done);
    }
}
```

Key properties:

- **Bounded parallelism** via `SemaphoreSlim(cores-1)` — scaffolds and `npm install`/`pip
  install`/DB init run concurrently but not unboundedly (avoids thrashing disk/CPU; the IDE
  context builder uses the same discipline, see `../ideuidevolopment.md §4.4`).
- **Wave/topo ordering** — a Postgres container/db must exist before the backend runs its
  migrations; the frontend's `.env` needs the backend URL. The runner resolves this from the
  plan's declared edges (frontend → backend → database).
- **One root `.syncro_db`** (fixes G5). Each member writes its record via the shared facade with
  the group as namespace; `projects.json`/`groups.json` are **appended, not overwritten** (fixes
  the registry-clobber bug B3 in `projectgenerator.md`) using `SyncroDb.ReadJsonAsync` →
  mutate → `WriteJsonAtomicAsync` (`SyncroDb.cs:60,68`).
- **Rollback** — a failed wave triggers compensating deletes of already-created folders/DBs, so a
  group is all-or-nothing. The runner records each node's created artifacts to know what to undo.
- **Structured progress** — `IProgress<StackProgress>` (phase, node, current/total) drives a real
  progress bar in `CreateProjectDialog`, replacing the scrolling `onLog` (see
  `../ideuidevolopment.md §7`).

### 3.3 Database provisioning as a first-class node type

The proposal's own manual test ("Next.js + FastAPI + **Postgres**") cannot pass today — there is
**no database archetype** (bug B7). The runner adds DB nodes with two strategies, chosen by
environment (§4):

| Strategy | When | Mechanism |
|---|---|---|
| **Docker** (preferred) | Docker detected | emit `docker-compose.yml` (postgres/mysql/mongo/redis) + `.env` with creds; `docker compose up -d` via CliWrap; wait for healthcheck |
| **Local binary** | no Docker, server installed | `initdb`/`createdb`, `mongod --dbpath`, etc. via CliWrap |
| **Embedded / file** | dev fallback | SQLite file / LiteDB — zero infra, always works offline |

DB nodes also **wire credentials into dependents**: the backend's `.env`
(`DATABASE_URL=postgres://…`), the frontend's `NEXT_PUBLIC_API_URL` → backend origin, and CORS
on the backend → frontend origin. This closes the "no inter-service wiring" gap (bug B6) — a
*group* becomes an actually-connected stack, not three unrelated folders.

### 3.4 Port allocation (fixes B5)

Flask=5000 and Express=5000 collide today (bug B5). The runner owns a **port broker**: it scans
for free ports at plan time, assigns each service a distinct one, and templates that port into
the generated config + the sibling `.env` + the nginx upstream (§6). No two members ever bind the
same port.

### 3.5 Concurrency safety of parallel DB creation

Parallel nodes all write to the **one** root `.syncro_db`. `SyncroDb` already serializes writes
with `SemaphoreSlim(1,1)` and writes atomically (`SyncroDb.cs:23,68`), and appends audit/jsonl
under the same lock (`SyncroDb.cs:96`). So fan-out workers can safely register concurrently. The
only addition: reads during the atomic tmp→move swap retry on transient IO (same helper as
`../ideuidevolopment.md §4.4`).

---

## 4. Environment detection & setup (G6)

`BusinessLogic/EnvironmentManager` already detects Node/Python/Java/.NET/Flutter (per
`CLAUDE.md`). The runner must **consult it before** choosing a strategy, instead of "try the CLI,
catch, fall back" (`ProjectGenerator.cs:37`).

### 4.1 Capability probe → strategy selection

```csharp
public record EnvProbe(
    bool Node, string? NodeVersion, bool Npm, bool Pnpm,
    bool Python, string? PyVersion, bool Pip,
    bool Dotnet, bool Java, bool Flutter,
    bool Docker, bool Git, bool Nginx, OsKind Os);
```

Selection table the runner uses per node:

| Node wants | Probe says | Action |
|---|---|---|
| Next.js | Node+npm present | `create-next-app` (non-interactive flags already set, `ProjectGenerator.cs:170`) |
| Next.js | Node missing | **don't run CLI**; local template (`ProjectGenerator.cs:242`) + warn "install Node to enable full scaffold" |
| FastAPI | Python+pip | venv + `pip install`; template main.py exists (`ProjectGenerator.cs:226`) |
| Postgres | Docker | compose strategy (§3.3) |
| Postgres | no Docker, `psql`/`initdb` | local binary strategy |
| Postgres | neither | SQLite fallback + clear note |

This makes failures **predictable and explained** instead of a CLI exception caught into a silent
fallback. The probe result is shown in the create dialog *before* the user commits ("Detected:
Node 20, Python 3.12, Docker ✓, nginx ✗") — a `EnvironmentPanel.razor` already exists in the
dashboard and can host this.

### 4.2 Post-scaffold setup runner

`setup.bat` is emitted but **never executed**, and `setup.sh` is never written for Mac/Linux
(bug B8). The runner:

- Emits the **right** setup script for `EnvProbe.Os` (bat on Windows, sh on Unix, both for
  cross-platform repos).
- Optionally **runs** it via CliWrap (same streaming/cancellation pattern as
  `RunNativeCliCreatorAsync`, `ProjectGenerator.cs:193`) so deps are installed and the project
  is *runnable*, not just scaffolded — gated behind a "Install dependencies now?" toggle.
- Captures setup failures as `TaskRecord`s in the loop so the agent's repair flow
  (`AgentOrchestrator.HandleCompileErrorAsync`, `AgentOrchestrator.cs:199`) can diagnose them.

---

## 5. Engine optimisation (CliWrap, async, allocation) 

### 5.1 Standardise on CliWrap everywhere (finish the migration)

`ProjectGenerator.RunNativeCliCreatorAsync` is already CliWrap with an event stream
(`ProjectGenerator.cs:193–211`) — good. But the codebase still has `System.Diagnostics.Process`
wrappers (`ProcessRunner`, `IProcessExecutor`/`ScriptRunnerAdapter`). Converge **all** external
process execution on a single `ICliRunner` abstraction backed by CliWrap with:

- `WithValidation(None)` + explicit exit-code handling (as today, `ProjectGenerator.cs:196`).
- **Mandatory** `CancellationToken` + timeout on every call (the generator's current call has
  neither — see G in `../ideuidevolopment.md §7`).
- Output piped to a buffered, **bounded** sink (avoid the classic deadlock *and* unbounded log
  growth).
- Working-directory and environment passed explicitly (the generator already sets
  `WithWorkingDirectory`, `ProjectGenerator.cs:195`).

One runner = one place to add retries, logging, and the audit hook.

### 5.2 RAG repair: de-hardcode the provider (G8)

`HandleCompileErrorAsync` hardcodes `http://localhost:3020/gemini` and digs Gemini's
`candidates[0].content.parts[0].text` JSON shape (`AgentOrchestrator.cs:207,228`). Route it
through the existing `ILlmGateway` (`Services/AgentCli/Llm/ILlmGateway.cs`) / `ILLMProvider`
(`Services/Engine/Core/ILLMProvider.cs`, impl `GroqProvider`) so provider/endpoint/shape live in
one place and the offline Hindsight fallback (`AgentOrchestrator.cs:243`) is the only thing the
orchestrator owns. The :3020 probe stays as the **online check**, but the call goes through the
gateway.

### 5.3 Cheap wins

- **LLM-online probe is already cached** 5 s in the monitor (`AgentMonitorServer.cs:30,221`) but
  the orchestrator re-probes per error (`AgentOrchestrator.cs:207`). Share one cached probe.
- **Token estimate** is `len/4` in three places (`AgentOrchestrator.cs:159,168,183`) — extract a
  single `EstimateTokens` helper; later swap for a real tokenizer without touching call sites.
- **Lexical overlap** RAG match (`AgentOrchestrator.cs:274`) recomputes hashsets per record per
  error — fine for the seed set, but pre-tokenize `OfflineHindsightMemory` problems once on load.

---

## 6. nginx / reverse-proxy provisioning for Linux-like targets (G7)

The ask: *"maybe nginx setup on linux like environment."* When a generated **group stack** is
deployed (or run in a Linux dev container / WSL), the services need a front door: one origin,
TLS, path/subdomain routing to each member, websocket upgrade for HMR/live reload.

### 6.1 What the runner can emit

For a stack `{ frontend:3000, backend:8000, db:5432 }` the runner generates, from the port
broker (§3.4):

```nginx
# generated: <stack>/deploy/nginx/<stack>.conf
upstream <stack>_frontend { server 127.0.0.1:3000; }
upstream <stack>_backend  { server 127.0.0.1:8000; }

server {
    listen 80;
    server_name <stack>.local;

    location /api/ {                       # backend
        proxy_pass http://<stack>_backend/;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    location / {                           # frontend (catch-all)
        proxy_pass http://<stack>_frontend/;
        proxy_http_version 1.1;            # websocket upgrade for HMR / live reload
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
    }
}
```

### 6.2 Provisioning modes (chosen by `EnvProbe`)

| Mode | Condition | Behavior |
|---|---|---|
| **Compose sidecar** (preferred) | Docker present | add an `nginx:alpine` service to the stack's `docker-compose.yml`, mount the generated conf, depend_on the services; `docker compose up -d` |
| **Host nginx** | Linux/WSL, `nginx` on PATH (`EnvProbe.Nginx`) | write conf to `./deploy/nginx/`, optionally symlink into `/etc/nginx/sites-enabled/` and `nginx -s reload` (CliWrap, **gated** — touches system paths, `RequiresAdminApproval`-style confirm) |
| **Config-only** | Windows host / no nginx | emit the conf + a `README` with exact apply commands; never touch the host |

### 6.3 Safety posture

System provisioning is **destructive/outward-facing**: writing to `/etc/nginx`, reloading a
service, binding :80. Per the loop's safety model, these are **mutating + admin** operations —
gated behind explicit confirmation (and `sudo` is the user's to run; the runner prints commands
rather than assuming root). TLS via certbot is **documented, not auto-run**. The default mode is
**config-only**; actually applying to a host is always opt-in.

### 6.4 Optional companions

- **systemd unit** generation per service (`<svc>.service`) for non-Docker Linux deploys, so the
  stack survives reboot — same gating.
- **Healthcheck wait**: after `up -d`, poll each upstream `/health` (reuse the monitor's cached
  probe pattern, `AgentMonitorServer.cs:221`) before declaring the stack live.

---

## 7. Cross-cutting: audit, observability, offline-first

Everything above feeds the **existing** observability surface, so no new dashboard is needed:

- **Audit**: MCP calls (§2.2), task transitions (`SyncroDb.AppendAuditAsync`, used in
  `TaskLoopEngine.cs:200`), and stack-runner steps all append to `intelligence/audit.jsonl`,
  rendered in the monitor's "Durable Audit Trail" (`AgentMonitorServer.cs:520`).
- **Live status**: server health (§1.1), DB snapshot (`/api/db`, `AgentMonitorServer.cs:184`),
  and runner progress surface on :3030 (whatever port the supervisor actually bound, G1).
- **Offline-first**: every layer degrades without the LLM — MCP tools are deterministic, the
  runner uses templates/SQLite when CLIs/Docker are absent, RAG falls back to Hindsight
  (`AgentOrchestrator.cs:243`). The model **enriches**; it is never required. (Same principle as
  `../ideuidevolopment.md §4.3` and `vikingide.md §1`.)

---

## 8. Phased delivery

| Phase | Deliverable | Closes |
|---|---|---|
| **E0** | `LocalServerSupervisor` + port fallback + `/api/health` | G1 |
| **E1** | Hardened `ExecuteToolAsync` (timeout, concurrency, approval gate, audit) | G2, G3 |
| **E2** | `ProjectStackRunner`: bounded fan-out, waves, one root `.syncro_db`, append-not-overwrite, rollback, progress | G4, G5 |
| **E3** | DB node types (Docker / local / SQLite) + credential wiring + port broker | B5, B6, B7 |
| **E4** | `EnvProbe`-driven strategy selection + setup runner (bat/sh, run-on-opt-in) | G6, B8 |
| **E5** | CliWrap convergence (`ICliRunner`) + RAG via `ILlmGateway` | G8, engine cleanup |
| **E6** | nginx/compose/systemd provisioning, config-only by default | G7 |

E0–E2 are the robustness backbone; E3 is what makes the headline manual test
("Next.js + FastAPI + Postgres") actually pass; E6 is the deploy story.

---

## 9. Test matrix

| Scenario | Expected |
|---|---|
| :3030 already bound | supervisor binds :3031, dashboard reports actual port, no crash |
| MCP tool hangs | call times out, returns `TimedOut`, task continues, audit records it |
| Mutating MCP tool, autonomous off | `NeedsApproval`, user gates, then runs |
| Group "Next + FastAPI + Postgres", Docker present | 3 nodes, postgres wave first, creds wired, all green, one root `.syncro_db` |
| Same, backend scaffold fails | whole group rolled back, no partial folders, clear error |
| Same, no Docker | SQLite fallback for DB, stack still wired & runnable |
| Two ports requested, both 5000 | broker assigns 5000 + 5001, configs + nginx upstreams match |
| No Node installed, Next.js requested | template path, explicit "install Node" note, no CLI exception |
| nginx config-only on Windows | conf + README emitted, host untouched |
| nginx host mode on WSL | gated confirm → conf applied → `nginx -s reload` → healthcheck passes |
| Parallel stack creation, 6 nodes, 4 cores | ≤3 concurrent, no DB-store corruption, audit consistent |
