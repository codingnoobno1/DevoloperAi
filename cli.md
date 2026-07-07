# Syncro CLI — Hindsight RAG, Safe LLM, and Structured Commands

> Analysis of the current CLI vs `docs/cli.html`, plus a plan to make **hindsight RAG**
> work in the CLI (offline-capable), **auto-use the LLM when available**, expose a
> **structured/callable** command surface, and make the CLI **safe to talk to the LLM agent**.

---

## 1. Reality vs. Docs (the gap)

**`docs/cli.html`** advertises a standalone `syncro-cli.exe` (.NET 9 console, separate process),
a Named-Pipe bridge, a `RagEngine`, an `LlmClient`, and **18 top-level commands**.

**Actual code** (`Services/SyncroCLI/`): commands run **in-process** inside the MAUI app via
`CliEngine`, and only these exist:

| # | Command (docs) | Status | Backing code |
|---|---|---|---|
| 1 | `init` | ✅ | `InitCommand.cs` (providers) |
| 2 | `doctor` | ✅ | `DoctorCommand.cs` + `EnvironmentManager` |
| 3 | `git` | ✅ | `GitCommand.cs` + `GitProvider` |
| 4 | `ast` | ✅ | `AstCommand.cs` + `AstService` |
| — | `script` | ✅ (not in docs) | `ScriptCommand.cs` + `MarketplaceService` |
| 5 | `scan` | ⚠️ | AST scan exists in `AstService`; **no `ScanCommand`** in repo |
| 6 | `run` | ❌ | — |
| 7 | `graph` | ❌ | `GraphExporter` exists, no command |
| 8 | `kb` | ❌ | — |
| 9 | `memory` | ❌ | — |
| 10 | `ai` | ❌ | `AIClient` exists, **not exposed as a command** |
| 11 | `project` | ❌ | `ProjectService` exists, no command |
| 12 | `template` | ❌ | `ProjectGenerator`/templates planned |
| 13 | `generate` | ❌ | `ProjectGenerator` exists, no command |
| 14 | `patch` | ❌ | — |
| 15 | `deploy` | ❌ | — |
| 16 | `status` | ❌ | no Named-Pipe bridge in repo |
| 17 | `install` | ❌ | PATH logic exists in `SyncroCLIService`, no command |
| 18 | `agent` | ❌ | — |

