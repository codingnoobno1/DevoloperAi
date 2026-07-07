# Syncro RAG & Hindsight Memory — Deep Reference

> **Retrieval-Augmented Generation · Agent Memory · Project Knowledge Graph · Self-Improving AI**  
> The intelligence layer that makes Syncro learn from every code generation, build, and failure.

---

## Table of Contents

1. [Overview & Architecture](#1-overview--architecture)
2. [AI-Aware CLI Command Pipeline (`AiCommand.cs`)](#2-ai-aware-cli-command-pipeline-aicommandcs)
3. [RAG Engine (`RagEngine.cs`)](#3-rag-engine-ragenginess)
4. [Agent Memory Store (`AgentMemoryStore.cs`)](#4-agent-memory-store-agentmemorystorecs)
5. [Hindsight Learning Loop](#5-hindsight-learning-loop)
6. [Memory Schema (Full)](#6-memory-schema-full)
7. [Few-Shot Injection Strategy](#7-few-shot-injection-strategy)
8. [Project Knowledge Graph (`ProjectKnowledgeGraph.cs`)](#8-project-knowledge-graph-projectknowledgegraphcs)
9. [Cross-Project Knowledge Resolution](#9-cross-project-knowledge-resolution)
10. [LLM Client Abstraction (`LlmClient.cs`)](#10-llm-client-abstraction-llmclientcs)
11. [Prompt Templates](#11-prompt-templates)
12. [Confidence & Scoring](#12-confidence--scoring)
13. [Failure Recovery & Auto-Patch](#13-failure-recovery--auto-patch)
14. [Memory Growth & Maintenance](#14-memory-growth--maintenance)

---

## 1. Overview & Architecture

The Syncro RAG & Hindsight system is made of four interlocking layers:

```
┌─────────────────────────────────────────────────────────┐
│                    CLI / Agent Query                    │
└──────────────────────────┬──────────────────────────────┘
                           │
              ┌────────────▼────────────┐
              │       RagEngine         │  ← Retrieval layer
              │  AST + Vector + Memory  │
              └────────────┬────────────┘
                           │
              ┌────────────▼────────────┐
              │       LlmClient         │  ← Inference layer
              │  Local or Cloud LLM     │
              └────────────┬────────────┘
                           │
              ┌────────────▼────────────┐
              │   AgentMemoryStore      │  ← Learning layer
              │  Hindsight + Outcomes   │
              └────────────┬────────────┘
                           │
              ┌────────────▼────────────┐
              │ ProjectKnowledgeGraph   │  ← Reasoning layer
              │  AST + Semantic edges   │
              └─────────────────────────┘
```

---

## 2. AI-Aware CLI Command Pipeline (`AiCommand.cs`)

`AiCommand.cs` is the CLI entry point for all `syncro ai *` subcommands.
It orchestrates the full RAG pipeline, from query parsing to LLM response streaming.

### Supported Subcommands

#### `syncro ai explain <symbol>`
Retrieves the AST node definition and its full dependency chain, then asks the LLM to write
a clear functional explanation in plain English.

```bash
syncro ai explain TenderController
syncro ai explain loginUser --depth 3     # Include 3 levels of callees
syncro ai explain UserService --format markdown
```

**What gets injected into the prompt:**
- AST node definition (signature, file, line range)
- Direct callers and callees from the call graph
- Vector-matched semantically similar functions
- Past memory records for `explain` on the same symbol (if any)

---

#### `syncro ai find-auth-flow`
Crawls the AST call graph and HTTP route table to identify the complete authentication
flow: middleware, login routes, token signing, token verification, and protected route guards.

```bash
syncro ai find-auth-flow
syncro ai find-auth-flow --format mermaid    # Output as Mermaid diagram
syncro ai find-auth-flow --symbol loginUser  # Start from a specific symbol
```

**Graph traversal algorithm:**
```
1. Find all route definitions of type POST matching patterns:
   /login, /auth, /signin, /token, /session
2. Follow call edges from each route handler
3. Flag nodes that:
   - Import jwt / jsonwebtoken / passportjs / bcrypt / argon2
   - Call .sign(), .verify(), .compare(), .hash()
4. Find all middleware that check Authorization headers
5. Assemble into ordered auth flow sequence
```

---

#### `syncro ai generate <type> <name>`
Scaffolds a code block matching the workspace project's language, framework conventions,
and best practices. Injects past successful generations as few-shot examples.

```bash
syncro ai generate controller Tender
syncro ai generate service UserAuth
syncro ai generate dto TenderRequest --framework nestjs
syncro ai generate model Product --db mongodb
syncro ai generate repository TenderRepo --pattern clean
syncro ai generate middleware RateLimiter
syncro ai generate test TenderController --coverage unit
```

**Supported generation types:**

| Type | Description |
|---|---|
| `controller` | REST controller class with CRUD endpoints |
| `service` | Business logic service with DI constructor |
| `repository` | Data access layer with interface definition |
| `dto` | Data Transfer Object / Request-Response models |
| `model` | Database entity / schema definition |
| `middleware` | Express/NestJS/ASP.NET middleware function |
| `test` | Unit test suite for a given class/function |
| `page` | UI page component (Next.js/Flutter/MAUI) |
| `hook` | React hook / Flutter provider |
| `migration` | Database migration file |

---

#### `syncro ai review <file>`
Performs an AI code review of the given file: checks for security issues, performance
anti-patterns, naming conventions, and architecture violations.

```bash
syncro ai review src/controllers/auth.ts
syncro ai review --all-changed           # Review all git-modified files
syncro ai review --security-only src/
```

---

#### `syncro ai ask "<question>"`
Answers arbitrary natural-language questions about the codebase using RAG context.

```bash
syncro ai ask "Where is JWT verified in this project?"
syncro ai ask "What database collections does the Tender API write to?"
syncro ai ask "Which service handles file uploads?"
syncro ai ask "What would break if I remove the UserService.authenticate method?"
```

---

## 3. RAG Engine (`RagEngine.cs`)

The `RagEngine` implements the retrieval portion of the pipeline.
It constructs a ranked context window from three sources: AST, Vectors, and Memory.

### Retrieval Flow

```csharp
public class RagEngine
{
    // Main retrieval method — returns ranked context snippets
    public async Task<RagContext> RetrieveAsync(string query, RagOptions options)
    {
        // Stage 1: Exact AST lookup (O(1))
        var astNodes = await _astIndex.FindAsync(query.ExtractSymbols());

        // Stage 2: Metadata-filtered vector candidates
        var candidates = _vectorIndex.Filter(
            projectId: options.ProjectId,
            framework: options.Framework,
            language: options.Language,
            nodeTypes: options.NodeTypes);

        // Stage 3: Cosine similarity over candidates
        var queryEmbedding = await _embedder.EmbedAsync(query);
        var topK = candidates
            .Select(c => (record: c, score: CosineSimilarity(queryEmbedding, c.Embedding)))
            .OrderByDescending(x => x.score)
            .Take(options.TopK)
            .ToList();

        // Stage 4: Memory RAG — find matching past generations
        var memoryHits = await _memoryStore.SearchAsync(
            promptHash: SHA256(NormalizePrompt(query)),
            framework: options.Framework,
            successOnly: true,
            limit: 3);

        // Stage 5: Re-rank and assemble
        return new RagContext(astNodes, topK, memoryHits);
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot  += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB));
    }
}
```

### `RagOptions` Configuration

| Option | Default | Description |
|---|---|---|
| `TopK` | `8` | Maximum number of vector results to retrieve |
| `MinSimilarity` | `0.72` | Discard candidates below this cosine similarity |
| `IncludeArtifacts` | `false` | Include build-artifact AST nodes in retrieval |
| `CrossProject` | `true` | Search across all projects in the group |
| `MaxContextTokens` | `4096` | Maximum tokens to inject into the prompt |
| `MemoryLimit` | `3` | Maximum past memory records to inject as few-shots |

---

## 4. Agent Memory Store (`AgentMemoryStore.cs`)

The `AgentMemoryStore` manages the hindsight learning dataset stored in
`SyncroDB/Memory/memory.jsonl` and its fast-access index.

```csharp
public class AgentMemoryStore
{
    private const string MemoryPath = ".syncro_db/Memory/memory.jsonl";
    private const string IndexPath  = ".syncro_db/Memory/index.json";

    // Save a new generation event (before build result is known)
    public async Task<string> SaveAsync(MemoryRecord record)
    {
        record.MemoryId = $"mem-gen-{_counter++}";
        record.Success  = null; // pending
        await AppendToJsonlAsync(MemoryPath, record);
        await UpdateIndexAsync(record.PromptHash, record.MemoryId);
        return record.MemoryId;
    }

    // Update outcome after build completes
    public async Task UpdateOutcomeAsync(string memoryId, bool success,
        IEnumerable<string> errors, int iterations)
    {
        // Read all records, update the matching one, rewrite
        // (JSONL append-only; use in-memory cache to avoid full re-scan)
        _cache[memoryId] = _cache[memoryId] with
        {
            Success          = success,
            ErrorsEncountered = errors.ToList(),
            IterationsCount   = iterations
        };
        await PersistCacheAsync();
    }

    // Search for relevant past records to use as few-shot examples
    public async Task<List<MemoryRecord>> SearchAsync(
        string promptHash, string framework, bool successOnly, int limit)
    {
        // First: exact hash match (same prompt → best result)
        if (_index.TryGetValue(promptHash, out var exactId))
        {
            var exact = _cache[exactId];
            if (!successOnly || exact.Success == true)
                return [exact];
        }

        // Fallback: fuzzy match by framework + success
        return _cache.Values
            .Where(r => r.TargetFramework == framework)
            .Where(r => !successOnly || r.Success == true)
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .ToList();
    }
}
```

---

## 5. Hindsight Learning Loop

The complete lifecycle of a code generation event:

```
┌──────────────────────────────────────────────────────────┐
│  1. syncro ai generate controller Tender                 │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│  2. RagEngine.RetrieveAsync(query)                       │
│     ├── AST symbol lookup                                │
│     ├── Vector cosine search                             │
│     └── Memory few-shot fetch (success=true)             │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│  3. Prompt assembled → LlmClient.GenerateAsync()         │
│     Response streamed to terminal                        │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│  4. AgentMemoryStore.SaveAsync()                         │
│     success = null (pending)                             │
└──────────────────────┬───────────────────────────────────┘
                       │
┌──────────────────────▼───────────────────────────────────┐
│  5. ShellRunner.RunAsync(buildCommand)                   │
│     e.g. "dotnet build" / "npm run build"                │
└──────────────┬────────────────────────────┬──────────────┘
               │ ExitCode == 0              │ ExitCode != 0
               │                           │
┌──────────────▼───────────┐  ┌────────────▼──────────────┐
│  6a. Update Memory:      │  │  6b. Save error to        │
│      success = true      │  │      Errors/build_errors  │
│      Done ✓              │  └────────────┬──────────────┘
└──────────────────────────┘               │
                                ┌──────────▼──────────────┐
                                │  7. syncro patch        │
                                │     AI auto-fix loop    │
                                │     (max 3 iterations)  │
                                └──────────┬──────────────┘
                                           │
                             ┌─────────────▼─────────────┐
                             │  8. Update Memory:        │
                             │     success = true/false  │
                             │     iterations_count = N  │
                             └───────────────────────────┘
```

---

## 6. Memory Schema (Full)

```json
{
  "memory_id":        "mem-gen-2047",
  "prompt_hash":      "sha256:a3f9c2d1e8b4f7...",
  "prompt":           "Generate PostgreSQL database connector module for Node.js",
  "generated_code":   "const { Pool } = require('pg'); ...",
  "generated_file":   "src/db/postgres.js",
  "target_framework": "Express",
  "language":         "JavaScript",
  "project_id":       "viking-backend-002",
  "group_id":         "grp-mern-001",
  "build_command":    "npm run build",
  "success":          true,
  "errors_encountered": [],
  "iterations_count": 1,
  "patch_applied":    false,
  "patch_history":    [],
  "duration_ms":      3240,
  "llm_model":        "gemini-2.5-pro",
  "context_tokens":   2840,
  "rag_sources": [
    { "type": "ast",    "symbol": "DatabaseConfig", "file": "src/config/db.js" },
    { "type": "vector", "symbol": "connectDatabase", "score": 0.91 },
    { "type": "memory", "memory_id": "mem-gen-1203", "score": "exact_hash" }
  ],
  "timestamp": "2026-06-05T23:20:45Z",
  "pruned":    false
}
```

---

## 7. Few-Shot Injection Strategy

When a generation request is received, the RAG engine injects past memory records into the
LLM system prompt as few-shot examples. The injection strategy:

### Priority Order

1. **Exact prompt hash match** — Same normalized prompt, `success=true`
   → Injected first, highest weight, labelled `[BEST MATCH]`

2. **Same framework + success** — Different prompt, same framework, `success=true`, most recent
   → Up to 2 additional examples injected

3. **Same language + success** — Different framework, same language, `success=true`
   → 1 example if no same-framework match found

4. **Failed examples (negative)** — `success=false`, same framework
   → Injected with label `[AVOID THIS PATTERN]` to steer the LLM away from known failures

### Injected Prompt Format

```
## Past Successful Generation (BEST MATCH)
Framework: Express | Language: JavaScript
Prompt: "Generate PostgreSQL database connector module for Node.js"
Build result: SUCCESS (1 iteration)

Generated code:
\`\`\`javascript
const { Pool } = require('pg');
const pool = new Pool({ connectionString: process.env.DATABASE_URL });
module.exports = { query: (text, params) => pool.query(text, params) };
\`\`\`

---

## Past Failed Generation (AVOID THIS PATTERN)
Framework: Express | Language: JavaScript
Prompt: "Create database connection for Node"
Build result: FAILED (3 iterations) — Error: Cannot find module 'pg-promise'

Generated code to avoid:
\`\`\`javascript
const pgp = require('pg-promise')();
\`\`\`
```

---

## 8. Project Knowledge Graph (`ProjectKnowledgeGraph.cs`)

The `ProjectKnowledgeGraph` integrates structural AST hierarchies and semantic vectors
to create a queryable map of the logical layers of the codebase.

### Graph Node Types

| Node Type | Example | Source |
|---|---|---|
| `RestEndpoint` | `POST /api/tenders` | Route parsing (Express/NestJS/Django/FastAPI) |
| `Controller` | `TenderController` | AST class detection |
| `Service` | `TenderService` | AST class detection (naming convention) |
| `Repository` | `TenderRepository` | AST class detection (naming + interface pattern) |
| `Model` | `TenderModel` | ORM entity detection (Mongoose/EF/SQLAlchemy) |
| `Dto` | `TenderDto` | AST class detection (naming convention) |
| `Middleware` | `AuthMiddleware` | Route middleware detection |
| `Database` | `MongoDB.tenders` | Connection string + query call analysis |
| `ExternalService` | `StripeAPI` | `fetch()` / HTTP client call detection |

### Graph Edge Types

| Edge Type | Meaning |
|---|---|
| `routed_to` | REST endpoint → Controller method |
| `invokes` | Controller → Service method call |
| `depends_on` | Service → Repository / DTO |
| `queries` | Repository → Database collection/table |
| `validates` | Controller → DTO |
| `calls_external` | Service → External API |
| `guarded_by` | Route → Middleware |
| `cross_project` | Frontend fetch → Backend route |

### Example Knowledge Graph Traversal

```
[REST Endpoint: POST /api/tenders]
    │  routed_to
    ▼
[TenderController.createTender()]
    │  invokes
    ▼
[TenderService.saveTender(dto: TenderDto)]
    │  depends_on DTO         │  invokes repository
    ▼                         ▼
[TenderDto]           [TenderRepository.insert()]
                              │  queries
                              ▼
                      [MongoDB: db.tenders.insertOne()]
                              │  Vector DB link
                              ▼
                      [Semantic: "Tender data validation rules"]
```

### AI Questions the Knowledge Graph Can Answer

```bash
# Find the service closest to TenderController by call distance
syncro ai ask "Which service is closest to TenderController?"

# Trace all database collections affected by an endpoint
syncro ai ask "What database collections are written when POST /api/tenders is called?"

# Find where JWT is verified across all projects
syncro ai ask "Where is JWT token verified across this workspace?"

# Impact analysis
syncro ai ask "What would break if I remove TenderService.saveTender?"

# Auth flow tracing
syncro ai ask "Trace the full auth flow from login request to database"
```

---

## 9. Cross-Project Knowledge Resolution

When a `group_id` is set, the Knowledge Graph spans all projects in the group.
Cross-project edges connect frontend components to backend routes to database operations.

### Resolution Algorithm

```
Given query: "Where does the login form write to the database?"

1. Scan Next.js AST for components matching /[Ll]ogin/
   → Found: LoginForm.tsx, LoginPage.tsx

2. Trace event handlers in LoginForm.tsx
   → Found: handleSubmit() → fetch("POST /api/auth/login")

3. Resolve cross-project edge: Next.js fetch → Express route
   → Found in cross_project.json:
     from: LoginForm.handleSubmit
     to:   POST /api/auth/login (viking-backend-002)

4. Traverse backend call graph:
   POST /api/auth/login → loginUser() → UserService.authenticate()
   → UserRepository.findByEmail() → db.users.findOne()

5. Answer:
   "The login form (LoginForm.tsx) calls POST /api/auth/login,
    which reads from the MongoDB 'users' collection via UserRepository."
```

---

## 10. LLM Client Abstraction (`LlmClient.cs`)

`LlmClient` provides a unified interface for multiple LLM backends.
The active backend is configured in `.syncro_db/config.json`.

```csharp
public interface ILlmClient
{
    IAsyncEnumerable<string> StreamAsync(LlmRequest request);
    Task<string> CompleteAsync(LlmRequest request);
}

public class LlmRequest
{
    public string SystemPrompt { get; init; }
    public string UserPrompt   { get; init; }
    public int    MaxTokens    { get; init; } = 4096;
    public float  Temperature  { get; init; } = 0.2f;   // Low temp for code generation
    public bool   Stream       { get; init; } = true;
}
```

### Supported Backends

| Backend | Config Key | Notes |
|---|---|---|
| **Ollama** (local) | `"provider": "ollama"` | Fully offline. Models: `codellama`, `deepseek-coder`, `llama3` |
| **Gemini API** | `"provider": "gemini"` | Requires `GEMINI_API_KEY`. Supports Gemini 2.5 Pro |
| **OpenAI API** | `"provider": "openai"` | Requires `OPENAI_API_KEY`. Supports GPT-4o |
| **Azure OpenAI** | `"provider": "azure_openai"` | Requires endpoint + key in config |
| **LM Studio** | `"provider": "lmstudio"` | Local OpenAI-compatible server |

### Backend Configuration

```json
// .syncro_db/config.json
{
  "llm": {
    "provider": "ollama",
    "model": "codellama:13b",
    "endpoint": "http://localhost:11434",
    "maxTokens": 4096,
    "temperature": 0.2,
    "fallback": {
      "provider": "gemini",
      "model": "gemini-2.5-pro"
    }
  }
}
```

---

## 11. Prompt Templates

### Code Generation Prompt Structure

```
[SYSTEM]
You are an expert software engineer working on a {language} project using {framework}.
You follow {architecture} architecture patterns.
The project is located at {path}.

Conventional directory structure:
  Controllers: {conventionalPaths.controllers}
  Services:    {conventionalPaths.services}
  Models:      {conventionalPaths.models}
  DTOs:        {conventionalPaths.dtos}

IMPORTANT: Match the exact code style and naming conventions shown in the context below.

[CONTEXT — AST Nodes]
{top 3 most relevant AST node definitions}

[CONTEXT — Related Code (Vector Search)]
{top 5 vector-matched code snippets with file paths}

[FEW-SHOT EXAMPLES — Past Successful Generations]
{injected memory records, success=true, max 3}

[NEGATIVE EXAMPLES — Known Failures for This Framework]
{injected memory records, success=false, max 1}

[USER REQUEST]
Generate a {type} named {name} for the {framework} project.
{additional constraints from CLI flags}
```

### Explain Prompt Structure

```
[SYSTEM]
You are a code analyst. Explain the following code in clear, plain English.
Focus on: what it does, why it exists, and how it connects to the rest of the system.

[CONTEXT — Symbol Definition]
{AST node: signature, file, line range, full source}

[CONTEXT — Callers]
{list of functions/routes that call this symbol}

[CONTEXT — Callees]
{list of functions/services this symbol calls}

[CONTEXT — Semantic Neighbours]
{2-3 semantically similar functions from vector search}

[USER REQUEST]
Explain {symbol_name} in plain English.
```

---

## 12. Confidence & Scoring

Every RAG retrieval result carries a confidence score used to decide how prominently
it is featured in the prompt context window.

### Final Score Formula

```
final_score(record) =
    (0.6 × cosine_similarity)
  + (0.3 × ast_proximity_score)
  + (0.1 × recency_score)

where:
  ast_proximity_score = 1 / (1 + shortest_path_in_call_graph)
  recency_score       = exp(-days_since_last_index / 30)
```

### Context Window Budget

| Source | Max Tokens | Priority |
|---|---|---|
| Exact AST symbol definition | 800 | Highest — always included |
| Call graph neighbours (depth 1) | 600 | High |
| Vector search top-3 | 1200 | Medium |
| Memory few-shots (success) | 800 | Medium |
| Memory negative examples | 300 | Low |
| System prompt + instructions | 400 | Fixed |
| **Total budget** | **4096** | Configurable |

If context exceeds the budget, lower-priority sources are truncated starting from the bottom.

---

## 13. Failure Recovery & Auto-Patch

When a build fails after code generation, `syncro patch` executes the auto-fix loop:

```
Iteration 1:
  → Read error from Errors/build_errors.jsonl
  → Extract: error_code, message, file, line, column
  → Query RagEngine for context around the failing file
  → Prompt: "Fix this specific compiler error: {error}
             In this file: {file_context}
             With this project context: {rag_context}"
  → LLM generates patch (diff format preferred)
  → Apply patch to file
  → Re-run build

Iteration 2 (if still failing):
  → Include original error + new error in prompt
  → Include failed patch as negative context
  → Broaden RAG context (increase TopK to 12)

Iteration 3 (if still failing):
  → Include full iteration history
  → Ask LLM for alternative approach rather than line fix
  → Final attempt

After 3 failures:
  → Update Memory: success=false, iterations_count=3, errors=[...]
  → Report to Desktop via Named Pipe: type="error"
  → Suggest manual intervention or `syncro ai ask` for diagnosis
```

### Patch Application

Patches are applied in unified diff format when possible:

```diff
--- src/controllers/tender.ts (before)
+++ src/controllers/tender.ts (after)
@@ -42,7 +42,7 @@
 export async function createTender(req: Request, res: Response) {
-  const dto = req.body as TenderDto;
+  const dto: TenderDto = req.body;
   const result = await tenderService.save(dto);
```

---

## 14. Memory Growth & Maintenance

### Growth Rate Estimates

| Activity | Records per day (typical dev) |
|---|---|
| `syncro ai generate` | 3–10 records |
| `syncro patch` | 0–5 records (each iteration is a record) |
| `syncro ai review` | Not stored (read-only) |
| `syncro ai explain` | Not stored (read-only) |
| **Total** | **~5–15 records/day** |

At 15 records/day average, `memory.jsonl` grows ~5 KB/day.
After 1 year of active use: ~1.8 MB — negligible.

### Pruning Strategy

```bash
# Mark records older than 90 days as pruned (soft delete)
syncro memory purge --older-than 90d

# Hard-delete all failed records older than 30 days
syncro memory purge --status failed --older-than 30d --hard

# Compact the JSONL file (remove pruned records)
syncro memory compact
```

### Quality Improvement Over Time

The memory store creates a **self-improving flywheel**:

```
Day 1:  No memory → LLM generates with only AST + Vector context
Day 7:  ~50 records → 30% of generate calls get few-shot examples
Day 30: ~200 records → 70% cache hit rate on common generation tasks
Day 90: ~500 records → Near-perfect few-shot injection for the project's patterns
```

As the memory store grows, generation quality improves and iteration counts drop
because the LLM learns the exact patterns that work for this specific project.
