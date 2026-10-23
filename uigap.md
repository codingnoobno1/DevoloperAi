# uigap.md — UI Flaws & Gaps in Syncro.Desktop

> Audit of the **Git Manager**, **Scanner** (Universe / Security), the **IDE shell**,
> and the **global theming** that makes the app feel "blurry / inconsistent".
> Each item lists the file, an approximate line anchor, the problem, and a fix direction.
> Severity: 🔴 blocking / broken · 🟠 confusing / incomplete · 🟡 polish.

---

## 0. The "blurry / inconsistent look" — root cause (global)

The app renders through **three competing styling systems at once**, so the same
concept (a card, a color, a radius) is expressed three different ways depending on
which file authored the markup. This is the single biggest reason the UI reads as
inconsistent.

| # | System | Where | Symptom |
|---|--------|-------|---------|
| 1 | **MudBlazor** theme (C# `MudTheme`) | `MainLayout.razor` `OnInitializedAsync` | component defaults |
| 2 | **Tailwind** utilities | `wwwroot/css/tailwind.css` + `index.html:9` | raw `text-gray-500`, `bg-indigo-600` |
| 3 | **Custom CSS tokens** (`--bg-side`, `--text`, `--fs-ui`) + **`--mud-palette-*`** overrides | `IdeShell.razor`, shared components | a 3rd/4th color vocabulary |

- 🔴 **`wwwroot/index.html:9` + `:21`** load **both** `tailwind.css` **and** `MudBlazor.min.css`
  app-wide — two full frameworks with overlapping resets and color scales fighting each other.
- 🟠 **`tailwind.css` (~lines 1246–1301, 1636–1723)** mixes hardcoded palette classes
  (`.text-gray-500`, `.border-red-400`) with token-mapped ones (`.text-textMain { color: var(--text) }`).
  A component using raw Tailwind grays visually clashes with the token-driven dark theme.
- 🟠 **`MainLayout.razor` (~lines 91–112 vs 186–198)** defines colors **twice** — once in the
  C# `MudTheme` object and again as local CSS `:root` hex vars (`#0066FF`, `#F1F5F9`). Two sources of truth.
- 🟠 Shared components (`LivePreviewPane`, `MobilePreview`, `MobileRunSelector`) declare
  `--mud-palette-*` vars directly instead of Syncro's `--bg-side`/`--text` tokens — a *fourth* vocabulary.
- 🟡 Per-component `!important` overrides fight MudBlazor specificity ad hoc
  (`IdeShell.razor:284–297`, `MobilePreview.razor:368`, `NavMenu.razor:110–113`) instead of a central layer.

**Fix direction:** pick **one** system as the source of truth (recommend: MudBlazor theme +
a single `tokens.css` mapped to `--mud-palette-*`), delete the Tailwind include unless a
purged, token-mapped build is genuinely needed, and remove the duplicate hex `:root` block.

---

## 1. Git Manager — `Components/Pages/Git/GitManager.razor`

### Missing features (functional gaps)
- 🔴 **No diff viewer.** `IGitEngine.DiffAsync` exists but is never called from the page — you
  can see commit lists and stat counts but never a file-level diff.
- 🔴 **No PR / merge-request creation.** No button, no base-branch field, nothing. (Repo-wide
  search finds zero PR-creation UI.) → addressed by `scripts/git/git-open-pr.ps1`.
- 🔴 **No account / OAuth / PAT connection UI.** The **Credentials** tab only *detects* local
  SSH/GPG/credential-helper presence — it never lets you *add* a credential or sign into
  GitHub/GitLab. → addressed by `scripts/git/git-connect-github.ps1` / `git-connect-ssh.ps1`.
- 🟠 **No remote management.** Can't add/change/remove a remote or view multiple remotes; the
  page always assumes whatever `origin` already is (`GitEngine.cs:247` hardcodes
  `--set-upstream origin HEAD`). → addressed by `scripts/git/git-set-remote.ps1`.
- 🟠 **No merge-conflict UI.** On a conflicting Pull/Checkout/Workflow, the raw `GitResult.Error`
  string is dumped into a Snackbar/`<pre>` — no conflict file list, no ours/theirs picker, no
  abort button. → addressed by `scripts/git/git-resolve-conflicts.ps1`.

### Broken / dead / placeholder
- 🔴 **`GitStatus.razor` (route `/gitstatus`) is a stub** — literally
  `<h3>Git Status</h3><p>Content for Git Status goes here.</p>`. Orphaned; superseded by
  `GitManager.razor`. Either delete the route or redirect it.
- 🟠 **Auto-sync toggle only ever sets `AutoFetch=true`.** `GitSyncPolicy` supports
  `AutoCommit`/`AutoPush`/`BranchAllowlist` but there's no UI to reach them — those fields are
  dead from the user's perspective.
- 🟠 **Workflow tab has no persistence.** Steps typed into `_wfText` are lost on navigation;
  there's no "save as named workflow" despite `GitWorkflow.cs` describing reusable named sequences.
- 🟠 **Copilot output is a single flat `MudAlert` (`_copilotOutput`).** No streaming, no
  per-tool-call transcript — you can't tell which MCP tools the agent ran (e.g. whether it
  silently invoked `RunBatchScript` with a force-push).

