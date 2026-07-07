window.SyncroTheme = {
    apply: function (themeName, fontSize) {
        // Remove existing theme classes
        document.documentElement.className = document.documentElement.className.replace(/\btheme-[a-z0-9-]+\b/g, '').trim();
        
        // Add new theme class
        if (themeName) {
            document.documentElement.classList.add(themeName);
        }

        // Apply font size globally
        if (fontSize) {
            document.documentElement.style.setProperty('--fs-ui', fontSize + 'px');
        }

        // Apply to Monaco if loaded
        if (window.SyncroMonaco && typeof window.SyncroMonaco.setTheme === 'function') {
            window.SyncroMonaco.setTheme(themeName);
            // Optionally, we could set font size on open editors, 
            // but Monaco requires updating options per instance.
        }
    }
};
