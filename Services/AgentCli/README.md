# Services/AgentCli — DB-based Agent ⇄ CLI core

Implements `agentcli.md` + `nlp.md` + `scriptstore.md`: a central file-DB, a loopable task
state machine, DB-based error→action mapping (no ML), and a reusable script store.
Runs **offline**; the LLM only enriches (default gateway is offline).

## Layout
```
SyncroDb.cs                 central facade (atomic JSON/JSONL, snake_case, audit)
Models/                     TaskRecord, ErrorMapping, ScriptRecord, ErrorSignature, ...
Llm/ILlmGateway.cs          LLM boundary + NullLlmGateway (offline default)
Nlp/                        TextNormalizer, KeywordLexicon, KeywordExtractor,
                            ErrorMappingStore, ErrorMatcher, TaskMapper, LlmTeacher
Scripts/                    IProcessExecutor, ScriptSafetyScanner, ScriptStore,
                            ScriptMatcher, ScriptRunnerAdapter
Loop/                       TaskStore, ResolutionPolicy, TaskLoopEngine
```

## Wire up (MauiProgram.cs)
```csharp
builder.Services.AddAgentCli();
```

## Storage (default %LOCALAPPDATA%/SyncroDesktop/)
```
intelligence/
├── nlp/error-keywords.json   seeded lexicon (extend by editing)
├── nlp/mappings.jsonl        error→action table (grows by rows + counters = "learning")
├── nlp/intents.json          task-text intent patterns
├── scripts/scripts.json      script index
├── scripts/bodies/*          script bodies (reused next time)
├── tasks.jsonl               the loop (event log, crash-resumable)
└── audit.jsonl               every mutation / run
```

## The loop (one status change → one action)
```
pending → running → succeeded → done
                 ↘ failed → diagnosing → resolving → patched → (back to pending)
                                                   ↘ needs_human  (bounded by MaxAttempts)
```

## Example
```csharp
var tasks  = sp.GetRequiredService<TaskStore>();
var engine = sp.GetRequiredService<TaskLoopEngine>();

var t = await tasks.EnqueueAsync(new TaskRecord {
    ProjectId = "demo", Title = "Install deps", Intent = TaskKind.RunScript, ScriptId = myScriptId
});
t = await engine.RunToCompletionAsync(t);   // runs, and on failure diagnoses + reuses a fix script
// t.Status is Done or NeedsHuman; t.ClassifiedBy shows db|llm; mappings/scripts counters updated
```

## Enable the LLM (optional)
Register a real `ILlmGateway` (e.g. over `localhost:3020`) **before** `AddAgentCli()`.
With it offline, everything still works deterministically (RAG/lookup + seeded rules).