### States & feedback
- 🟠 **No error/empty states** for the History/Contributors/Branches tables beyond MudTable
  defaults — no "no commits yet" and no persistent fetch-failure banner (toasts are transient).
- 🟠 **No loading skeletons per tab.** Only the top-level `_busy` flag disables buttons; the four
  `await Git.*Async` calls in `Refresh()` run **sequentially**, so a large repo visibly stalls the whole page.
- 🟡 **Push confirmation is a "click-twice" pattern** (`_pushArmed`, ~`GitManager.razor:294–309`)
  — the button label doesn't change on arm, only a Snackbar appears, so it's easy to misclick.

---

## 2. Universe Scanner — `Components/Pages/Connector/UniverseScan.razor`

The page is genuinely data-driven (bound to `MultiProjectScanService`, "Live, nothing hardcoded"),
so these are UX gaps, not fake data:

- 🟠 **`PickRoot()` (~:513–526) doesn't auto-scan** after picking a folder — it only sets `_root`
  and calls `StateHasChanged()`. Inconsistent with `SecurityScan.razor`'s `Pick()` (~:102) which
  *does* auto-scan. Users expect picking a folder to start the scan.
- 🟠 **No cancel button while scanning.** `MultiProjectScanService.ScanAsync` accepts a
  `CancellationToken` but the page passes `default` — a scan on a huge tree can't be aborted.
- 🟠 **Per-project failures are invisible.** `ProjectScan.Notes` (populated on resolve failures,
  `MultiProjectScanService.cs:126`) is stored but **never rendered** — a failed project just shows
  0 routes/calls with no reason.
- 🟡 **Connection-graph SVG has fixed layout constants (~:372)** and only `overflow-x:auto` — with
  many projects it grows tall/wide with no zoom or scroll affordance.
- 🟡 **No retry after failure** — errors surface only as a transient Snackbar (~:466); the user must
  re-click "Scan all". No persistent error banner.
- 🟡 **Models/DTOs `_typeFilter` box has no clear (×) button**, unlike the Connections filter.

---

## 3. Security Scanner — `Components/Pages/Security/SecurityScan.razor`

Real end-to-end (bound to `SecurityScanner`), but:

- 🟠 **No spinner/progress.** It disables the button via `_busy` but shows no
  `MudProgressCircular` like `UniverseScan.razor` does — inconsistent loading feedback.
- 🟠 **Finding-count mismatch risk.** The MCP `SecurityScan` tool caps findings at 200 while the
  UI table is unbounded — an LLM consumer may think there are only 200.
- 🟡 **No history / export / CI cross-link.** The page can't compare past scans, export results,
  or hand its findings to the CI/CD YAML generator (`PipelineBuilder` already references
  Trivy/Gitleaks), even though the two subsystems are conceptually linked.

---

## 4. IDE Shell — `Components/IDE/IdeShell.razor`

- 🔴 **"AST Graph" activity icon is mislabeled** — id `"ast"` (~:377) opens the **Hindsight** panel
  (`case "ast": Focus("blazor-hindsight-portal")`, ~:484), not an AST graph. Clicking it confuses.
