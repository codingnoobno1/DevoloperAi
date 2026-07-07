# NLP Mapper Engine — TODO & Hardening Roadmap

> Scope: `Services/AgentCli/Nlp/TaskMapper.cs` and the **intent layer** around it. The recent
> antigravity pass moved verb matching from `.Contains()` to `\b…\b` regex and grew the seed
> dictionary — a real improvement. This doc is the **next mile**: the bugs that change introduced
> or left, the integration gaps, and the path to a router that is deterministic, learning, and
> safe.
>
> Sister docs: [`engine/optimisation server cli.md`](engine/optimisation%20server%20cli.md)
> (runtime), [`ideuidevolopment.md`](ideuidevolopment.md) (IDE/UX), [`nlp.md`](nlp.md),
> [`agentcli.md`](agentcli.md). The mapper lives inside the AgentCli NLP pipeline documented
> there.

---

## 0. What the mapper does today (ground truth)

`TaskMapper.MapAsync(goal)` (`TaskMapper.cs:38`):

1. `EnsureSeedAsync()` — writes a default `intents.json` **only if the file is absent**
   (`TaskMapper.cs:82`).
2. Reads `IntentConfig` from `_db.IntentsPath` → `intelligence/nlp/intents.json` (`SyncroDb.cs:202`).
3. Normalizes the goal via `TextNormalizer` (lowercases, strips paths/GUIDs/numbers,
   `TextNormalizer.cs:20`).
4. **Intent**: first intent whose any verb matches `\b{verb}\b` (case-insensitive) → confidence
   `0.8`; else `"explain"` @ `0.3` (`TaskMapper.cs:44–58`).
5. **Feature slot**: first feature whose any keyword matches `\b{word}\b` → `slots["feature"]`
   (`TaskMapper.cs:60–72`).
6. **Target slot**: first `\b[A-Z][A-Za-z0-9]{2,}\b` in the **raw** goal → `slots["target"]`
   (`TaskMapper.cs:27,74`).
7. Returns `TaskIntentResult(intent, slots, confidence)` (`Matches.cs:12`).

Seeded intents (`TaskMapper.cs:85`): `generate, patch, analyse, explain, refactor, test, deploy`.
Seeded features (`TaskMapper.cs:95`): `jwt-auth, crud, database, api, docker, ci-cd, routing`.

**Verdict:** the word-boundary fix is correct and kills the "paddle"→"add" class of false
positives. But the engine is still **first-match-wins, single-slot, non-learning, and not wired
to the type system that consumes it.** Those are the real problems.

---

## 1. Bugs & sharp edges (concrete)

