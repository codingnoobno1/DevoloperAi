/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./**/*.{razor,html}"
  ],
  theme: {
    extend: {
      colors: {
        editor: 'var(--bg-editor)',
        panel: 'var(--bg-panel)',
        side: 'var(--bg-side)',
        activity: 'var(--bg-activity)',
        title: 'var(--bg-title)',
        tabActive: 'var(--bg-tab-active)',
        tabInactive: 'var(--bg-tab-inactive)',
        input: 'var(--bg-input)',
        hover: 'var(--hover)',
        hoverStrong: 'var(--hover-strong)',
        borderBase: 'var(--border)',
        borderSoft: 'var(--border-soft)',
        textMain: 'var(--text)',
        textStrong: 'var(--text-strong)',
        textMuted: 'var(--text-muted)',
        textFaint: 'var(--text-faint)',
        accent: 'var(--accent)',
        accentHover: 'var(--accent-hover)',
        onAccent: 'var(--on-accent)',
        statusBg: 'var(--status-bg)',
        statusBgEmpty: 'var(--status-bg-empty)',
        statusFg: 'var(--status-fg)',
        error: 'var(--error)',
        warn: 'var(--warn)',
        ok: 'var(--ok)',
        info: 'var(--info)',
        git: 'var(--git)'
      },
      borderRadius: {
        ui: 'var(--radius)',
        uiLg: 'var(--radius-lg)'
      },
      fontFamily: {
        ui: 'var(--font-ui)',
        mono: 'var(--font-mono)'
      }
    },
  },
  plugins: [],
}
