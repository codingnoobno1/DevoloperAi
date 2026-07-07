using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli;

/// <summary>
/// The ONE central facade over the file-based DB. Every store goes through this so the agent
/// and the CLI always see one consistent, atomic, audited state. Default base dir is the
/// per-user global store; set <see cref="BaseDir"/> to a project's <c>.syncro_db</c> for
/// per-project data.
/// </summary>
public class SyncroDb
{
    public string BaseDir { get; set; }

    private readonly SemaphoreSlim _lock = new(1, 1);

    public static readonly JsonSerializerSettings Pretty = CreateSettings(Formatting.Indented);
    public static readonly JsonSerializerSettings Line = CreateSettings(Formatting.None);

    public SyncroDb(string? baseDir = null)
    {
        BaseDir = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SyncroDesktop");
        Directory.CreateDirectory(BaseDir);
    }

    private static JsonSerializerSettings CreateSettings(Formatting formatting)
    {
        var settings = new JsonSerializerSettings
        {
            Formatting = formatting,
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() }
        };
        settings.Converters.Add(new StringEnumConverter(new SnakeCaseNamingStrategy()));
        return settings;
    }

    /// <summary>Resolve a path under BaseDir, creating parent directories.</summary>
    public string Resolve(params string[] parts)
    {
        var combined = new string[parts.Length + 1];
        combined[0] = BaseDir;
        Array.Copy(parts, 0, combined, 1, parts.Length);
        var path = Path.Combine(combined);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        return path;
    }

    private async Task<TResult> RetryReadAsync<TResult>(Func<Task<TResult>> action, int maxRetries = 3)
    {
        int delay = 10;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (IOException)
            {
                if (i == maxRetries - 1) throw;
                await Task.Delay(delay).ConfigureAwait(false);
                delay *= 5;
            }
        }
        return await action().ConfigureAwait(false);
    }

    public async Task<T?> ReadJsonAsync<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return await RetryReadAsync(async () => 
        {
            var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return default;
            return JsonConvert.DeserializeObject<T>(text, Pretty);
        }).ConfigureAwait(false);
    }

    public async Task WriteJsonAtomicAsync<T>(string path, T value)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonConvert.SerializeObject(value, Pretty)).ConfigureAwait(false);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        finally { _lock.Release(); }
    }

    public async Task<List<T>> ReadJsonlAsync<T>(string path)
    {
        var list = new List<T>();
        if (!File.Exists(path)) return list;
        return await RetryReadAsync(async () => 
        {
            var result = new List<T>();
            foreach (var line in await File.ReadAllLinesAsync(path).ConfigureAwait(false))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var item = JsonConvert.DeserializeObject<T>(line, Line);
                if (item != null) result.Add(item);
            }
            return result;
        }).ConfigureAwait(false);
    }

    public async Task AppendJsonlAsync<T>(string path, T value)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(value, Line);
            await File.AppendAllTextAsync(path, json + Environment.NewLine).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    /// <summary>Rewrite an entire jsonl file (used when a store updates rows in place).</summary>
    public async Task RewriteJsonlAsync<T>(string path, IEnumerable<T> items)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            await using (var writer = new StreamWriter(tmp, append: false))
            {
                foreach (var item in items)
                    await writer.WriteLineAsync(JsonConvert.SerializeObject(item, Line)).ConfigureAwait(false);
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        finally { _lock.Release(); }
    }

    public Task AppendAuditAsync(AuditEntry entry) =>
        AppendJsonlAsync(Resolve("intelligence", "audit.jsonl"), entry);

    public async Task<TaskRecord> EnqueueTaskAsync(TaskRecord t)
    {
        t.Transition(LoopStatus.Pending, "enqueued via facade");
        var all = await ReadJsonlAsync<TaskRecord>(TasksPath);
        all.Add(t);
        await RewriteJsonlAsync(TasksPath, all);
        return t;
    }

    public async Task UpdateTaskStatusAsync(string taskId, LoopStatus to, string? note = null)
    {
        var all = await ReadJsonlAsync<TaskRecord>(TasksPath);
        var idx = all.FindIndex(t => t.TaskId == taskId);
        if (idx >= 0)
        {
            all[idx].Transition(to, note);
            await RewriteJsonlAsync(TasksPath, all);
        }
    }

    public async Task<IReadOnlyList<TaskRecord>> QueryTasksAsync(string projectId, LoopStatus? status = null)
    {
        var all = await ReadJsonlAsync<TaskRecord>(TasksPath);
        return all.FindAll(t => 
            (string.IsNullOrEmpty(projectId) || t.ProjectId == projectId) && 
            (status == null || t.Status == status));
    }

    public async Task<ScriptRecord?> GetScriptAsync(string scriptId)
    {
        var scripts = await ReadJsonAsync<List<ScriptRecord>>(ScriptsIndexPath);
        return scripts?.Find(s => s.Id == scriptId);
    }

    public async Task AppendErrorAsync(ErrorRecord e)
    {
        string path = Resolve("intelligence", "errors", "errors.jsonl");
        await AppendJsonlAsync(path, e);
    }

    public async Task UpsertVectorsAsync(string projectId, IEnumerable<VectorRecord> v)
    {
        string path = Resolve("Vectors", projectId, "index.json");
        await WriteJsonAtomicAsync(path, v);
    }

    public async Task<ProjectRecord?> GetProjectAsync(string id)
    {
        string path = Resolve("Projects", "projects.json");
        var list = await ReadJsonAsync<List<ProjectRecord>>(path);
        return list?.Find(p => p.ProjectId == id);
    }

    public async Task<IReadOnlyList<MemoryNote>> GetTodoAsync(string projectId)
    {
        string path = Resolve("Memory", "todo.jsonl");
        var list = await ReadJsonlAsync<MemoryNote>(path);
        return list.FindAll(m => m.ProjectId == projectId && m.Kind == "todo");
    }

    public async Task AddTodoAsync(MemoryNote note)
    {
        string path = Resolve("Memory", "todo.jsonl");
        await AppendJsonlAsync(path, note);
    }

    // ── Well-known paths ─────────────────────────────────────────────
    public string NlpDir => Resolve("intelligence", "nlp", "_");      // ensures dir exists
    public string MappingsPath => Resolve("intelligence", "nlp", "mappings.jsonl");
    public string LexiconPath => Resolve("intelligence", "nlp", "error-keywords.json");
    public string IntentsPath => Resolve("intelligence", "nlp", "intents.json");
    public string ScriptsIndexPath => Resolve("intelligence", "scripts", "scripts.json");
    public string ScriptBodyPath(string fileName) => Resolve("intelligence", "scripts", "bodies", fileName);
    public string TasksPath => Resolve("intelligence", "tasks.jsonl");
}
