/*
 * monaco.js — the Monaco editor SURFACE module (IDE doc §2.4 / §3 / W2).
 *
 * This is the ONLY file that knows Monaco's API. Everything else (docking, the Blazor shell,
 * the C# IEditorEngine) talks to window.SyncroMonaco. Swapping Monaco for another editor later
 * means rewriting only this file. No docking / GoldenLayout knowledge lives here.
 *
 * Loaded as a classic script AFTER Monaco's AMD loader (lib/monaco/vs/loader.js) and BEFORE
 * js/syncro-ide.js.
 */
window.SyncroMonaco = (function () {
    let _editors = {};   // uri -> monaco editor instance
    let _diffs = {};     // hostId -> diff editor
    let _ready = null;

    require.config({ paths: { 'vs': 'lib/monaco/vs' } });

    function defineTheme() {
        try {
            const commonRules = [
                { token: 'comment', foreground: '6a9955' },
                { token: 'keyword', foreground: '569cd6' },
                { token: 'string', foreground: 'ce9178' },
                { token: 'number', foreground: 'b5cea8' },
                { token: 'type', foreground: '4ec9b0' }
            ];

            const createDarkTheme = (bg, fg, sel, line) => ({
                base: 'vs-dark', inherit: true, rules: commonRules,
                colors: {
                    'editor.background': bg, 'editor.foreground': fg,
                    'editor.selectionBackground': sel, 'editor.lineHighlightBackground': line,
                    'editorLineNumber.foreground': '#858585', 'editorGutter.background': bg
                }
            });

            const createLightTheme = (bg, fg, sel, line) => ({
                base: 'vs', inherit: true, rules: [
                    { token: 'comment', foreground: '008000' },
                    { token: 'keyword', foreground: '0000ff' },
                    { token: 'string', foreground: 'a31515' },
                    { token: 'number', foreground: '098658' },
                    { token: 'type', foreground: '2b91af' }
                ],
                colors: {
                    'editor.background': bg, 'editor.foreground': fg,
                    'editor.selectionBackground': sel, 'editor.lineHighlightBackground': line,
                    'editorLineNumber.foreground': '#2b91af', 'editorGutter.background': bg
                }
            });

            monaco.editor.defineTheme('theme-syncro-special', createDarkTheme('#0f121b', '#e2e8f0', '#2a304a', '#1f2438'));
            monaco.editor.defineTheme('theme-syncro-dark', createDarkTheme('#1e1e1e', '#d4d4d4', '#264f78', '#2a2d2e'));
            monaco.editor.defineTheme('theme-syncro-light', createLightTheme('#ffffff', '#333333', '#add6ff', '#e8e8e8'));
            monaco.editor.defineTheme('theme-midnight', createDarkTheme('#001122', '#aaccff', '#003366', '#002244'));
            monaco.editor.defineTheme('theme-hc-dark', createDarkTheme('#000000', '#ffffff', '#444444', '#222222'));
            
            // Solarized approximations
            monaco.editor.defineTheme('theme-solarized-dark', createDarkTheme('#002b36', '#839496', '#073642', '#073642'));
            monaco.editor.defineTheme('theme-solarized-light', createLightTheme('#fdf6e3', '#657b83', '#eee8d5', '#eee8d5'));
            
            monaco.editor.defineTheme('theme-monokai', createDarkTheme('#2d2a2e', '#fcfcfa', '#403e41', '#3a373b'));
            monaco.editor.defineTheme('theme-github-dark', createDarkTheme('#0d1117', '#c9d1d9', '#21262d', '#161b22'));
            monaco.editor.defineTheme('theme-dracula', createDarkTheme('#282a36', '#f8f8f2', '#44475a', '#44475a'));

            // Set default theme from localStorage if available
            try {
                let saved = localStorage.getItem("syncro-settings");
                if (saved) {
                    let opts = JSON.parse(saved);
                    if (opts.ThemeName) {
                        monaco.editor.setTheme(opts.ThemeName);
                        baseOptions.theme = opts.ThemeName;
                    }
                }
            } catch (e) {}

        } catch (e) { /* theme optional */ }
    }

    // Resolve once Monaco's AMD module is loaded + theme defined.
    function ready() {
        if (_ready) return _ready;
        _ready = new Promise(resolve => {
            require(['vs/editor/editor.main'], function () { defineTheme(); resolve(); });
        });
        return _ready;
    }

    const baseOptions = {
        theme: 'syncro-dark',
        automaticLayout: true,
        minimap: { enabled: true, scale: 0.8 },
        fontSize: 13, lineHeight: 20,
        fontFamily: "'JetBrains Mono','Cascadia Code',monospace",
        fontLigatures: true, smoothScrolling: true, cursorBlinking: 'smooth',
        roundedSelection: true, padding: { top: 12 }, scrollBeyondLastLine: false,
        renderLineHighlight: 'all'
    };

    // Mount an editor into `host` for `uri`. dotnetRef receives change/save callbacks.
    function mount(host, uri, content, language, dotnetRef) {
        const editor = monaco.editor.create(host, Object.assign({}, baseOptions, {
            value: content || '', language: language || 'plaintext'
        }));
        _editors[uri] = editor;

        let fromBlazor = false;
        editor.onDidChangeModelContent(() => {
            if (fromBlazor) return;
            dotnetRef && dotnetRef.invokeMethodAsync('OnEditorChangedJS', uri, editor.getValue());
        });
        editor.addCommand(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyS, () => {
            dotnetRef && dotnetRef.invokeMethodAsync('OnEditorSaveJS', uri, editor.getValue());
        });
        // Report cursor position for the status bar (Ln/Col).
        editor.onDidChangeCursorPosition(e => {
            dotnetRef && dotnetRef.invokeMethodAsync('OnCursorJS', e.position.lineNumber, e.position.column);
        });
        editor._setFromBlazor = function (c) { fromBlazor = true; editor.setValue(c); fromBlazor = false; };
        return editor;
    }

    function setContent(uri, content) {
        const e = _editors[uri];
        if (e && e._setFromBlazor) e._setFromBlazor(content);
    }
    function getContent(uri) {
        const e = _editors[uri];
        return e ? e.getValue() : null;
    }
    function dispose(uri) {
        const e = _editors[uri];
        if (e) { e.dispose(); delete _editors[uri]; }
    }

    // Read-only side-by-side diff (versioning UI — IDE doc §6).
    async function showDiff(hostId, original, modified, language) {
        await ready();
        const host = document.getElementById(hostId);
        if (!host) return;
        if (_diffs[hostId]) { _diffs[hostId].dispose(); delete _diffs[hostId]; }
        const diff = monaco.editor.createDiffEditor(host, Object.assign({}, baseOptions, {
            readOnly: true, renderSideBySide: true, minimap: { enabled: false }
        }));
        diff.setModel({
            original: monaco.editor.createModel(original || '', language || 'plaintext'),
            modified: monaco.editor.createModel(modified || '', language || 'plaintext')
        });
        _diffs[hostId] = diff;
    }
    function setTheme(themeName) {
        if (monaco && monaco.editor) {
            monaco.editor.setTheme(themeName);
            baseOptions.theme = themeName;
        }
    }

    let _providersRegistered = false;
    function registerProviders(dotNetObj) {
        if (_providersRegistered || !monaco.languages) return;
        _providersRegistered = true;

        monaco.languages.registerCompletionItemProvider('csharp', {
            triggerCharacters: ['.', ' '],
            provideCompletionItems: async function(model, position) {
                if (!dotNetObj) return { suggestions: [] };
                try {
                    const comps = await dotNetObj.invokeMethodAsync('GetCompletionsJS', model.uri.toString(), position.lineNumber, position.column);
                    return {
                        suggestions: comps.map(c => ({
                            label: c.label,
                            kind: c.kind,
                            insertText: c.insertText,
                            detail: c.detail
                        }))
                    };
                } catch (e) {
                    return { suggestions: [] };
                }
            }
        });

        monaco.languages.registerHoverProvider('csharp', {
            provideHover: async function(model, position) {
                if (!dotNetObj) return null;
                try {
                    const hover = await dotNetObj.invokeMethodAsync('GetHoverJS', model.uri.toString(), position.lineNumber, position.column);
                    if (hover && hover.content) {
                        return {
                            contents: [{ value: hover.content }]
                        };
                    }
                } catch (e) { }
                return null;
            }
        });
    }

    function setDiagnostics(uri, markers) {
        const model = monaco.editor.getModel(monaco.Uri.parse(uri));
        if (model) {
            monaco.editor.setModelMarkers(model, "csharp", markers || []);
        }
    }

    return { ready, mount, setContent, getContent, dispose, showDiff, setTheme, registerProviders, setDiagnostics };
})();
