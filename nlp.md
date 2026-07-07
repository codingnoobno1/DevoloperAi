# NLP Layer — Error Keyword Extraction, Task Mapping & Learn-from-LLM (DB-based)

> Extends `agentcli.md`. Adds a **lightweight, DB-driven** NLP layer that (1) **extracts
> keywords/signals from errors**, (2) **maps errors and task text to an action** by **looking
> them up in a database table** (no ML), and (3) **learns** by **inserting/updating rows** —
> from task **outcomes** and from the **LLM as a teacher**.
>
> **No model training.** No KNN / CNN / RNN / ANN, no embeddings, no centroids, no retrain step.
> Everything is **keyword extraction + a mappings table + counters**. Deterministic, explainable,
> works offline; the LLM only fills gaps and writes new rows.

---

## 0. Where it plugs in

In the `agentcli.md` loop, `Diagnose` currently uses a hardcoded `ErrorClassifier` +
`ResolutionPolicy`. This layer replaces them with **keyword extraction + a DB lookup**:

```
failed → DIAGNOSE:
   stderr → TextNormalizer → KeywordExtractor → ErrorSignature {code, keywords[]}
          → ErrorMatcher.Match(signature)   ← QUERIES the mappings TABLE (DB), scores by overlap + worked-rate
          → strong match : act (deterministic)            (source = db)
          → weak/no match + LLM on : LlmTeacher → INSERT a new mapping row → act   (LEARN = new row)
          → weak/no match + LLM off : needs_human
   ...later... outcome (worked?) → bump hits/worked/fails counters on the matched row (LEARN = counters)
```

It also serves **task intake**: `syncro agent "add JWT auth"` → `TaskMapper` (verb/keyword lookup) → intent + slots.

---

## 1. The two jobs (both are table lookups)

| Job | Input | Lookup table | Output |
|---|---|---|---|
| **Error → action** | stderr / build output | `Nlp/mappings.jsonl` | `solution` label + matched `script_id` + confidence |
| **Task text → intent** | natural-language goal | `Nlp/intents.json` | `TaskIntent` + slots (target, framework, lang) |

No vectorization, no classifier — just **extract keywords, then query a table**.

---

## 2. Pipeline

```
 raw error text
      │ TextNormalizer       (lowercase; strip absolute paths, GUIDs, line numbers, timestamps)
      ▼
 normalized text ──► KeywordExtractor ──► ErrorSignature { code:"CS0246", keywords:["missing-using"] }
      │                    (curated lexicon error-keywords.json: phrase/regex → signal)
      ▼
 ErrorMatcher.Match(signature)        ← scans mappings.jsonl, scores each row:
      │     score = codeMatch + keywordOverlap + phraseHit + reliability(worked/hits)
      ▼
 best row  ──►  confidence gate ──► act  |  LlmTeacher(+insert row)  |  needs_human
```

Everything above the gate is **string normalization + set overlap + a counter ratio** — no training.

---

## 3. The mappings table (the heart) — `.syncro_db/Nlp/mappings.jsonl`

One row per known error→action rule. Append-only; updated in place via `SyncroDb`.

```jsonc
{
  "id": "map_cs0246",
  "code": "CS0246",                       // optional exact error code (strongest signal)
  "keywords": ["missing-using", "cannot-find-name"],   // signal set from the lexicon
  "match_phrases": ["are you missing a using directive"], // optional raw substring check
  "label": "add_using",                   // human-readable action label
  "solution": "edit_code",                // edit_code | run_another | rerun | install_dep | llm_fix
  "script_id": null,                      // optional script to run (for run_another/install_dep)
  "hits": 12,                             // times this row matched
  "worked": 9,                            // times its action actually fixed the error
  "fails": 3,                             // worked-rate = worked / hits  → drives confidence
  "source": "seed",                       // seed | llm | human | outcome
  "created_at": "...", "updated_at": "..."
}
```

**Matching (`ErrorMatcher`) — deterministic scoring, no ML:**
```
for each row:
   codeMatch     = (row.code != null && row.code == sig.code) ? 1.0 : 0.0
   overlap       = |row.keywords ∩ sig.keywords| / |row.keywords|     // containment, set math
   phraseHit     = any(row.match_phrases substring-of normalizedText) ? 1.0 : 0.0
   reliability   = row.hits == 0 ? 0.5 : row.worked / row.hits         // historical success
   score = 0.45*codeMatch + 0.30*overlap + 0.10*phraseHit + 0.15*reliability
pick row with max score
confidence = score        // ≥ τ_high → act ; else → LlmTeacher (if on) ; else needs_human
```

