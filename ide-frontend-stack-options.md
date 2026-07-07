# Syncro IDE — Frontend Stack Decision (Electron? React+shadcn? Blazor?)

> The question: do we get "professional IDE components" by going **Electron** (like VS Code /
> Antigravity), by building richer components ourselves in **C# + HTML/CSS/JS**, or by adopting
> **shadcn/ui + Tailwind**? This doc analyzes each against the *actual* current stack and gives a
> recommendation.
>
> Current stack (verified): **.NET MAUI** host → **BlazorWebView (WebView2)** → **Blazor + MudBlazor**
> chrome → **JS interop** for the heavy surfaces (**Monaco, GoldenLayout, xterm** — all vendored,
> MIT). No Electron, no Node runtime.

---

## 0. Clearing up three misconceptions first

These matter because two of the three options don't fit the way they sound like they should.

### "Implement reusable Electron like VS Code/Antigravity"
**You can't add Electron to a MAUI app — Electron *is* the application.** Electron = a bundled
Chromium **+ Node.js** that is the process model, window manager, and backend. VS Code and
Antigravity are Electron apps from line one. To "use Electron" you would **delete the MAUI host and
rewrite the shell** as an Electron app (`Electron.NET` exists but is a different architecture and is
effectively unmaintained). That also throws away the reason Syncro is MAUI: the **C# brain** (AST,
Roslyn, agents, MCP) runs in-process. → **Not an incremental option. It's a rewrite.**

### "shadcn/ui"
**shadcn/ui is not a library — it's a set of copy-paste *React* components** (built on Radix UI +
Tailwind). It runs in React/Next, **not in Blazor**. You cannot `@inject` or `<UseShadcn />` it into
a Razor page. To use shadcn you must build that UI **in React**. → shadcn is only on the table if
you build the IDE surface as a **React app**, not Blazor.

### "Tailwind"
**Tailwind works in Blazor Hybrid** — it's just a generated CSS file of utility classes you apply in
Razor markup. The catch: you already use **MudBlazor** (which ships its own design system + CSS).
Running Tailwind *and* MudBlazor means two overlapping styling systems fighting over the same
elements. Doable, but messy. → Tailwind is usable, but pick a lane per surface; don't sprinkle it
on top of MudBlazor.

---

## 1. The three real options

| | **A. Blazor + JS hybrid** (current) | **B. React + shadcn + Tailwind island** | **C. Electron** |
|---|---|---|---|
| App shell | MAUI (keep) | MAUI (keep) | **rewrite to Electron** |
| Renderer | WebView2 | WebView2 | bundled Chromium |
| IDE UI built in | Blazor/Razor + JS interop | **React + shadcn + Tailwind** (Vite app in `wwwroot`) | React/HTML (Electron renderer) |
| Heavy surfaces | Monaco/xterm/GoldenLayout (have) | same JS libs (native fit in React) | same |
| C# backend bridge | **Blazor JS interop** (have, easy) | JS interop **or** local HTTP/WS to C# | IPC to a separate .NET process |
| Component polish out-of-box | low (hand-build) | **high (shadcn)** | high (reuse VS Code) |
| Reuses current work | **all of it** | little (rebuild shell in React) | almost none |
| Effort to "pro" | medium | medium-high (rebuild + learn) | very high (rewrite) |
| Keeps C# integration | ✅ tight | ⚠️ via a bridge | ⚠️ separate process |

---

## 2. How you'd actually *use* each (concretely)

### A. Blazor + JS hybrid (what's already here)
- **C#**: services, state, AST/Roslyn/agents (unchanged).
- **Razor + scoped CSS**: chrome (activity bar, status bar, panels, Start screen). Build the
  "professional components" by hand (per [`ide-components-evaluation.md`](ide-components-evaluation.md)):
  file icons, real status bar, breadcrumbs, command palette.
- **JS interop**: Monaco/xterm/GoldenLayout behind the provider model (`IEditorEngine`, etc.).
- **MudBlazor**: dialogs, menus, snackbars, settings forms.
- *Optional Tailwind*: only if you drop MudBlazor for the IDE chrome and style everything with
  utilities. Not recommended while MudBlazor is the main-app kit.

### B. React + shadcn + Tailwind island (the "shadcn" path)
- Create a **Vite + React + Tailwind** app whose build output goes to `wwwroot/ide/` (or a separate
  WebView page). `npx shadcn@latest add button dialog command tabs …` copies polished components
  into the React app.
