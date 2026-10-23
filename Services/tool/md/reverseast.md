# ⚡🔁 Reverse AST — Thunder Reverse-Engineering Engine

> Design + implementation plan for extending the **Thunder** tool (`Services/tool/`) to run its
> pipeline **backwards**: from an existing Flutter app's source → structured architecture JSON.
> Companion to [`master_architecture.md`](./master_architecture.md).

---

## 0. The core idea — Thunder is symmetric

Thunder today is a **forward compiler**:

```
config/custom_config/*.json  ──ConfigParser──▶  AppModel
        ──IRBuilder──▶  ProjectIR (features▸screens▸WidgetNode, services, theme)
        ──PhaseRegistry──▶  Flutter code (lib_gen/)
```

**Reverse AST** runs the identical pipeline in the opposite direction:

```
Flutter source (lib/**/*.dart)
        ──DartParser (analyzer)──▶  Dart AST
        ──NodeLifter──▶  Thunder WidgetNode tree (reuse core/ast.dart)
        ──ReverseIRBuilder──▶  ProjectIR  (same IR, populated from code)
        ──ConfigEmitter──▶  config JSON  (the SAME schema Thunder forward-consumes)
        ──ConnectorEmitter──▶  connector/*.json  (services · api · server · dto · routes · models)
```

The decisive property: **the forward config schema _is_ the reverse extraction target.**
Every file Thunder reads to *build* an app is a file Reverse AST *emits* from an app. This gives
a free correctness oracle — **round-trip**: `scan(app) → generate() → app'` should be
structurally equivalent to `app`.

> Reverse AST is the deep, AST-grounded successor to the shallow regex extraction already in the
> C# connector (`Services/Connector/Frontend/FrontendApiExtractors.cs`). Regex finds `dio.get('/x')`.
> Reverse AST understands *which screen* made the call, *which DTO* it deserializes into, and
> *which server* it targets.

---

## 1. CLI surface (new commands on `thunder.dart`)

```
thunder scan [path]        → Reverse-engineer a Flutter app at [path] (default: cwd) into
                             .thunder/reverse/*.json  (separate file per concern)
thunder scan --emit-config → Also emit forward-compatible config/custom_config/*.json (round-trip)
thunder scan --connector   → Emit only the connector bundle (services/api/server/dto/routes)
thunder rediff             → Re-scan and diff against the previous scan (drift detection)
thunder reverse-graph      → Print the reconstructed dependency graph (screen→service→endpoint)
```

`scan` is **read-only** and mode-agnostic (never touches `/lib`; respects `ModeManager` locks only
for `--emit-config` which writes to `custom_config/`).

---

## 2. Parsing layer — real Dart AST, not regex

Use the official **`analyzer`** package (`package:analyzer/dart/analysis/*`) to get a resolved AST +
element model per `.dart` file. Regex is the fallback only when a file fails to resolve.

- `AnalysisContextCollection` over the app root → per-file `ResolvedUnitResult`.
- Visit with a `RecursiveAstVisitor`. Two visitor tiers:
  1. **Structure visitor** — `ClassDeclaration`, `MethodDeclaration`, `InstanceCreationExpression`,
     `MethodInvocation`, `FieldDeclaration`, top-level functions.
  2. **Widget visitor** — lift Flutter `build()` bodies into Thunder's existing `WidgetNode` tree
     (`core/ast.dart`: `ScaffoldNode`, `ColumnNode`, `RowNode`, `ContainerNode`, `TextNode`,
     `ButtonNode`, `TextFieldNode`, `ListViewNode`, `GridViewNode`, `AppBarNode`, `CardNode`, …).
     Unknown widgets → `RawNode` (preserve source span; never lose information).

Every extracted record carries **provenance**: `{ file, line, column, confidence }`. Confidence is
`resolved` (element model), `syntactic` (AST only), or `heuristic` (regex fallback).

---

## 3. Extraction passes → separate JSON (the deliverable)

Each pass is an independent visitor emitting **one JSON file** into `.thunder/reverse/`. Files mirror
the forward `custom_config/` names where a forward analogue exists, plus connector-only files.

