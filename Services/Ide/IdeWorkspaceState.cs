using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Ide.Models;

namespace Syncro.Desktop.Services.Ide;

/// <summary>Single source of UI truth for the IDE (open files, active tab, side view). Scoped per circuit.</summary>
public class IdeWorkspaceState
{
    private readonly IdeStateStore _store;
    private readonly IProjectContextBuilder _contextBuilder;

    public IdeWorkspaceState(IdeStateStore store, IProjectContextBuilder contextBuilder)
    {
        _store = store;
        _contextBuilder = contextBuilder;
    }

    public Workspace? Current { get; private set; }
    public ProjectContext? Context { get; private set; }
    public List<EditorTab> Tabs { get; } = new();
    public Dictionary<string, string> Buffers { get; } = new();   // uri -> in-memory edited content
    public EditorTab? ActiveTab => Tabs.FirstOrDefault(t => t.IsActive);
    public SidePanelView SidePanel { get; set; } = SidePanelView.Explorer;

    public event Action? Changed;
    private void Notify() => Changed?.Invoke();

    public async Task OpenWorkspaceAsync(string rootPath)
    {
        Current = new Workspace
        {
            Id = WorkspaceId(rootPath),
            Name = Path.GetFileName(rootPath.TrimEnd('/', '\\')),
            RootPath = rootPath
        };
        Tabs.Clear();
        foreach (var t in await _store.LoadSessionAsync(Current.Id)) Tabs.Add(t);
        if (Tabs.Count > 0 && !Tabs.Any(t => t.IsActive)) Tabs[0].IsActive = true;
        await _store.AddRecentAsync(rootPath);
        Notify();

        _ = Task.Run(async () =>
        {
            Context = await _contextBuilder.BuildAsync(rootPath, Current.Id);
            Notify();
        });
    }

    public EditorTab OpenFile(string filePath)
    {
        var uri = "file:///" + filePath.Replace('\\', '/');
        var existing = Tabs.FirstOrDefault(t => t.Uri == uri);
        if (existing != null) { Activate(uri); return existing; }

        foreach (var t in Tabs) t.IsActive = false;
        var tab = new EditorTab
        {
            Uri = uri,
            FilePath = filePath,
            IsActive = true,
            Language = FileService.DetectLanguage(filePath)
        };
        Tabs.Add(tab);
        Persist();
        Notify();
        return tab;
    }

    public void Activate(string uri)
    {
        foreach (var t in Tabs) t.IsActive = t.Uri == uri;
        Persist();
        Notify();
    }

    public void CloseTab(string uri)
    {
        var t = Tabs.FirstOrDefault(x => x.Uri == uri);
        if (t == null) return;
        bool wasActive = t.IsActive;
        Tabs.Remove(t);
        Buffers.Remove(uri);
        if (wasActive && Tabs.Count > 0) Tabs[^1].IsActive = true;
        Persist();
        Notify();
    }

    public void MarkDirty(string uri, bool dirty)
    {
        var t = Tabs.FirstOrDefault(x => x.Uri == uri);
        if (t != null) { t.IsDirty = dirty; Notify(); }
    }

    private void Persist()
    {
        if (Current != null) _ = _store.SaveSessionAsync(Current, Tabs);
    }

    public static string WorkspaceId(string s)
    {
        // Normalize path separators to ensure C:\App and C:/App yield the same ID
        s = s.Replace('\\', '/').TrimEnd('/');
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
