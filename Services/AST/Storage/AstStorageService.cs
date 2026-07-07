using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Storage;

public class AstStorageService
{
    public string CacheDirectory
    {
        get
        {
            string workspaceRoot = Microsoft.Maui.Storage.Preferences.Default.Get("__syncro_workspace_root__", "");
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                workspaceRoot = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "SyncroWorkspace");
            }
            string path = Path.Combine(workspaceRoot, "ast_cache");
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch { }
            return path;
        }
    }

    public AstStorageService()
    {
    }

    public async Task SaveMapAsync(string projectPath, AstProjectMap map)
    {
        string safeKey = GetSafeKey(projectPath);
        string filePath = Path.Combine(CacheDirectory, $"{safeKey}.json");

        string json = JsonConvert.SerializeObject(map, Formatting.Indented);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<AstProjectMap?> LoadMapAsync(string projectPath)
    {
        string safeKey = GetSafeKey(projectPath);
        string filePath = Path.Combine(CacheDirectory, $"{safeKey}.json");

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(filePath);
            return JsonConvert.DeserializeObject<AstProjectMap>(json);
        }
        catch
        {
            return null;
        }
    }

    public void ClearCache()
    {
        string path = CacheDirectory;
        if (Directory.Exists(path))
        {
            foreach (var file in Directory.GetFiles(path, "*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Suppress deletion errors
                }
            }
        }
    }

    public long GetCacheSize()
    {
        try
        {
            string path = CacheDirectory;
            if (!Directory.Exists(path)) return 0;
            long size = 0;
            foreach (var file in Directory.GetFiles(path, "*.json"))
            {
                size += new FileInfo(file).Length;
            }
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private string GetSafeKey(string projectPath)
    {
        // Replace invalid path characters with underscores
        string invalidChars = new string(Path.GetInvalidFileNameChars()) + ":\\/";
        string safe = projectPath;
        foreach (char c in invalidChars)
        {
            safe = safe.Replace(c, '_');
        }
        return safe.Trim('_');
    }
}