| Pass | Output file | What it captures | Primary signals |
|---|---|---|---|
| **Screens / Pages** | `screens.json` | Screen objects: name, route, scaffold shape, feature | classes extending `StatelessWidget`/`StatefulWidget` used as route targets; `Scaffold` in `build()` |
| **Widget tree** | `widgets.json` | Per-screen `WidgetNode` tree (layout, components) | Widget visitor over `build()` |
| **Layout depth** | `layout.json` | Geometry: padding, sizes, flex, alignment | `Padding`, `SizedBox`, `Expanded`, `EdgeInsets`, `MainAxisAlignment` |
| **Models** | `model.json` | Plain data classes: fields + types | classes with final fields + `fromJson`/`toJson` |
| **DTOs** | `dto.json` | Request/response DTOs (subset of models bound to API) | models referenced by `fromJson` at a call's decode site |
| **Routes / Navigation** | `nav.json` | Route table + transitions | `MaterialApp.routes`, `GoRouter`, `Navigator.push*`, `onGenerateRoute` |
| **Services** | `service.json` | Service/API-client classes & their methods | classes holding `Dio`/`http.Client`; methods issuing calls |
| **Repositories** | `repository.json` | Repository layer over services | classes named `*Repository` delegating to services |
| **API calls** | `api.json` | Endpoint call sites: verb, path template, req/res DTO | `dio.get/post/...`, `http.*`, resolved to enclosing service+method |
| **Server detection** | `server.json` | Inferred backend(s): base URL(s), auth, stack guess | `BaseOptions(baseUrl:)`, env/const base URLs, header patterns |
| **State management** | `bloc.json` | BLoC/Cubit/Provider/Riverpod graph, events→states | `Bloc`/`Cubit` subclasses, `emit`, `on<Event>` |
| **Data sources / DB** | `datasource.json`, `db.json` | Local stores (Hive/sqflite/shared_prefs), remote sources | package imports + client construction |
| **Theme** | `theme.json` | Colors, typography, tokens | `ThemeData`, `ColorScheme`, `TextTheme` |
| **Manifest** | `master_ir.json` | Consolidated orchestration+implementation IR (§`master_architecture.md`) | assembled from all passes |
| **Provenance index** | `sources.json` | Every record → `{file,line,confidence}` | cross-cut |

> The user's requested set — *services, api-linked-being-called, server detection, arch type, DTO,
> routes, pages, models, screen objects* — maps 1:1 onto
> `service.json · api.json · server.json · (screentype in screens.json) · dto.json · nav.json ·
> screens.json · model.json`.

---

## 4. The connector bundle — API + server detection

This is the heart of the "connector engine" ask. Output `connector/` sub-folder, consumed directly by
the existing C# `Services/Connector/` layer.

### 4.1 API call resolution (`api.json`)

For each HTTP call site, resolve **up** the AST to bind context the regex extractor can't:

```jsonc
{
  "endpoint": "/users/{id}/orders",     // path template (interpolations → {param})
  "method": "GET",
  "client": "dio",                       // dio | http | chopper | retrofit
  "server": "api_main",                  // FK → server.json (base URL group)
  "declaredIn": { "service": "OrderService", "method": "fetchOrders" },
  "calledFrom": ["OrdersScreen", "OrderRepository.load"],   // reverse call graph
  "request":  { "dto": "OrderQuery",  "fields": [...] },
  "response": { "dto": "OrderList",   "decodedBy": "OrderList.fromJson" },
  "auth": "bearer",                      // inferred from header injection
  "provenance": { "file": "lib/data/order_service.dart", "line": 42, "confidence": "resolved" }
}
```

Path templates: string interpolations (`'/users/$id'`, `'/users/${u.id}'`) canonicalize to
`/users/{id}` — matching the connector's `PathTemplate` convention and its `ApiCallSite` model.

### 4.2 Server detection (`server.json`)

Infer the backend target(s) — the key "server detection" requirement — from multiple signals, ranked:

1. **`BaseOptions(baseUrl: ...)`** on Dio construction (strongest).
2. **Const / env base URLs**: `const kBaseUrl = 'https://api.x.com'`, `String.fromEnvironment`,
   `dotenv.get('API_URL')`, `--dart-define` keys.
3. **Retrofit `@RestApi(baseUrl:)`** / Chopper `baseUrl`.
4. **Clustering**: call sites sharing a client instance or a common path prefix are grouped into one
   logical server even when the base URL is dynamic.

```jsonc
{
  "servers": [{
    "id": "api_main",
    "baseUrl": "https://api.example.com/v1",   // or null when dynamic
    "source": "BaseOptions",                    // provenance of the guess
    "stackGuess": "rest",                        // rest | graphql | grpc | firebase | supabase
    "authScheme": "bearer",
    "endpointsCount": 17,
    "confidence": "resolved"
  }],
  "unresolvedBaseUrls": [ /* call sites with no attributable server */ ]
}
```

