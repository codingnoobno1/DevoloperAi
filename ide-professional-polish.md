# Syncro IDE — Professionalism Plan (vs Antigravity)

> Honest read from the two screenshots: **Antigravity looks like a tool. Syncro looks like an
> AI-generated landing page for a tool.** Same building blocks (Monaco, a tree, a terminal, an AI
> panel) — but Syncro broadcasts that it's a demo: a gradient hero logo, the library names printed
> in the title bar, marketing copy where code should be, and internal dev jargon leaking into the
> chrome. This doc is the comparison, the cut-list, the add-list, and a concrete VS Code-grade
> theme spec.
>
> Companion to [`ideuidevolopment.md`](ideuidevolopment.md) (architecture + W-packages). This is
> the **polish** pass; that is the **capability** plan.
>
> **Direction (updated):** cutting decorations isn't enough on its own — the durable fix is a
> **reusable design system, `Syncro.UI`** (Option 3): ~80+ components + a theme engine + UI
> subsystems that *every* Syncro screen consumes, which is exactly how VS Code is built internally.
> §7 adds the design-system architecture, §8 the **component catalog (80+)**, §9 the **file
> structure**, and §10 the per-component C#/JS boundary (the "Electron-renderer-grade interactions,
> C# backend" model that WebView2 already enables). §3–§6 (cut-list + theme) become the *first
> bricks* of that system, not a one-off pass.

---

## 0. The core diagnosis

Antigravity is a **VS Code fork** — it inherits a decade of restraint: neutral grays, one accent,
file-type icons, breadcrumbs, real tabs, a command palette, a dense informative status bar, and
**zero** self-promotion in the chrome. Syncro is a **from-scratch shell**, and every "look how
cool this is" decision works against it. Professional IDEs are **quiet, dense, and neutral**; they
get out of the way. Syncro is **loud, sparse, and decorative**.

Three rules a professional tool follows that Syncro currently breaks:

1. **The chrome never talks about itself.** No tech names, no version codes, no "pending", no
   "layout ready". (Antigravity's title bar says the *filename*, not "Electron · Monaco · Node".)
2. **The editor area is for the editor.** No hero logo, no tagline. When empty, show a *small*,
   left-aligned Start/Recent list — not a poster.
3. **Restraint over decoration.** One accent used sparingly. No gradients, no glows, no neon. Color
   carries *meaning* (errors red, git orange), not vibe.

---

## 1. Side-by-side comparison

| Dimension | Antigravity (professional) | Syncro (today) | Gap |
|---|---|---|---|
| **Title bar** | Menu bar + centered **filename**; neutral | "⚡ SYNCRO IDE" + chips **Monaco / GoldenLayout / xterm / Roslyn** | self-promo; no menu |
| **Color scheme** | Neutral grays, single blue accent | Purple→cyan **gradients**, glows, neon dots | decorative, not neutral |
| **Editor empty state** | (a file is always open) | **Giant gradient ⚡ + "SYNCRO IDE" + tagline** | landing page, not a Start panel |
| **Explorer** | File-type **icons**, expand arrows, folders | Plain text names, "No folder opened" | no icons, no density |
| **Tabs** | Icon + filename, dirty dot, close | Title text only | no file icons |
| **Editor extras** | Minimap, **breadcrumbs**, line/col, Run button | Minimap only | no breadcrumbs, no run |
| **Terminal** | Real **WSL bash**, git push output | Echo banner: *"ConPTY shell binding pending (W3)"* | not a real shell; leaks W-codes |
| **AI panel** | Rich threaded chat, model picker, @/​/ actions, 👍👎 | One canned greeting + plain composer | chatbot tone, thin |
| **Status bar** | Branch, **0 errors 0 warnings**, Ln/Col, Spaces:4, UTF-8, LF, Python, interpreter, language server | "no workspace · 0 open · **layout ready** · Roslyn · UTF-8" | dev strings, no real info |
| **Activity bar** | Full icon set, badges | Sliver, partly cut off | looks broken |
| **Overall read** | A tool | A demo / AI mockup | — |

---

## 2. Why Syncro reads as "superficial / AI-generated" — the exact tells

These are the specific things that scream "demo." Each is in the code:

| Tell | Where | Why it's unprofessional |
|---|---|---|
| Tech-stack **chips** in title bar | `IdeShell.razor` title bar (`ide-chip` Monaco/GoldenLayout/xterm/Roslyn) | No shipping tool advertises its dependencies in the chrome |
| **Gradient hero logo + title** | `.welcome-logo` (58px ⚡, drop-shadow glow), `.welcome-title` (gradient text) | Landing-page hero; IDEs don't have one |
| **Marketing tagline** | "An AI-native editor grounded in your project's AST, hindsight memory & agents." | Copywriting in the editor surface |
| **Internal jargon in UI** | terminal banner "ConPTY shell binding **pending (W3)**"; status "**layout ready**"; "JS Pivot"; "Syncro IDE · Phase 1" | Leaks the *development process* to the user |
| **Gradient status bar** + glowing dots | `.ide-status` (linear-gradient bg), `.ai-dot` (box-shadow glow), `.live-pulse` | Decoration where there should be information |
| **Chatbot greeting** | `_aiMessages` seed "Hi — I'm the Syncro agent…" | Consumer-chatbot tone, not a dev tool |
| **Oversized welcome buttons** | `.wa` big pill buttons centered | Poster layout, not a Start list |
| Purple/cyan **gradient brand** everywhere | `.ide-brand`, `.welcome-title`, accents | One flat accent reads as a product; gradients read as a template |

> **The pattern:** every one of these is a *decoration* or a *self-reference*. Strip them and
> Syncro instantly reads more like a tool — before adding a single feature.

---

## 3. REMOVE (cut-list — do this first; it's free professionalism)

Pure deletions / replacements in `IdeShell.razor` + `syncro-ide.js`. No new capability needed.

- [ ] **Title-bar tech chips** (Monaco/GoldenLayout/xterm/Roslyn) → delete. Show the **active
      filename** (or workspace name) centered instead, like every IDE.
- [ ] **Hero welcome** → delete the 58px gradient logo, the gradient "SYNCRO IDE" title, and the
      tagline. Replace with a **small left-aligned Start panel** (§5).
- [ ] **Marketing copy** everywhere ("AI-native editor grounded in…", welcome-foot "Hybrid Blazor
      + JS · …") → delete.
- [ ] **Internal jargon from UI**: terminal banner "ConPTY shell binding pending (W3)" → a neutral
      blank prompt; status bar "layout ready" / "JS Pivot" / "Phase 1" → delete. **No W-codes, no
      "pending", no phase numbers anywhere a user can see.**
- [ ] **Gradients & glows**: `.ide-status` gradient → flat accent; `.ide-brand` gradient text →
      flat; `.welcome-title` gradient → n/a (removed); remove `box-shadow` glows on dots/logo;
      remove `drop-shadow` on logo.
- [ ] **Chatbot greeting** in the AI panel → start **empty** with a one-line muted hint
      ("Describe a task…"), not a persona introduction.
- [ ] **Oversized pill buttons** on welcome → small text links in a Start list.
- [ ] **Pulsing "AI online" dot** → a static, muted status text or nothing.

**Net effect:** ~80% of the "AI demo" feel comes off with deletions alone.

---

## 4. ADD (to reach professional density)

Ordered by impact-per-effort. Each maps to a W-package where relevant.

| # | Add | Why it matters | Effort |
|---|---|---|---|
| A1 | **Custom VS Code-grade theme** (§6) — neutral grays, one flat accent, no gradients; applied to Monaco **and** shell | The single biggest "looks pro" lever | S |
| A2 | **File-type icons** in explorer + tabs (Seti/Material icon set, MIT) | Antigravity's explorer reads instantly because of icons | M |
| A3 | **Real status bar** — git branch, ⛔ errors / ⚠ warnings counts, Ln/Col, indent, encoding, EOL, language, interpreter | Density = credibility | M |
| A4 | **Breadcrumbs** bar above the editor (path + symbol) | Standard pro affordance; you have the AST for symbols | M |
| A5 | **Quiet Start/Recent panel** replacing the hero (§5) | Empty state should help, not market | S |
| A6 | **Real terminal** (ConPTY via `ICliRunner`) — W3 | "pending" text is the most damning tell | M |
| A7 | **Command palette** (Ctrl+Shift+P) over a C# command registry — W16 | Defines "an IDE" for power users | M |
| A8 | **Roslyn → Monaco IntelliSense** (diagnostics/hover/completion) — W10 | Feeds A3's error counts + the Problems panel; real editing | L |
| A9 | **Problems panel** fed by A8 (not always-empty) | A panel that's always empty reads as fake | S after A8 |
| A10 | **Run button** + run config for the open project (▶ in editor toolbar) | Antigravity has it; expected | M |
| A11 | **Settings UI** (theme, font, tab size) | "Antigravity Settings" in their status bar; pro tools are configurable | M |

---

## 5. The Start panel (replace the hero)

VS Code's empty editor is a **small, left-aligned, two-column list** — not a poster:

```
Start                         Recent
  New File…                     my-new-project      ~/d/my-new-project
  Open Folder…                  syncro-platform     ~/dev/pixel
  Clone Repository…             (more…)

Walkthroughs
  Get Started with Syncro
```

- Left-aligned, ~13px text links, muted headers, **no logo**, **no gradient**, **no tagline**.
- A tiny wordmark ("Syncro") is fine **once**, top-left, flat color — not a 58px glowing hero.
- This is calmer, more useful, and instantly more professional than the current poster.

---

## 6. Custom theme spec — "Syncro Dark" (VS Code-grade)

The ask: *a custom theme like VS Code.* The current `syncro-dark` is too colorful (purple+cyan,
gradients). Re-spec it as a **neutral, single-accent** theme — VS Code Dark+ structure with one
restrained Syncro accent. **No gradients anywhere.**

### 6.1 Core tokens (shell **and** Monaco share these via CSS variables)

| Role | Color | Notes |
|---|---|---|
| `--bg-editor` | `#1e1e1e` | editor surface (VS Code Dark+) |
| `--bg-panel` | `#1e1e1e` | terminal/output |
| `--bg-side` | `#252526` | explorer / side panels |
| `--bg-activity` | `#333333` | activity bar |
| `--bg-title` | `#2d2d2d` | title bar |
| `--bg-tab-active` | `#1e1e1e` | active tab |
| `--bg-tab-inactive` | `#2d2d2d` | inactive tab |
| `--border` | `#1b1b1b` / `#3c3c3c` | separators |
| `--text` | `#cccccc` | primary |
| `--text-muted` | `#858585` | secondary |
| `--accent` | `#3b82f6` *(or keep a single Syncro indigo `#5b6cff`)* | **flat**, used only for: active-tab underline, focus ring, status bar, links |
| `--status-bg` | `--accent` (flat, **no gradient**) | turns muted gray `#252526` when no folder, like VS Code |
| `--error` | `#f14c4c` · `--warn` `#cca700` · `--ok` `#89d185` | semantic only |

### 6.2 Monaco theme (replaces the colorful `syncro-dark`)

```js
monaco.editor.defineTheme('syncro-dark', {
  base: 'vs-dark', inherit: true,
  rules: [
    { token: 'comment', foreground: '6a9955' },   // VS Code green, not gray-italic
    { token: 'keyword', foreground: '569cd6' },    // VS Code blue
    { token: 'string',  foreground: 'ce9178' },    // VS Code orange-tan
    { token: 'number',  foreground: 'b5cea8' },
    { token: 'type',    foreground: '4ec9b0' }     // VS Code teal
  ],
  colors: {
    'editor.background': '#1e1e1e',
    'editor.foreground': '#d4d4d4',
    'editorLineNumber.foreground': '#858585',
    'editorLineNumber.activeForeground': '#c6c6c6',
    'editor.selectionBackground': '#264f78',
    'editor.lineHighlightBackground': '#2a2d2e',
    'editorCursor.foreground': '#aeafad',
    'editorIndentGuide.background': '#404040',
    'editorGutter.background': '#1e1e1e'
  }
});
```

> Use **VS Code Dark+'s actual token colors** (the table above) instead of the current
> purple/cyan. This is what makes code "look right" to every developer — it's the palette they've
> read for years. Syncro's identity lives in *one* accent on the chrome, not in the syntax colors.

### 6.3 Shell rules

- Replace the gradient `.ide-brand` / `.ide-status` / `.welcome-title` with **flat** `--accent` /
  `--status-bg`.
- Drop all `box-shadow` glows and `drop-shadow` filters.
- Status bar: `--accent` flat when a folder is open, `#252526` (muted) when not — exactly VS Code.
- One accent, used sparingly. Everything else is gray.

---

## 7. Syncro.UI — the reusable design system (the real fix)

Antigravity is consistent because VS Code is built from **one internal component library** used by
every surface. Syncro is inconsistent because each panel is hand-styled in `IdeShell.razor`. The
fix is the same one VS Code made: **build `Syncro.UI` — a single library every screen consumes.**

### 7.1 Why WebView2 makes this "Electron-grade" without Electron

The host is **MAUI → BlazorWebView → WebView2 (Chromium)**. Because the renderer *is* Chromium,
the entire modern web UI toolbox is available to Syncro.UI components — the same primitives
Electron's renderer uses:

> ✅ ES2023+ · TypeScript (compiled) · CSS animations/transitions · Tailwind · Monaco · GoldenLayout
> · xterm.js · Canvas/WebGL · Web Workers · drag & drop · global keyboard shortcuts · full DOM APIs
> · ResizeObserver · IntersectionObserver

The **only** difference from Electron is the backend: **C#/.NET instead of Node** — which for
Syncro is an advantage (Roslyn, AST, Git, agents, workspace state already live in .NET). So the
boundary is clean and fixed:

| Layer | Owns |
|---|---|
| **C# (Blazor component + services)** | parameters, state, events, file ops, Roslyn, Git, AI, workspace/business logic |
| **JS (collocated `*.razor.js` module)** | animations, drag-drop, resize handles, custom context menus, keyboard navigation, virtual scrolling, DOM measurements |

### 7.2 The subsystems (services behind the components)

`Syncro.UI` is more than widgets — it's a small platform. Each subsystem exposes a clean **C#
API** and uses JS internally only where the DOM requires it:

| Subsystem | Role | JS-backed? |
|---|---|---|
| **Design Tokens** | single source of color/space/type (CSS vars, §6) | css |
| **Theme Engine** | load/switch themes, push tokens to Monaco + shell | partial |
| **Icon Service** | file-type + product icons (Seti/Material set) | no |
| **Command Registry** | id → title → keybinding → handler; powers palette + menus | no |
| **Keybinding Manager** | global capture, chords, rebinding | yes (`keybindings.js`) |
| **Notification Manager** | toasts / status messages | no |
| **Context-Menu Engine** | custom right-click menus anywhere | yes (`contextmenu.js`) |
| **Docking Engine** | panels/split/float — GoldenLayout behind `IDockEngine` | yes (`docking.js`) |
| **Window Manager** | native MAUI windows + per-window state | no (C#) |
| **Dialog Service** | modal/drawer orchestration + focus trap | partial |
| **Animation Engine** | enter/exit transitions, reduced-motion aware | yes (`animations.js`) |
| **Editor / Terminal / Graph / Diagram** | Monaco / xterm / Cytoscape / Mermaid behind `IEditorEngine` etc. | yes |

---

## 8. Component catalog (80+) — `Syncro.UI`

Designed in tiers; lower tiers are dependencies of higher ones. Every IDE screen composes **only**
these — never raw HTML in a page. `B` = Blazor-only, `B+JS` = collocated JS module for DOM/interaction.

### Tier 1 — Primitives (~26)
`Button` (B) · `IconButton` (B) · `ToggleButton` (B) · `SplitButton` (B) · `Input` (B) · `Textarea` (B)
· `NumberInput` (B) · `SearchInput` (B) · `Select` (B) · `Combobox` (B+JS) · `Checkbox` (B) · `Radio` (B)
· `Switch` (B) · `Slider` (B+JS) · `Label` (B) · `Kbd` (B) · `Badge` (B) · `Tag` (B) · `Chip` (B)
· `Avatar` (B) · `Spinner` (B) · `ProgressBar` (B) · `ProgressRing` (B) · `Skeleton` (B) · `Divider` (B)
· `Tooltip` (B+JS)

### Tier 2 — Overlays & navigation (~16)
`Dialog`/`Modal` (B+JS focus-trap) · `Drawer`/`Sheet` (B+JS) · `Popover` (B+JS) · `DropdownMenu` (B+JS)
· `ContextMenu` (B+JS) · `MenuBar` (B) · `Tabs` (B) · `TabStrip` (B+JS overflow) · `Breadcrumbs` (B)
· `CommandPalette` (B+JS) · `QuickInput` (B+JS) · `Toast`/`Notification` (B+JS) · `InlineAlert`/`Banner` (B)
· `HoverCard` (B+JS) · `Accordion` (B) · `Tour`/`Walkthrough` (B)

### Tier 3 — Layout & docking (~13)
`Panel` (B) · `DockPanel` (B+JS) · `SplitView` (B+JS) · `ResizeHandle` (B+JS) · `Sidebar` (B)
· `ActivityBar` (B) · `TitleBar` (B) · `StatusBar` (B) · `StatusBarItem` (B) · `Toolbar` (B)
· `ScrollArea` (B+JS) · `Resizable`/`Grid` (B+JS) · `EmptyState` (B)

### Tier 4 — Data & display (~13)
`TreeView` (B+JS virtual+dnd) · `TreeNode` (B) · `VirtualList` (B+JS) · `DataTable` (B+JS)
· `ListView` (B) · `KeyValueGrid` (B) · `PropertyGrid` (B) · `Timeline` (B) · `DiffView` (B+JS Monaco)
· `CodeBlock` (B+JS Monaco read-only) · `Markdown` (B+JS) · `TagInput` (B+JS) · `Pagination` (B)

### Tier 5 — IDE feature components (~24)
`Explorer` (B) · `FileTree` (B+JS) · `FileIcon` (B) · `EditorTabs` (B) · `EditorHost` (B+JS Monaco)
· `SymbolBreadcrumb` (B) · `TerminalHost` (B+JS xterm) · `TerminalTabs` (B) · `ProblemsPanel` (B)
· `OutputPanel` (B) · `SearchPanel` (B) · `SourceControlPanel` (B) · `GitDiff` (B+JS) · `GitGraph` (B+JS)
· `CommitBox` (B) · `AIChat` (B) · `AIMessage` (B) · `AIComposer` (B) · `AgentTaskList` (B)
· `HindsightTimeline` (B) · `ASTGraph` (B+JS Cytoscape) · `DiagramView` (B+JS Mermaid) · `RunToolbar` (B)
· `SettingsPage` (B)

### Tier 6 — Subsystem services (~12, non-visual, see §7.2)
`ThemeEngine` · `IconService` · `CommandRegistry` · `KeybindingManager` · `NotificationManager`
· `ContextMenuService` · `DockingEngine` · `WindowManager` · `DialogService` · `FocusManager`
· `ClipboardService` · `DragDropService`

> **Total: ~92 named components + 12 subsystems.** Build in tier order; Tier 1–3 + the theme engine
> are the foundation everything else stands on.

---

## 9. File structure (`Syncro.UI`)

A Razor Class Library folder (or a sibling `Syncro.UI.csproj`), with **collocated** `.razor` +
`.razor.css` (CSS isolation) + `.razor.js` (JS isolation) per component — Blazor's native
mechanism for "every component has its own JS module."

```
Syncro.Desktop/
├── Syncro.UI/                          # the design system
│   ├── Tokens/
│   │   ├── tokens.css                  # CSS variables — single source of color/space/type (§6)
│   │   └── DesignTokens.cs             # C# mirror (consts) for code that needs token values
│   ├── Theme/
│   │   ├── ThemeEngine.cs              # load/switch, push to Monaco + shell
│   │   ├── ThemeDefinition.cs
│   │   └── themes/  syncro-dark.json · syncro-light.json · high-contrast.json
│   ├── Abstractions/                   # interfaces (provider model, §2.4 of ideuidevolopment.md)
│   │   ├── IEditorEngine.cs · ITerminalEngine.cs · IDockEngine.cs
│   │   ├── IGraphRenderer.cs · IDiagramRenderer.cs
│   │   ├── ICommandRegistry.cs · IKeybindingManager.cs
│   │   ├── INotificationManager.cs · IContextMenuService.cs · IDialogService.cs
│   ├── Components/
│   │   ├── Primitives/
│   │   │   ├── Button/      Button.razor · Button.razor.css
│   │   │   ├── IconButton/  IconButton.razor · IconButton.razor.css
│   │   │   ├── Tooltip/     Tooltip.razor · Tooltip.razor.css · Tooltip.razor.js
│   │   │   └── …            (one folder per primitive)
│   │   ├── Overlays/        Dialog/ · Popover/ · DropdownMenu/ · ContextMenu/ · CommandPalette/ · Toast/ …
│   │   ├── Layout/          Panel/ · SplitView/ · ResizeHandle/ · ActivityBar/ · StatusBar/ · Toolbar/ …
│   │   ├── Data/            TreeView/ · VirtualList/ · DataTable/ · DiffView/ · CodeBlock/ · Markdown/ …
│   │   └── Ide/             Explorer/ · EditorHost/ · TerminalHost/ · SourceControlPanel/ · AIChat/ · ProblemsPanel/ …
│   ├── Services/                       # subsystem implementations (§7.2)
│   │   ├── ThemeEngine.cs · CommandRegistry.cs · KeybindingManager.cs
│   │   ├── NotificationManager.cs · ContextMenuService.cs · DialogService.cs
│   │   ├── DockingEngine.cs · WindowManager.cs · IconService.cs
│   ├── Icons/
│   │   ├── files/                      # Seti/Material SVGs (MIT), mapped by extension
│   │   └── product/                    # app/product glyphs
│   ├── SyncroUiServiceCollectionExtensions.cs   # AddSyncroUi()
│   └── _Imports.razor
│
├── wwwroot/
│   ├── lib/                            # vendored libs (committed): monaco · xterm · golden-layout · cytoscape · mermaid
│   ├── js/ide/                         # shared interop modules (not component-collocated)
│   │   ├── core.js                     # module loader + DotNetRef helpers (interop runtime)
│   │   ├── monaco.js  docking.js  terminal.js  graph.js  diagram.js
│   │   ├── tree.js    contextmenu.js   keybindings.js   animations.js   utils.js
│   ├── css/
│   │   ├── tokens.css                  # imported from Syncro.UI/Tokens
│   │   └── tailwind.css                # GENERATED (only if Tailwind is adopted, §10.3)
│   └── index.html
│
├── Components/IDE/                     # IDE pages — compose Syncro.UI, contain no raw styling
│   ├── IdeShell.razor                  # now just lays out <ActivityBar/> <Explorer/> <EditorHost/> …
│   └── …
├── tailwind.config.js  package.json    # (only if Tailwind adopted)
└── Syncro.Desktop.csproj               # AddSyncroUi() registered in MauiProgram
```

**Collocation rule:** component-specific JS lives next to the component as `X.razor.js` (Blazor JS
isolation, imported as `./_content/Syncro.UI/Components/.../X.razor.js`). **Cross-cutting** JS
(Monaco, docking, terminal, the interop runtime) lives in `wwwroot/js/ide/` and is shared.

---

## 10. Per-component pattern + the Tailwind decision

### 10.1 The three-file component (what "Electron JS component with Blazor" means here)

```razor
@* Components/Primitives/IconButton/IconButton.razor *@
<button class="sui-iconbtn @(Active ? "is-active" : "")" title="@Title" @onclick="OnClick">
    <i class="@Icon"></i>
</button>
@code {
    [Parameter] public string Icon { get; set; } = "";
    [Parameter] public string? Title { get; set; }
    [Parameter] public bool Active { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
}
```
```css
/* IconButton.razor.css — scoped (CSS isolation), uses tokens only */
.sui-iconbtn { width:40px; height:40px; border:0; background:transparent; color:var(--text-muted);
    border-radius:var(--radius); cursor:pointer; display:flex; align-items:center; justify-content:center; }
.sui-iconbtn:hover { color:var(--text); background:var(--hover); }
.sui-iconbtn.is-active { color:var(--text); box-shadow:inset 2px 0 0 var(--accent); }
```
```js
// Tooltip.razor.js — collocated module, only when the DOM needs JS (positioning, ResizeObserver…)
export function attach(el, dotnet) { /* position popover, observe resize, return disposer */ }
```

C# stays the contract; the optional `.razor.js` handles only DOM-native behavior. This is the same
separation high-quality web apps use — and what makes Syncro.UI feel native without Electron.

### 10.2 Consumption — pages stop hand-styling

`IdeShell.razor` shrinks to composition; **no inline `<style>` blocks**, no raw chrome:

```razor
<TitleBar />
<div class="ide-body">
  <ActivityBar Items="_activities" />
  <DockPanel>
    <Explorer />            <EditorHost />          <AIChat />
    <TerminalHost />        <ProblemsPanel />
  </DockPanel>
</div>
<StatusBar Branch="@_branch" Errors="@_errors" Warnings="@_warns" Line="@_ln" Col="@_col" />
```

### 10.3 Tailwind — yes, but scoped and token-driven

Tailwind is a good fit **for Syncro.UI**, with three rules so it doesn't become utility soup or
collide with MudBlazor:

1. **Tokens are the source of truth.** Configure `tailwind.config.js` to read the §6 design tokens
   (`colors.bg.editor = 'var(--bg-editor)'`, etc.) so Tailwind *consumes* tokens, never hardcodes.
2. **Scope it to the IDE surface.** Tailwind styles `Syncro.UI` (the IDE window). The **main app
   stays MudBlazor** — don't migrate it; don't run both on the same elements.
3. **Components, not pages.** Utilities live inside `Syncro.UI` component templates (or their
   `.razor.css`), so pages compose semantic components — they never see Tailwind classes.

> Alternative if you'd rather not add a JS build step: **skip Tailwind**, use the design tokens +
> per-component scoped CSS directly (the `.razor.css` shown above). This is fully sufficient for a
> design system and keeps the toolchain pure .NET. **Recommendation: start token+scoped-CSS; add
> Tailwind only if the team wants utility ergonomics** — the tokens make the switch non-breaking.

---

## 11. Honest tradeoff (why Antigravity is ahead, and the choice)

Antigravity is a **VS Code fork** — it gets file icons, breadcrumbs, the command palette, settings,
the language-server protocol, and a decade of restraint **for free**. Syncro chose a **custom
shell** (documented decision: don't embed VS Code wholesale). That choice is still right for
Syncro's AST/agent integration — **but it means polish is not free; it must be built.** Two paths:

- **Path A — Custom shell + build `Syncro.UI`** (chosen, §7–§10): do §3 (cut) + §6 (theme) as the
  first bricks, then grow the design system tier by tier. Reaches professional feel *and* leaves a
  reusable library every screen shares. Keeps full control + deep Syncro/C# integration.
- **Path B — Reconsider OpenVSCode/Theia** for the editor surface only: more polish out of the box,
  but you inherit a Node backend and lose the tight C# integration (rejected earlier for good
  reasons — see `ideuidevolopment.md` §3.4).

Recommendation: **Path A.** The §3 cut-list + §6 theme + A2/A3 (icons + real status bar) closes
most of the *perceived* gap immediately; packaging them as `Syncro.UI` Tier-1–3 components (§8)
makes that polish **reusable** instead of one-off, so consistency holds as the IDE grows.

---

## 12. Phased plan

Each phase now *also* deposits reusable `Syncro.UI` components, so polish compounds instead of
being thrown away.

| Phase | Work | `Syncro.UI` deposit | Outcome |
|---|---|---|---|
| **P0 — Cut + tokens (hours)** | §3 deletions + §6 theme; create `Tokens/tokens.css` + `ThemeEngine` | Design Tokens, Theme Engine | Stops reading as an AI demo |
| **P1 — Primitives & layout** | Tier 1–3 (§8): Button/IconButton/Input/Tabs/Panel/SplitView/ActivityBar/StatusBar/Toolbar | ~40 components | Chrome is consistent + reusable |
| **P2 — Density** | A2 icons (`FileIcon`+IconService) · A3 `StatusBar` real data · A5 `EmptyState` Start · A4 `SymbolBreadcrumb` | Tier 4–5 begins | Reads as a real editor |
| **P3 — Make it work** | A6 `TerminalHost` (ConPTY) · A8 Roslyn→Monaco → A9 `ProblemsPanel` | `EditorHost`, `TerminalHost`, `ProblemsPanel` | Functional, not staged |
| **P4 — Pro affordances** | A7 `CommandPalette` + `CommandRegistry`/`KeybindingManager` · A10 `RunToolbar` · A11 `SettingsPage` | subsystems + Tier 5 | Power-user complete |

P0 is still the highest leverage per minute — **deletions + a theme swap** — but it now lands as
the design system's foundation (tokens + theme engine), so P1+ builds *on* it rather than beside it.

---

## 13. Acceptance criteria ("tool, not demo")

- [ ] **Every IDE screen composes `Syncro.UI` components** — no raw HTML chrome or inline `<style>`
      blocks left in pages like `IdeShell.razor`.
- [ ] One token set (`tokens.css`) drives both the shell and Monaco; switching the theme restyles
      everything.
- [ ] No technology names, version numbers, phase/W-codes, or "pending" text anywhere in the UI.
- [ ] No gradients or glows in the chrome; one flat accent used sparingly.
- [ ] Empty editor shows a small left-aligned Start/Recent — no hero logo, no tagline.
- [ ] Syntax colors match VS Code Dark+ (developers recognize them instantly).
- [ ] Explorer and tabs have file-type icons.
- [ ] Status bar shows real, dense info (branch, errors/warnings, Ln/Col, encoding, language).
- [ ] Terminal is a real shell, or shows nothing — never "pending (W3)".
- [ ] A screenshot of Syncro next to VS Code/Antigravity is not instantly distinguishable as "the
      amateur one."
