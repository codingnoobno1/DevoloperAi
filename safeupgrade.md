# Safe Upgrade Plan — New Project Creation Wizard

> **Goal of the end state:** one place, **1 click + 1 prompt**, and the AI is *informed* —
> the user types *"a flutter app with a node backend and postgres"* and Syncro plans the stack,
> folder structure, ports, and env wiring, shows it for a quick review, then scaffolds + indexes
> everything in one go.
>
> **Goal of THIS document:** get there **without ever breaking the wizard that works today.**
> The current `CreateProjectDialog` must keep functioning at every commit while the new
> smart flow is built *alongside* it. This is a refactor-safety plan, not a feature spec.

---

## 0. The one rule

> **Refactor, fix, and add are three separate kinds of change. Never combine them in one step.**

1. **Refactor** = move code behind a seam, behavior identical (Phases 1–2).
2. **Fix** = correct the B1–B12 bugs from [projectgenerator.md](projectgenerator.md) (Phase 3, *after* the seam exists, each fix isolated and reversible).
3. **Add** = the 1-prompt AI flow + Connector (Phases 4–6, purely additive, behind a flag).

Mixing them is how "an upgrade" turns into "the create button is broken and we don't know which change did it."

---

## 1. Current state — exactly what works today (the baseline to protect)

**Entry points (2):**
- [Components/Layout/NavMenu.razor:59](Components/Layout/NavMenu.razor) → `DialogService.ShowAsync<CreateProjectDialog>(...)`
- [Components/Pages/Projects/MyProjects.razor:107](Components/Pages/Projects/MyProjects.razor) → same

**The dialog** ([Components/Shared/CreateProjectDialog.razor](Components/Shared/CreateProjectDialog.razor)):
- 3-step stepper: **(1)** pick type (Python / Node.js / Mobile / API / Group Stack) → **(2)** config
  (framework, package name, group components, native-CLI toggle) → **(3)** path + sync-with-agent.
- On submit calls one of two generator methods, streams `onLog` into a fake terminal, then on success:
  `ProjService.ImportProject(path)` → optional `AstService.ScanProjectAsync` + `TokenizeProjectAsync`
  → `MudDialog.Close(DialogResult.Ok(project))`.

**The generator** ([Services/projectgenerator/ProjectGenerator.cs](Services/projectgenerator/ProjectGenerator.cs)):
```csharp
Task<bool> ScaffoldProjectAsync(name, targetPath, type, useNativeCli, Action<string>? onLog)
Task<bool> ScaffoldGroupProjectAsync(groupName, targetPath, archetypes, useNativeCli, onLog)
```
Native-CLI path (`npx create-next-app`, `flutter create`, …) with a static-template fallback,
then writes `.syncro_db/` + `projects.json` / `groups.json`.

**Contract the callers depend on (must stay stable):**
- Dialog returns `DialogResult.Ok(ProjectModel)` or `Cancel`.
- Caller navigates to `myprojects` on non-cancel.

**Known-bad but currently shipped:** B1–B12 in [projectgenerator.md](projectgenerator.md) (wrong Vite/Next
branch, registry overwrite, port collisions, no rollback, interactive-CLI hangs, …). **We do not touch
these in Phases 1–2** — preserving current behavior means preserving its bugs *until the seam exists*.

---

## 2. Why a naive upgrade is dangerous

- Both call sites bind to the concrete `CreateProjectDialog` type — editing it edits the live path.
- Generation, registry I/O, AST indexing, and UI are **fused inside one `.razor` `@code` block**
  (`Submit()` does file writes, parsing, and navigation). There's no seam to test or swap.
- The registry writes **overwrite** (B3): a botched experiment can wipe a user's `projects.json`.
- Native CLI runs real `npx`/`flutter` with no timeout (B10): a refactor that changes working-dir or
  args can hang the UI with no error.

So: **build the seam first, leave the old UI calling through it, then add the new UI in parallel.**

---

## 3. Strategy — Parallel Change (expand → migrate → contract)

```
        TODAY                 PHASE 1-2 (seam)            PHASE 4-5 (parallel)        PHASE 6 (contract)
   ┌───────────────┐      ┌───────────────┐           ┌───────────────┐          ┌───────────────┐
   │ CreateProject │      │ CreateProject │           │ CreateProject │ (manual) │  SmartCreate  │
   │    Dialog     │─────▶│    Dialog     │──┐        │    Dialog     │──┐       │   Dialog      │
   │  (does all)   │      └───────────────┘  │        └───────────────┘  │       │ ┌───────────┐ │
   └───────────────┘            calls         ▼            calls          ▼       │ │prompt mode│ │
                          ┌────────────────────────┐  ┌────────────────────────┐ │ │manual mode│ │
                          │ IProjectCreationService │  │ IProjectCreationService │ │ └───────────┘ │
                          │   (behavior-identical)  │  │  + IProjectPlanner(AI)  │ └──────┬────────┘
                          └────────────────────────┘  └────────────────────────┘        │
                                                                                   same service
```

