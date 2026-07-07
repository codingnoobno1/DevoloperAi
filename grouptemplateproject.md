# Group Template Project System — Master Plan

> A reusable, packable, architecture-aware project-template engine for Syncro.
> Templates are authored as **literate Markdown** (`fastapi.md`, `django.md`, `express.md`, …),
> compiled into **template packages**, optionally **zipped** (`.synctmpl`), discovered through a
> **JSON registry**, and **summoned** into single projects or multi-service **groups** — with a
> **choosable architecture** (Flat → N-Tier → Clean → Hexagonal → Vertical-Slice).
>
> Builds on `projectgenerator.md` (stack registry, group orchestration, wiring). This document
> is the deep-dive on the **template** layer specifically.

---

## 0. Goals

1. **One template, many architectures.** Pick FastAPI as *Flat*, *N-Tier*, or *Clean* at summon time.
2. **Literate authoring.** Each template lives in a single human-readable `.md` that *contains* its files.
3. **Packable with Syncro.** Templates ship embedded in the app, and/or as a content folder, and/or as importable `.synctmpl` zips.
4. **Extensible via JSON.** Add a template by dropping a `.md`/`.zip` and adding one registry line — no recompile.
5. **Summonable.** A single C# call instantiates a template (placeholders resolved) into a target dir.
6. **Composable into groups.** A group = several templates (frontend + backend + database), each with its own architecture, wired together.
7. **Standard C#.** Plain services, DI-registered, no exotic dependencies (uses `System.IO.Compression` for zips, Newtonsoft for JSON).

---

## 1. The Literate Template Format (`<template>.md`)

A template `.md` is **both documentation and the source of truth** for the template's files.
It has three parts: **frontmatter manifest**, **shared files**, and **per-architecture files**.

### 1.1 Grammar

````text
---
<YAML manifest>            # see §1.2
---

# <Title>                  # free prose, ignored by the compiler

## Shared                  # files common to ALL architectures
### file: <relative/path>
```<lang>
<file contents, may contain {{Placeholders}}>
```
### file: <relative/path>
```<lang>
...
```

## Architecture: <styleId>   # styleId ∈ flat | ntier | clean | hexagonal | vertical
### file: <relative/path>
```<lang>
...
```
````

**Parsing rules** (implemented by `MarkdownTemplateParser`):
- `## Shared` → files added to every architecture.
- `## Architecture: <id>` → files added only when that architecture is summoned.
- `### file: <path>` followed by the next fenced code block ⇒ one `TemplateFile`.
  The `<path>` may itself contain placeholders (e.g. `src/{{PyPackage}}/main.py`).
- The code-fence language hint is metadata only (used for syntax highlighting / AST hints).
- A file path ending in `.bin.b64` is treated as **base64 binary** (icons, fonts).

### 1.2 Manifest (frontmatter) schema

```yaml
id: fastapi                 # unique, lowercase, canonical (kills the "Vite React" vs "React/Next/Vite" drift)
label: FastAPI
language: Python
kind: Backend               # Frontend | Backend | Database | Mobile | Fullstack
version: 1.0.0
packageManager: pip         # pip | npm | gradle | pub | dotnet
defaultPort: 8000
architectures: [flat, ntier, clean]   # which sections exist below
defaultArchitecture: flat
entry: main.py              # run entry (may differ per architecture → see entryByArchitecture)
entryByArchitecture:
  clean: app/api/main.py
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "MyApp" }
  - { key: Port,        prompt: "HTTP port",     default: 8000, type: int }
  - { key: PyPackage,   prompt: "Python package", default: "app" }
dependencies:               # informational + used to seed requirements/package files
  - fastapi>=0.110.0
  - uvicorn>=0.28.0
provides: [API_BASE_URL]    # wiring outputs (see projectgenerator.md §D)
needs: [DATABASE_URL]       # wiring inputs
postInstall: "pip install -r requirements.txt"
run: "uvicorn {entry} --reload --port {{Port}}"
tags: [api, python, async]
```

---

## 2. Template Package Model (C#)

`Services/projectgenerator/Templates/Model/`