| # | Issue | Where | Effect |
|---|---|---|---|
| M1 | **Seed never updates existing installs.** `EnsureSeedAsync` early-returns if `intents.json` exists (`TaskMapper.cs:82`). Anyone who ran the old build has the *old* 4-intent dictionary on disk; the "expanded vocabulary" never reaches them. | `TaskMapper.cs:82` | shipped dictionary upgrades are silently ignored |
| M2 | **Intent string ≠ `TaskKind`.** Mapper emits `"patch"`, `"explain"`, `"refactor"`, `"test"`, `"deploy"` — **none exist** in `TaskKind {RunScript,EditCode,Analyse,Generate,LlmFix,Clone,Report,Show,Custom}` (`Enums.cs:21`). No translation layer exists. | mapper ↔ `TaskRecord.Intent` (`TaskRecord.cs:15`) | mapped intents can't drive the loop; whatever consumes the result must hand-map or drop them |
| M3 | **First-match-wins, order-dependent.** Verbs are scanned in `Intents` list order and break on first hit (`TaskMapper.cs:52`). A goal with two verbs ("refactor and test X") gets whichever intent is listed first, not the dominant one. | `TaskMapper.cs:46–58` | ambiguous goals resolve by table order, not intent strength |
| M4 | **Target grabs the sentence-initial capital.** `TargetRx` takes the *first* `[A-Z]\w{2,}` (`TaskMapper.cs:74`). "Add JWT auth" → target = **"Add"**, not "JWT". Capitalized leading verb wins. | `TaskMapper.cs:27` | wrong target entity on any capitalized sentence |
| M5 | **Single feature only.** Loop breaks after the first feature (`TaskMapper.cs:71`). "add jwt auth with postgres in docker" → only `jwt-auth`; `database` and `docker` are dropped. | `TaskMapper.cs:60–72` | multi-feature goals lose slots |
| M6 | **Binary confidence (0.3 / 0.8).** No graded score; a 1-verb match and a 3-signal match look identical (`TaskMapper.cs:45,53`). | `TaskMapper.cs` | downstream can't threshold; can't decide when to ask the LLM |
| M7 | **No LLM fallback / no learning** — unlike the *error* side, which has `LlmTeacher` (`LlmTeacher.cs:28`) that asks the LLM for a label and **inserts a mapping row** for next-time offline reuse. The intent mapper has neither. | whole `TaskMapper` | unknown phrasing always degrades to `"explain"@0.3`, forever; never improves |
| M8 | **`TargetRx` misses lowercase & dotted identifiers.** Only catches `Capitalized` words ≥3 chars — misses `userController`, `app.py`, `package.json`, `ILogger` is caught but `i18n` isn't. | `TaskMapper.cs:27` | many real targets unextracted |
| M9 | **Regex recompiled per call for verbs/features.** `Regex.IsMatch(norm, $@"\b{Regex.Escape(verb)}\b", …)` builds a new regex every verb every call (`TaskMapper.cs:50,65`); only `TargetRx` is precompiled. | `TaskMapper.cs:50,65` | avoidable allocation/JIT on the hot path |
| M10 | **No verb→intent collisions guard.** "optimize" is under `refactor` (`TaskMapper.cs:91`); if a future feature or another intent also wants it, silent shadowing by list order. No validation that verbs are unique across intents. | seed + any edited `intents.json` | dictionary edits can silently break routing |
| M11 | **Synonyms via raw substrings only.** `"entity framework"` is a feature keyword with a space (`TaskMapper.cs:99`); `\bentity framework\b` works but multi-word phrases aren't tokenized/stemmed ("authentications", "dockerized" miss). | `TaskMapper.cs` | morphological variants slip through |

---

## 2. Design target — what "robust deterministic router" means

A three-stage cascade, **offline-first**, mirroring the error pipeline's posture
(`KeywordExtractor` → `ErrorMatcher` → `LlmTeacher`):

```
goal ──▶ [1 normalize] ──▶ [2 deterministic score] ──confident?──▶ result
                                   │ unsure (M6 threshold)
                                   ▼
                          [3 LLM classify + LEARN] ──insert mapping row──▶ result
                                   │ LLM offline
                                   ▼
                          best deterministic guess + low confidence (honest)
```

Stage 2 is a **scorer**, not a first-match. Stage 3 reuses the *exact* `LlmTeacher` pattern
(fixed candidate labels, secret redaction, `<untrusted_context>` wrapping, label-only reply,
persist for offline reuse — `LlmTeacher.cs:33–46`) but for **intents** instead of error labels.

---

## 3. The TODO list (prioritised)

### P0 — correctness & wiring (do first)

- [ ] **M2: bind intent → `TaskKind`.** Introduce a single source of truth mapping the mapper's
      string intents to `TaskKind` *and* to the loop's notion of work:
      `generate→Generate`, `patch→LlmFix|EditCode`, `analyse→Analyse`, `explain→Show`,
      `refactor→EditCode`, `test→RunScript`, `deploy→Custom`. Put it in one `IntentCatalog` so the
      string list and the enum can never drift (validated at startup). Without this, expanded
      intents are decorative.