- 🟠 **"Search" activity icon (~:375) has no search UI** — it just focuses the Explorer (~:481).
  The icon promises a feature that doesn't exist.
- 🟠 **CodeOSS fallback panel (~:56–71) is developer-facing** — raw exception text (~:348) in a
  `<pre>` with no icon/hierarchy; reads as an error dump rather than a user message.
- 🟠 **Output panel (~:149–159) has no empty state** (Problems panel does, ~:133). Blank when idle.
- 🟠 **No workbench loading state** while GoldenLayout initializes (`OnAfterRenderAsync`, ~:400) —
  panels pop in with no skeleton/spinner. Terminal panel (~:125) has no chrome/connecting indicator.
- 🟡 **Hardcoded pixel styles** scattered inline (`padding:18px`, `font-size:15px`, ~:50–72, :126)
  mix with the token system (`var(--fs-ui)`, ~:219) → inconsistent spacing.
- 🟡 **Icon-only buttons lack accessible names** — e.g. the AI send button (~:180) has no `aria-label`.
- 🟡 **Empty placeholder** `<!-- Add explorer actions here later -->` (~:78) ships as a visible TODO seam.

---

## 5. Layout & Nav — `MainLayout.razor`, `NavMenu.razor`

- 🔴 **Header user is hardcoded** — "Tushar" / "Admin" (`MainLayout.razor:62–63`) + static
  `images/syncro.jpg` (~:59), not bound to auth/profile. Misleads any other signed-in user.
- 🟠 **Fake live-status pills.** "CLI Ready" / "API Online" (~:33–38) are static markup with a
  pulsing dot but no binding to real health.
- 🟠 **Dead notification bell** (~:47–49) — no `OnClick`, only a `title`.
- 🟡 **Dead code**: `NetlifySvc.GetSiteStatus` result (`_netlifySite`, ~:115) is fetched but never
  rendered; `OpenMobileStore()` (~:168–182) is defined but never invoked from this file.
- 🟠 **NavMenu mixes navigation patterns without visual distinction** — most items use `Href=`
  (routes) but Editor (:35), Mobile Toolkit (:39), Insights (:47) use `OnClick=` (open a window).
  Users can't tell which behave which way.
- 🟡 **Arbitrary group expansion** — only "WORKSPACE" is `Expanded` (~:27); DEVELOPMENT/
  INTELLIGENCE/ECOSYSTEM default collapsed (~:34/42/50), hiding most of the app's nav with no cue.
- 🟡 **Inline styles** for brand icon/button/text (`NavMenu.razor:7–10,19`) instead of CSS classes.

---

## 6. Shared preview components

`LivePreviewPane.razor`, `MobilePreview.razor`, `MobileRunSelector.razor` are mostly wired to real
services with reasonable running/ready/error states, but:

- 🟠 **`MobilePreview.razor` AI sidebar writes files from LLM JSON with a single click**
  (`ProcessAiRequest`, ~:422–565) — no diff/preview/confirm step and no undo. Destructive UX.
- 🟡 **Fake "live" clock** — `DateTime.Now.ToString("HH:mm")` (`MobilePreview.razor:79`) renders once
  and never ticks; the status-bar time goes stale.
- 🟡 **Inconsistent guardrails in `MobileRunSelector.razor`** — the emulator card disables Launch on
  low resources (~:81) but the "ADB (Physical Device)" card only shows a warning icon (~:32–37) and
  stays selectable.
- 🟡 **Duplicated device-scaling hack** — `scale(min(1, calc(100vh/950)))` copy-pasted in
  `LivePreviewPane` and `MobilePreview.razor:301` instead of a shared style.

---

## Priority shortlist

| Priority | Item |
|---|---|
| P0 | Collapse the 3-way styling conflict (§0) — the root of "blurry" |
| P0 | Add account-connection + PR + diff to Git Manager (§1) — or wire in the new `scripts/git/*` |
| P1 | Delete/redirect the `GitStatus.razor` stub; fix mislabeled IDE "AST Graph"/"Search" icons (§1, §4) |
| P1 | Bind the hardcoded header user & fake status pills to real state (§5) |
| P1 | Add confirm/diff before the Mobile AI file-write (§6) |
| P2 | Loading/empty/error states across Git Manager, Universe, Security (§1–3) |
