using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Syncro.Desktop.SyncroUI.Theme;

public class ThemeEngine
{
    private readonly IJSRuntime _js;
    private ThemeOptions _options = new();
    
    public event Action? OnThemeChanged;
    
    public ThemeOptions CurrentOptions => _options;

    public ThemeEngine(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        // Try to load from local storage
        try
        {
            var saved = await _js.InvokeAsync<string>("localStorage.getItem", "syncro-settings");
            if (!string.IsNullOrEmpty(saved))
            {
                var opts = System.Text.Json.JsonSerializer.Deserialize<ThemeOptions>(saved);
                if (opts != null)
                {
                    _options = opts;
                }
            }
        }
        catch { }

        await ApplyToDomAsync();
    }

    public async Task SetThemeAsync(string themeName)
    {
        _options.ThemeName = themeName;
        await SaveAndApplyAsync();
    }

    public async Task SetFontSizeAsync(int size)
    {
        _options.FontSize = size;
        await SaveAndApplyAsync();
    }

    public async Task SetTabSizeAsync(int size)
    {
        _options.TabSize = size;
        await SaveAndApplyAsync();
    }

    private async Task SaveAndApplyAsync()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_options);
            await _js.InvokeVoidAsync("localStorage.setItem", "syncro-settings", json);
        }
        catch { }

        await ApplyToDomAsync();
        OnThemeChanged?.Invoke();
    }

    private async Task ApplyToDomAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("window.SyncroTheme.apply", _options.ThemeName, _options.FontSize);
        }
        catch { }
    }
}
