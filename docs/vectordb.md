# SyncroDB — Vector Database Deep Reference

> **Filesystem-native · Multi-project · Vectorized · Context-Aware**  
> The embedded vector database powering all Syncro AI context retrieval.

---

## Table of Contents

1. [Introduction & Design Philosophy](#1-introduction--design-philosophy)
2. [Storage Layout](#2-storage-layout)
3. [Namespace Catalog (11 Namespaces)](#3-namespace-catalog-11-namespaces)
4. [JSON Schemas](#4-json-schemas)
   - [Project Index Record](#41-project-index-record)
   - [Project Group / Combo Record](#42-project-group--combo-record)
   - [Vector Index Record](#43-vector-index-record)
   - [Memory Record (Hindsight)](#44-memory-record-hindsight)
   - [Archetype / Framework Record](#45-archetype--framework-record)
   - [Template Registry Record](#46-template-registry-record)
5. [Hybrid Search Architecture](#5-hybrid-search-architecture)
6. [Indexing Strategy](#6-indexing-strategy)
7. [Project Combos & Cross-Project Queries](#7-project-combos--cross-project-queries)
8. [Supported Archetype Combos](#8-supported-archetype-combos)
9. [Framework Knowledge Base (30+ entries)](#9-framework-knowledge-base-30-entries)
10. [Data Lifecycle & Maintenance](#10-data-lifecycle--maintenance)

---

## 1. Introduction & Design Philosophy

SyncroDB is **not a remote database service**. It is a local, filesystem-native database
that lives inside the user's configured Syncro workspace directory (`.syncro_db/`).

- ✅ No Docker, no server process, no network required
- ✅ Every record is human-readable JSON
- ✅ Vector embeddings stored as compact binary `float32` arrays (`.vec` files)
- ✅ JSON sidecar indexes make the DB inspectable, debuggable, and trivially exportable
- ✅ Works offline — the AI context engine never needs internet access to query local code

**Location:** `<workspace>/.syncro_db/`  
**Example:** `D:/DeveloperAI/DEVOLOPER/.syncro_db/`

Initialized automatically by `syncro init` or the first `syncro scan`.

---

## 2. Storage Layout

```
.syncro_db/
│
├── AST/                            # Parsed AST node maps per project
│   └── <project_id>/
│       ├── ast_index.json          # Symbol → {file, line, type} — O(1) lookup table
│       ├── nodes.jsonl             # All AST node records (streaming JSON lines)
│       └── filtered.json          # Source-only nodes (build artifacts excluded)
│
├── Graphs/                         # Dependency graphs and call maps
│   └── <project_id>/
│       ├── calls.dot               # Function call graph in Graphviz DOT format
│       ├── deps.mermaid            # Dependency graph in Mermaid format
│       └── cross_project.json      # Cross-project edges for combo stacks
│
├── Vectors/                        # Semantic embedding store
│   └── <project_id>/
│       ├── index.json              # Vector metadata: id, symbol, file, hash, offset
│       └── embeddings.vec          # Binary float32 arrays (dim=768 or 1536)
│
├── Memory/                         # Agent hindsight learning store
│   ├── memory.jsonl                # Append-only log of generations + outcomes
│   ├── index.json                  # Prompt hash → memory record lookup
│   └── scripts/                   # AI-generated helper scripts
│       └── <script_id>.sh/.ps1
│
├── Errors/                         # Compilation and parser failure logs
│   ├── build_errors.jsonl          # MSBuild / npm / python build errors
│   └── ast_errors.jsonl            # AST parse failures with file/line context
│
├── Templates/                      # Code scaffolding blueprints
│   ├── jwt-auth/                   # JWT authentication boilerplate
│   │   ├── template.json           # Metadata: name, tags, files
│   │   └── files/                  # The actual template files
│   ├── mongodb-connector/
│   ├── stripe-checkout/
│   ├── redis-cache/
│   ├── rest-crud/
│   └── index.json                  # Template registry with keyword search index
│
├── Frameworks/                     # Static framework knowledge base (30+ entries)
│   ├── nextjs.json
│   ├── nestjs.json
│   ├── vite.json
│   ├── django.json
│   ├── flask.json
│   ├── fastapi.json
│   ├── flutter.json
│   ├── react-native.json
│   ├── spring-boot.json
│   ├── aspnet-core.json
│   ├── maui.json
│   ├── laravel.json
│   └── ... (30+ total)
│
├── Architectures/                  # Detected arch patterns per project
│   └── <project_id>/
│       └── detected.json           # MVC, Clean, Onion, Hexagonal, Microservice layers
│
├── Agents/                         # Subagent task logs and audit trail
│   ├── tasks.jsonl                 # Multi-step agent task history and checkpoints
│   └── audit.jsonl                 # Elevated CLI operation audit trail
│
├── Projects/                       # Project master registry
│   └── projects.json               # Array of all registered project index records
│
└── Groups/                         # Multi-project combo configurations
    └── groups.json                 # Stack combos: MERN, Flutter+Next, Rust+Flutter, etc.
```

---

## 3. Namespace Catalog (11 Namespaces)

### `AST/`
**Purpose:** Stores parsed AST node maps: classes, functions, imports, REST routes, component trees.

**Context Filtering:** Build artifact directories are excluded from the main source index
and stored in `filtered.json` instead. This prevents artifact nodes from polluting RAG context.

Excluded directories (auto-detected per framework):
```
.next/    node_modules/    bin/    obj/    dist/    build/
.vercel/  __pycache__/     .dart_tool/    .pub-cache/
```

**Access pattern:** O(1) symbol lookup via `ast_index.json`. Streaming scan via `nodes.jsonl`.

---

### `Graphs/`
**Purpose:** Kahn topological sort DAGs and function call graphs.

- `calls.dot` — Full call graph in Graphviz DOT format, renderable by `syncro graph --format dot`
- `deps.mermaid` — Dependency graph renderable directly in Markdown/Mermaid viewers
- `cross_project.json` — Edges linking symbols across different projects in a Group (e.g. Next.js `fetch()` → Express route)

---

### `Vectors/`
**Purpose:** Semantic embeddings for functions, classes, REST endpoints, and database schemas.

- **Embedding format:** Binary `float32` arrays in `embeddings.vec`
- **Dimensions:** 768 (local model) or 1536 (OpenAI/Gemini)
- **Queried via:** Cosine similarity search filtered by metadata
- **Updated:** Incrementally — only re-embed files changed since last scan

---

### `Memory/`
**Purpose:** Agent hindsight learning store. Guides future RAG-based few-shot injection.

- `memory.jsonl` — Append-only log. Never deletes records; marks them `pruned: true` when superseded.
- `index.json` — SHA256(prompt) → `memory_id` lookup for instant retrieval of past results.
- Records include: generated code, build outcome, iteration count, errors, duration, LLM model used.

---

### `Errors/`
**Purpose:** Compiler error logs from all build attempts.

- Used by `syncro patch --last-error` to locate the last build failure
- Each error record includes: error code, message, file path, line number, column, build command used
- AI reads from this namespace to understand what went wrong before generating a fix

---

### `Templates/`
**Purpose:** Reusable micro-architecture templates, applied via `syncro template apply`.

Each template folder contains:
- `template.json` — Metadata: name, description, tags, required variables
- `files/` — The actual boilerplate files with `{{variable}}` placeholder substitution

---

### `Frameworks/`
**Purpose:** Static framework knowledge base powering the `FrameworkKnowledgeStore`.

Each JSON file defines: detection signals, source roots, excluded dirs, dev command,
build command, default port, env file location, conventional directory paths.

---

### `Architectures/`
**Purpose:** Detected architectural layers per project.

The AST engine classifies each project's layer structure. `detected.json` stores:
- Architecture type: `MVC`, `Clean`, `Onion`, `Hexagonal`, `Microservice`, `Monolith`
- Layer mapping: which directories correspond to Controllers, Services, Repositories, Models
- Confidence score: 0.0–1.0 how certain the detection is

---

### `Agents/`
**Purpose:** Checkpoint logs and audit trails.

- `tasks.jsonl` — Every step of every multi-step `syncro agent` task
- `audit.jsonl` — Every elevated operation: PATH install, service registration, file system changes outside workspace

---

### `Projects/`
**Purpose:** Master registry of all Syncro-managed projects.

Single file `projects.json` is an array of project records. The primary lookup table for all
cross-namespace queries. Any namespace operation (vector search, AST lookup) starts here to
resolve `project_id` → path mapping.

---

### `Groups/`
**Purpose:** Multi-project combo configurations.

Links frontend + backend + database + mobile projects into a named stack.
Enables cross-project graph queries and ordered startup (`syncro run --group grp-mern-001`).

---

## 4. JSON Schemas

### 4.1 Project Index Record

```json
{
  "project_id": "viking-backend-002",
  "name": "Viking Backend",
  "path": "D:/Workspaces/viking/backend",
  "language": "TypeScript",
  "framework": "Next.js",
  "packageManager": "npm",
  "entryPoint": "src/index.ts",
  "architecture": "Clean",
  "databases": ["MongoDB", "Redis"],
  "hasVenv": false,
  "requiresDocker": true,
  "groupIds": ["grp-mern-001"],
  "lastScanned": "2026-06-05T23:00:00Z",
  "astNodeCount": 2847,
  "vectorCount": 412,
  "tags": ["rest-api", "auth", "file-upload"]
}
```

**Field Reference:**

| Field | Type | Description |
|---|---|---|
| `project_id` | string | Unique identifier (kebab-case, auto-generated) |
| `language` | string | Primary language detected by fingerprinting |
| `framework` | string | Primary framework (matched against `Frameworks/` KB) |
| `packageManager` | string | `npm`, `yarn`, `pnpm`, `pip`, `cargo`, `go mod`, `maven`, `gradle` |
| `architecture` | string | Detected architecture pattern |
| `databases` | string[] | Databases detected from config/connection strings |
| `hasVenv` | bool | Whether a Python venv was detected |
| `requiresDocker` | bool | Whether `docker-compose.yml` exists |
| `groupIds` | string[] | All Group combos this project belongs to |
| `astNodeCount` | int | Total AST nodes in last scan |
| `vectorCount` | int | Total vector embeddings indexed |

---

### 4.2 Project Group / Combo Record

```json
{
  "group_id": "grp-mern-001",
  "name": "MERN Stack — Viking Platform",
  "archetype": "MERN",
  "projects": [
    {
      "project_id": "viking-frontend-001",
      "role": "frontend",
      "framework": "Next.js",
      "port": 3000,
      "devCommand": "npm run dev"
    },
    {
      "project_id": "viking-backend-002",
      "role": "api",
      "framework": "Express",
      "port": 5000,
      "devCommand": "npm run dev"
    },
    {
      "project_id": "viking-db-003",
      "role": "database",
      "framework": "MongoDB",
      "connectionString": "mongodb://localhost:27017/viking"
    }
  ],
  "startOrder": ["viking-db-003", "viking-backend-002", "viking-frontend-001"],
  "envFile": ".env.group",
  "created": "2026-06-01T10:00:00Z"
}
```

---

### 4.3 Vector Index Record

```json
{
  "vector_id": "vec-meth-0912",
  "project_id": "viking-backend-002",
  "group_id": "grp-mern-001",
  "file_path": "src/controllers/auth.ts",
  "symbol_name": "loginUser",
  "node_type": "Method",
  "language": "TypeScript",
  "framework": "Express",
  "tags": ["auth", "jwt", "controller"],
  "embedding_offset": 4096,
  "embedding_dim": 768,
  "embedding_hash": "2F4E91A3...",
  "code_snippet": "export async function loginUser(req, res) { ... }",
  "line_start": 42,
  "line_end": 87,
  "last_indexed": "2026-06-05T22:00:00Z"
}
```

**`embedding_offset`:** Byte offset in `embeddings.vec` where this record's `float32[768]` begins.
The binary layout is: `record0_floats[0..767] | record1_floats[0..767] | ...`

---

### 4.4 Memory Record (Hindsight)

```json
{
  "memory_id": "mem-gen-2047",
  "prompt_hash": "sha256:a3f9c2...",
  "prompt": "Generate PostgreSQL database connector module for Node.js",
  "generated_code": "const { Pool } = require('pg'); ...",
  "target_framework": "Express",
  "language": "JavaScript",
  "project_id": "viking-backend-002",
  "build_command": "npm run build",
  "success": true,
  "errors_encountered": [],
  "iterations_count": 1,
  "patch_applied": false,
  "duration_ms": 3240,
  "llm_model": "gemini-2.5-pro",
  "timestamp": "2026-06-05T23:20:45Z",
  "pruned": false
}
```

**Hindsight Learning Logic:**  
When `syncro ai generate` is called, the RAG engine hashes the normalized prompt and checks
`Memory/index.json`. If a matching record with `success: true` exists, its `generated_code`
is injected as a **few-shot example** at the top of the LLM prompt. Failed records (`success: false`)
are used as **negative examples** when iteration count allows.

---

### 4.5 Archetype / Framework Record

```json
{
  "id": "nextjs",
  "displayName": "Next.js",
  "language": "TypeScript",
  "ecosystem": "Node.js",
  "detectionSignals": {
    "fileExists": ["next.config.js", "next.config.mjs", "next.config.ts"],
    "packageJsonDeps": ["next"]
  },
  "excludeDirs": [".next", "node_modules", ".vercel"],
  "sourceRoots": ["src/", "app/", "pages/", "components/"],
  "entryPattern": "app/layout.tsx|pages/_app.tsx",
  "devCommand": "npm run dev",
  "buildCommand": "npm run build",
  "defaultPort": 3000,
  "envFile": ".env.local",
  "requiresNodeModules": true,
  "requiresVenv": false,
  "typicalArchitecture": "App Router MVC",
  "conventionalPaths": {
    "controllers": "app/api/",
    "components": "components/",
    "services": "lib/",
    "models": "models/"
  }
}
```

---

### 4.6 Template Registry Record

```json
{
  "template_id": "tpl-jwt-auth",
  "name": "jwt-auth",
  "displayName": "JWT Authentication",
  "description": "Complete JWT access + refresh token auth flow",
  "tags": ["auth", "jwt", "security", "token"],
  "languages": ["TypeScript", "JavaScript"],
  "frameworks": ["Express", "NestJS", "Fastify"],
  "variables": [
    { "name": "JWT_SECRET", "description": "Secret key for signing tokens", "required": true },
    { "name": "ACCESS_EXPIRES", "default": "15m" },
    { "name": "REFRESH_EXPIRES", "default": "7d" }
  ],
  "files": [
    "src/middleware/auth.ts",
    "src/services/token.ts",
    "src/routes/auth.ts"
  ],
  "created": "2026-06-01T10:00:00Z"
}
```

---

## 5. Hybrid Search Architecture

SyncroDB implements a four-stage hybrid search pipeline. All stages can be composed.

```
[Agent / CLI Query]
         │
         ├─► Stage 1: Exact Symbol Search
         │   ├── Look up ast_index.json for exact symbol name
         │   └── O(1) — returns in < 1ms
         │
         ├─► Stage 2: Metadata Filtering
         │   ├── Filter vector index.json by:
         │   │     project_id, framework, language, node_type, tags
         │   └── Narrows candidate pool before cosine search
         │
         ├─► Stage 3: Cosine Similarity Search
         │   ├── Embed query string → float32[768]
         │   ├── Scan filtered embeddings.vec for top-K nearest
         │   └── Supports cross-project when group_id filter applied
         │
         └─► Stage 4: Result Re-ranking
             ├── Merge AST + Vector results, de-duplicate
             ├── Score = 0.6 × semantic_similarity + 0.4 × ast_proximity_score
             └── Top results injected as RAG context into LLM prompt
```

### Score Formula

```
final_score(record) =
    (0.6 × cosine_similarity(query_embedding, record_embedding))
  + (0.4 × (1 / (1 + ast_call_distance(query_symbol, record_symbol))))
```

`ast_call_distance` is the shortest path in the call graph between the queried symbol
and the candidate record's symbol. Symbols that are one call away score highest.

---

## 6. Indexing Strategy

| Namespace | Index Type | Key | Update Strategy |
|---|---|---|---|
| `AST/` | Hash Map (JSON Object) | Symbol name → `{file, line, type}` | Full re-scan on `syncro scan` |
| `Vectors/` | Binary flat file + JSON index | SHA256 of code snippet | Incremental: re-embed only changed files |
| `Memory/` | Append-only JSONL + hash index | SHA256 of normalized prompt | Append on every generation event |
| `Projects/` | JSON Array | `project_id` (UUID v4) | On `syncro init` / `syncro project add` |
| `Groups/` | JSON Array | `group_id` (UUID v4) | On `syncro project group` |
| `Templates/` | Directory + keyword index | Template name slug | On template install / AI generation |
| `Frameworks/` | Static JSON files | Framework `id` slug | Shipped with CLI; extended via `syncro kb add` |

### Incremental Vector Re-Indexing

When `syncro scan --incremental` is run:
1. Walk source files and compute SHA256 of each file's content
2. Compare against `hash` field in `Vectors/index.json`
3. Only re-embed files where the hash changed
4. Write new embedding at the next available offset in `embeddings.vec`
5. Update `index.json` with new offset, hash, and `last_indexed` timestamp
6. Old embedding slots are marked `stale: true` and compacted on next full scan

---

## 7. Project Combos & Cross-Project Queries

When projects are grouped into a combo, SyncroDB builds cross-project dependency edges
stored in `Graphs/<project_id>/cross_project.json`.

### Example Cross-Project Query

> *"Which MongoDB collection is written to when the user submits the login form on the Next.js frontend?"*

**Traversal path:**
```
Next.js page (app/login/page.tsx)
    → fetch("POST /api/auth/login")              ← cross_project edge: frontend → backend
    → Express route: POST /api/auth/login
    → router.post("/login", loginUser)
    → loginUser() controller
    → UserService.authenticate()
    → MongoDB: db.collection("users").findOne()  ← resolved: "users" collection
```

This traversal crosses **3 separate project ASTs** using the Group cross-project graph.

### Cross-Project Edge Schema

```json
{
  "edge_id": "xedge-001",
  "from_project": "viking-frontend-001",
  "from_symbol": "LoginPage.handleSubmit",
  "from_file": "app/login/page.tsx",
  "to_project": "viking-backend-002",
  "to_symbol": "POST /api/auth/login",
  "to_file": "src/routes/auth.ts",
  "edge_type": "http_call",
  "method": "POST",
  "url_pattern": "/api/auth/login"
}
```

---

## 8. Supported Archetype Combos

| Archetype | Components | Languages |
|---|---|---|
| `MERN` | MongoDB + Express + React + Node.js | JavaScript / TypeScript |
| `MEAN` | MongoDB + Express + Angular + Node.js | TypeScript |
| `Flutter+Next` | Flutter Mobile + Next.js Web | Dart + TypeScript |
| `Flutter+Express` | Flutter Mobile + Express API | Dart + JavaScript |
| `Rust+Flutter` | Rust HTTP Backend + Flutter Frontend | Rust + Dart |
| `GoLang+React` | Go Fiber/Echo Server + React SPA | Go + TypeScript |
| `Django+React` | Django REST Framework + React | Python + JavaScript |
| `FastAPI+Next` | FastAPI Backend + Next.js Frontend | Python + TypeScript |
| `MAUI+API` | .NET MAUI Desktop/Mobile + ASP.NET Core | C# |
| `NestMongo` | NestJS + MongoDB + Next.js | TypeScript |
| `SpringReact` | Spring Boot + React + PostgreSQL | Java + TypeScript |
| `SpringFlutter` | Spring Boot + Flutter | Java + Dart |
| `LaravelVue` | Laravel + Vue 3 + MySQL | PHP + JavaScript |
| `RailsReact` | Ruby on Rails + React | Ruby + JavaScript |
| `GoFlutter` | Go Backend + Flutter | Go + Dart |
| `RustAxum+Next` | Rust Axum API + Next.js | Rust + TypeScript |

---

## 9. Framework Knowledge Base (30+ entries)

Built-in framework configs shipped with the CLI:

| Framework | Language | Ecosystem | Detection Signal |
|---|---|---|---|
| Next.js | TypeScript | Node.js | `next.config.js` exists OR `"next"` in `package.json` deps |
| NestJS | TypeScript | Node.js | `@nestjs/core` in deps |
| Vite | TypeScript/JS | Node.js | `vite.config.ts` exists |
| React (CRA) | JavaScript | Node.js | `react-scripts` in deps |
| Express | JavaScript | Node.js | `express` in deps, no framework config file |
| Fastify | TypeScript | Node.js | `fastify` in deps |
| Hono | TypeScript | Node.js/Bun | `hono` in deps |
| Django | Python | Python | `manage.py` + `django` in `requirements.txt` |
| Flask | Python | Python | `flask` in `requirements.txt`, no `manage.py` |
| FastAPI | Python | Python | `fastapi` in `requirements.txt` |
| Flutter | Dart | Dart/Flutter | `pubspec.yaml` + `flutter:` section |
| React Native | TypeScript | Node.js | `react-native` in deps |
| ASP.NET Core | C# | .NET | `*.csproj` + `Microsoft.AspNetCore` |
| .NET MAUI | C# | .NET | `*.csproj` + `UseMaui=true` |
| Blazor | C# | .NET | `*.csproj` + `Microsoft.AspNetCore.Components` |
| Spring Boot | Java | Maven/Gradle | `@SpringBootApplication` annotation |
| Quarkus | Java | Maven | `quarkus-maven-plugin` in `pom.xml` |
| Laravel | PHP | Composer | `artisan` file + `laravel/framework` |
| Symfony | PHP | Composer | `symfony/framework-bundle` |
| Ruby on Rails | Ruby | Bundler | `config/application.rb` |
| Go Fiber | Go | Go modules | `github.com/gofiber/fiber` in `go.mod` |
| Go Echo | Go | Go modules | `github.com/labstack/echo` in `go.mod` |
| Gin | Go | Go modules | `github.com/gin-gonic/gin` in `go.mod` |
| Rust Axum | Rust | Cargo | `axum` in `Cargo.toml` |
| Rust Actix | Rust | Cargo | `actix-web` in `Cargo.toml` |
| Kotlin Ktor | Kotlin | Gradle | `io.ktor:ktor-server-core` in build.gradle |
| Android (Compose) | Kotlin | Gradle | `androidx.compose` in build.gradle |
| SwiftUI | Swift | Xcode | `*.xcodeproj` + SwiftUI imports |
| Tauri | Rust + JS | Cargo + Node | `tauri.conf.json` |
| Electron | JavaScript | Node.js | `electron` in deps + `main` field in package.json |

---

## 10. Data Lifecycle & Maintenance

### Automated Cleanup

| Action | Trigger | Effect |
|---|---|---|
| `syncro scan` | Manual / scheduled | Refreshes AST namespace; marks stale vectors |
| `syncro scan --incremental` | Manual | Re-embeds only changed files |
| Full re-index | On `syncro scan` (full) | Compacts `embeddings.vec`, removes stale offsets |
| Memory pruning | `syncro memory purge` | Marks old/failed records `pruned: true` (never hard-deletes by default) |
| Error rotation | On each build | Keeps last 100 error records per project; older ones archived |

### Backup & Export

```bash
# Export entire SyncroDB as a zip for migration
syncro kb export --all --output syncro_backup.zip

# Export just the memory store
syncro memory export --format jsonl --output memory_export.jsonl

# Import into a new workspace
syncro kb import --file syncro_backup.zip
```

### Storage Estimates

| Namespace | Typical Size (medium project, ~50k LOC) |
|---|---|
| `AST/` | 2–8 MB (JSON nodes) |
| `Vectors/` | 15–60 MB (float32 arrays, 768 dim) |
| `Memory/` | 1–5 MB (grows with usage) |
| `Graphs/` | 0.5–3 MB (DOT/Mermaid text) |
| `Templates/` | 0.5–2 MB (bundled templates) |
| `Frameworks/` | ~200 KB (static configs) |
| **Total** | **~20–80 MB per project** |