- Host it in a `BlazorWebView`/`WebView` page (or the existing one), or run it standalone.
- **Bridge to C#**: either
  - `window.chrome.webview.postMessage` / `JSInterop` to call C# (works because it's the same
    WebView2), or
  - a tiny **local HTTP/WebSocket API** in the MAUI process that React calls (`/api/fs`, `/api/ast`,
    `/api/agent`) — this is the cleanest, mirrors how cloud IDEs talk to a backend.
- Monaco/xterm/GoldenLayout integrate **natively** in React (`@monaco-editor/react`, `xterm` hooks).
- **Cost**: you rebuild the shell that already exists in Blazor, run a JS build toolchain, and own a
  C#↔React bridge. **Benefit**: shadcn's component quality is the fastest route to a polished,
  modern look, and the IDE surface is *already* mostly JS anyway.

### C. Electron — not recommended (rewrite). Listed for completeness only.

---

## 3. Recommendation

**Don't do Electron.** It's a rewrite that discards MAUI and the in-process C# brain — and the
earlier docs already rejected embedding VS Code wholesale for the same reasons.

**Then it's A vs B**, and the honest call depends on one question: *how much does shadcn's
out-of-box polish matter vs. the cost of rebuilding the working shell in React?*

- If the priority is **ship a professional-feeling IDE without a rewrite** → **Option A.** The
  "professional" gap is **not** a framework problem; it's the missing workflow components + chrome
  restraint already enumerated in [`ide-components-evaluation.md`](ide-components-evaluation.md) and
  [`ide-professional-polish.md`](ide-professional-polish.md). Hand-building file icons, a real
  status bar, breadcrumbs, and a command palette in Razor is **less work** than rebuilding the
  shell in React — and keeps the tight C# integration.

- If the priority is **maximum component polish + modern DX and you accept a rebuild** → **Option B**,
  scoped to the IDE surface only (the main app stays Blazor/MudBlazor). shadcn + Tailwind + React +
  `@monaco-editor/react` is a legitimately excellent IDE-frontend stack and pairs naturally with the
  JS libraries you've already vendored. Treat it as a **deliberate rewrite of the IDE window**, not
  a sprinkle.

**My recommendation: Option A now, keep Option B as a known escape hatch.** Reasons: the shell,
docking, Monaco provider model, AI-loop wiring, and vendored libs already work; the remaining gap is
a *bounded list of components*, not a framework. Build those (icons, status bar, palette, real
terminal, Roslyn→Monaco) and Syncro reaches professional feel without a second UI paradigm. Revisit B
only if, after that, the hand-built components still don't satisfy and shadcn's polish is worth a
React rewrite of the IDE window.

> Note: **A and B both keep MAUI + WebView2 + the C# backend.** The only thing that changes between
> them is whether the IDE *surface* is Blazor or React. So choosing A now does **not** lock you out
> of B later — the C# services and the JS surface libraries are reused either way.

---

## 4. If you choose B — the migration shape (so it's not a surprise)

1. `wwwroot/ide-react/` → Vite + React + TS + Tailwind; `shadcn init`.
2. Components: `command` (palette), `resizable`/docking (or keep GoldenLayout), `tabs`, `dialog`,
   `tooltip`, `context-menu`, `scroll-area`, `tree` (custom), plus `@monaco-editor/react` and
   `xterm` React wrappers.
3. C# bridge: add a minimal local API in the MAUI process (reuse the existing :3030-style listener
   pattern) exposing `fs`, `ast`, `git`, `agent`, `terminal` endpoints — React calls these.
4. Point a `WebView`/`BlazorWebView` page at the built React `index.html`.
5. Port panels one at a time; keep Blazor IDE working until React reaches parity.

Effort: ~2–4 weeks for parity with what exists, mostly the bridge + re-porting panels.

---

## 5. Bottom line

- **Electron:** no (rewrite, abandons MAUI/C# brain).
- **shadcn/ui:** React-only — usable **only** if you build the IDE surface in React (Option B).
- **Tailwind:** usable in Blazor, but conflicts with MudBlazor; pick per-surface.
- **Best path:** **Option A** — finish the bounded component list in Blazor + JS; that closes the
  professionalism gap without a rewrite. Keep **Option B** (React + shadcn) as a deliberate,
  reversible escape hatch for the IDE window if polish still falls short.