**Also missing vs docs:** `RagEngine.cs`, `LlmClient.cs`, `AgentBridge.cs` (Named Pipe),
standalone `Syncro.CLI.csproj`, `AgentMonitorServer` (walkthrough mentioned :3030 but it's not committed).

**`AIClient` issues** (`BusinessLogic/AIClient.cs`):
- Hardcoded `http://localhost:3020/gemini`; **no availability probe**; default `HttpClient` timeout (100 s) → UI hang risk.
- `GetSystemPrompt()` / `CleanBatchScriptResponse()` are defined but **unused**, and framed for *batch scripts only* — too narrow for `ai explain/generate`.
- `VerifyApiKeyAsync()` always returns `false`.

---

## 2. Architecture decision — one core, two front-ends

The docs want a **standalone exe** (crash-isolated, named-pipe). The reality is **in-process**.
Don't fork the logic. Extract a shared core:

```
Syncro.Cli.Core/         (class library — command definitions, RAG, LLM gateway, safety)
   ├── used IN-PROCESS by the MAUI embedded terminal (today's CliEngine)
   └── used by a thin Syncro.CLI.exe (console host)  ← the "safe to crash" model from docs
```

- **Commands are defined once** (in core) and registered by both hosts.
- The **exe + Named Pipe** gives the crash-isolation and "safe" boundary the docs promise:
  a CLI crash never takes down the MAUI host.
- Migrate incrementally: keep the current in-process engine working; add the exe later.

---

## 3. Hindsight RAG in the CLI — yes, and offline-capable

The vector pieces already exist (`AstService.TokenizeProjectAsync` → `.syncro_db/Vectors/<id>/index.json`),
but the vectors are **simulated** and there is **no retrieval engine**. Plan:

```
Services/AST/Hindsight/
├── IEmbeddingProvider.cs      # string -> float[]
├── LexicalEmbedder.cs         # TF-IDF / feature-hashing  (DETERMINISTIC, NO LLM) ← default
├── LlmEmbedder.cs             # uses LlmGateway.EmbedAsync; falls back to Lexical
├── VectorStore.cs             # read/write .syncro_db/Vectors/<id>/index.json
├── VectorRecord.cs            # { id, kind, text, vector[], sourceFile, entityId }
├── RagEngine.cs               # Retrieve(projectId, query, topK) -> RagContext (cosine)
├── RagContext.cs              # ranked chunks + provenance (file:line)
└── HindsightStore.cs          # past Q&A / generations / outcomes -> few-shot reuse
```

```csharp
public interface IEmbeddingProvider { string Id { get; } Task<float[]> EmbedAsync(string text, CancellationToken ct); }

public class RagEngine
{
    // Pure C#, no network. Works with LexicalEmbedder when the LLM is offline.
    public Task<RagContext> RetrieveAsync(string projectId, string query, int topK = 8, CancellationToken ct = default);
}
```

**Key principle:** RAG retrieval is **deterministic and offline**. The LLM is only used to
(a) optionally produce higher-quality embeddings, and (b) *synthesize* an answer from retrieved
context. With `--no-llm` or when 3020 is down, the CLI still returns ranked snippets + provenance.

> Replaces the "simulated semantic weights" with a real (if simple) vector space.
> Upgrade path: swap `LexicalEmbedder` → `LlmEmbedder` with zero call-site changes.

---

## 4. LLM availability — detect and auto-use

Define a **gateway** with a health probe and capability states:

```csharp
public enum LlmState { Available, Degraded, Offline }

public interface ILlmGateway
{
    LlmState State { get; }                                   // cached
    Task<LlmState> ProbeAsync(CancellationToken ct = default);// GET 3020/health, 1.5s timeout, cache 30s
    Task<LlmResponse> CompleteAsync(LlmRequest req, CancellationToken ct = default);
    Task<float[]?> EmbedAsync(string text, CancellationToken ct = default); // null if no /embed
}
```

- **Health contract (3020 server):** `GET /health → { "ok": true, "model": "gemini-2.5-flash" }`.
  Probe with a **short timeout (1.5 s)** and **cache the result (30 s)** so every command
  doesn't re-probe. (Fixes the current no-probe / 100 s-hang problem.)
- **Auto-use rule:** before any `ai`/`generate`/`patch`, call `ProbeAsync`.
  - `Available` → enrich with LLM synthesis.
  - `Offline` / `--no-llm` → **degrade**: RAG snippets + deterministic output, exit code documents it.
- Every `CompleteAsync` carries a **timeout + CancellationToken** (default 30 s) — no unbounded hangs.
- `AIClient` is refactored to implement `ILlmGateway` (keep the 3020 transport, add probe/timeout/streaming),
  and the dead batch-only prompt is removed in favor of task-specific prompts.

---

## 5. Safe to talk to the LLM agent (the core requirement)

**Trust boundary:** *everything the LLM returns is untrusted data.* The deterministic core
proposes/executes; the LLM only suggests. Concretely:

```
Services/AST/Agents/Safety/
├── LlmSafetyGate.cs        # wraps ILlmGateway with the rules below
├── ContextRedactor.cs      # strips secrets before sending context out
├── FileAccessPolicy.cs     # allowlist of readable/writable paths
├── ToolAllowlist.cs        # the only "actions" the agent may request
├── ApprovalBroker.cs       # gates mutating actions (preview -> approve -> apply)
└── AuditLog.cs             # append-only SyncroDB/Agents/audit.jsonl
```

1. **Never auto-execute LLM output.** Generated scripts/patches are written to a **quarantine**
   file, shown as a **preview/diff**, and applied **only** with explicit `--yes`/approval.
   No `--yes` in a non-interactive `--json` call ⇒ exit code `4 (NeedsApproval)`.
2. **Redact before sending.** `ContextRedactor` strips `.env` values, API keys, tokens, connection
   strings from any context sent to the LLM (regex + key allowlist). Never send `.git`, `.syncro_db`, secrets.
3. **File access policy.** Reads/writes restricted to the project root via `FileAccessPolicy`;
   path-traversal (`..`) blocked; `.synctmpl`/secrets excluded.
4. **Tool allowlist.** If the agent uses tool-calling, only a fixed verb set is permitted
   (`read_file`, `search_rag`, `propose_patch`, `run_build` — *no* arbitrary `run_shell`).
   Anything else is rejected and audited.
5. **Prompt-injection mitigation.** Retrieved AST/RAG context is wrapped in explicit
   `<untrusted_context>` delimiters; the system prompt states context is data, not instructions.
6. **Resource caps.** Max context tokens (truncate), max output size, per-call timeout, cancellation.
7. **Sandboxed execution.** Approved builds/scripts run in a **child process** (reuse
   `ProcessRunner`/`ElevationService`) with a minimal env; elevation only for `install`.
8. **Audit everything.** Every LLM call + every applied mutation → `audit.jsonl`
   (timestamp, command, files touched, llm-state, approved-by). Matches the docs' audit promise.

**Degradation = safety too:** with the LLM offline the CLI is still useful (RAG + deterministic),
so users never get blocked or tempted to disable safety to "make it work."

---

## 6. Structured, callable command contract

Make commands invokable by humans **and** programs (the Desktop UI, scripts, the agent).

```csharp
public interface ICliCommandV2
{
    string Name { get; }                 // "ai", "scan"…
    string Summary { get; }
    string Usage { get; }
    IReadOnlyList<CliFlag> Flags { get; }
    Task<CliResult> ExecuteAsync(CliInvocation inv, CancellationToken ct);
}

public record CliInvocation(
    IReadOnlyList<string> Args,
    IReadOnlyDictionary<string,string> Flags,
    bool Json, bool Yes, bool DryRun, bool NoLlm, string Cwd);

public record CliResult(int ExitCode, string? Text, object? Data, IReadOnlyList<string> Warnings);
```

**Global flags (all commands):** `--json` (machine output), `--yes/-y` (approve mutations),
`--dry-run` (plan only), `--no-llm` (force deterministic), `--quiet`, `--cwd <path>`, `--timeout <s>`.

**Exit codes (stable, for callers):**
| Code | Meaning |
|---|---|
| 0 | success |
| 1 | generic error |
| 2 | usage error |
| 3 | LLM required but unavailable (and not degradable) |
| 4 | needs approval (mutating action in non-interactive mode) |
| 5 | sandbox / permission denied |

`--json` emits `CliResult.Data` as JSON so the Desktop / agent can parse results
(e.g. `syncro ai explain X --json` → structured answer + cited sources).

---

## 7. The missing commands — spec + priority

`Services/SyncroCLI/Commands/` (or `Syncro.Cli.Core/Commands/`). Each is an `ICliCommandV2`.

| Priority | Command | Spec | Reuses |
|---|---|---|---|
| **P1** | `scan` | `syncro scan [path] [--json] [--tokenize]` → AST scan, write `.syncro_db/AST`, optional vectorize | `AstService` |
| **P1** | `ai` | `syncro ai <explain\|generate\|find\|review> <target> [--no-llm] [--json] [--yes]` → RAG (always) + LLM (if available) + safety gate | `RagEngine`, `LlmGateway`, `LlmSafetyGate` |
| **P1** | `memory` | `syncro memory <list\|search\|purge>` → hindsight store | `HindsightStore` |
| **P2** | `graph` | `syncro graph [--format mermaid\|dot] [--output f]` | `GraphExporter` |
| **P2** | `generate` | `syncro generate <controller\|dto\|service> <name> [--arch clean]` → patch plan → approve → apply | `ProjectGenerator`, templates |
| **P2** | `template` | `syncro template <list\|apply\|import>` | template system (`grouptemplateproject.md`) |
| **P2** | `project` | `syncro project <list\|group\|remove>` | `ProjectService` |
| **P2** | `run` | `syncro run [--port] [--env]` → detect & launch dev server | `ProcessRunner` |
| **P3** | `patch` | `syncro patch --last-error [--yes]` → fix→build→record (safe loop) | error intel + `LlmSafetyGate` |
| **P3** | `kb` | `syncro kb <add\|search\|list>` | knowledge base |
| **P3** | `status` | `syncro status --msg --level` → Named-Pipe push | `AgentBridge` (new) |
| **P3** | `install` | `syncro install [--uninstall]` → UAC PATH register | `SyncroCLIService` PATH logic |
| **P3** | `deploy` | `syncro deploy --target` | deploy config |
| **P3** | `agent` | `syncro agent "<goal>" [--plan-only]` → multi-step (Architect→Generator→Validator→Repair), each step safety-gated | agent layer (`ast.md` Ext II/III) |

`help` stays; `init`/`doctor`/`git`/`script` migrate to `ICliCommandV2` for `--json` support.

---

## 8. Refactors to existing code

1. **`AIClient` → `LlmGateway`**: add `ProbeAsync` (GET /health, 1.5 s), per-call timeout + CT,
   streaming, task-specific prompts; drop the unused batch-only prompt; `VerifyApiKeyAsync` → real probe.
2. **`CliEngine`**: support `ICliCommandV2`, global flags, exit codes, `--json`.
3. **`AstService.TokenizeProjectAsync`**: route through `IEmbeddingProvider` (Lexical default) instead of simulated weights; write real `VectorRecord`s.
4. **Single `.syncro_db`** per project/group root (carry over the fix from `projectgenerator.md`).
5. **Align `docs/cli.html`** to the phased reality (mark unbuilt commands "planned") or build to it — don't ship docs that overstate the binary.

---

## 9. Build order

1. **`LlmGateway` + probe** (availability detection, timeouts) — unblocks safe auto-use.
2. **RAG core** (`IEmbeddingProvider`, `LexicalEmbedder`, `VectorStore`, `RagEngine`) — offline hindsight.
3. **`LlmSafetyGate`** (redactor, approval broker, audit, allowlists) — before any AI command ships.
4. **`ICliCommandV2`** + global flags + exit codes in `CliEngine`.
5. **P1 commands**: `scan`, `ai`, `memory` — first end-to-end safe RAG+LLM loop.
6. **P2 commands**: `graph`, `generate`, `template`, `project`, `run`.
7. **Standalone `Syncro.CLI.exe` + Named-Pipe `AgentBridge`** (crash isolation) → `status`, `install`.
8. **P3**: `patch`, `kb`, `deploy`, `agent` (multi-step, fully gated).

---

## 10. Verification (safety-first)

- **Offline:** stop the 3020 server → `syncro ai explain X` still returns RAG snippets + sources; exit `0` with a "LLM offline, degraded" warning (not a hang, not a crash).
- **Probe:** `LlmGateway.ProbeAsync` returns within 1.5 s when 3020 is down; result cached 30 s.
- **No auto-exec:** `syncro generate controller X` (no `--yes`) writes nothing — only prints a diff; `--json` returns exit `4`. With `--yes`, applies, builds in sandbox, records to `audit.jsonl`.
- **Redaction:** seed a fake `API_KEY` in context → confirm it never appears in the LLM request payload or logs.
- **Path policy:** `syncro ai generate --out ../../etc/x` → rejected (exit `5`), audited.
- **All-commands smoke:** every command responds to `--help` and `--json`; `dotnet build` → 0 errors.
```