Fully explainable: *"matched `map_cs0246` because code=CS0246 (1.0) and all keywords present (1.0); this fix has worked 9/12 times."*

---

## 4. Components (C#) — `Services/AgentCli/Nlp/`

```csharp
public class TextNormalizer { string Normalize(string raw); }   // paths→<path>, nums→<n>, lowercase

public class KeywordExtractor
{
    // Curated lexicon (error-keywords.json) maps phrase/regex → signal token. Extensible by JSON.
    ErrorSignature Signature(string raw);                       // { Code, Keywords[], NormalizedText }
}
public record ErrorSignature(string? Code, IReadOnlyList<string> Keywords, string NormalizedText);

public class ErrorMappingStore                                  // the DB table (via SyncroDb)
{
    Task<IReadOnlyList<ErrorMapping>> AllAsync();
    Task UpsertAsync(ErrorMapping row);                         // insert (LLM/human) or replace
    Task BumpAsync(string mappingId, bool worked);              // hits++, worked++/fails++  ← "learning"
}

public class ErrorMatcher                                       // NO ML — scan + score + pick
{
    Task<MatchResult?> MatchAsync(ErrorSignature sig);          // returns best row + confidence + why
}
public record MatchResult(ErrorMapping Row, double Confidence, string Explanation);

public class TaskMapper                                         // verb/keyword lookup in intents.json
{
    TaskIntentResult Map(string goalText);                      // "add JWT auth to X" → generate / X / jwt-auth
}
```

**Label/solution space** (mirrors `agentcli.md`):
`install_dep · change_port · add_using · fix_signature · create_file · rerun · run_another · llm_fix · human`

---

## 5. Learn from the LLM — by writing rows, not training

```csharp
public class LlmTeacher
{
    private readonly ILlmGateway _llm;          // cli.md
    private readonly ContextRedactor _redact;   // cli.md safety
    private readonly ErrorMappingStore _store;

    // Only when confidence < threshold AND _llm.Available.
    // LLM picks a label from a FIXED candidate set; text is REDACTED first.
    // Its answer is written as a NEW mappings.jsonl row (source="llm", hits=0).
    Task<ErrorMapping?> TeachAsync(ErrorSignature sig, string[] candidateLabels, CancellationToken ct);
}
```

**The whole "learning" mechanism is DB writes:**

| Event | DB action (no training) |
|---|---|
| Cold start | ship `error-keywords.json` + seed rows in `mappings.jsonl` |
| LLM resolves an unknown error | **INSERT** a new row (`source=llm`) → same error matched offline next time |
| Action worked / failed | **BUMP** counters (`hits`, `worked`, `fails`) on the matched row |
| Row keeps failing | worked-rate drops → its `reliability` score drops → it loses to better rows (auto-demote, just a ratio) |
| Human corrects a label | **UPSERT** row (`source=human`), highest trust |

No `model.json`, no `RetrainAsync`, no centroids. The "model" **is** the `mappings.jsonl` table.
It gets smarter purely by **rows accumulating and counters updating**.

---

## 6. Data stores — `.syncro_db/Nlp/` (via `SyncroDb`)

```
.syncro_db/Nlp/
├── error-keywords.json   # lexicon: phrase/regex → signal  (ships seeded, extensible by JSON)
├── mappings.jsonl        # THE table: signature → action rows + counters  ← grows/updates over time
├── intents.json          # task-intent patterns: verbs/keywords → intent + slot rules
└── stats.json            # roll-up: matches by source (db/llm/human), overall worked-rate
```

`error-keywords.json` — extend by adding lines, no recompile:
```jsonc
{
  "cannot find module|ModuleNotFoundError|ERR_MODULE_NOT_FOUND": { "signal": "missing-module", "label": "install_dep" },
  "EADDRINUSE|address already in use|port .* in use":            { "signal": "port-in-use",   "label": "change_port" },
  "CS0246|CS0103|are you missing a using":                       { "signal": "missing-using", "label": "add_using" },
  "TS2307|cannot find name":                                     { "signal": "ts-missing",    "label": "install_dep" }
}
```

`intents.json` — task-text mapping, also pure lookup:
```jsonc
{
  "intents": [
    { "verbs": ["add","create","scaffold","generate"], "intent": "generate" },
    { "verbs": ["fix","repair","patch"],                "intent": "patch" },
    { "verbs": ["analyse","analyze","scan","inspect"],  "intent": "analyse" },
    { "verbs": ["explain","describe","what is"],        "intent": "explain" }
  ],
  "featureKeywords": { "jwt-auth": ["jwt","auth","token"], "crud": ["crud","controller"] }
}
```

---

## 7. TaskRecord additions (extends `agentcli.md`)