```csharp
namespace Syncro.Desktop.Services.projectgenerator.Templates.Model;

public enum ArchitectureStyle { Flat, NTier, Clean, Hexagonal, VerticalSlice }

public class TemplateManifest
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Language { get; set; } = "";
    public string Kind { get; set; } = "";          // Frontend|Backend|Database|Mobile|Fullstack
    public string Version { get; set; } = "1.0.0";
    public string PackageManager { get; set; } = "";
    public int DefaultPort { get; set; }
    public List<ArchitectureStyle> Architectures { get; set; } = new();
    public ArchitectureStyle DefaultArchitecture { get; set; } = ArchitectureStyle.Flat;
    public string Entry { get; set; } = "";
    public Dictionary<ArchitectureStyle,string> EntryByArchitecture { get; set; } = new();
    public List<TemplatePlaceholder> Placeholders { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public List<string> Provides { get; set; } = new();
    public List<string> Needs { get; set; } = new();
    public string? PostInstall { get; set; }
    public string? Run { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class TemplatePlaceholder
{
    public string Key { get; set; } = "";       // ProjectName
    public string Prompt { get; set; } = "";
    public string? Default { get; set; }
    public string Type { get; set; } = "string"; // string|int|bool|choice
    public List<string>? Choices { get; set; }
    public bool Required { get; set; } = true;
}

public class TemplateFile
{
    public string RelativePath { get; set; } = "";   // may contain {{Placeholders}}
    public string Content { get; set; } = "";        // may contain {{Placeholders}}
    public bool IsBinaryBase64 { get; set; }
    public ArchitectureStyle? Architecture { get; set; }  // null = shared
    public string? LanguageHint { get; set; }
}

public class TemplatePackage
{
    public TemplateManifest Manifest { get; set; } = new();
    public List<TemplateFile> Files { get; set; } = new();   // shared + all architectures
    public string SourceRef { get; set; } = "";              // md path | zip path | embedded id

    // Files that apply for a chosen architecture (shared + that arch)
    public IEnumerable<TemplateFile> FilesFor(ArchitectureStyle a) =>
        Files.Where(f => f.Architecture is null || f.Architecture == a);
}
```

---

## 3. Architecture Variants

A template declares which architectures it supports; each is a file-tree variant. The engine
selects `Shared ∪ Architecture:<chosen>`.

| Style | Folder shape (backend example) | When |
|---|---|---|
| **Flat** | `main.py`, `requirements.txt` | demos, prototypes |
| **N-Tier** | `api/` · `services/` · `repositories/` · `models/` · `core/config` | classic layered apps |
| **Clean** | `domain/` · `application/` · `infrastructure/` · `api/` (deps point inward) | DDD-ish, testable |
| **Hexagonal** | `domain/` + `ports/` + `adapters/` | ports & adapters |
| **Vertical Slice** | `features/<feature>/{handler,dto,endpoint}` | feature-first |

The **same** `ArchitectureStyle` enum is shared with `ast.md`'s `PatternDetector` — so a project
scaffolded as *Clean* will be **fingerprinted back** as Clean by the analyzer (closed loop).

`Engine/ArchitectureLayout.cs` also provides *post-hoc* relocation helpers (e.g. promote a Flat
project to N-Tier) for future "refactor architecture" actions.

---

## 4. Template Sources + Registry

`Services/projectgenerator/Templates/Sources/` — pluggable origins, all implement `ITemplateSource`:

```csharp
public interface ITemplateSource
{
    string Name { get; }
    Task<IReadOnlyList<TemplatePackage>> LoadAsync();
}
```

| Source | Loads from | Use |
|---|---|---|
| `EmbeddedTemplateSource` | `Resources/Raw/templates/*.md` (bundled in app) | ships with Syncro |
| `DirectoryTemplateSource` | `%LOCALAPPDATA%\SyncroDesktop\templates\*.md` | user/local templates |
| `ZipTemplateSource` | `*.synctmpl` / `*.zip` | imported/shared templates |
| `MarkdownTemplateSource` | a single `.md` path | dev/testing |

### 4.1 Registry config — `templates.json`

```jsonc
{
  "templates": [
    { "id": "fastapi", "source": "embedded", "ref": "templates/fastapi.md", "enabled": true },
    { "id": "django",  "source": "embedded", "ref": "templates/django.md",  "enabled": true },
    { "id": "express", "source": "embedded", "ref": "templates/express.md", "enabled": true },
    { "id": "nextjs",  "source": "embedded", "ref": "templates/nextjs.md",  "enabled": true },
    { "id": "postgres","source": "embedded", "ref": "templates/postgres.md","enabled": true },
    { "id": "my-saas", "source": "zip",      "ref": "imported/my-saas.synctmpl", "enabled": true }
  ],
  "groupPresets": [
    { "id": "fullstack-py", "label": "Vite + FastAPI + Postgres",
      "members": [
        { "templateId": "vite-react", "architecture": "flat",  "subfolder": "webdashboard" },
        { "templateId": "fastapi",    "architecture": "clean", "subfolder": "backend" },
        { "templateId": "postgres",   "architecture": "flat",  "subfolder": "database" }
      ] }
  ]
}
```

**Adding a template later** = (1) drop `mytemplate.md` (or `.synctmpl`), (2) add one line to
`templates.json`. No recompile. `TemplateRegistry` hot-reloads on file change.

---

## 5. Engine — Summoning

`Services/projectgenerator/Templates/Engine/`

