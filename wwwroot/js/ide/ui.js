/*
 * ui.js — Syncro.UI shared REUSABLE interaction behaviors (window.SyncroUI).
 *
 * The browser-native half of the design system: any Blazor component calls these instead of
 * re-implementing DOM behavior. Keeps components thin (C# = state/logic, JS = DOM interaction).
 * ide-professional-polish.md §7.1 / §10.1.
 *
 * Every attach() returns a disposer so callers (Blazor IDisposable / IAsyncDisposable) clean up.
 */
window.SyncroUI = (function () {
    const noop = () => { };

    // ── Tooltip ─────────────────────────────────────────────────────────
    // Lightweight hover tooltip positioned relative to the host element.
    function tooltip(el, text, placement) {
        if (!el) return noop;
        let tip = null;
        const show = () => {
            if (tip || !text) return;
            tip = document.createElement('div');
            tip.className = 'sui-tooltip';
            tip.textContent = text;
            document.body.appendChild(tip);
            const r = el.getBoundingClientRect();
            const tr = tip.getBoundingClientRect();
            let top = r.bottom + 6, left = r.left + (r.width - tr.width) / 2;
            if ((placement || 'bottom') === 'right') { top = r.top + (r.height - tr.height) / 2; left = r.right + 6; }
            tip.style.top = Math.max(4, top) + 'px';
            tip.style.left = Math.max(4, left) + 'px';
            requestAnimationFrame(() => tip && tip.classList.add('is-in'));
        };
        const hide = () => { if (tip) { tip.remove(); tip = null; } };
        el.addEventListener('mouseenter', show);
        el.addEventListener('mouseleave', hide);
        el.addEventListener('mousedown', hide);
        return () => { hide(); el.removeEventListener('mouseenter', show); el.removeEventListener('mouseleave', hide); el.removeEventListener('mousedown', hide); };
    }

    // ── Ripple ──────────────────────────────────────────────────────────
    // Material-style click ripple. el should be position:relative; overflow:hidden.
    function ripple(el) {
        if (!el) return noop;
        const onDown = (e) => {
            const r = el.getBoundingClientRect();
            const span = document.createElement('span');
            span.className = 'sui-ripple';
            const size = Math.max(r.width, r.height);
            span.style.width = span.style.height = size + 'px';
            span.style.left = (e.clientX - r.left - size / 2) + 'px';
            span.style.top = (e.clientY - r.top - size / 2) + 'px';
            el.appendChild(span);
            span.addEventListener('animationend', () => span.remove());
        };
        el.addEventListener('mousedown', onDown);
        return () => el.removeEventListener('mousedown', onDown);
    }

    // ── Context menu ────────────────────────────────────────────────────
    // items: [{ label, icon, disabled, separator, onClick(dotnet) }] — but since handlers must be
    // C#, we pass item ids back to dotnet.invokeMethodAsync(callbackName, id).
    function contextMenu(hostEl, items, dotnet, callbackName) {
        if (!hostEl) return noop;
        let menu = null;
        const close = () => { if (menu) { menu.remove(); menu = null; document.removeEventListener('mousedown', onDoc, true); } };
        const onDoc = (e) => { if (menu && !menu.contains(e.target)) close(); };
        const onCtx = (e) => {
            e.preventDefault();
            close();
            menu = document.createElement('div');
            menu.className = 'sui-menu';
            (items || []).forEach(it => {
                if (it.separator) { const s = document.createElement('div'); s.className = 'sui-menu-sep'; menu.appendChild(s); return; }
                const row = document.createElement('div');
                row.className = 'sui-menu-item' + (it.disabled ? ' is-disabled' : '');
                row.innerHTML = (it.icon ? `<i class="${it.icon}"></i>` : '<span class="sui-menu-gap"></span>') + `<span>${it.label}</span>`;
                if (!it.disabled) row.addEventListener('click', () => { close(); dotnet && dotnet.invokeMethodAsync(callbackName, it.id); });
                menu.appendChild(row);
            });
            document.body.appendChild(menu);
            const mw = menu.offsetWidth, mh = menu.offsetHeight;
            menu.style.left = Math.min(e.clientX, window.innerWidth - mw - 6) + 'px';
            menu.style.top = Math.min(e.clientY, window.innerHeight - mh - 6) + 'px';
            document.addEventListener('mousedown', onDoc, true);
        };
        hostEl.addEventListener('contextmenu', onCtx);
        return () => { close(); hostEl.removeEventListener('contextmenu', onCtx); };
    }

    // ── Auto-resizing textarea ──────────────────────────────────────────
    function autoResize(el, maxPx) {
        if (!el) return noop;
        const fit = () => { el.style.height = 'auto'; el.style.height = Math.min(el.scrollHeight, maxPx || 200) + 'px'; };
        el.addEventListener('input', fit); fit();
        return () => el.removeEventListener('input', fit);
    }

    // ── ResizeObserver bridge (notify C# of element size) ───────────────
    function observeResize(el, dotnet, callbackName) {
        if (!el || !window.ResizeObserver) return noop;
        const ro = new ResizeObserver(entries => {
            const r = entries[0].contentRect;
            dotnet && dotnet.invokeMethodAsync(callbackName, Math.round(r.width), Math.round(r.height));
        });
        ro.observe(el);
        return () => ro.disconnect();
    }

    // ── Drag-to-resize handle (split panes) ─────────────────────────────
    // handle = the grabber; target = element whose width/height changes. axis 'x' | 'y'.
    function dragResize(handle, target, axis, min, max) {
        if (!handle || !target) return noop;
        let startPos = 0, startSize = 0, dragging = false;
        const onMove = (e) => {
            if (!dragging) return;
            const delta = (axis === 'y' ? e.clientY : e.clientX) - startPos;
            let size = startSize + delta;
            size = Math.max(min || 80, Math.min(max || 1200, size));
            if (axis === 'y') target.style.height = size + 'px'; else target.style.width = size + 'px';
        };
        const onUp = () => { dragging = false; document.body.style.cursor = ''; document.removeEventListener('mousemove', onMove); document.removeEventListener('mouseup', onUp); };
        const onDown = (e) => {
            dragging = true; startPos = axis === 'y' ? e.clientY : e.clientX;
            const r = target.getBoundingClientRect(); startSize = axis === 'y' ? r.height : r.width;
            document.body.style.cursor = axis === 'y' ? 'row-resize' : 'col-resize';
            document.addEventListener('mousemove', onMove); document.addEventListener('mouseup', onUp);
            e.preventDefault();
        };
        handle.addEventListener('mousedown', onDown);
        return () => handle.removeEventListener('mousedown', onDown);
    }

    // ── Global keybinding capture ───────────────────────────────────────
    // bindings: [{ combo: 'ctrl+shift+p', id }]; fires dotnet.invokeMethodAsync(callbackName, id).
    function keybindings(bindings, dotnet, callbackName) {
        const norm = (e) => [e.ctrlKey && 'ctrl', e.shiftKey && 'shift', e.altKey && 'alt', e.key.toLowerCase()].filter(Boolean).join('+');
        const onKey = (e) => {
            const combo = norm(e);
            const hit = (bindings || []).find(b => b.combo.toLowerCase() === combo);
            if (hit) { e.preventDefault(); dotnet && dotnet.invokeMethodAsync(callbackName, hit.id); }
        };
        window.addEventListener('keydown', onKey, true);
        return () => window.removeEventListener('keydown', onKey, true);
    }

    return { tooltip, ripple, contextMenu, autoResize, observeResize, dragResize, keybindings };
})();
