# Syncro Extension System — Plan

> How Syncro becomes a first-class citizen *inside* code-oss by using its extension machinery —
> instead of rebuilding editor features outside it. Companion to `syncro-codeoss-mcp.md`
> (architecture reference). Status: **PLAN** — grounded in a recon of the actual `vscode-main`
> checkout (2026-07-16); phase gates below.

---

## 0. What the recon found (facts, not assumptions)

| Fact | Evidence in `vscode-main` | Why it matters |
|---|---|---|
| **109 builtin extensions** ship in-tree | `extensions/` (git, docker, dart, csharp, emmet, …) | Language smarts, git UI, debuggers already exist — we don't rebuild any of it |
| **Copilot Chat is in-tree** | `extensions/copilot` → `copilot-chat`, compiled by `npm run compile` (`compile-copilot`) | The full agentic chat UI + tool loop ships with our build |
| Copilot has **BYOK model providers** | `package.json` → `languageModelChatProviders`, `copilot.claude.*` settings | Groq (OpenAI-compatible) can back the chat — no GitHub account required path |
| **Native MCP support** in the workbench | `vscode.proposed.mcpServerDefinitions.d.ts`, `chat/…/chatMcpAppModel.ts` | An extension can register MCP servers; their tools appear natively in chat |
| Rich proposed API surface | `src/vscode-dts/`: `chatParticipant*`, `chatProvider`, `languageModel*`, `embeddings`, `contribLanguageModelToolSets` | Deep integration points beyond the stable API |
| Server manages extensions via CLI | `--extensions-dir`, `--install-extension <vsix>`, `--install-builtin-extension`, `--list-extensions`, `--uninstall-extension`, `--server-data-dir` | Sideloading works headlessly — Syncro can provision extensions per workspace |
| **No marketplace configured** | `product.json` has no `extensionsGallery` | Only builtin + sideloaded extensions load today; Open VSX is an optional add |
| **AgentHostChannel patch** (ours) | `src/vs/server/node/agentHostChannel.ts`, `serverAgentHostManager.ts`, `--agent-host-bridge-port/-path` | A C#-reachable IPC channel into the running server — our control plane |

**Strategic consequence:** the highest-leverage move is NOT writing many editor features — it is
**one bridge extension + one MCP endpoint**, which makes all ~50 Syncro engine tools available to
the editor's existing agentic chat, and all 109 builtin extensions available to Syncro users.

---

## 1. Architecture — three lanes

```
┌─ Syncro.Desktop (C#) ──────────────────────────────────────────────────────────┐
│  McpOrchestrator (14 servers / ~50 tools)                                      │
│      │                                                                          │
│      │  LANE B: MCP endpoint (localhost HTTP/SSE or stdio)                      │
│      ▼                                                                          │
│  SyncroMcpEndpoint ◄────────────────────────────┐                               │
│                                                 │                               │
│  CodeOssIdeHost ── launches ──► code-server ────┼── LANE C: AgentHostChannel    │
│      (IIdeHost)                 (server-main.js)│      (--agent-host-bridge-*)  │
└─────────────────────────────────────────────────┼───────────────────────────────┘
                                                  │
                    ┌─ code-oss workbench ────────┼──────────────────────────────┐
                    │  extension host             │                              │
                    │  ├─ LANE A: syncro-bridge extension                        │
                    │  │    • registers McpServerDefinition → Syncro tools       │
                    │  │      appear in chat natively                            │
                    │  │    • commands / tree views / webviews (scan, graph)     │
                    │  │    • status bar: Syncro engine health                   │
                    │  ├─ copilot-chat (in-tree) — chat UI + agent loop + BYOK   │
                    │  └─ 108 other builtins (git, dart, csharp, debuggers…)     │
                    └────────────────────────────────────────────────────────────┘
```

- **Lane A — `syncro-bridge` extension** (TypeScript, runs in the extension host): the presence of
  Syncro inside the editor. Registers the MCP server definition, contributes commands/views, and
  talks to the C# side over localhost.
- **Lane B — MCP endpoint in C#**: expose the existing `McpOrchestrator` over a real MCP transport.
  One endpoint → every engine tool (FileSystem, Connector, Universe, Runtime, AST, Git, Patch,
  Planning, Knowledge, Repair, JSON) becomes a native chat tool. **No per-tool porting.**
- **Lane C — AgentHostChannel**: C# → editor control RPC using our existing server patch
  (open file at line, reveal scan results, trigger commands) — the reverse direction of Lane B.