Stack guess heuristics: `graphql` (query/mutation strings, `graphql_flutter`), `firebase`
(`cloud_firestore`/`firebase_*`), `supabase` (`supabase_flutter`), else `rest`.

---

## 5. Reverse IR + round-trip validation

- `ReverseIRBuilder` populates the **same `ProjectIR`** (`core/ir.dart`) the forward path uses —
  `features ▸ screens ▸ WidgetNode`, `services`, `theme`. Reusing the IR guarantees the emitted config
  is forward-compilable.
- **Round-trip oracle** (`thunder scan --verify`): `scan → ConfigEmitter → generateApp() → lib_gen'`,
  then structurally diff `lib_gen'` against original `lib/`. Divergences are reported as
  *lossy-extraction* warnings with the responsible pass — this is the engine's self-test and the
  quality metric for each pass.

---

## 6. Integration points

- **C# Connector** (`Services/Connector/`): `api.json` + `server.json` deserialize into
  `FrontendContract` / `ApiCallSite` (richer than today's regex output), then flow through the existing
  `ContractMatcher` to pair against backend routes → connection edges. Reverse AST becomes the
  high-fidelity replacement for `FrontendApiExtractors.Flutter()`.
- **Universe graph** (`Services/Engine/.../Universe*`): emit nodes (`Screen`, `Service`, `Endpoint`,
  `Server`, `Dto`) and edges (`screen→service`, `service→endpoint`, `endpoint→server`,
  `endpoint→dto`) so the workspace graph gains a full frontend→backend call map.
- **Agent host** (the Groq engine): `master_ir.json` + `api.json` become grounding context injected
  into the agent prompt, so edits are architecture-aware (fixes the "prompt N forgets the arch" gap).

---

## 7. Directory layout produced by `thunder scan`

```
.thunder/reverse/
  screens.json      widgets.json    layout.json
  model.json        dto.json        theme.json
  nav.json          bloc.json       datasource.json   db.json
  service.json      repository.json
  connector/
    api.json        server.json
  master_ir.json    sources.json    scan_report.json
```

`scan_report.json`: counts per pass, confidence histogram, unresolved sites, round-trip result,
elapsed time — the at-a-glance health of the extraction.

---

## 8. Phased implementation plan

| Phase | Deliverable | Exit criterion |
|---|---|---|
| **R0 — Skeleton** | `thunder scan` command + `analyzer` dep + file walker + empty-pass framework writing `.thunder/reverse/` | `scan` runs on a sample app, emits stub JSONs + `scan_report.json` |
| **R1 — Structure** | Screens, models, routes passes (`screens.json`, `model.json`, `nav.json`) | Correctly lists every screen + route on a 3-screen sample |
| **R2 — Widget lift** | NodeLifter → `widgets.json` + `layout.json` reusing `core/ast.dart` | `thunder ast` renders a lifted tree that matches the source |
| **R3 — Connector** | `service.json`, `api.json`, `server.json`, `dto.json` + call graph | Every `dio/http` call resolved to service+server; base URL detected |
| **R4 — State/theme/data** | `bloc.json`, `theme.json`, `datasource.json`, `db.json` | BLoC event→state graph + theme tokens extracted |
| **R5 — IR + round-trip** | `master_ir.json` + `--verify` oracle | Round-trip structural match ≥ target on the sample app |
| **R6 — Integrations** | C# `FrontendContract` ingest + Universe graph emit | Connector matcher consumes `api.json`; graph shows screen→server edges |

Each phase is independently shippable and testable against a fixture Flutter app checked into
`Services/tool/test/fixtures/`.

---

## 9. Non-goals & limitations (be honest)

- **Not a semantic verifier** — extraction is structural; it reports what the code *says*, not whether
  the API contract is *correct*.
- **Dynamic base URLs** stay `null` with provenance rather than being guessed wrongly.
- **Reflection/codegen'd clients** (retrofit `.g.dart`) are read from generated output when present,
  flagged `heuristic` otherwise.
- **Round-trip is structural**, not byte-exact — hand-written business logic in `RawNode` bodies is
  preserved verbatim but not re-synthesized.

---

## 10. Why this shape

Reusing Thunder's forward IR + config schema (rather than inventing a parallel model) means the reverse
engine is validated *by construction* — anything it emits, Thunder can compile back. The separate-JSON
layout keeps each architectural concern independently consumable (the connector only needs
`api.json`+`server.json`; the agent only needs `master_ir.json`), and the provenance index makes every
claim auditable back to a source line. Reverse AST turns an opaque Flutter app into the same structured
truth Thunder uses to build one — closing the loop.
