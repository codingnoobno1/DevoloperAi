window.SyncroIde = (function () {
    let _dotNetObj = null;
    let _layout = null;
    let _term = null;    // xterm instance

    // The Monaco editor surface lives in js/ide/monaco.js (window.SyncroMonaco) per IDE doc §2.4.
    // This bridge owns docking (GoldenLayout) + the terminal, and DELEGATES editing to that module.

    // ── Default workbench layout (VS Code-like) ────────────────────────
    const defaultLayoutConfig = {
        settings: {
            hasHeaders: true,
            constrainDragToContainer: true,
            reorderEnabled: true,
            selectionEnabled: false,
            showPopoutIcon: false,
            showMaximiseIcon: true,
            showCloseIcon: true
        },
        dimensions: { borderWidth: 4, headerHeight: 30, minItemHeight: 40, minItemWidth: 120,
                      dragProxyWidth: 320, dragProxyHeight: 220 },
        content: [{
            type: 'row',
            content: [
                {
                    type: 'component', componentName: 'blazorPortal',
                    componentState: { portalId: 'blazor-explorer-portal' },
                    width: 17, isClosable: false, title: 'EXPLORER'
                },
                {
                    type: 'column', width: 64, content: [
                        {
                            type: 'stack', id: 'editor-stack', height: 70, content: [
                                {
                                    type: 'component', componentName: 'blazorPortal',
                                    componentState: { portalId: 'blazor-codeoss-portal' },
                                    isClosable: false, title: 'Code-OSS'
                                },
                                {
                                    type: 'component', componentName: 'blazorPortal',
                                    componentState: { portalId: 'blazor-settings-portal' },
                                    isClosable: true, title: '⚙️ Settings'
                                }
                            ]
                        },
                        {
                            type: 'stack', id: 'panel-stack', height: 30, content: [
                                { type: 'component', componentName: 'blazorPortal', componentState: { portalId: 'blazor-terminal-portal' }, isClosable: false, title: 'TERMINAL' },
                                { type: 'component', componentName: 'blazorPortal', componentState: { portalId: 'blazor-problems-portal' }, isClosable: false, title: 'PROBLEMS' },
                                { type: 'component', componentName: 'blazorPortal', componentState: { portalId: 'blazor-output-portal' }, isClosable: false, title: 'OUTPUT' }
                            ]
                        }
                    ]
                },
                {
                    type: 'stack', id: 'side-stack', width: 19, content: [
                        { type: 'component', componentName: 'blazorPortal', componentState: { portalId: 'blazor-ai-portal' }, isClosable: false, title: 'AI AGENT' },
                        { type: 'component', componentName: 'blazorPortal', componentState: { portalId: 'blazor-hindsight-portal' }, isClosable: false, title: 'HINDSIGHT' }
                    ]
                }
            ]
        }]
    };

    function placePortal(container, state) {
        container.on('open', () => {
            const portal = document.getElementById(state.portalId);
            if (portal) {
                portal.style.display = 'flex';
                portal.style.width = '100%';
                portal.style.height = '100%';
                portal.style.overflow = 'hidden';
                container.getElement().append(portal);
            }
        });
    }

    function initLayout(containerId, dotNetObj) {
        _dotNetObj = dotNetObj;
        const container = document.getElementById(containerId);
        if (!container) return;

        // Ensure the Monaco surface module is loaded, then build the layout.
        window.SyncroMonaco.ready().then(function () {
            window.SyncroMonaco.registerProviders(_dotNetObj);
            _layout = new window.GoldenLayout(defaultLayoutConfig, container);

            // Editor panel — delegates ALL Monaco work to the surface module (window.SyncroMonaco).
            _layout.registerComponent('monacoEditor', function (container, state) {
                const host = document.createElement('div');
                host.style.cssText = 'width:100%;height:100%;overflow:hidden;';
                container.getElement().append(host);
                container.on('open', () => {
                    window.SyncroMonaco.mount(host, state.uri, state.content, state.language || 'plaintext', _dotNetObj);
                });
                container.on('destroy', () => {
                    window.SyncroMonaco.dispose(state.uri);
                    _dotNetObj.invokeMethodAsync('OnTabClosedJS', state.uri);
                });
                state.setEditorContent = function (c) { window.SyncroMonaco.setContent(state.uri, c); };
            });

            // Blazor portal panel (explorer, welcome, terminal, ai, …)
            _layout.registerComponent('blazorPortal', placePortal);

            _layout.init();
            window.addEventListener('resize', () => { if (_layout) _layout.updateSize(); });

            if (_dotNetObj) _dotNetObj.invokeMethodAsync('OnLayoutReadyJS');
        });
    }

    function openFile(uri, content, language, fileName) {
        if (!_layout) return;
        const existing = _layout.root.getItemsByFilter(i =>
            i.isComponent && i.config.componentName === 'monacoEditor' && i.config.componentState.uri === uri);
        if (existing.length > 0) { existing[0].parent.setActiveContentItem(existing[0]); return; }

        let stack = _layout.root.getItemsById('editor-stack')[0];
        if (!stack) {
            const stacks = _layout.root.getItemsByType('stack');
            stack = stacks.length ? stacks[0] : _layout.root.contentItems[0];
        }
        if (stack) {
            stack.addChild({
                type: 'component', componentName: 'monacoEditor',
                componentState: { uri, content, language }, title: fileName, isClosable: true
            });
        }
    }

    function setFileContent(uri, content) {
        const items = _layout?.root?.getItemsByFilter(i =>
            i.isComponent && i.config.componentName === 'monacoEditor' && i.config.componentState.uri === uri);
        if (items && items.length && items[0].config.componentState.setEditorContent)
            items[0].config.componentState.setEditorContent(content);
    }

    function focusPanel(portalId) {
        if (!_layout) return;
        const items = _layout.root.getItemsByFilter(i =>
            i.isComponent && i.config.componentState && i.config.componentState.portalId === portalId);
        if (items.length && items[0].parent && items[0].parent.setActiveContentItem)
            items[0].parent.setActiveContentItem(items[0]);
    }

    // ── xterm integration (visual + echo; ConPTY backend = W3) ─────────
    function mountTerminal(mountId) {
        const el = document.getElementById(mountId);
        if (!el || !window.Terminal || _term) return;
        _term = new window.Terminal({
            fontFamily: "'JetBrains Mono','Cascadia Code',monospace",
            fontSize: 12.5, cursorBlink: true, convertEol: true,
            theme: { background: '#1e1e1e', foreground: '#cccccc', cursor: '#aeafad',
                     selectionBackground: '#264f78', brightBlue: '#569cd6', green: '#4ec9b0' }
        });
        let fit = null;
        try { fit = new (window.FitAddon.FitAddon)(); _term.loadAddon(fit); } catch (e) { }
        _term.open(el);
        try { fit && fit.fit(); } catch (e) { }
        window.addEventListener('resize', () => { try { fit && fit.fit(); } catch (e) { } });

        const prompt = '\x1b[38;2;78;201;176m$\x1b[0m ';
        _term.write(prompt);
        let line = '';
        _term.onData(d => {
            if (d === '\r') {
                _term.write('\r\n');
                if (line.trim()) _dotNetObj && _dotNetObj.invokeMethodAsync('OnTerminalCommandJS', line.trim());
                line = '';
                _term.write(prompt);
            } else if (d === '') {           // backspace
                if (line.length) { line = line.slice(0, -1); _term.write('\b \b'); }
            } else { line += d; _term.write(d); }
        });
    }

    function termWrite(text) { if (_term) _term.writeln(text); }

    return { initLayout, openFile, setFileContent, focusPanel, mountTerminal, termWrite };
})();