---

## 2. What "all the tools of the code-oss repo" concretely buys us

| code-oss capability | Syncro use |
|---|---|
| `vscode.commands` / contributed menus | `Syncro: Scan Workspace`, `Syncro: Map Contracts`, `Syncro: Run Stack`, `Syncro: Thunder Scan` in palette + explorer context menus |
| Tree views (`contributes.views`) | Connector graph sidebar: routes / calls / matched / missing per project |
| Webview panels | The Multi-Scan dashboard + connection map rendered *inside* the editor |
| `McpServerDefinition` (proposed) | The one-line registration that surfaces all ~50 Syncro tools in chat |
| Chat participant (`@syncro`) | Domain answers grounded in the Universe graph ("which frontend calls `/api/v1/orders`?") |
| `languageModelChatProviders` (copilot BYOK) | Route the in-editor agent through **Groq `openai/gpt-oss-120b`** (the sidecar decision) |
| Diagnostics API | Publish `MapContracts` mismatches as squiggles on the exact `ApiConstants`/controller lines (we have file:line provenance) |
| CodeLens | "▶ 3 consumers · 1 mismatch" above each backend route handler |
| FileSystemProvider | Virtual `syncro-scan://` tree exposing `.thunder/reverse/*.json` read-only |
| Tasks / Terminal API | `RunGroup`/`StartStack` as VS Code tasks with live output |
| Debug adapters (builtin dart/js/csharp) | Debugging works day-one for the PG_Connect stacks — zero Syncro code |
| Language extensions (dart, csharp, ts, python…) | Same — free |
| `--install-extension` / `--extensions-dir` | `CodeOssIdeHost` provisions the Syncro extension automatically at launch |

---

## 3. Repo layout

```
vscode-main/extensions/syncro-bridge/     ← LANE A (builtin = always present in our build)
  package.json          # contributes: commands, views, mcpServerDefinitionProviders,
                        #   chatParticipants, menus, configuration (syncro.endpoint, syncro.token)
  src/extension.ts      # activate(): connect to Syncro endpoint, register everything
  src/mcpProvider.ts    # McpServerDefinitionProvider → points at Syncro's MCP endpoint
  src/scanViews.ts      # tree views over MultiProjectScanReport
  src/diagnostics.ts    # MapContracts → vscode.Diagnostics (file:line from provenance)
  tsconfig.json / esbuild config (copy the pattern from a small builtin, e.g. `dotenv`)

Syncro.Desktop/Services/Engine/Mcp/Transport/
  McpHttpEndpoint.cs    ← LANE B: streamable-HTTP MCP endpoint over McpOrchestrator
                        #   localhost-only, per-session bearer token, RequiresAdminApproval
                        #   tools gated behind an explicit consent prompt in Syncro UI
```

**Builtin vs sideload:** builtin (in `extensions/`) means it ships with every `npm run compile` and
can use **proposed APIs** (needed for `mcpServerDefinitions` + chat participant). A `.vsix` sideload
lane stays available for iterating without recompiling the workbench
(`--install-extension syncro-bridge.vsix --extensions-dir <dir>`).

---

## 4. Phases

Format matches the other Syncro plans: each phase is independently shippable and ends with a
**verify gate** (a runnable proof, not "should work").

