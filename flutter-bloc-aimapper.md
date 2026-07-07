# Flutter Create→Run, BLoC Architecture & the Parallel FE/BE AI Mapper

> Analysis of the existing **Flutter mobile app create→run** flow, the gap that it has **no AST-engine
> knowledge** and doesn't follow **BLoC architecture**, the **BLoC↔MAUI** architecture mapping, and a
> design for the **parallel frontend + backend AI mapper**.
>
> Grounded in: `Services/FlutterService.cs`, `Services/SyncroCLI/Providers/FlutterProvider.cs`,
> `Components/Shared/MobilePreview.razor` + `MobileRunSelector.razor`, the Dart codegen toolchain
> under `Services/tool/` ("thunder"), and the C# AST engine `Services/AST/*`.
> Sister docs: [`engine/optimisation server cli.md`](engine/optimisation%20server%20cli.md) (parallel
> runner), [`mappertodo.md`](mappertodo.md) (NLP router), [`ast.md`](ast.md).

---

## 0. The big finding: two disconnected worlds

Syncro currently has **two independent Flutter pipelines that don't know about each other**, plus a
C# AST engine that can't read either:

| World | What it is | Produces | Knows BLoC? | Wired to AST? |
|---|---|---|---|---|
| **A. Create→Run** | `FlutterService` + `FlutterProvider` + `MobilePreview.razor` | **vanilla** `flutter create` app | ❌ no | ❌ no |
| **B. "thunder"** | Dart codegen under `Services/tool/` (IR, compiler, generators) | **BLoC-structured** Flutter from a config/`AppModel` | ✅ yes | ❌ no |
| **C. AST engine** | `Services/AST/*` (C#, JS, TS, Python, Config parsers) | symbol/dep maps | — | **no Dart parser** |

So: the **window the user runs** (World A) makes a plain app; the tool that **does** BLoC (World B)
isn't connected to it or to the AI; and the **AST engine** (World C) is blind to Dart entirely.
That's exactly the "creates/runs Flutter but has no AST knowledge / doesn't follow BLoC" problem.

---

## 1. World A — the create→run flow (what exists)

### 1.1 Create
- `FlutterService.CreateProject` → `flutter create --org com.syncro {name}` ([FlutterService.cs:22](Services/FlutterService.cs:22)).
- `FlutterProvider.Create` (CLI path) → `flutter create --org com.syncro --project-name {sanitized} {name}` ([FlutterProvider.cs:24](Services/SyncroCLI/Providers/FlutterProvider.cs:24)).
- **Both produce the stock Flutter counter app** — `lib/main.dart`, no `features/`, no BLoC, no
  repository/service layers.

### 1.2 Run (the "run window")
`FlutterService.RunProject(path, mode)` ([FlutterService.cs:38](Services/FlutterService.cs:38)):
- `web` → `flutter run -d web-server --web-port=5000` → `http://localhost:5000`
- `adb`/`emulator` → `flutter run -d android` / `flutter run`
- Output streamed via `OnOutput`; **hot reload** (`r`) / **hot restart** (`R`) over stdin
  ([FlutterService.cs:84,93](Services/FlutterService.cs:84)).
- `SmartReload` ([FlutterService.cs:102](Services/FlutterService.cs:102)) already inspects changed
  files and **restarts when `bloc`/`state`/`provider`/`main`/`routes` change** — i.e. it *assumes*
  a BLoC layout that the create step never generates. **Mismatch.**

### 1.3 UI
- `MobilePreview.razor` ([Components/Shared/MobilePreview.razor](Components/Shared/MobilePreview.razor))
  — MudDialog "Syncro Quick Preview": BUILDING/LIVE chips, **hot-reload/restart** buttons, an **AI
  assistant** toggle (injects `ILLMProvider`, `ContextBuilder`, `ISessionManager`, `FlutterService`).
- `MobileRunSelector.razor` — picks the run target (web/emulator/device).

### 1.4 Gaps in World A
| # | Gap | Effect |
|---|---|---|
| F1 | Create makes a **vanilla** app, no BLoC/feature structure | `SmartReload`'s bloc/state heuristics never apply; no scalable arch |
| F2 | Run uses **raw `Process`/`cmd.exe`**, not CliWrap/`ICliRunner` | inconsistent with the rest of the engine; deadlock/cancel risk |
| F3 | `taskkill /F /IM flutter.exe /T` kills **all** Flutter processes | kills *other* running apps; not scoped to this run |
| F4 | No device discovery (`flutter devices`) | run modes are guessed, not enumerated |
| F5 | AI assistant edits files but **AST engine can't parse Dart** | "AI-native" preview has no structural grounding for Flutter |

---

## 2. World B — "thunder" already does BLoC (but is orphaned)

`Services/tool/thunder.dart` is a real Dart codegen CLI (`generate`/`expand`/`apply`/`reset`) with
its **own IR + compiler + dependency graph + expansion engine** (`core/ir.dart`, `compiler/*`,
`expansion/expansion_engine.dart`). Its `MainGenerator`
([Services/tool/generators/main_generator.dart](Services/tool/generators/main_generator.dart)) emits a
**clean BLoC architecture**:

```
lib/
├── main.dart                       # MultiRepositoryProvider + MultiBlocProvider wiring
├── core/  theme/ navigation/ services/ (api_service, db_service) routes/ ui/ (ui_factory)
└── features/<feature>/
        ├── bloc/<feature>_cubit.dart
        ├── repository/<feature>_repository.dart
        └── (ui/ …)
```

It imports `flutter_bloc`, builds repository + cubit providers per feature from an `AppModel`. **This
is the BLoC generator the user wants — it already exists.** The problem is it's **not wired** to:
- the create→run window (World A still calls vanilla `flutter create`),
- the AI mapper (nothing translates a natural-language request into thunder's `AppModel`/config),
- the AST engine (the generated Dart is never indexed).

> **Key insight:** we don't need to *build* a BLoC generator — we need to **promote thunder to the
> default Flutter path** and **connect it to the AI + AST**.

---

## 3. World C — the AST engine has no Dart/BLoC knowledge

`Services/AST/Parsers/` ships `CSharpAstParser`, `JavaScriptAstParser`, `TypeScriptAstParser`,
`PythonAstParser`, `ConfigFileParser` — **no `DartAstParser`**. So:
- The AST engine can't map a Flutter project's classes, widgets, cubits, repositories, or routes.
- `FrameworkDetector` recognizes Flutter as a *project type* but the engine can't read its *contents*.
- The AI assistant in `MobilePreview` has **no structural model** of the Flutter app to ground edits.

### 3.1 What "AST engine knowledge + BLoC + MAUI arch" means concretely
1. **Add a Dart parser** (`DartAstParser`) producing `AstNode`s for: classes, widgets, **Cubit/Bloc**,
   **Repository/Service**, routes, DI providers — i.e. BLoC-aware node types.
2. **Teach the AST engine the BLoC *pattern*** (not just syntax): a feature = {Cubit, State,
   Repository, UI}; a violation = a widget calling a repository directly (skipping the cubit). This
   makes the engine able to *audit* and *extend* BLoC apps.
3. **Mirror it against MAUI architecture** (§4) so the same pattern-vocabulary spans both stacks.

---

## 4. BLoC ↔ MAUI architecture mapping (the shared vocabulary)

The user wants Flutter BLoC to mirror MAUI's structure. They're the same MVVM-ish layering with
different names — encode this mapping once so the AST engine, the generator, and the AI mapper all
speak it:

| Layer | Flutter (BLoC) | .NET MAUI / Blazor | Role |
|---|---|---|---|
| View | Widget / Page | `.razor` / `ContentPage` (XAML) | render + dispatch intents |
| State holder | **Cubit / Bloc** + State | **ViewModel** (`INotifyPropertyChanged`) / scoped service | UI logic, emits state |
| Domain | UseCase / Repository iface | Service interface | business rules |
| Data | Repository impl + API/DB service | Repository / `HttpClient` / DbContext | I/O |
| DI | `MultiRepositoryProvider`/`MultiBlocProvider` | `MauiProgram` DI container | wiring |
| Routing | `app_routes.dart` / NavigationService | `Routes.razor` / `Shell` | navigation |

A `core/architecture-map.json` (consumed by AST engine + mapper) makes "feature", "state holder",
"repository", "route" **stack-agnostic** — so one AI intent can target Flutter *or* MAUI.

---

## 5. The parallel frontend + backend AI mapper

The ask: a mapper that, from one natural-language request, produces **frontend (Flutter/BLoC) and
backend (API) in parallel**, wired together. This composes three things that already exist:
`TaskMapper` (NLP→intent, see [`mappertodo.md`](mappertodo.md)), thunder (FE BLoC codegen), and the
`ProjectStackRunner` pattern (parallel waves, see
[`engine/optimisation server cli.md §3`](engine/optimisation%20server%20cli.md)).

### 5.1 Shape

```
 NL: "add a wishlist feature with persistence"
        │
        ▼
 ┌──────────────────────────  AiFeatureMapper  ─────────────────────────┐
 │  TaskMapper → intent=generate, feature=wishlist, target=Wishlist      │
 │  splits into a DUAL spec:                                             │
 │     FrontendSpec { feature:'wishlist', state:cubit, repo:WishlistRepo,│
 │                    screens:[list,detail], apiBase:'/api/wishlist' }   │
 │     BackendSpec  { resource:'wishlist', endpoints:[GET,POST,DELETE],  │
 │                    model:Wishlist, store:db }                         │
 └──────────────────────────────────────────────────────────────────────┘
        │ parallel (one wave, both run at once — bounded fan-out)
        ├───────────────► FE generator (thunder): features/wishlist/{bloc,repository,ui}
        └───────────────► BE generator: controller + model + route + migration
        │  then WIRE: FE WishlistRepository.baseUrl ← BE route; BE CORS ← FE origin
        ▼
   contract check: FE repo calls ↔ BE endpoints match (shared OpenAPI/types)
```

### 5.2 Components to add
| Component | Role | Builds on |
|---|---|---|
| `AiFeatureMapper` | NL → `{FrontendSpec, BackendSpec}` dual intent | `TaskMapper` (`mappertodo.md`) |
| `FrontendSpec`/`BackendSpec` | typed contracts for each side | new |
| `IFeatureGenerator` (FE=thunder, BE=api) | generate per side behind one interface | thunder + a BE generator |
| Parallel runner | run FE+BE concurrently, then **wire** + rollback on partial fail | `ProjectStackRunner` |
| `ContractLinker` | FE repo base URL ↔ BE route; shared DTOs; CORS | new (closes the "no inter-service wiring" gap, bug B6) |
| AST verify | parse FE (DartAstParser) + BE (CSharp/TS parser) → confirm the cubit calls the repo and the repo hits the real endpoint | §3 + existing parsers |

### 5.3 Why "parallel" matters
FE and BE are independent generations with a single join at the end (the contract). Running them in
one parallel wave (bounded by cores, like the stack runner) halves wall-clock vs sequential, and the
**ContractLinker** is the barrier that guarantees they actually connect — not two unrelated folders.

---

## 6. Phased plan

| Phase | Work | Outcome |
|---|---|---|
| **FB0** | Wire **thunder as the default Flutter create path** (replace vanilla `flutter create` in World A with thunder `generate`); fall back to `flutter create` if thunder unavailable | new Flutter apps are BLoC-structured |
| **FB1** | Harden run flow: CliWrap/`ICliRunner` + `CancellationToken` (F2); scope process kill to the run (F3); `flutter devices` discovery (F4) | reliable run window |
| **FB2** | **`DartAstParser`** + BLoC node types; teach AST engine the BLoC pattern (§3) | AST-aware Flutter; AI edits grounded |
| **FB3** | `core/architecture-map.json` BLoC↔MAUI vocabulary (§4) | one pattern model across stacks |
| **FB4** | `AiFeatureMapper` (dual spec) + `ContractLinker` + parallel FE/BE runner (§5) | one prompt → wired FE+BE feature |
| **FB5** | AST contract verify (FE↔BE) + surface in Problems/Hindsight | generated features are validated, not assumed |

**FB0 is the highest-leverage:** the BLoC generator already exists (thunder) — pointing the create→run
window at it (instead of vanilla `flutter create`) immediately makes the user's "follow BLoC arch"
real, and makes `SmartReload`'s existing bloc/state heuristics finally correct.

---

## 7. Acceptance criteria
- [ ] New Flutter project from the run window has `features/<f>/{bloc,repository,ui}` + `core/` (BLoC), not the stock counter app.
- [ ] Run uses CliWrap with cancellation; stopping kills only this run's process tree.
- [ ] AST engine parses Dart and reports BLoC features/violations (a widget calling a repo directly is flagged).
- [ ] One NL request generates **both** a Flutter BLoC feature and a backend resource, **wired** (FE repo base URL = BE route), verified by AST.
- [ ] FE and BE generate in parallel (one wave), with rollback if either side fails.
- [ ] The BLoC↔MAUI map lets the same intent target either stack.