```jsonc
{
  // …existing fields…
  "keywords": ["missing-module"],     // ← from KeywordExtractor
  "matched_mapping": "map_cs0246",    // ← which DB row was used (or null)
  "match_confidence": 0.83,
  "classified_by": "db"               // db | llm | human   (no "knn"/model sources)
}
```

`classified_by` lets you see work shift `llm → db` as rows accumulate — the proof it's learning,
without any model.

---

## 8. Safety

- **Redact before teaching:** `LlmTeacher` runs error text through `ContextRedactor` before any LLM call.
- **Constrained output:** the LLM picks a label from a **fixed candidate set** and the result is stored as a **data row** — it never executes or free-writes commands.
- **No auto-exec from a row:** a matched row only *selects* a `solution`; mutating ones (`edit_code`, new scripts) still pass the `agentcli.md` approval gate. SAFE ones (`install_dep`, `change_port`, `rerun`) may auto-loop.
- **Bounded LLM use:** only low-confidence/no-match cases call the teacher; per-call timeout + cancellation.
- **All writes via `SyncroDb`** → atomic, audited; `mappings.jsonl` changes are in the audit log.

---

## 9. Integration points

| Existing piece | Change |
|---|---|
| `agentcli.md` `ErrorClassifier` | **becomes** `KeywordExtractor` + `ErrorMatcher` (DB lookup) |
| `agentcli.md` `ResolutionPolicy` | consumes the matched row's `solution`; keeps the SAFE/approval split |
| `agentcli.md` `TaskRecord` | + `keywords`, `matched_mapping`, `match_confidence`, `classified_by` |
| `cli.md` `LlmGateway` / `ContextRedactor` | reused by `LlmTeacher` |
| `ast.md` failure/bug memory | a confirmed error→fix is both a `mappings.jsonl` row **and** a `FailureFact` |
| `cli.md` `LexicalEmbedder` | **still used for RAG only** — NOT for NLP classification here (NLP is table lookup) |

---

## 10. Module layout

```
Services/AgentCli/Nlp/
├── TextNormalizer.cs
├── KeywordExtractor.cs        + ErrorSignature.cs
├── KeywordLexicon.cs          (loads error-keywords.json)
├── ErrorMapping.cs            (the row model)
├── ErrorMappingStore.cs       (mappings.jsonl CRUD + BumpAsync counters)
├── ErrorMatcher.cs            (scan + score + pick — no ML)
├── TaskMapper.cs              (intents.json verb/keyword lookup)
├── LlmTeacher.cs              (low-confidence → label → INSERT row)
└── Models/  MatchResult.cs · TaskIntentResult.cs
```
DI: register `TextNormalizer`, `KeywordExtractor`, `KeywordLexicon`, `ErrorMappingStore`,
`ErrorMatcher`, `TaskMapper`, `LlmTeacher`. (No vectorizer/classifier/model-store.)

---

## 11. Build order

1. **`TextNormalizer` + `KeywordExtractor` + lexicon** — "find keywords in errors" works offline immediately.
2. **`ErrorMapping` + `ErrorMappingStore` + seed rows** — the table exists.
3. **`ErrorMatcher`** (scoring) — wire into `agentcli` `Diagnose`; deterministic mapping, no LLM.
4. **Outcome counters** (`BumpAsync`) — rows self-rank by worked-rate as tasks run.
5. **`LlmTeacher`** — unknown errors → LLM label → **new row** → handled offline next time.
6. **`TaskMapper`** — NL task intake for `syncro agent "<goal>"`.
7. **`stats.json` tile** — show matches shifting `llm → db`.

---

## 12. Verification

- **Keywords (offline):** `ModuleNotFoundError` stderr → `KeywordExtractor` → `["missing-module"]`;
  `ErrorMatcher` finds the `install_dep` row with high confidence — **no LLM, no model**.
- **Learn-from-LLM (DB):** novel error, LLM **on** → `LlmTeacher` inserts a new `mappings.jsonl` row →
  repeat with LLM **off** → now matched from the table (`classified_by` flips `llm → db`). Proof of learning = a new row exists.
- **Outcome learning:** force a row's action to fail repeatedly → `worked/hits` drops → its score falls →
  a different row (or the LLM) is chosen next time — all via counters, no retrain.
- **Task mapping:** `syncro agent "add JWT auth to TenderController"` → `generate / TenderController / jwt-auth`.
- **Safety:** fake secret in error text is redacted before the teacher call; teacher can only return a candidate label.
- **Bounds:** LLM-off + no match → `needs_human` (never guesses).
- **No ML deps:** the project references no ML/NN packages; `ErrorMatcher` is set math + a ratio.
```
