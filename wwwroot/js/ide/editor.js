// Viking IDE — lightweight code editor enhancement (line-number gutter, tab + Ctrl+S handling).
// No dependencies; vendored locally. Monaco can replace this later behind the same component.

function buildGutter(textarea, gutter) {
    const count = (textarea.value.split('\n').length) || 1;
    let s = '';
    for (let i = 1; i <= count; i++) s += i + '\n';
    gutter.textContent = s;
    gutter.scrollTop = textarea.scrollTop;
}

export function attach(textarea, gutter, dotnetRef) {
    if (!textarea || !gutter) return;

    textarea.addEventListener('input', () => buildGutter(textarea, gutter));
    textarea.addEventListener('scroll', () => { gutter.scrollTop = textarea.scrollTop; });

    textarea.addEventListener('keydown', (e) => {
        if (e.key === 'Tab') {
            e.preventDefault();
            const s = textarea.selectionStart, en = textarea.selectionEnd;
            textarea.value = textarea.value.substring(0, s) + '    ' + textarea.value.substring(en);
            textarea.selectionStart = textarea.selectionEnd = s + 4;
            textarea.dispatchEvent(new Event('input', { bubbles: true }));
        } else if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) {
            e.preventDefault();   // stop the WebView's "save page" dialog
            if (dotnetRef) dotnetRef.invokeMethodAsync('SaveFromJsAsync');
        }
    });

    buildGutter(textarea, gutter);
}

export function refresh(textarea, gutter) {
    if (textarea && gutter) buildGutter(textarea, gutter);
}
