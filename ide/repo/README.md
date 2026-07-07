# ide/repo — IDE library vendoring (staging area)

This folder is the **staging area** for the open-source IDE libraries the Syncro IDE reuses,
per the plan in [`../../ideuidevolopment.md`](../../ideuidevolopment.md) §3.2 (provenance) and §11
(work packages). Libraries are fetched as their **published distributions** via `npm pack` (the
real, usable repo content — prebuilt, no build step needed), extracted here for reference, and the
**runtime subset is copied into `wwwroot/lib/`**, which is what the app actually loads.

> **Why npm-pack, not `git clone`:** Monaco/xterm/etc. ship prebuilt distributables on npm; their
> GitHub source repos require a full toolchain build. `npm pack <pkg>` gives the exact files the
> ecosystem consumes — the right thing to vendor for an offline-first desktop binary.

## What was vendored

| Library | Version | License | Staged here | Shipped to `wwwroot/lib/` |
|---|---|---|---|---|
| Monaco Editor | 0.45.0 | MIT | `monaco-editor-0.45.0/` | `monaco/vs/**` (AMD `min/vs`) |
| jQuery (for GoldenLayout 1.x) | 3.6.0 | MIT | `jquery-3.6.0/` | `jquery/jquery.min.js` |
| Golden Layout | 1.5.9 | MIT | `golden-layout-1.5.9/` | `golden-layout/goldenlayout.min.js` + `css/` |
| Xterm.js | 6.0.0 (`@xterm/xterm`) | MIT | `xterm-xterm-6.0.0/` | `xterm/xterm.js` + `xterm.css` |
| Xterm fit addon | 0.11.0 (`@xterm/addon-fit`) | MIT | `xterm-addon-fit-0.11.0/` | `xterm/addon-fit.js` |
| Cytoscape.js | 3.34.0 | MIT | `cytoscape-extract/` | `cytoscape/cytoscape.min.js` |
| Mermaid | 11.16.0 | MIT | `mermaid-extract/` | `mermaid/mermaid.min.js` |
| Split.js | 1.6.5 | MIT | `split.js-extract/` | `split/split.min.js` |
| Interact.js | 1.10.27 | MIT | `interactjs-extract/` | `interact/interact.min.js` |

**All MIT** → clean to ship in a closed-source desktop binary (see §3.2 license matrix).

## Loaded by

- `wwwroot/index.html` — `<link>`/`<script>` tags point at `lib/**` (no CDN).
- `wwwroot/js/syncro-ide.js` — Monaco AMD loader: `require.config({ paths: { vs: 'lib/monaco/vs' }})`.

## Reproduce / upgrade

```bash
cd ide/repo
npm pack monaco-editor@0.45.0 jquery@3.6.0 golden-layout@1.5.9 @xterm/xterm @xterm/addon-fit \
         cytoscape mermaid split.js interactjs
# extract each *.tgz (strip the leading package/ dir) then copy the runtime subset into wwwroot/lib/
```

## Note

This staging tree (tarballs + extracted sources, tens of MB) is **git-ignored** — only this
README is tracked. The shipped runtime copies under `wwwroot/lib/` are committed. The whole
`ide/**` tree is excluded from the MSBuild compile/content globs in `Syncro.Desktop.csproj`.
