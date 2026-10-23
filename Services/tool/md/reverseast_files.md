# ⚡🔁 Reverse AST — File Addition Plan

> Concrete file manifest for building the Reverse AST engine described in [`reverseast.md`](./reverseast.md).
> **Strategy:** reuse the forward engine's *data model* (AST, IR, writer, graph, logger) verbatim;
> write everything in the *reverse direction* (parse, lift, extract, emit) as new files under a new
> `Services/tool/reverse/` subtree. Mostly new code — thin, surgical edits to the existing engine.

---

## A. Reuse — existing engine files imported as-is (0 changes)

These are the forward engine's stable cores. Reverse AST **imports** them; it does not modify them.

| Reused file | What Reverse AST uses it for |
|---|---|
| `core/ast.dart` | **Lift target.** All `WidgetNode` subclasses (`ScaffoldNode`, `ColumnNode`, `TextNode`, `ButtonNode`, `TextFieldNode`, `ListViewNode`, `AppBarNode`, `RawNode`, …). NodeLifter builds these from Dart source. |
| `core/ir.dart` | **Same IR.** `ProjectIR`, `FeatureIR{screens, widgetASTs, bloc, repository}`, `ScreenIR{name, route, type, root}`, `ServiceIR{name, type, config}`, `ThemeIR`. ReverseIRBuilder populates these from code. |
| `core/file_writer.dart` | `FileWriter.write(path, content)` / `.clean(dir)` — write `.thunder/reverse/*.json`. |
| `core/dependency_graph.dart` | `DependencyGraph` — reused to hold the reconstructed screen→service→endpoint→server graph. |
| `utils/logger.dart` | `Logger.info/success/error` — consistent CLI logging. |
| `utils/string_utils.dart` | Name canonicalization (snake/camel/Pascal) for stable IDs. |
| `utils/cache_manager.dart` | `CacheManager` — incremental re-scan (skip unchanged files by hash). |
| `core/models.dart` | `AppModel` shape — the **emit contract** for `--emit-config` round-trip. |

**Reference-only (read for the inverse mapping, not imported):**
`generators/model_generator.dart`, `generators/api_generator.dart`, `generators/route_generator.dart`,
`core/config_parser.dart` — each forward generator/parser defines the exact JSON shape its reverse pass
must reproduce.

---

## B. Edit — existing files touched (small, additive)

| File | Change | Size |
|---|---|---|
| `thunder.dart` | Add command cases: `scan`, `rediff`, `reverse-graph`; add help lines. Delegates to `reverse/reverse_engine.dart`. | ~15 lines |
| `pubspec.yaml` | Add dependency **`analyzer: ^6.0.0`** (Dart AST + element model). Only new dep. | ~1 line |
| `reverse.dart` *(new top-level, mirrors `generate.dart`/`apply.dart`/`reset.dart`)* | `Future<void> scanApp(...)` entry that `thunder.dart` calls. | new |

---

## C. New files — the `reverse/` subtree (the bulk of the work)

Placed under `Services/tool/reverse/`, mirroring the forward layout (`core/`, `generators/`).

### C1. Orchestration
| New file | Purpose | Reuses |
|---|---|---|
| `reverse/reverse_engine.dart` | Top orchestrator for `thunder scan`: walk files → parse → run passes → assemble IR → write JSON → report. | Logger, FileWriter, CacheManager |
| `reverse/reverse_config.dart` | Scan options (path, `--connector`, `--emit-config`, `--verify`), output dir resolution. | — |

### C2. Parsing layer
| New file | Purpose | Reuses |
|---|---|---|
| `reverse/parser/dart_source_parser.dart` | Wrap `package:analyzer` `AnalysisContextCollection` → per-file `ResolvedUnitResult`; regex fallback when unresolved. | analyzer (new dep) |
| `reverse/parser/node_lifter.dart` | Visit Flutter `build()` bodies → thunder `WidgetNode` tree. Unknown widgets → `RawNode`. | **core/ast.dart** |
| `reverse/parser/provenance.dart` | `Provenance{file,line,column,confidence}` record attached to every extracted item. | — |
| `reverse/parser/dart_visitors.dart` | Shared `RecursiveAstVisitor` helpers (class/method/invocation/field scanners). | analyzer |