### E0 — Walking skeleton (go/no-go)
Scaffold `extensions/syncro-bridge` (clone the `dotenv` builtin's build shape). One command:
`Syncro: Ping` → calls `http://127.0.0.1:<port>/health` on a minimal C# listener, shows the reply.
- **Gate:** command appears in the palette of our built workbench and round-trips to C#.
- **Risk retired:** builtin registration, esbuild wiring, extension-host ↔ Syncro networking.

### E1 — MCP endpoint (the multiplier)
`McpHttpEndpoint.cs`: streamable-HTTP MCP server wrapping `McpOrchestrator.Dispatch` —
`tools/list` from the registered `IMcpTool`s (name, description, `InputSchema`), `tools/call` →
dispatch. Localhost bind, random port, bearer token handed to the extension via the launch env.
`mcpProvider.ts` registers it as a `McpServerDefinition`.
- **Gate:** in code-oss chat, the tools panel lists Syncro tools; asking the agent to
  "map contracts for D:\PG_Connectjlpl" invokes `MapContracts` and returns the real report
  (66 routes / 39 calls / 31 matched — the verified numbers).
- **Security gate:** `RequiresAdminApproval` tools (`WriteFileBatch`, `ApplyPatch`,
  `WriteBackendFiles`, `StartStack`…) refuse unless approved in the Syncro UI; read-only tools flow.

### E2 — Auto-provision + model routing
`CodeOssIdeHost` injects endpoint/port/token env at launch; extension reads them (no manual config).
Configure copilot-chat BYOK (`languageModelChatProviders`) with the Groq OpenAI-compatible provider
so the in-editor agent runs on `openai/gpt-oss-120b` without a GitHub dependency.
- **Gate:** fresh IDE window → chat works against Groq → Syncro tools callable, zero manual setup.

### E3 — Surface the engines in the editor UI
- Tree view: Multi-Scan results (projects → routes/calls/data layer) with click-to-open (provenance file:line).
- Diagnostics: `MapContracts` mismatches as warnings on call sites and route handlers.
- Commands: `Thunder Scan`, `Index Workspace`, `Run Stack` (as tasks with terminal output).
- **Gate:** open `pgconnect_student_app` in the IDE → squiggle on a mismatched `ApiConstants` line
  with the backend diff in the hover.

### E4 — `@syncro` chat participant
Chat participant that answers architecture questions from the Universe graph + scan JSON
(`@syncro who consumes POST /api/v1/orders?`), citing files. Distinct from Lane B: this is
*retrieval + synthesis*, not tool execution.
- **Gate:** three canned questions answered correctly against PG_Connect with file citations.

### E5 — AgentHostChannel round-trip (C# → editor)
Wire `serverAgentHostManager` (`--agent-host-bridge-port`) to a C# listener: Syncro pushes
"open file X:line Y", "reveal scan view", "run command Z" into the live workbench. This merges the
copilot-engine sidecar plan (Phase 2 `NodeAgentBridge`) with the extension system.
- **Gate:** clicking a route in Syncro's Multi-Scan window opens the exact controller line in code-oss.

### E6 — Distribution & ecosystem (optional)
- Add **Open VSX** as `extensionsGallery` in `product.json` (licensing-safe for OSS builds;
  Microsoft's marketplace is contractually off-limits to non-official builds).
- Package `syncro-bridge` as `.vsix` for non-builtin installs; version it with the app.
- **Gate:** extension view can search/install from Open VSX in our build.

---

## 5. Design rules

1. **One bridge, not fifty ports.** New engine capability = new `IMcpTool` in C#; the editor picks
   it up from `tools/list` automatically. The extension stays thin and stable.
2. **Security is C#'s job.** The MCP endpoint is localhost + token; approval-gated tools prompt in
   Syncro's own UI (the editor never gets a bypass). Write-tools stay behind `PathGuard`.
3. **Proposed APIs only inside the builtin.** Anything needing `enabledApiProposals`
   (mcpServerDefinitions, chat participant) lives in the builtin extension; the sideloadable `.vsix`
   variant degrades gracefully to stable-API features (commands, views, diagnostics).
4. **Never fork more than the patch.** All integration flows through extension points + the existing
   AgentHostChannel; upstream merges stay survivable. (The patch already has tests:
   `src/vs/server/test/node/agentHostChannel.test.ts` — keep them green.)
5. **Provenance everywhere.** Every UI surface (tree items, diagnostics, chat citations) carries
   file:line from the scan JSONs — same rule as the reverse-AST engine.

## 6. Risks / open questions

| Risk | Mitigation |
|---|---|
| Proposed APIs shift under us (`mcpServerDefinitions` is not stable) | Builtin extensions ride the same commit as the workbench — API and consumer move together; pin upstream merges |
| Copilot-chat BYOK config shape changes | It's in-tree; treat its `package.json` contribution as the contract, verify at E2 gate |
| MCP endpoint exposed beyond localhost | Bind 127.0.0.1 only + per-session token + no reverse-proxy headers honored |
| Extension host perf with big scan payloads | Tools return summaries + URIs; heavy JSON stays on disk, read via the `syncro-scan://` FS provider |
| First-run cost (compile, extension host warmup) | Already solved for the server (§ syncro-codeoss-mcp.md); extension adds negligible weight |

## 7. Sequence & effort (rough)

`E0` (small) → `E1` (the payoff, medium) → `E2` (small) → `E3`/`E4` (medium, parallelizable) →
`E5` (medium, unlocks sidecar Phase 2) → `E6` (small, optional).
Recommended first slice: **E0 + E1** — after those two, every existing and future Syncro tool is
usable from the editor's chat, which is 80 % of the value of the whole plan.