- [ ] **M1: make the seed self-healing.** Version the dictionary (`intents.json` gets a
      `"schemaVersion"`). On load, if the on-disk version < shipped version, **merge** new
      intents/features (preserving user edits) instead of early-returning. Mirror how the error
      lexicon seeds but allow upgrades.
- [ ] **M4: fix target extraction.** Don't take the first capital. Strategy: (a) skip tokens that
      are themselves intent verbs; (b) prefer all-caps acronyms (`JWT`, `API`), dotted filenames
      (`app.py`), and `I`-prefixed interfaces (`ILogger`); (c) return **all** candidates ranked,
      put the best in `slots["target"]`, keep the rest in `slots["targets"]`.
- [ ] **M9/M11: precompile + tokenize.** Build the verb/feature matchers **once** when
      `IntentConfig` loads (compiled `Regex` per verb, or a single alternation regex per intent),
      cache on the config object. Tokenize the goal once; match against the token set.

### P1 — scoring & multi-slot

- [ ] **M3/M6: graded scorer.** Replace break-on-first with: each intent accumulates a score from
      (verb hits × weight) + (proximity to target) + (feature affinity). Confidence =
      normalized top score; also expose the **runner-up** and margin so downstream/LLM-escalation
      can see ambiguity. Deterministic given the same dictionary.
- [ ] **M5: multi-feature slots.** Collect **all** matching features into `slots["features"]`
      (comma-joined or a list-typed result), keep `slots["feature"]` = top for back-compat.
- [ ] **M10: dictionary validation.** On load, assert no verb maps to two intents and no feature
      keyword collides; log + reject a bad `intents.json` with a clear message instead of silent
      shadowing. A `syncro nlp validate` CLI subcommand surfaces this.

### P2 — learning (close the asymmetry with the error side)

- [ ] **M7: `IntentTeacher`.** Clone `LlmTeacher` (`LlmTeacher.cs`) for intents: when stage-2
      confidence < threshold **and** `ILlmGateway.IsAvailable`, ask the LLM to pick exactly one
      intent from the candidate set, redact secrets (`LlmTeacher.cs:35`), wrap the goal in
      `<untrusted_context>` (`LlmTeacher.cs:40`), accept a **label-only** reply, and **persist a
      learned mapping** (`goal-signature → intent`) so the same phrasing routes offline next time.
      The LLM **classifies, never executes** — same invariant as the error teacher
      (`LlmTeacher.cs:12`).
- [ ] **Mapping store for intents.** A `intents-learned.jsonl` (mirrors `mappings.jsonl`,
      `SyncroDb.cs:200`) with `{signature, intent, source: seed|user|llm, worked, lastUsed}`, bumped
      on success like `ErrorMappingStore.BumpAsync` is in `TaskLoopEngine.cs:197`. Surfaced in the
      monitor's NLP panel (`AgentMonitorServer.cs:563`) next to error mappings.
- [ ] **Feedback loop.** When a task that started from a mapped intent reaches `Done`
      (`TaskLoopEngine.cs:195`), bump the intent mapping's `worked` count; on `NeedsHuman`, decay
      it. Over time the table reflects what actually routed correctly.

### P3 — quality & morphology

- [ ] **M8/M11: light stemming + identifier grammar.** Add a tiny suffix stemmer
      (`-ing/-ed/-s/-ize/-ization`) so "dockerized"→"docker", "authentications"→"auth" match
      without bloating the dictionary. Extend target grammar to `camelCase`, `PascalCase`,
      `snake_case`, dotted filenames, and route paths.
- [ ] **Negation & scope guards.** "don't add auth" / "without docker" must not set those
      features. A small negation window (`no|without|don't|skip` before a keyword) suppresses the
      slot.
- [ ] **Confidence calibration test set.** A fixtures file of `(goal → expected intent+slots)` (50+
      cases incl. the M3/M4/M5 traps) run in CI so dictionary edits can't regress routing.

---

## 4. Reference shapes (what the upgraded mapper returns)

Today (`Matches.cs:12`):

