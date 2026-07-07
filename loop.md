# The Closed Engineering Loop + Domain-Agnostic Engine

> Your architecture, optimized and sequenced. The core idea: **only 2 of 5 engines are AI.** The
> engine understands the system, measures progress, and verifies results; the LLM only *proposes
> solutions*. And the whole thing is **domain-agnostic** — web, compilers, OpenCV, DSP, embedded — via
> declarative **profiles** and pluggable **capability workers**, not hardcoded technology workers.

```
   Goal ──▶ Reality ──▶ Gap ──▶ Solution ──▶ Verification ──▶ Reality′  (loop)
   (Expectation  (Reality   (deterministic  (LLM Engineer)   (Reality
    Engine)       Engine)    diff)                            re-measured)
```

---

## 0. The lesson from your first real run (why this ordering matters)

The Connector ran on a real .NET repo: **the app launched and previewed (localhost:7001)** — the
runtime spine works. But Reality **misread** it: stack "Unknown", 2 routes, because
`BackendContractResolver.DetectStack` only searches the *top* directory for a `.csproj`
(`FindCsproj` → `TopDirectoryOnly`), and the web project sits in a subfolder.

**This is the whole thesis in one bug.** In a closed loop, a wrong Reality fact →
a wrong Gap → the Engineer AI "fixes" something that isn't broken (or misses what is). So the
non-negotiable first investment is **Reality fidelity**, not more AI. Fix (L0):
- recursive `.csproj` search; detect `Sdk="Microsoft.NET.Sdk.Web"`, `FrameworkReference … AspNetCore.App`, `[ApiController]`, `[Route("api/[controller]")]` + controller-level route prefixes;
- treat "0 frontend calls" as possibly-correct (Blazor Server calls services in-process — not a bug).

---

## 1. The five engines (and what already exists)

| Engine | AI? | What it does | Status today |
|---|---|---|---|
| **Reality Engine** | No | Workers measure what *is*: processes, ports, APIs, symbols, builds, tests, containers | 🟡 R1–R6 built (Run/Health/ApiMap/Bootstrap/Deps); **web-shaped, needs fidelity + generalization** |
| **Expectation Engine** | Mostly no | Loads a **Project Profile** → the expected capability set. LLM *only* when it can't classify | ❌ new (profiles + matcher) |
| **Gap Engine** | No | `expected − present` → a typed gap list | ❌ new — but it's a **graph diff**, not new infra |
| **Solution Engine** | **Yes (Engineer)** | Consumes ONE typed gap + relevant subgraph → typed code changes | 🟡 `BackendGenerator` is a first instance |
| **Verification Engine** | No | Re-measure (compile/test/map/health) → did the gap close? | 🟡 workers exist; needs a verify gate |

**The optimization that makes this cheap:** the Universe graph already stores Reality as typed
nodes. A Profile expresses **expected node-patterns**. The Gap Engine is therefore a *query*:
"which expected patterns have no matching node?" So "12 APIs missing" (web) and "IR stage missing"
(compiler) are the **same mechanism** — no per-domain gap code.

---

## 2. The two AI roles — roles, not models (optimized)

Same model, different prompt + permissions initially; split models later only if needed.

### Planner (Architect) — never touches files
- **Deterministic first:** a profile-matcher classifies the project from detectors (manifest files,
  languages, keywords). **The LLM is called only on a classification miss**, or to *refine* an
  expected set the profile can't fully specify. Output = a **typed expected-capability set**, nothing
  executable.
- This is your "planner shouldn't ask the LLM every time" — encoded as: profile hit = 0 tokens.

### Engineer (Solution) — the only file-writer
- Input = **one gap** + a **retrieved subgraph** (the relevant symbols/endpoints/mappings, budgeted —
  not 500 files) + the profile's conventions.
- Output = typed changes, applied through the **existing batch-write + PathGuard actuator** (atomic,
  rollback).
- **Then it must pass Verification before the gap is marked closed.** Bounded retries (e.g. 3), then
  escalate to human. This is your "don't trust the Engineer" loop, made concrete.

**The Gap is the contract** — between deterministic and AI, and between the two roles. The Engineer
never decides architecture (the profile did); the Planner never writes code. That separation is the
reliability win.

---

## 3. Domain-agnostic core (your biggest point), optimized

**Do not build "technology workers." Build one worker interface that emits typed facts, and make
domains data.**

```csharp
public interface ICapabilityWorker {
    string Id { get; }
    CapabilityKind Kind { get; }              // Language | Build | Runtime | Analysis | Domain | Infra | Verify
    WorkerTier Tier { get; }
    Task RunAsync(IEventBus bus, WorkContext ctx, CancellationToken ct);
}
public sealed record Fact(string Key, string Value, double Confidence, string Source);
```

- The existing Run/Health/ApiMap workers become the first `Runtime`/`Analysis` instances. **Nothing is
  rewritten** — they're reframed under one interface, publishing facts into the same graph.
- **Capability layers (Language/Build/Runtime/Analysis/Domain/Infra/Verify) are categories, not
  separate infra.** A C++ project's "CMake configured", a compiler's "parser present", OpenCV's "CUDA
  available" are all just facts with different keys.