```csharp
public class TemplateRegistry            // discovers + indexes all sources
{
    Task LoadAllAsync();                              // merge embedded + dir + zip
    TemplatePackage? Get(string id);
    IReadOnlyList<TemplateManifest> List();           // for the UI picker
    IReadOnlyList<GroupPreset> Presets();
}

public class PlaceholderResolver         // {{Key}} substitution in paths + contents
{
    string Resolve(string text, IDictionary<string,string> values);
    IDictionary<string,string> BuildValues(TemplateManifest m, IDictionary<string,string> userInput,
                                            int allocatedPort);   // injects {{Port}}, {{ProjectName}}…
}

public class ArchitectureLayout          // chooses + (optionally) relocates files per style
{
    IEnumerable<TemplateFile> Select(TemplatePackage pkg, ArchitectureStyle style);
}

public class TemplateEngine              // THE summon
{
    // Instantiate one template into targetPath
    Task<SummonResult> SummonAsync(string templateId, ArchitectureStyle style,
                                   string targetPath, IDictionary<string,string> values,
                                   Action<string>? onLog = null, CancellationToken ct = default);
}

public record SummonResult(bool Ok, string Path, List<string> FilesWritten, string? Error);
```

**Summon flow (single):**
```
TemplateRegistry.Get(id)
   → ArchitectureLayout.Select(pkg, style)        // shared ∪ chosen architecture
   → PlaceholderResolver.BuildValues(...)         // ProjectName, Port (from PortAllocator), …
   → for each TemplateFile: resolve path + content → write (or base64-decode)
   → emit setup.bat AND setup.sh (platform-aware)
   → return SummonResult
```

**Group summon** (delegates to `Groups/GroupOrchestrator` from projectgenerator.md):
```
For preset/members:
  order Database → Backend → Frontend
  PortAllocator.Reserve(member.defaultPort)       // collision-free
  TemplateEngine.SummonAsync(member into /subfolder, member.architecture)
  ProjectWiringService.Wire(provides/needs)        // API_BASE_URL ⇄ CORS, DATABASE_URL
ComposeGenerator.Emit(root docker-compose.yml + run-all.{ps1,sh} + README)
GroupManifest.Append(groups.json)                  // single store at group root
PostGenerationIndexer.Index(each member)           // AST scan + tokenize
```

---

## 6. Packaging — Zip / `.synctmpl`

`Services/projectgenerator/Templates/Packaging/`

```csharp
public class TemplatePackager   // .synctmpl == zip(System.IO.Compression)
{
    Task<string> PackAsync(TemplatePackage pkg, string outZipPath);     // → .synctmpl
    Task<TemplatePackage> UnpackAsync(string zipPath);
}
public class TemplateExporter   // literate .md  → compiled .synctmpl (for sharing/marketplace)
{
    Task<string> ExportAsync(string markdownPath, string outDir);
}
public class TemplateImporter   // .synctmpl → DirectoryTemplateSource + registry line
{
    Task<string> ImportAsync(string zipPath);     // validates manifest, registers, returns id
}
```

### 6.1 `.synctmpl` layout (inside the zip)

```
my-saas.synctmpl  (zip)
├── template.json          # the manifest (compiled from frontmatter)
├── source.md              # original literate md (round-trippable)
└── files/
    ├── _shared/<tree>
    ├── flat/<tree>
    ├── ntier/<tree>
    └── clean/<tree>
```

- **Export**: `fastapi.md` → parse → emit `template.json` + `files/<arch>/…` → zip → `.synctmpl`.
- **Import**: unzip → validate → copy to local templates dir → add `templates.json` line.
- **Marketplace**: `.synctmpl` is publishable through the existing `MarketplaceService`
  (templates become first-class marketplace items alongside scripts).

---

## 7. Module Layout (new files)

```
Services/projectgenerator/Templates/
├── Model/
│   ├── ArchitectureStyle.cs
│   ├── TemplateManifest.cs
│   ├── TemplatePlaceholder.cs
│   ├── TemplateFile.cs
│   └── TemplatePackage.cs
├── Sources/
│   ├── ITemplateSource.cs
│   ├── EmbeddedTemplateSource.cs
│   ├── DirectoryTemplateSource.cs
│   ├── ZipTemplateSource.cs
│   └── MarkdownTemplateSource.cs
├── Parsing/
│   ├── MarkdownTemplateParser.cs      # md → TemplatePackage
│   └── TemplateManifestReader.cs      # YAML/JSON frontmatter → TemplateManifest
├── Engine/
│   ├── TemplateRegistry.cs
│   ├── PlaceholderResolver.cs
│   ├── ArchitectureLayout.cs
│   └── TemplateEngine.cs
├── Packaging/
│   ├── TemplatePackager.cs
│   ├── TemplateExporter.cs
│   └── TemplateImporter.cs
├── templates.json
└── library/                            # the literate templates (also copied to Resources/Raw/templates)
    ├── fastapi.md
    ├── django.md
    ├── express.md
    ├── nextjs.md
    └── postgres.md
```

