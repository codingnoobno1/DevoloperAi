using System.Collections.Concurrent;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Syncro.Desktop.Services.Ide;

/// <summary>Opens Syncro IDE as its own native OS window (chrome-less, no app sidebar).</summary>
public class IdeWindowService
{
    private readonly IdeLaunchContext _ctx;
    private readonly ConcurrentDictionary<string, Window> _open = new();

    public IdeWindowService(IdeLaunchContext ctx) => _ctx = ctx;

    public void Open(string? workspacePath = null)
    {
        if (workspacePath != null)
        {
            var id = IdeWorkspaceState.WorkspaceId(workspacePath);
            if (_open.TryGetValue(id, out var existing))
            {
                MainThread.BeginInvokeOnMainThread(() => Application.Current?.ActivateWindow(existing));
                return;
            }
        }

        var token = _ctx.Stage(new IdeLaunchRequest(workspacePath, null, "ide"));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var app = Application.Current;
            if (app == null) return;
            var window = new Window(new Syncro.Desktop.IdePage(token))
            {
                Title = "Syncro IDE",
                Width = 1400,
                Height = 900
            };
            
            if (workspacePath != null)
            {
                var id = IdeWorkspaceState.WorkspaceId(workspacePath);
                _open[id] = window;
                window.Destroying += (s, e) => _open.TryRemove(id, out _);
            }
            
            app.OpenWindow(window);
        });
    }

    public void OpenAnalysis(string? projectPath = null)
    {
        var token = _ctx.Stage(new IdeLaunchRequest(null, projectPath, "analysis"));
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var app = Application.Current;
            if (app == null) return;
            var window = new Window(new Syncro.Desktop.AnalysisPage(token))
            {
                Title = "Project Analysis",
                Width = 1200,
                Height = 820
            };
            app.OpenWindow(window);
        });
    }
}
