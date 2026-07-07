using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Ide.Models;

namespace Syncro.Desktop.Services.Ide;

/// <summary>
/// Persists IDE session state (open tabs per workspace, recents) as JSON.
/// Phase-1 storage; upgrade to SQLite (Microsoft.Data.Sqlite) later without touching callers.
/// </summary>
public class IdeStateStore
{
    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncroDesktop", "ide");

    private string SessionPath(string workspaceId) => Path.Combine(_dir, $"session_{workspaceId}.json");
    private string RecentsPath => Path.Combine(_dir, "recents.json");

    public async Task<List<EditorTab>> LoadSessionAsync(string workspaceId)
    {
        var p = SessionPath(workspaceId);
        if (!File.Exists(p)) return new();
        try { return JsonConvert.DeserializeObject<List<EditorTab>>(await File.ReadAllTextAsync(p)) ?? new(); }
        catch { return new(); }
    }

    public async Task SaveSessionAsync(Workspace ws, IEnumerable<EditorTab> tabs)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            await File.WriteAllTextAsync(SessionPath(ws.Id), JsonConvert.SerializeObject(tabs, Formatting.Indented));
        }
        catch { /* best effort */ }
    }

    public async Task AddRecentAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var list = File.Exists(RecentsPath)
                ? (JsonConvert.DeserializeObject<List<string>>(await File.ReadAllTextAsync(RecentsPath)) ?? new())
                : new List<string>();
            list.Remove(path);
            list.Insert(0, path);
            if (list.Count > 15) list = list.GetRange(0, 15);
            await File.WriteAllTextAsync(RecentsPath, JsonConvert.SerializeObject(list, Formatting.Indented));
        }
        catch { }
    }
}