The old dialog keeps working the whole time because it always calls the **same service contract**;
the new dialog is a *new file* that calls the *same service*. Nothing about the old path changes
except *where* the generation code physically lives.

---

## 4. The seam — `IProjectCreationService`

A single behavior-preserving facade that wraps **exactly what `Submit()` does today**. No new logic.

```csharp
// Services/projectgenerator/IProjectCreationService.cs
public interface IProjectCreationService
{
    Task<ProjectCreationResult> CreateAsync(ProjectCreationRequest req, IProgress<string> log, CancellationToken ct = default);
}

public sealed class ProjectCreationRequest
{
    public string Name = "";
    public string Path = "";
    public CreationKind Kind;                 // Single | Group  (Connected added later, additively)
    public string Type = "";                  // "FastAPI", "Flutter", "Express/Node", …
    public List<string> GroupComponents = new();
    public bool UseNativeCli;
    public bool SyncWithAgent = true;
    // Phase 4 adds (nullable, ignored by old path): ProjectPlan? Plan;
}

public sealed class ProjectCreationResult
{
    public bool Success;
    public ProjectModel? Project;
    public string? Error;
}
```

**Implementation `DefaultProjectCreationService`** = a literal copy of the orchestration in
`CreateProjectDialog.Submit()` (call `ScaffoldProjectAsync`/`ScaffoldGroupProjectAsync`, `ImportProject`,
optional AST scan/tokenize). The dialog then becomes a thin caller:

```csharp
var result = await Creation.CreateAsync(req, new Progress<string>(l => { _generatorLogs.Add(l); StateHasChanged(); }));
if (result.Success) MudDialog.Close(DialogResult.Ok(result.Project));
```

`CancellationToken` is added now (even if unused by old path) so the new flow can cancel hung CLIs later
without re-touching the seam.

---

## 5. Phased plan — each phase is independently shippable & reversible

### Phase 1 — Introduce the seam (pure extraction)
- **Do:** create `IProjectCreationService` + `DefaultProjectCreationService` containing the *exact*
  logic currently in `Submit()`. Register in `MauiProgram.cs`. Re-point `CreateProjectDialog.Submit()`
  to call it. **No behavior change.**
- **Safe because:** old generator methods are untouched; the dialog's public contract
  (`DialogResult.Ok(ProjectModel)`) is identical; both call sites unchanged.
- **Rollback:** revert the dialog's `Submit()` to inline; delete the new service. One-file revert.
- **Done when:** creating a Python project and a Group Stack behaves byte-identically to `master`
  (same files on disk, same `projects.json`, same snackbar). **Verify by diffing output dirs.**

### Phase 2 — Lock the baseline with characterization tests
- **Do:** add a small test/console harness that runs `CreateAsync` for each current type into a temp
  dir and asserts the produced file tree + registry JSON. This *captures today's behavior — bugs and all*.
- **Safe because:** tests only; no product change.
- **Done when:** the harness is green against current `master` output. This is the tripwire for Phases 3–6.

### Phase 3 — Fix B1–B12, one isolated guarded commit each
- **Do:** with the seam + characterization net in place, fix the [projectgenerator.md](projectgenerator.md)
  bugs *individually*. Each fix updates the characterization expectation deliberately (so the diff shows
  *exactly* what behavior changed). Priority order: **B3 (registry overwrite — data loss)** → **B10 (CLI hang)**
  → **B2 (wrong folder)** → **B1/B12 (wrong template branch)** → rest.
- **Safe because:** one behavioral change per commit, each visible as one expectation diff, each revertible.
- **Done when:** the "Next.js + FastAPI + Postgres" manual test from the generator doc passes.

> Phases 1–3 leave the UX *identical* but the foundation correct and tested. **Only now** do we add.

### Phase 4 — `IProjectPlanner` (AI intent → plan), headless
- **Do:** add the AI brain as a *separate* service, no UI yet:
  ```csharp
  public interface IProjectPlanner { Task<ProjectPlan> PlanAsync(string prompt, PlannerContext ctx, CancellationToken ct); }
  ```
  `ProjectPlan` is just a pre-filled `ProjectCreationRequest` (+ a human `Summary` and per-member
  `Architecture`/`Port`/`Role`). Implementation calls `ILLMProvider` / `AIClient` with the
  **informed context** from §6, returns structured JSON validated the same way
  [MobilePreview.razor](Components/Shared/MobilePreview.razor) validates AI JSON.
- **Safe because:** nothing calls it yet; the planner only *produces* a request that the proven Phase-1
  service consumes. AI can never write files directly — it only proposes a plan a human approves.
- **Done when:** a unit test feeds 5 prompts and gets valid `ProjectPlan`s mapping to runnable requests.

### Phase 5 — `SmartCreateDialog` in parallel, behind a flag
- **Do:** new file `Components/Shared/SmartCreateDialog.razor` with two modes in one place:
  - **Prompt mode (default):** one textarea + Generate → `IProjectPlanner` → editable plan preview
    (chips for stack/folders/ports the user can tweak — the "User Review Required" gate) → Create via
    the **same `IProjectCreationService`**.
  - **Manual mode:** embeds/links the existing stepper for users who want full control.
  - Gate which dialog opens behind a flag (`SettingsPanel` toggle or `appsettings`):
    `SmartCreateEnabled` → open `SmartCreateDialog`, else `CreateProjectDialog`. **Default off** until proven.