### Project Profiles = declarative, not code
```yaml
id: llvm-compiler
detectors: [ "CMakeLists.txt", "llvm", "*.td", "lib/**/*.cpp" ]
expects:
  - capability: build.cmake            # a fact that must exist
  - capability: stage.lexer
  - capability: stage.parser
  - capability: stage.semantic
  - capability: stage.ir
  - capability: stage.optimizer
  - capability: tests.unit
verify:
  - run: "cmake --build build && ctest"
completion: [ stage.ir, tests.unit ]
recommendedWorkers: [ cpp-language, cmake-build, ctest-verify ]
```
Adding LLVM / OpenCV / DSP / Jenkins / Prometheus support = **add a profile file (+ optional worker
plugin)**, never touch the core. Three profiles prove genericity:
- `full-stack-web`: backend, frontend, db, api, tests, ci (mostly built)
- `llvm-compiler`: cmake, lexer→parser→semantic→ir→optimizer, ctest
- `opencv-vision`: opencv-installed, cuda, camera, model, inference, visualization

The core (scheduler, graph, gap engine, planner interface, actuator) never learns "web" or "compiler"
— it only knows **facts, expected-patterns, and gaps**.

---

## 4. The minimal vertical slice — prove the loop before generalizing

**Don't build all domains. Close ONE loop end-to-end, reusing ~80% of what exists:**

```
Profile: full-stack-web  (one file)
   │  Expectation: expects endpoints for each frontend ApiCall
Reality: existing ApiMap worker (after L0 .NET fix)  →  graph
   │  Gap Engine: ApiCall nodes with no Consumes edge = "missing endpoint" gaps
Solution: BackendGenerator (already built!) drafts the endpoint  →  batch-write
   │  Verification: re-run ApiMap + Health  →  edge now exists?
Gap closed ✓  or  retry / escalate
```

This is one honest thread through all five engines using code that mostly exists. If it closes a real
"missing endpoint" gap and verifies it, the architecture is proven — then profiles + capability
workers generalize it to compilers, vision, DSP, etc.

---

## 5. Sequencing (each phase shippable; deterministic value before AI)

| Phase | Deliverable | AI? | Proves |
|---|---|---|---|
| **L0** | **Harden Reality** — fix .NET/csproj detection; language + build detectors as facts | No | Reality is trustworthy (the foundation) |
| **L1** | Profile schema + registry + **deterministic profile-matcher** (Expectation Engine) | No | project → expected set, 0 tokens on a hit |
| **L2** | **Gap Engine** — expected patterns vs graph nodes → typed gap list | No | "what's missing" without any AI |
| **L3** | **Gap Dashboard** (`/gaps`) — expected/reality/gaps per project, live | No | your Facts/Expectations/Gaps view, on screen |
| **L4** | **Solution Engine** — Engineer AI per-gap + **Verification gate + bounded retry** (reuse BackendGenerator + batch-write) | Yes | the closed loop actually closes a gap |
| **L5** | **Planner AI** — classify unknown projects + refine expected set (only on miss) | Yes | handles projects no profile matches |
| **L6** | `ICapabilityWorker` + **one non-web profile** (C++/CMake) end-to-end | No/plugin | domain-agnosticism is real, not claimed |

Rationale: **L0–L3 deliver value with zero AI** (a project self-describes its gaps deterministically —
already more than most IDEs). AI enters only at L4 once Reality + Gap are trustworthy. L6 is the
proof the core is generic.

---

## 6. The caution (yours, sharpened)

1. **Keep the core generic.** Scheduler, graph, gap engine, planner interface, actuator know only
   facts/expectations/gaps — never a domain.
2. **Domain knowledge is declarative** — profiles + plugins, shipped as data.
3. **LLM fills only what deterministic layers can't.** Detection, gap-finding, verification are code.
4. **Reality fidelity is make-or-break** (the .NET bug is the warning). A generic engine on top of
   wrong facts is confidently wrong. Invest in detectors first.
5. **Gap ≠ defect.** An "expected" capability legitimately absent by design (a library with no CLI, a
   Blazor Server app with no HTTP calls) must be suppressible per-profile, or the loop nags forever.

## 7. Risks

- **Profile explosion / ambiguity** — multi-domain repos (a web app *with* a native module). Profiles
  must compose, and detection must allow multiple.
- **Engineer non-termination** — a gap the AI can't close loops forever; hard retry budget + escalate.
- **Verification cost/flakiness** — running builds/tests continuously is expensive and noisy;
  tier + cache + debounce (the R9 scheduler).
- **False facts at scale** — heuristic detectors mislabel; confidence-scored facts + human override.

## 8. Decisions before L0

1. **Profile format** — YAML files under `ProjectProfiles/` (human-editable, like `ProjectTemplates/`)
   vs embedded JSON. Recommend YAML files (`YamlDotNet` already referenced), hot-loadable.
2. **Gap surface** — a `/gaps` page (deterministic, shippable at L3) vs folding into `/runtime`.
   Recommend a dedicated page; it's the product's headline.
3. **Engineer scope in v1** — restrict to *additive* gaps (missing endpoint/DTO/test) before allowing
   *modifying* gaps. Recommend additive-only first; smaller blast radius, easier verification.
