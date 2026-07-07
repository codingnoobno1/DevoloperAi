using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Syncro.CLI;

/// <summary>
/// Shared helpers for reading/writing the .syncro_db filesystem database.
/// Hardened for concurrent access with cross-thread SemaphoreSlim and transient retry reads.
/// </summary>
public static class SyncroDb
{
    private static readonly SemaphoreSlim _writeLock = new(1, 1);

    // ── Concurrency Helpers ──────────────────────────────────────────────────

    private static T RetryRead<T>(Func<T> action, int maxRetries = 3)
    {
        int delay = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return action();
            }
            catch (IOException)
            {
                if (i == maxRetries - 1) throw;
                Thread.Sleep(delay);
                delay *= 5; // 10ms -> 50ms -> 250ms
            }
            catch (JsonException)
            {
                return default!;
            }
        }
        return action(); // Should never hit
    }

    private static void AtomicWrite(string path, string content)
    {
        _writeLock.Wait();
        try
        {
            string tmp = path + ".tmp." + Guid.NewGuid().ToString("n");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void AtomicWriteLines(string path, IEnumerable<string> lines)
    {
        _writeLock.Wait();
        try
        {
            string tmp = path + ".tmp." + Guid.NewGuid().ToString("n");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(tmp, lines);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void LockedAppend(string path, string content)
    {
        _writeLock.Wait();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, content);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ── DB root resolution ───────────────────────────────────────────────────

    public static string GetDbRoot(string? workspaceRoot = null)
    {
        workspaceRoot ??= Directory.GetCurrentDirectory();
        string current = workspaceRoot;
        while (current != null)
        {
            string candidate = Path.Combine(current, ".syncro_db");
            if (Directory.Exists(candidate)) return candidate;
            string? parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent!;
        }
        return Path.Combine(workspaceRoot, ".syncro_db");
    }

    public static string Namespace(string ns, string? workspaceRoot = null)
        => Path.Combine(GetDbRoot(workspaceRoot), ns);

    // ── Init DB structure ────────────────────────────────────────────────────

    public static void EnsureStructure(string workspaceRoot)
    {
        string root = Path.Combine(workspaceRoot, ".syncro_db");
        string[] dirs =
        [
            "AST", "Graphs", "Vectors", "Memory", "Memory/scripts",
            "Errors", "Templates", "Frameworks", "Architectures",
            "Agents", "Projects", "Groups", "Tasks"
        ];
        
        _writeLock.Wait();
        try
        {
            foreach (var d in dirs)
                Directory.CreateDirectory(Path.Combine(root, d));
        }
        finally
        {
            _writeLock.Release();
        }

        string projectsFile = Path.Combine(root, "Projects", "projects.json");
        if (!File.Exists(projectsFile)) AtomicWrite(projectsFile, "[]");

        string groupsFile = Path.Combine(root, "Groups", "groups.json");
        if (!File.Exists(groupsFile)) AtomicWrite(groupsFile, "[]");
    }

    // ── Projects ─────────────────────────────────────────────────────────────

    public static List<JObject> LoadProjects(string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Projects", workspaceRoot), "projects.json");
        if (!File.Exists(file)) return new List<JObject>();
        return RetryRead(() => JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(file))) ?? new List<JObject>();
    }

    public static void SaveProjects(List<JObject> projects, string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Projects", workspaceRoot), "projects.json");
        AtomicWrite(file, JsonConvert.SerializeObject(projects, Formatting.Indented));
    }

    public static JObject? FindProject(string id, string? workspaceRoot = null)
        => LoadProjects(workspaceRoot).FirstOrDefault(p => p["project_id"]?.Value<string>() == id);

    // ── Groups ───────────────────────────────────────────────────────────────

    public static List<JObject> LoadGroups(string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Groups", workspaceRoot), "groups.json");
        if (!File.Exists(file)) return new List<JObject>();
        return RetryRead(() => JsonConvert.DeserializeObject<List<JObject>>(File.ReadAllText(file))) ?? new List<JObject>();
    }

    // ── Memory ───────────────────────────────────────────────────────────────

    public static void AppendMemory(object record, string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Memory", workspaceRoot), "memory.jsonl");
        LockedAppend(file, JsonConvert.SerializeObject(record) + "\n");
    }

    public static IEnumerable<JObject> ReadMemory(string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Memory", workspaceRoot), "memory.jsonl");
        if (!File.Exists(file)) yield break;
        
        var lines = RetryRead(() => File.ReadAllLines(file));
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JObject? obj = null;
            try { obj = JObject.Parse(line); } catch { }
            if (obj != null) yield return obj;
        }
    }

    // ── Errors ───────────────────────────────────────────────────────────────

    public static void AppendError(object record, string ns = "build_errors", string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Errors", workspaceRoot), $"{ns}.jsonl");
        LockedAppend(file, JsonConvert.SerializeObject(record) + "\n");
    }

    public static JObject? LastBuildError(string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Errors", workspaceRoot), "build_errors.jsonl");
        if (!File.Exists(file)) return null;

        var lines = RetryRead(() => File.ReadAllLines(file));
        return lines.Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(l => { try { return JObject.Parse(l); } catch { return null; } })
                    .LastOrDefault(x => x != null);
    }

    // ── Frameworks ───────────────────────────────────────────────────────────

    public static JObject? LoadFramework(string id, string? workspaceRoot = null)
    {
        string file = Path.Combine(Namespace("Frameworks", workspaceRoot), $"{id}.json");
        if (!File.Exists(file)) return null;
        return RetryRead(() => JObject.Parse(File.ReadAllText(file)));
    }

    // ── AST index ────────────────────────────────────────────────────────────

    public static JObject? LookupSymbol(string symbol, string projectId, string? workspaceRoot = null)
    {
        string indexPath = Path.Combine(Namespace("AST", workspaceRoot), projectId, "ast_index.json");
        if (!File.Exists(indexPath)) return null;
        var index = RetryRead(() => JObject.Parse(File.ReadAllText(indexPath)));
        return index?[symbol] as JObject;
    }

    // ── Tasks ────────────────────────────────────────────────────────────────

    public static List<JObject> LoadTasks(string? workspaceRoot = null)
    {
        string file = Path.Combine(GetDbRoot(workspaceRoot), "Tasks", "tasks.jsonl");
        var list = new List<JObject>();
        if (!File.Exists(file)) return list;
        
        var lines = RetryRead(() => File.ReadAllLines(file));
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { list.Add(JObject.Parse(line)); } catch { }
        }
        return list;
    }

    public static void SaveTasks(List<JObject> tasks, string? workspaceRoot = null)
    {
        string file = Path.Combine(GetDbRoot(workspaceRoot), "Tasks", "tasks.jsonl");
        var lines = tasks.Select(t => JsonConvert.SerializeObject(t, Formatting.None));
        AtomicWriteLines(file, lines);
    }

    public static JObject EnqueueTask(string projectId, string title, string intent, string? scriptId = null, string? workspaceRoot = null)
    {
        var task = new JObject
        {
            ["task_id"] = "tsk_" + Guid.NewGuid().ToString("N")[..8],
            ["project_id"] = projectId,
            ["title"] = title,
            ["intent"] = intent,
            ["script_id"] = scriptId,
            ["template_script_executed"] = false,
            ["status"] = "pending",
            ["error"] = false,
            ["error_count"] = 0,
            ["source"] = null,
            ["error_type"] = null,
            ["solution"] = null,
            ["next_action"] = null,
            ["llm_involved"] = false,
            ["attempts"] = 0,
            ["max_attempts"] = 4,
            ["needs_approval"] = false,
            ["created_at"] = DateTime.UtcNow.ToString("o"),
            ["updated_at"] = DateTime.UtcNow.ToString("o"),
            ["history"] = new JArray(new JObject { ["status"] = "pending", ["at"] = DateTime.UtcNow.ToString("o"), ["note"] = "enqueued via CLI" })
        };
        var tasks = LoadTasks(workspaceRoot);
        tasks.Add(task);
        SaveTasks(tasks, workspaceRoot);
        return task;
    }
}
