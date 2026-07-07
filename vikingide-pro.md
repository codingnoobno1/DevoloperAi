# Viking IDE — Professional Plan (real services, no simulation)

> Hard rule: **every feature calls a real Syncro desktop‑agent service.** No mock data, no
> simulated LLM, no "showcase" theatre. If a capability can't be backed by a real service yet,
> it isn't shipped — it's listed as a future step.

## Real services we already have (and will wire)

| Service (exists) | Real capability | IDE surface |
|---|---|---|
| `AstService.ScanProjectAsync` | parse project → `AstProjectMap` (nodes, **endpoints**, dtos, deps, ports, graphs) | Analysis window |
| `AstService.GeneratePdfAsync` | **QuestPDF report** to disk | "Generate Report" |
| `AstService.ExportJsonAsync` | machine‑readable map | "Export JSON" |
| `AstProjectMap.Endpoints` (`AstEndpoint`) | Method · Path · Params · Request/Response DTO · Controller · File:Line | **API Endpoint Map** |
| `AstService.TokenizeProjectAsync` + `HindsightVectorStore` | vector index | Hindsight panel |
| `HindsightQueryService` | RAG with/without LLM | Hindsight panel |
| `SyncroCLIService` | run `syncro` verbs | CLI panel |
| `DbStatusService` (`/api/db`, :3030) | tasks/scripts/nlp/tokens telemetry | Telemetry |
| `GitProvider` / `LibGit2Sharp` | status/diff/commit | Source Control (later) |
| `ProjectGenerator` + `ProjectTemplates` | scaffold projects | New Project |
| `FileService` + editor | open/edit/save | Editor |

## Windows model (per request: open report / API map as separate windows, 1–2)

- **Viking IDE window** — the workbench (shell + explorer + editor + panels). Already a separate OS window (`IdePage` → `IdeRoot`, chrome‑less).
- **Project Analysis window** — a dedicated OS window (`AnalysisPage` → `AnalysisRoot`) that runs `AstService` for real and shows the **API Endpoint Map** + **report generator**. Opened from the IDE and from the main app nav. *(This is "1" — the report + endpoint map live as tabs in one analysis window; trivially split into two later.)*
- Both opened via `IdeWindowService` using the proven `Application.Current.OpenWindow(...)` pattern.

## Step 1 (this iteration) — Project Analysis window, for real

`Components/IDE/AnalysisView.razor` (+ `AnalysisRoot` + `AnalysisPage`):
- Pick a project (real `ProjectService.GetProjects()`), or inherit the workspace from the IDE.
- **Scan** → `AstService.ScanProjectAsync(path)` (real, cached). Shows live counts: language, framework, nodes, endpoints, DTOs, deps, ports.
- **API Endpoint Map** tab → real table from `map.Endpoints`: method‑colored rows, path, controller·handler, params, request/response DTO, `file:line`.
- **Report** tab → **Generate PDF** (`GeneratePdfAsync` → real `.pdf`, opens it) and **Export JSON** (`ExportJsonAsync`).
- Professional dark UI, consistent with the IDE.

Wiring: `IdeWindowService.OpenAnalysis(path)` + `IdeLaunchContext.AnalysisPath`; nav entry "Project Analysis"; an **Analyze Project** button on the IDE welcome/activity bar.

## Step 2 — Editor upgrade to Monaco
Swap the textarea in `CodeEditor.razor` for **BlazorMonaco** (syntax highlight, minimap, IntelliSense via Roslyn). Contained change behind the same component (per `vikingide-spec.md` Phase 1.5).

## Step 3 — Embedded panels (move full pages into IDE docks)
Hindsight (RAG with/without LLM), Telemetry (`/api/db`), CLI, Source Control become IDE side/bottom panels instead of separate routes.

## Step 4 — Terminal (Xterm + ConPTY) and Git panel
Per `vikingide-spec.md` Phase 2.

## Non‑negotiables
- Real service calls only; show real errors (don't swallow into fake "success").
- Each new window disposes cleanly; build stays green (build with the app closed — running instance locks the DLL).
- No new heavy UI kits; MudBlazor + custom CSS.
