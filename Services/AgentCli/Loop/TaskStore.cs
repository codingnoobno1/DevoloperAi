using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Loop;

/// <summary>Persists tasks to <c>tasks.jsonl</c> (full rewrite on update; crash-resumable).</summary>
public class TaskStore
{
    private readonly SyncroDb _db;

    public TaskStore(SyncroDb db) => _db = db;

    public async Task<List<TaskRecord>> AllAsync() =>
        await _db.ReadJsonlAsync<TaskRecord>(_db.TasksPath);

    public async Task<TaskRecord?> GetAsync(string taskId) =>
        (await AllAsync()).Find(t => t.TaskId == taskId);

    public async Task<TaskRecord> EnqueueAsync(TaskRecord task)
    {
        task.Transition(LoopStatus.Pending, "enqueued");
        await SaveAsync(task);
        return task;
    }

    public async Task SaveAsync(TaskRecord task)
    {
        var all = await AllAsync();
        var idx = all.FindIndex(t => t.TaskId == task.TaskId);
        if (idx >= 0) all[idx] = task; else all.Add(task);
        await _db.RewriteJsonlAsync(_db.TasksPath, all);
    }

    public async Task<List<TaskRecord>> QueryAsync(string? projectId = null, LoopStatus? status = null)
    {
        var all = await AllAsync();
        return all.FindAll(t =>
            (projectId == null || t.ProjectId == projectId) &&
            (status == null || t.Status == status));
    }
}