### C3. Extraction passes (one file → one JSON, per [`reverseast.md` §3](./reverseast.md))
| New file | Emits | Reuses |
|---|---|---|
| `reverse/passes/pass.dart` | `IReversePass` interface + `PassResult`. | — |
| `reverse/passes/screens_pass.dart` | `screens.json` (screen objects, route, type) | node_lifter, ScreenIR |
| `reverse/passes/widgets_pass.dart` | `widgets.json` (per-screen WidgetNode tree) | node_lifter, core/ast.dart |
| `reverse/passes/layout_pass.dart` | `layout.json` (geometry/flex/padding) | core/ast.dart |
| `reverse/passes/models_pass.dart` | `model.json` (data classes + fields) | string_utils |
| `reverse/passes/dto_pass.dart` | `dto.json` (models bound to API decode sites) | models_pass, api_pass |
| `reverse/passes/routes_pass.dart` | `nav.json` (route table + transitions) | — |
| `reverse/passes/services_pass.dart` | `service.json` (API-client classes/methods) | — |
| `reverse/passes/repository_pass.dart` | `repository.json` (repo layer over services) | services_pass |
| `reverse/passes/api_pass.dart` | `connector/api.json` (endpoint call sites, verb, path template) | call_graph_resolver |
| `reverse/passes/server_pass.dart` | `connector/server.json` (base URL, stack guess, auth) | api_pass |
| `reverse/passes/state_pass.dart` | `bloc.json` (Bloc/Cubit/Provider events→states) | — |
| `reverse/passes/datasource_pass.dart` | `datasource.json` + `db.json` (Hive/sqflite/prefs/remote) | — |
| `reverse/passes/theme_pass.dart` | `theme.json` (ThemeData/ColorScheme/TextTheme tokens) | ThemeIR |

### C4. Connector model + call-graph
| New file | Purpose | Reuses |
|---|---|---|
| `reverse/connector/api_model.dart` | Dart DTOs (`ApiCallSite`, `ServerInfo`, `EndpointDto`) whose JSON aligns to C# `Services/Connector/Models/FrontendContract.cs` / `ApiCallSite`. | — |
| `reverse/connector/call_graph_resolver.dart` | Resolve each HTTP call *up* the AST → enclosing service+method → server group → callers (reverse call graph). Canonicalize `'/users/$id'`→`/users/{id}`. | dart_visitors |
| `reverse/connector/server_detector.dart` | Ranked base-URL inference (`BaseOptions` → env/const → retrofit → prefix clustering); stack guess. | — |

### C5. IR assembly + emit
| New file | Purpose | Reuses |
|---|---|---|
| `reverse/ir/reverse_ir_builder.dart` | Fold all pass results into a populated `ProjectIR`. | **core/ir.dart**, core/ast.dart |
| `reverse/ir/config_emitter.dart` | `ProjectIR` → forward-compatible `custom_config/*.json` (inverse of `config_parser.dart`). Enables round-trip. | core/models.dart, FileWriter |
| `reverse/ir/master_ir_assembler.dart` | Consolidate passes → `master_ir.json` (orchestration + implementation per `master_architecture.md`). | core/ir.dart |

### C6. Output + reporting
| New file | Purpose | Reuses |
|---|---|---|
| `reverse/output/json_writer.dart` | Uniform JSON emit (indent, `.thunder/reverse/` layout, `connector/` subfolder). | FileWriter |
| `reverse/output/scan_report.dart` | `scan_report.json`: per-pass counts, confidence histogram, unresolved sites, timing. | Logger |