- **Safe because:** old dialog still exists and is the default; the flag flips one line at each of the
  two call sites; both share the Phase-1 service so generation can't diverge.
- **Done when:** with the flag on, a prompt produces the same on-disk result as doing it manually.

### Phase 6 — Contract (only after Smart is trusted)
- **Do:** make `SmartCreateDialog` the default; fold the classic stepper in as its "Manual" tab; remove
  the separate `CreateProjectDialog` entry once both call sites point at Smart. Keep the old `.razor`
  one release as a fallback before deleting.
- **Safe because:** by now Smart has shipped behind a flag and been dogfooded; this is just changing the
  default and deleting a now-unused file.

> **Connector Engine (`connector.md`) attaches at Phase 5+** as a third `CreationKind.Connected` — it
> needs the planner + seam to already exist, which is exactly why this refactor comes first.

---

## 6. What "AI is informed" means (the PlannerContext)

The planner must never guess in a vacuum. It receives:

| Signal | Source (already in repo) | Why |
|---|---|---|
| Installed toolchains (Node/Python/Java/Flutter/.NET) | `EnvironmentManager.CheckAllEnvironments()` | Don't plan Flutter if it's not installed → offer install (reuse `FlutterSetupView`) instead of failing |
| Available stacks + architectures | `ProjectTemplates/*.md` front-matter (`architectures`, `dependencies`, `defaultPort`, `provides`/`needs`) | Plan only real, runnable stacks; pull ports + deps from templates, not hallucination |
| Existing projects / ports in use | `ProjectService.GetProjects()` + group `projects.json` | Avoid port collisions (fixes the *spirit* of B5) and name clashes |
| Output contract | strict JSON schema, validated like `MobilePreview.ValidateAiResponse` | Reject malformed plans before they reach the file system |

Output `ProjectPlan` example for *"flutter app + node backend + postgres"*:
```json
{
  "kind": "group",
  "name": "myapp",
  "summary": "Flutter mobile client, Express API, Postgres — wired with a shared API base URL.",
  "members": [
    { "stack": "Flutter",       "role": "frontend", "folder": "mobile",  "port": null },
    { "stack": "Express/Node",  "role": "api",      "folder": "server",  "port": 5000, "architecture": "ntier" },
    { "stack": "Postgres",      "role": "database", "folder": "db",       "port": 5432 }
  ],
  "envNeeds": ["DATABASE_URL", "API_BASE_URL"]
}
```
The user sees this as editable chips, fixes anything, hits Create → Phase-1 service runs it.

---

## 7. Guardrails checklist (apply to every PR in this effort)

- [ ] Old `CreateProjectDialog` still opens and creates a project (manual smoke test) — every phase.
- [ ] No phase both *moves* code and *changes* its behavior.
- [ ] Registry writes are **append/merge**, never overwrite, before any flag goes on (B3 is a data-loss bug).
- [ ] Every native-CLI invocation has a `CancellationToken` + timeout before Smart mode ships (B10).
- [ ] AI output is schema-validated and human-approved before any file write — no autonomous scaffolding.
- [ ] Feature flag defaults **off**; flipping it is a one-line change at 2 call sites and instantly reversible.

---

## 8. File-level work map

```
Services/projectgenerator/
├── IProjectCreationService.cs          [P1] new seam contract
├── DefaultProjectCreationService.cs    [P1] extracted-as-is orchestration
├── Models/ProjectCreationRequest.cs    [P1]
├── Models/ProjectCreationResult.cs     [P1]
├── ProjectGenerator.cs                 [P3] bug fixes only (B1–B12), one per commit
├── IProjectPlanner.cs                  [P4] AI intent → plan
├── ProjectPlanner.cs                   [P4] ILLMProvider + PlannerContext
└── Models/ProjectPlan.cs               [P4]

Components/Shared/
├── CreateProjectDialog.razor           [P1] Submit() now calls the service (else unchanged)
└── SmartCreateDialog.razor             [P5] new 1-prompt UI, both modes, behind flag

MauiProgram.cs                          [P1,P4] register the two new services
Components/IDE/SettingsPanel.razor      [P5] SmartCreateEnabled toggle
tests/ (or a console harness)           [P2] characterization of current output
```

---

## 9. Open questions before Phase 1

1. **Flag home** — a `SettingsPanel` toggle (user-flippable, good for dogfooding) or a build/`appsettings`
   constant (safer, devs only)? Recommendation: SettingsPanel, default off.
2. **Phase 3 scope** — fix all of B1–B12 before Smart, or only the data-loss/hang ones (B3, B10) and defer
   cosmetic ones? Recommendation: B3 + B10 are blockers; the rest can land alongside Phase 5.
3. **Planner provider** — reuse Groq via `AIClient`/`ILLMProvider` (already wired in `MobilePreview`), correct?