```csharp
public record TaskIntentResult(string Intent, IReadOnlyDictionary<string,string> Slots, double Confidence);
```

Target (back-compat superset):

```csharp
public record TaskIntentResult(
    string Intent,                       // canonical, validated against IntentCatalog (M2)
    IReadOnlyDictionary<string,string> Slots,
    double Confidence,                   // graded 0..1 (M6)
    string? RunnerUp = null,             // 2nd-best intent (ambiguity signal, M3)
    double Margin = 0,                   // top - runnerUp
    IReadOnlyList<string>? Features = null, // ALL features (M5)
    IReadOnlyList<string>? Targets = null,  // ranked targets (M4)
    string Source = "deterministic");    // deterministic | learned | llm  (M7)
```

`IntentCatalog` (new — the M2 fix, the single source of truth):

```csharp
public sealed record IntentSpec(string Intent, TaskKind Kind, string[] DefaultVerbs, bool Mutating);
// generate→Generate, patch→LlmFix, analyse→Analyse, explain→Show,
// refactor→EditCode, test→RunScript, deploy→Custom …
// `Mutating` feeds the same approval gate as scripts/tools (TaskLoopEngine.Autonomous).
```

The `Mutating` flag matters: a routed `deploy`/`patch`/`refactor` is a **mutating** action and
must pass the same approval gate the loop uses for unsafe scripts (`TaskLoopEngine.cs:87`) and
that the MCP registry will use for tools (see
[`engine/optimisation server cli.md §2.2`](engine/optimisation%20server%20cli.md)). Intent
routing should never *silently* trigger a mutating action.

---

## 5. Worked examples (current vs target)

| Goal | Today | Target |
|---|---|---|
| `add JWT auth` | intent `generate`@0.8, feature `jwt-auth`, target **`JWT`** ✓ | same, + `Kind=Generate`, margin reported |
| `Add JWT auth` (capital A) | target **`Add`** ✗ (M4) | target `JWT`, `Add` skipped (it's a verb) |
| `add jwt auth with postgres in docker` | feature **`jwt-auth` only** ✗ (M5) | features `[jwt-auth, database, docker]` |
| `refactor and test UserService` | `refactor` *or* `test` by table order (M3) | scored: `refactor`@0.7 runner-up `test`@0.5, target `UserService` |
| `dockerize the api` | likely `explain`@0.3 ("dockerize" not a verb) (M7,M11) | stem→`docker`; LLM teaches `deploy`/`generate`; learned for next time |
| `deploy to prod` (old install) | `explain`@0.3 — `deploy` intent not on disk (M1) | seed-merge adds `deploy`; routes `deploy`@0.8, `Kind=Custom`, **mutating→gated** |

---

## 6. Acceptance criteria

- [ ] Every intent the mapper can emit resolves to a `TaskKind` (M2) — asserted at startup; no
      orphan strings.
- [ ] Shipping a new intent/feature reaches existing installs via seed-merge (M1).
- [ ] Capitalized leading verbs never become the target (M4); multi-feature goals keep all
      features (M5).
- [ ] Ambiguous goals expose runner-up + margin; confidence is graded, not binary (M3, M6).
- [ ] Unknown phrasing, LLM available → classified once, **persisted**, offline thereafter (M7);
      LLM offline → honest low-confidence best guess, never a crash.
- [ ] Mutating intents route through the existing approval gate (§4), never auto-fire.
- [ ] Calibration fixture set passes in CI; dictionary edits that regress it fail the build (P3).
- [ ] No per-call regex compilation on the hot path (M9).

---

## 7. Sequencing

P0 (wiring + seed-merge + target fix + precompile) is the foundation — without M2 the expanded
dictionary does nothing useful downstream, and without M1 it doesn't even reach users. P1 makes
routing *defensible* (scored, multi-slot, validated). P2 is the high-value leap: the intent
mapper finally **learns** like the error mapper already does. P3 is polish that the CI fixture
set protects. Do them in order; each phase ships independently and leaves the mapper working.
