# Script Store — Store a Script Once, Reuse It Next Time (DB-based)

> Completes the loop across `agentcli.md` (the `Scripts/` registry + `TaskRecord.script_id`) and
> `nlp.md` (the `mappings` row's `script_id`). When a fix/setup needs a script, **generate it once,
> store it in the DB keyed by the error signature/purpose, and reuse it on every later occurrence**
> — no LLM call, deterministic, offline.
>
> Same philosophy: **DB-based, no ML.** A script is found by **table lookup + keyword/purpose
> overlap + a worked-rate ratio** — never regenerated if a good one already exists.

---

## 0. Goal

```
First time  : error → no stored script → (LLM on) generate → SCAN → STORE → run → record
Next time   : same error → ErrorMatcher → mapping.script_id → ScriptStore.Get → run   ← REUSE, no LLM
Similar err : ScriptMatcher finds a script by purpose+keywords (no exact id) → run
```

The script becomes a **reusable asset**. Over time the system runs more from stored scripts and
calls the LLM less — exactly the "AI-extendable script registry" the CLI docs describe.

---

## 1. Where it fits (the reuse decision)

In the `agentcli.md` loop's `Resolve` step, once a `solution` needs a script:

```
solution needs a script (install_dep | run_another | edit-via-script | setup):
   │
   ├─ mapping.script_id set?  ── yes ──► ScriptStore.Get(id) ──► run (sandboxed)        [REUSE]
   │
   ├─ ScriptMatcher.FindFor(purpose, keywords, platform) ── hit ──► run + link id back   [REUSE-by-match]
   │
   └─ no match:
          LLM on?  ── yes ──► generate script ► ScriptSafetyScanner ► ScriptStore.Save
          │                     ► link script_id into the mapping (nlp upsert) ► run (FIRST run = approval)
          └─ no  ──► needs_human
   ...after run... ScriptStore.Bump(id, worked)  +  mapping counters  (learning = counters)
```

---

## 2. Store layout — `.syncro_db/Scripts/`

```
.syncro_db/Scripts/
├── scripts.json              # index (records below)
└── bodies/
    ├── scr_install_fastapi.bat
    ├── scr_install_fastapi.sh     # platform variants share an id family
    └── scr_free_port_3000.ps1
```

Bodies are **real files** (viewable, diffable, executable); the index holds metadata + counters.

---

## 3. ScriptRecord model (expands `agentcli.md` `ScriptRecord`)

```jsonc
{
  "id": "scr_install_fastapi",
  "name": "Install FastAPI dependencies",
  "shell": "bat",                       // bat | sh | ps1 | py
  "body_path": "bodies/scr_install_fastapi.bat",
  "hash": "sha256:…",                   // dedup + integrity
  "purpose": "install_dep",             // the solution label it serves (matches nlp/agentcli)
  "keywords": ["missing-module","fastapi","uvicorn"],  // error signals it resolves
  "platform": ["windows"],              // windows | linux | mac
  "params": ["{{ProjectPath}}"],        // placeholders resolved at run time (NEVER bake secrets)
  "safe": false,                        // auto-runnable without approval? (promoted after N successes)
  "source": "llm",                      // seed | llm | human
  "runs": 7, "succeeded": 6, "failed": 1,   // reliability = succeeded / runs
  "created_at": "…", "updated_at": "…", "last_used": "…",
  "flagged": false                      // set by ScriptSafetyScanner → always needs human
}
```

---

## 4. Components (C#) — `Services/AgentCli/Scripts/`

```csharp
public class ScriptStore                       // via SyncroDb (atomic, audited)
{
    Task<ScriptRecord> SaveAsync(ScriptDraft d);          // DEDUP by hash; write body + index row
    Task<ScriptRecord?> GetAsync(string id);
    Task<string> ReadBodyAsync(string id);
    Task BumpAsync(string id, bool succeeded);            // runs++, succeeded/failed++  ← learning
    Task MarkSafeAsync(string id, bool safe);
}
public record ScriptDraft(string Name, string Shell, string Body, string Purpose,
                          string[] Keywords, string[] Platform, string[] Params, string Source);

public class ScriptMatcher                     // find a reusable script — NO ML, same scoring style as nlp
{
    Task<ScriptMatch?> FindForAsync(string purpose, IReadOnlyList<string> keywords, string platform);
    // score = purposeMatch(0.4) + keywordOverlap(0.3) + platformMatch(0.15) + reliability(0.15)
}
public record ScriptMatch(ScriptRecord Script, double Confidence, string Explanation);

public class ScriptSafetyScanner               // deny-list before store/run
{
    ScanResult Scan(string body, string shell);   // flags rm -rf /, format, shutdown, del /s /q, curl|sh, etc.
}

public class ScriptRunnerAdapter               // sandboxed exec; resolves {{params}} at run time
{
    Task<RunResult> RunAsync(ScriptRecord s, IDictionary<string,string> args, CancellationToken ct);
}
```

---

## 5. Store-and-reuse rules

1. **Dedup on save.** Hash the body; if an identical script exists, **link to it** (don't duplicate).
   `ScriptMatcher.FindForAsync` first so we don't store near-identical scripts for the same purpose.
2. **Key by signature + purpose.** A stored script carries the `keywords` + `purpose` of the error it
   fixed, so it's findable for the **same or similar** future errors (table lookup + overlap, no ML).
3. **Link back to NLP.** On save, write the new `script_id` into the matched `mappings.jsonl` row
   (`nlp.md`). Next occurrence resolves with a pure DB lookup → run, no LLM.
4. **Counters are the learning.** `runs/succeeded/failed` give a reliability ratio; a flaky script
   loses to a better one on `ScriptMatcher` score — no retraining, just counters.
5. **Platform families.** One logical script can have `bat`/`sh`/`ps1` bodies under an id family;
   the matcher picks by host `platform`.

---

## 6. Safety (scripts are the highest-risk asset)

- **First run = approval.** An LLM-generated script is stored with `safe=false`; its **first execution
  goes through the `agentcli.md` approval gate** (preview the body). It's **auto-promoted to `safe=true`
  only after N successful runs** (or a human marks it safe) — then later reuse can auto-run in `--auto` mode.
- **`ScriptSafetyScanner` before store AND before run.** Deny-list of destructive patterns
  (`rm -rf /`, `format`, `del /s /q`, `shutdown`, `:(){ :|:& };:`, pipe-to-shell `curl … | sh`).
  A flagged script is `flagged=true` → **always needs human**, never auto-runs.
- **No secrets in bodies.** Stored scripts use `{{placeholders}}`; real values (paths, ports, tokens)
  are injected at run time by `ScriptRunnerAdapter`. Bodies are scrubbed/redacted before save.
- **Sandboxed exec** via `ProcessRunner` with minimal env; elevation only when explicitly required.
- **Audited.** Save, promote-to-safe, and every run → `Agents/audit.jsonl` (id, hash, purpose, worked).

---

## 7. Integration points

| Piece | Link |
|---|---|
| `agentcli.md` `ScriptRecord` / `Scripts/` | this **is** that registry, fully specified |
| `agentcli.md` `TaskRecord.script_id` / `template_script_executed` | set when a stored script runs (yes/no now meaningful) |
| `nlp.md` `mappings.jsonl` row `script_id` | populated on save → future errors reuse the script with no LLM |
| `grouptemplateproject.md` / `ProjectGeneratorScripts` | the static `setup.bat`/`setup.sh` become **seed** scripts (`source=seed`) in the store |
| `cli.md` `syncro ai generate script` | generates → scans → stores → registers (the docs' AI-extendable registry) |
| `cli.md` `LlmSafetyGate` | reused for the first-run approval of generated scripts |

---

## 8. Module layout

```
Services/AgentCli/Scripts/
├── ScriptRecord.cs   + ScriptDraft.cs
├── ScriptStore.cs            (scripts.json + bodies/, dedup, counters)
├── ScriptMatcher.cs          (find reusable script — table lookup + overlap, no ML)
├── ScriptSafetyScanner.cs    (deny-list scan)
└── ScriptRunnerAdapter.cs    (sandboxed exec + {{param}} resolution)
```
DI: register `ScriptStore`, `ScriptMatcher`, `ScriptSafetyScanner`, `ScriptRunnerAdapter`.
Seed the store at first run from the existing template scripts.

---

## 9. Build order

1. **`ScriptRecord` + `ScriptStore`** (index + bodies, dedup by hash) — scripts can be stored/fetched.
2. **`ScriptRunnerAdapter`** (sandboxed, `{{param}}` resolution) — stored scripts can run.
3. **Seed** from `ProjectGeneratorScripts` (`setup.bat`/`setup.sh`) → `source=seed`.
4. **`ScriptMatcher`** (purpose+keyword reuse) + link `script_id` into `nlp.md` mappings.
5. **`ScriptSafetyScanner`** + first-run approval + promote-to-`safe` after N successes.
6. **`syncro ai generate script`** wires generate → scan → store → register.

---

## 10. Verification

- **Store + reuse:** trigger a `missing-module` error with LLM **on** → a script is generated, scanned,
  stored (`source=llm`), linked into the mapping, and run after approval. Trigger the **same error with
  LLM off** → it runs the **stored** script directly (no LLM); `TaskRecord.classified_by=db`, `script_id` set.
- **Dedup:** generate an identical script twice → only **one** body file + index row; second save links to it.
- **Reuse-by-match:** a *similar* error (same purpose, overlapping keywords) → `ScriptMatcher` returns the
  existing script with an explanation; no new script created.
- **Promotion:** a stored script reaches N successful runs → `safe=true` → reuses auto-run in `--auto`.
- **Safety:** feed a script containing `rm -rf /` → `ScriptSafetyScanner` flags it → `flagged=true`,
  never auto-runs, requires human. Confirm bodies contain `{{placeholders}}`, not real secrets.
- **Audit:** save / promote / run all appear in `Agents/audit.jsonl`.
```