### C7. Verification (round-trip oracle)
| New file | Purpose | Reuses |
|---|---|---|
| `reverse/verify/round_trip_verifier.dart` | `scan → config_emitter → generateApp() → lib_gen'`, structural-diff vs original `lib/`; report lossy passes. | compiler.dart (`generateApp`), config_emitter |
| `reverse/verify/structural_differ.dart` | AST-level diff between two Flutter trees. | core/ast.dart |

### C8. Fixtures + tests
| New path | Purpose |
|---|---|
| `test/fixtures/sample_app/` | Minimal Flutter app (3 screens, 1 service, 2 models, dio client) — the round-trip target. |
| `test/reverse/*_test.dart` | Per-pass unit tests against fixtures. |

---

## D. New-file tree (at a glance)

```
Services/tool/
  reverse.dart                     ← (edit-adjacent) scanApp() entry
  reverse/
    reverse_engine.dart
    reverse_config.dart
    parser/
      dart_source_parser.dart
      node_lifter.dart
      dart_visitors.dart
      provenance.dart
    passes/
      pass.dart
      screens_pass.dart   widgets_pass.dart   layout_pass.dart
      models_pass.dart    dto_pass.dart        routes_pass.dart
      services_pass.dart  repository_pass.dart
      api_pass.dart       server_pass.dart
      state_pass.dart     datasource_pass.dart theme_pass.dart
    connector/
      api_model.dart      call_graph_resolver.dart   server_detector.dart
    ir/
      reverse_ir_builder.dart  config_emitter.dart  master_ir_assembler.dart
    output/
      json_writer.dart    scan_report.dart
    verify/
      round_trip_verifier.dart  structural_differ.dart
  test/
    fixtures/sample_app/…
    reverse/…_test.dart
```

**Tally:** ~28 new files + 3 tiny edits (`thunder.dart`, `pubspec.yaml`, new `reverse.dart`).
8 existing engine files reused unchanged; 4 read as inverse-mapping references.

---

## E. Build order (maps to `reverseast.md` §8 phases)

| Phase | New files landed | Verifies |
|---|---|---|
| **R0 Skeleton** | `pubspec.yaml` edit, `reverse.dart`, `reverse_engine.dart`, `reverse_config.dart`, `pass.dart`, `output/*`, `thunder.dart` edit | `thunder scan` runs, writes empty JSONs + report |
| **R1 Structure** | `dart_source_parser.dart`, `dart_visitors.dart`, `provenance.dart`, `screens_pass`, `models_pass`, `routes_pass` | screens/models/routes correct on fixture |
| **R2 Widget lift** | `node_lifter.dart`, `widgets_pass`, `layout_pass` | lifted tree matches source (`thunder ast`) |
| **R3 Connector** | `connector/*`, `api_pass`, `server_pass`, `services_pass`, `repository_pass`, `dto_pass` | every call → service+server; base URL detected |
| **R4 State/theme/data** | `state_pass`, `theme_pass`, `datasource_pass` | bloc graph + theme tokens extracted |
| **R5 IR + round-trip** | `ir/*`, `verify/*` | round-trip structural match on fixture |
| **R6 Integrations** | (C# side) FrontendContract ingest of `api.json`/`server.json`; Universe graph emit | connector matcher + graph edges |

Each phase compiles and runs independently; no phase blocks `thunder`'s existing forward commands.

---

## F. Guardrails

- **No forward regressions:** nothing in `reverse/` is imported by the forward path; `thunder generate`
  is untouched. Only `thunder.dart`'s command switch gains cases.
- **Read-only default:** `scan` never writes to `/lib`; `--emit-config` is the only writer and honors
  `ModeManager` locks.
- **One new dependency** (`analyzer`) — everything else is stdlib + reused engine code.
- **Provenance on every record** — each extracted item is auditable to `file:line` with a confidence tag.
