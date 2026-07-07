# Project Generator — Analysis & Expansion

> Reviewing the **existing** implementation in `Services/projectgenerator/`
> (`ProjectGenerator.cs`, `ProjectGeneratorScripts.cs`) — already wired into
> `CreateProjectDialog.razor` and registered in `MauiProgram.cs`.

---

## 1. What's actually implemented today

| Piece | Status |
|---|---|
| `ProjectGenerator.ScaffoldProjectAsync(name, path, type, useNativeCli, onLog)` | ✅ exists |
| `ProjectGenerator.ScaffoldGroupProjectAsync(name, path, archetypes, useNativeCli, onLog)` | ✅ exists |
| Native-CLI path (`npx create-next-app`, `npm create vite`, `flutter create`, `django-admin`) | ⚠️ partial |
| Local static template fallback (FastAPI, Flask, Express, Next.js, Spring Boot) | ⚠️ partial |
| `.syncro_db/` local memory folders + `projects.json` / `groups.json` | ⚠️ buggy |
| `CreateProjectDialog.razor` (Group Stack, CLI toggle, Python/Node sub-frameworks) | ✅ exists |
| DI registration | ✅ exists |

**Verdict:** the skeleton works for a *single* Python/Node template project. The **group**
path, the **native-CLI** path, and the **registry** logic have correctness bugs that will
fail the proposal's own manual test ("Next.js + FastAPI + Postgres"). There is **no database
archetype at all**, and the existing `IProjectProvider` system is duplicated rather than reused.

---

## 2. Correctness bugs (concrete)

| # | Bug | Where | Effect |
|---|---|---|---|
| B1 | **`/webdashboard` generates the wrong framework.** Dialog adds component `"React/Next/Vite"`; in templates `t.Contains("next")` matches `"react/next/vite"` → Next.js template fires instead of Vite. | `ProjectGenerator.cs:224` (`t.Contains("next")`) vs dialog `CreateProjectDialog.razor:80` | "Vite React Dashboard" folder gets a Next.js scaffold |
| B2 | **Native-CLI output folder ≠ intended subfolder.** For a group, sub-name is `"{group}_{subfolder}"` and CLI runs in the *parent*, so `create-next-app {group}_app` produces `…/group_app`, but the manifest + `.syncro_db` were written to `…/app`. | `ProjectGenerator.cs:163,168,192` | Group app lands in the wrong directory; manifest path is wrong; empty `/app` left behind |
| B3 | **`groups.json` / `projects.json` overwrite, never append.** Each call writes `new List<JObject>{ record }`. | `ProjectGenerator.cs:69-73, 144` | Creating a 2nd project/group **wipes** the 1st registry entry |
| B4 | **N+1 `.syncro_db` folders.** `ScaffoldGroupProjectAsync` calls `ScaffoldProjectAsync` per member, so every subfolder gets its own `.syncro_db/`, plus one at the group root. | `ProjectGenerator.cs:47-53` (runs per member) | Duplicated memory stores; ambiguous which one the AST indexer reads |
| B5 | **Port collisions in groups.** Flask=5000 and Express=5000; two frontends both 3000. Hardcoded in templates. | `ProjectGeneratorScripts.cs:54,64,153` | Two services bind the same port → one fails to start |
| B6 | **No inter-service wiring.** `/app` and `/backend` are generated but the frontend has no API base URL, backend CORS isn't set to the frontend origin, no proxy. | group path overall | A "group" is just unrelated folders — the integration value is missing |
| B7 | **No database archetype.** Neither CLI nor template handles Postgres/Mongo. | entire service | The proposal's own manual test (…+ Postgres) cannot pass |
| B8 | **`setup.sh` never written; setup never run.** `GetSetupBashSh` exists but only `setup.bat` is emitted, and nothing executes it. | `ProjectGenerator.cs:210-232` | Mac/Linux hosts get no runnable script; deps never installed |
| B9 | **Group reports success on partial failure.** A failed member is silently skipped; group still returns `true`. No rollback. | `ProjectGenerator.cs:116-126,148` | User thinks the stack is complete when it isn't |
| B10 | **Interactive CLI can hang.** `npm create vite@latest` (and sometimes `create-next-app`) prompt; no `--yes`, no timeout, no `CancellationToken`. | `ProjectGenerator.cs:173,193` | UI spinner hangs forever on a prompt |
| B11 | **Spring Boot package hardcoded** `com.example.demo` regardless of name. | `ProjectGeneratorScripts.cs:121` | Every Spring project has identical package/namespace |
| B12 | **Type strings are inconsistent across the codebase** ("Vite React" vs "React/Next/Vite"; "Express/Node" vs `.Contains("express")`). Matching is ad-hoc `.Contains` in some places, `.Equals` in others. | dialog + generator | Silent wrong-branch selection (root cause of B1) |