**DI (MauiProgram.cs):**
```csharp
builder.Services.AddSingleton<ITemplateSource, EmbeddedTemplateSource>();
builder.Services.AddSingleton<ITemplateSource, DirectoryTemplateSource>();
builder.Services.AddSingleton<ITemplateSource, ZipTemplateSource>();
builder.Services.AddSingleton<MarkdownTemplateParser>();
builder.Services.AddSingleton<TemplateManifestReader>();
builder.Services.AddSingleton<TemplateRegistry>();
builder.Services.AddSingleton<PlaceholderResolver>();
builder.Services.AddSingleton<ArchitectureLayout>();
builder.Services.AddSingleton<TemplateEngine>();
builder.Services.AddSingleton<TemplatePackager>();
builder.Services.AddSingleton<TemplateExporter>();
builder.Services.AddSingleton<TemplateImporter>();
```

---

## 8. Integration with existing code

- **`ProjectGenerator`** (`ScaffoldProjectAsync` / `ScaffoldGroupProjectAsync`) is refactored to
  delegate into `TemplateEngine` + `GroupOrchestrator`. Public signatures the dialog calls **stay
  the same**, so no UI break during migration. The `type` string maps to a canonical template `id`
  via `TemplateRegistry` (fixes the "React/Next/Vite" wrong-template bug from projectgenerator.md).
- **`ProjectGeneratorScripts`** becomes the *seed* for `library/*.md` (its strings move into the
  literate templates), then is deprecated.
- **`CreateProjectDialog.razor`** gains an **Architecture** dropdown (Flat/N-Tier/Clean…) populated
  from `manifest.architectures`, plus an **Import Template** (`.synctmpl`) button.
- **AST loop:** scaffolded projects are indexed via `AstService.ScanProjectAsync` + `TokenizeProjectAsync`;
  the chosen architecture is recorded so `ast.md`'s `PatternDetector` validates it.
- **Port map** (centralized via `PortAllocator`): `3020` LLM · `3030` diagnostics · templates use
  their `defaultPort`, reallocated on collision.

---

## 9. Template Library Index

Each entry is a literate `.md` under `library/` following §1. Shipped templates:

| id | label | kind | architectures | default port |
|---|---|---|---|---|
| `fastapi` | FastAPI | Backend | flat, ntier, clean | 8000 |
| `django` | Django | Backend | flat, layered | 8000 |
| `express` | Express/Node | Backend | flat, ntier, clean | 5000 |
| `nextjs` | Next.js | Frontend | flat | 3000 |
| `vite-react` | Vite React | Frontend | flat | 5173 |
| `springboot` | Spring Boot | Backend | ntier, clean | 8080 |
| `flutter` | Flutter | Mobile | flat | — |
| `postgres` | PostgreSQL | Database | flat (compose) | 5432 |

> Concrete authored examples in this repo: **`ProjectTemplates/fastapi.md`**,
> **`ProjectTemplates/django.md`**, **`ProjectTemplates/express.md`**.

---

## 10. Build Order

1. **Model + Parsing** (`TemplateManifest`, `TemplateFile`, `TemplatePackage`, `MarkdownTemplateParser`) — parse a `.md` into a package.
2. **Engine** (`TemplateRegistry`, `PlaceholderResolver`, `ArchitectureLayout`, `TemplateEngine`) — summon a single template.
3. **Author `library/fastapi.md`** + wire `ProjectGenerator.ScaffoldProjectAsync` to summon it → first end-to-end template.
4. **Sources + `templates.json`** — discovery + extensibility (drop-in templates).
5. **Packaging** (`Packager`/`Exporter`/`Importer`) — zip/import/marketplace.
6. **Group** integration (`GroupOrchestrator` + presets) — multi-service summon with wiring.
7. **UI** — architecture dropdown + import button in `CreateProjectDialog`.

---

## 11. Verification

- **Unit:** parse each `library/*.md` → manifest valid, every `### file:` yields a `TemplateFile`,
  every declared `architecture` section is non-empty; placeholders all resolvable.
- **Round-trip:** `md → export → .synctmpl → import → summon` produces byte-identical files.
- **Summon (single):** summon `fastapi` as `clean` → `app/domain`, `app/application`,
  `app/infrastructure`, `app/api/main.py` exist; `{{Port}}`/`{{ProjectName}}` substituted; `setup.bat` + `setup.sh` present.
- **Summon (group):** preset `fullstack-py` → `/webdashboard` + `/backend` + `/database`,
  root `docker-compose.yml`, wired env (`API_BASE_URL` ⇄ CORS, `DATABASE_URL`), no port collision,
  single `.syncro_db` at root, all members AST-indexed.
- **Build:** `dotnet build` → 0 errors.
```