---

## 3. Design gaps

1. **Duplicate scaffolding systems.** `IProjectProvider` (PythonProvider, MernProvider, JavaGradleProvider, FlutterProvider, …) already exists and is used by `syncro init`. `ProjectGenerator` re-implements the same thing inline → two code paths drift apart.
2. **Five hardcoded switch statements** for the same stack list (`RunNativeCliCreatorAsync`, `ScaffoldFromLocalTemplatesAsync`, `GetLanguageForType`, `GetPackageManagerForType`, dialog subfolder map). Adding a stack means editing 5 places.
3. **No environment-driven strategy.** `EnvironmentManager` already detects Python/Node/Java/Flutter/etc., but the CLI-vs-template decision ignores it (just "try and catch").
4. **No "User Review Required" gate.** The proposal calls for review before executing native tools, but commands run immediately with no dry-run/preview.
5. **No verification.** Nothing confirms the generated project builds or runs.
6. **AST API mismatch.** Dialog calls `AstService.ScanProjectAsync(path)` → `map.Count` and `AstService.TokenizeProjectAsync(map)`; the `ast.md` plan models `AstProjectMap` with collections, not `.Count`. Reconcile the two.

---

## 4. Expansion — how it can be made

### A. Declarative Stack Registry (removes the 5 switches)

Create `Services/projectgenerator/Registry/`:

```
StackArchetype.cs     — one record per stack
StackKind.cs          — enum: Frontend | Backend | Database | Mobile
StackRegistry.cs      — loads stacks.json, single source of truth
stacks.json           — data
```

```jsonc
// stacks.json — adding a stack = adding an entry (no code change)
[
  {
    "id": "fastapi", "label": "FastAPI", "kind": "Backend",
    "language": "Python", "packageManager": "pip",
    "subfolder": "backend", "defaultPort": 8000,
    "detect": { "tool": "python", "versionArg": "--version" },
    "cli": null,                                  // no official scaffolder → template only
    "template": "fastapi",
    "install": "pip install -r requirements.txt",
    "run": "uvicorn main:app --reload --port {PORT}",
    "provides": ["API_BASE_URL"], "needs": ["DATABASE_URL"]
  },
  {
    "id": "vite-react", "label": "Vite React", "kind": "Frontend",
    "language": "TypeScript", "packageManager": "npm",
    "subfolder": "webdashboard", "defaultPort": 5173,
    "detect": { "tool": "npm", "versionArg": "-v" },
    "cli": "npm create vite@latest {folder} -- --template react-ts --yes",
    "template": "vite-react",
    "install": "npm install", "run": "npm run dev -- --port {PORT}",
    "needs": ["API_BASE_URL"]
  },
  {
    "id": "postgres", "label": "PostgreSQL", "kind": "Database",
    "subfolder": "database", "defaultPort": 5432,
    "compose": "postgres:16", "provides": ["DATABASE_URL"]
  }
  // … next, express, flask, django, spring, flutter, mongo
]
```

`StackArchetype` carries everything the 5 switches encoded: language, package manager,
subfolder, port, CLI command, template key, install/run commands, and **`needs`/`provides`**
keys that drive wiring (§D). One canonical `id` fixes B1/B12.

### B. Strategy selection + preview gate

```
Strategy/GenerationStrategySelector.cs   — picks Cli vs Template per archetype
Strategy/GenerationPlan.cs               — dry-run: the exact commands + files
```

- `GenerationStrategySelector` asks `EnvironmentManager.CheckAllEnvironments()`; if the
  archetype's `detect.tool` is present **and** `useNativeCli`, choose CLI, else Template.
- Build a **`GenerationPlan`** first (list of `GenerationStep`: "run `npx …` in `/app`",
  "write `main.py`", "compose service `postgres:16`"). Show it in the dialog for
  **User Review** (the proposal's gate). Only on confirm does `GenerationExecutor` run it.
- Fixes B10 by adding `--yes`, a per-step **timeout**, and a `CancellationToken`.

### C. Reuse `IProjectProvider`

Make `ProjectGenerator` an **orchestrator**, not a re-implementation:

```
Scaffolders/IStackScaffolder.cs      — { StackArchetype Archetype; Task<bool> Scaffold(ctx) }
Scaffolders/CliScaffolder.cs         — runs Archetype.cli via ProcessRunner
Scaffolders/TemplateScaffolder.cs    — writes Archetype.template via TemplateEngine
Scaffolders/ProviderAdapter.cs       — wraps existing IProjectProvider (Mern/Flutter/…)
```

Where a provider already exists (MERN, Flutter, Java/Gradle), `ProviderAdapter` delegates to
it; otherwise `Cli`/`Template` scaffolders handle it. One code path, no drift.

### D. Group orchestration done properly

```
Groups/GroupOrchestrator.cs    — plan → order → scaffold → wire → manifest
Groups/PortAllocator.cs        — collision-free ports (reuse ast.md PortScanner)
Groups/ProjectWiringService.cs — cross-service env / CORS / proxy
Groups/ComposeGenerator.cs     — root docker-compose.yml + run-all scripts
Groups/GroupManifest.cs        — append-safe groups.json (fixes B3)
Groups/GroupPreset.cs          — named combos (presets.json)
```

- **PortAllocator** assigns each member a free port (scan + reserve), writing the real port
  into each template via `{PORT}` placeholder → fixes **B5**.
- **ProjectWiringService** resolves `needs`/`provides`: a Frontend that `needs API_BASE_URL`
  gets `.env` `VITE_API_BASE_URL=http://localhost:{backendPort}`; the Backend that
  `provides API_BASE_URL` gets CORS origin = `http://localhost:{frontendPort}`; a Backend
  that `needs DATABASE_URL` gets it from the Postgres member → fixes **B6/B7**.
- **ComposeGenerator** emits a root `docker-compose.yml` (db + services), a `run-all.ps1` /
  `run-all.sh`, and a root `README.md` describing the group + ports.
- **Dependency ordering**: Database → Backend → Frontend (so wiring values exist before
  the dependent member is written).
- **GroupManifest** reads existing `groups.json`, appends, dedupes by `group_id` → fixes **B3**.

### E. Database archetype (the missing piece)

`Database` archetypes don't scaffold a folder of code — they contribute a **service** to the
root compose file and a `DATABASE_URL` to the wiring graph. Add `postgres`, `mongo`, `mysql`
entries to `stacks.json`; `DatabaseScaffolder` writes an init `/database/init.sql` (or
`mongo-init.js`) and the compose service block. This makes "Next.js + FastAPI + Postgres"
actually runnable end-to-end.

### F. Execution, verification, single registry

```
Execution/GenerationExecutor.cs    — runs the plan, streams onLog, idempotent
Execution/GenerationTransaction.cs — snapshot + rollback on failure (fixes B9)
PostGen/ProjectVerifier.cs         — runs install; optional smoke-run (curl /health)
PostGen/PostGenerationIndexer.cs   — calls AstService scan + tokenize once per project
```

- Optionally **run** the install step (existing `ScriptRunner`/`ElevationService`) instead of
  only writing `setup.bat` → fixes **B8** (and also emit `setup.sh` on non-Windows).
- After success, **append** to a single global registry under
  `%LOCALAPPDATA%\SyncroDesktop\` (not one `.syncro_db` per subfolder) → fixes **B4**.
  Keep a single `.syncro_db` at the **group root** only.
- Auto-index each member with the AST service so the new project is immediately searchable.

### G. `.syncro_db` placement

- **Single** `.syncro_db` at the group root (or project root for singles).
- Subfolders do **not** get their own `.syncro_db`.
- `projects.json` is a project list at the group/db root; `groups.json` lives in the
  user-global store and is append-safe.

---

## 5. Target module layout

```
Services/projectgenerator/
├── ProjectGenerator.cs              (facade — keep public API ScaffoldProjectAsync/Group)
├── ProjectGeneratorScripts.cs       (existing templates — parameterize {PORT},{PKG},{NAME})
├── Registry/
│   ├── StackArchetype.cs
│   ├── StackKind.cs
│   ├── StackRegistry.cs
│   └── stacks.json
├── Strategy/
│   ├── GenerationStrategySelector.cs
│   ├── GenerationPlan.cs
│   └── GenerationStep.cs
├── Scaffolders/
│   ├── IStackScaffolder.cs
│   ├── CliScaffolder.cs
│   ├── TemplateScaffolder.cs
│   ├── ProviderAdapter.cs
│   └── DatabaseScaffolder.cs
├── Templates/
│   ├── ProjectTemplateEngine.cs     (placeholder substitution)
│   └── TemplateStore.cs             (embedded resources / templates dir)
├── Groups/
│   ├── GroupOrchestrator.cs
│   ├── GroupManifest.cs
│   ├── GroupPreset.cs   + presets.json
│   ├── PortAllocator.cs
│   ├── ProjectWiringService.cs
│   └── ComposeGenerator.cs
├── Execution/
│   ├── GenerationExecutor.cs
│   └── GenerationTransaction.cs
└── PostGen/
    ├── ProjectVerifier.cs
    └── PostGenerationIndexer.cs
```

The public methods the dialog already calls (`ScaffoldProjectAsync`,
`ScaffoldGroupProjectAsync`) stay — they just delegate into this structure, so **no UI change
is required** to adopt the refactor (the dialog keeps working through migration).

---

## 6. Suggested build order (incremental, non-breaking)

1. **Fix the bugs first** (no new architecture): B1/B12 (canonical stack ids), B3 (append
   registry), B2 (run CLI in the member folder, name it after the subfolder), B4 (single
   `.syncro_db`), B10 (`--yes` + timeout). Small, high-value.
2. **Stack Registry** (`stacks.json` + `StackRegistry`) → delete the 5 switches.
3. **PortAllocator + ProjectWiringService** → groups become connected (B5/B6).
4. **Database archetype + ComposeGenerator** → the FE+API+DB test passes (B7).
5. **GenerationPlan preview gate** in the dialog → the "User Review Required" requirement.
6. **Verifier + PostGenerationIndexer** → install + AST index + single global registry.

---

## 7. Verification plan (expanded)

- **Build:** `dotnet build` clean.
- **Unit:** `StackRegistry` loads all archetypes; `PortAllocator` never returns a used port;
  `ProjectWiringService` produces matching API_BASE_URL ⇄ CORS origin pairs.
- **Manual:** create group **Next.js + FastAPI + Postgres** →
  - subfolders `/app`, `/backend`, `/database` exist with correct templates (not wrong-framework);
  - root `docker-compose.yml` + `run-all` script present;
  - `/app/.env` has `…API_BASE_URL=http://localhost:8000`; `/backend` CORS allows
    `http://localhost:3000`; backend `DATABASE_URL` points at the Postgres service;
  - `docker compose up` brings all three up with **no port collision**;
  - one `.syncro_db` at the group root, AST-indexed.
```
