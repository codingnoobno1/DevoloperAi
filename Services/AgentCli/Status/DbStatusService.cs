using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.AgentCli.Loop;
using Syncro.Desktop.Services.AgentCli.Models;
using Syncro.Desktop.Services.AgentCli.Nlp;
using Syncro.Desktop.Services.AgentCli.Scripts;

namespace Syncro.Desktop.Services.AgentCli.Status;

/// <summary>
/// Aggregates a <see cref="DbSnapshot"/> from the durable DB: tasks/loop status, script-execution
/// &amp; reuse stats, NLP mappings (+ learning source), token/vector totals, and a recent audit tail.
/// Cached briefly so the 1.5s dashboard poll doesn't thrash the disk. No ML — pure reads + counts.
/// </summary>
public class DbStatusService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMilliseconds(1500);

    private readonly SyncroDb _db;
    private readonly TaskStore _tasks;
    private readonly ScriptStore _scripts;
    private readonly ErrorMappingStore _mappings;
    private readonly KeywordLexicon _lexicon;

    private DbSnapshot? _cache;
    private DateTime _cacheAt;

    /// <summary>Project roots whose <c>.syncro_db/Projects/projects.json</c> contribute token/vector stats.</summary>
    public List<string> ProjectRoots { get; } = new();

    public DbStatusService(SyncroDb db, TaskStore tasks, ScriptStore scripts,
                           ErrorMappingStore mappings, KeywordLexicon lexicon)
    {
        _db = db;
        _tasks = tasks;
        _scripts = scripts;
        _mappings = mappings;
        _lexicon = lexicon;
    }

    public void RegisterProjectRoot(string root)
    {
        if (!string.IsNullOrWhiteSpace(root) && !ProjectRoots.Contains(root))
            ProjectRoots.Add(root);
    }

    public async Task<DbSnapshot> GetSnapshotAsync()
    {
        if (_cache != null && DateTime.UtcNow - _cacheAt < Ttl) return _cache;
        var snap = await BuildAsync();
        _cache = snap;
        _cacheAt = DateTime.UtcNow;
        return snap;
    }

    private async Task<DbSnapshot> BuildAsync()
    {
        var snap = new DbSnapshot();

        // ── Tasks / loop ──
        var tasks = await Safe(_tasks.AllAsync(), new List<TaskRecord>());
        foreach (var root in ProjectRoots.ToArray())
        {
            try
            {
                var projectTasksPath = Path.Combine(root, ".syncro_db", "Tasks", "tasks.jsonl");
                if (File.Exists(projectTasksPath))
                {
                    var pTasks = await _db.ReadJsonlAsync<TaskRecord>(projectTasksPath);
                    if (pTasks != null)
                    {
                        foreach (var pt in pTasks)
                        {
                            if (!tasks.Any(t => t.TaskId == pt.TaskId))
                            {
                                tasks.Add(pt);
                            }
                        }
                    }
                }
            }
            catch { }
        }
        snap.Tasks.Total = tasks.Count;
        foreach (var t in tasks)
        {
            var k = t.Status.ToString();
            snap.Tasks.ByStatus[k] = snap.Tasks.ByStatus.TryGetValue(k, out var c) ? c + 1 : 1;
        }
        snap.Tasks.Recent = tasks.OrderByDescending(t => t.UpdatedAt).Take(20).Select(t => new TaskRow
        {
            Id = t.TaskId, Title = t.Title, Status = t.Status.ToString(),
            Attempts = t.Attempts, ErrorCount = t.ErrorCount,
            Solution = t.Solution, ClassifiedBy = t.ClassifiedBy, ScriptId = t.ScriptId,
            TemplateScriptExecuted = t.TemplateScriptExecuted,
            Error = t.Error,
            Source = t.Source,
            ErrorType = t.ErrorType,
            NextAction = t.NextAction,
            LlmInvolved = t.LlmInvolved,
            ProjectId = t.ProjectId,
            Intent = t.Intent.ToString(),
            MaxAttempts = t.MaxAttempts,
            NeedsApproval = t.NeedsApproval,
            CreatedAt = t.CreatedAt.ToString("o"),
            UpdatedAt = t.UpdatedAt.ToString("o"),
            HistoryCount = t.History.Count
        }).ToList();

        // ── Scripts (execution + reuse) ──
        var scripts = await Safe(_scripts.AllAsync(), new List<ScriptRecord>());
        snap.Scripts.Total = scripts.Count;
        snap.Scripts.Safe = scripts.Count(x => x.Safe);
        snap.Scripts.Flagged = scripts.Count(x => x.Flagged);
        snap.Scripts.TotalRuns = scripts.Sum(x => x.Runs);
        snap.Scripts.TotalSucceeded = scripts.Sum(x => x.Succeeded);
        snap.Scripts.ReuseRate = snap.Scripts.TotalRuns == 0
            ? 0 : Math.Round((double)snap.Scripts.TotalSucceeded / snap.Scripts.TotalRuns, 2);
        snap.Scripts.Recent = scripts.OrderByDescending(x => x.LastUsed ?? x.CreatedAt).Take(20).Select(x => new ScriptRow
        {
            Id = x.Id, Name = x.Name, Purpose = x.Purpose, Runs = x.Runs,
            Succeeded = x.Succeeded, Failed = x.Failed, Safe = x.Safe, Flagged = x.Flagged, Source = x.Source
        }).ToList();

        // ── NLP mappings (DB-based "learning") ──
        var maps = await Safe(_mappings.AllAsync(), new List<ErrorMapping>());
        snap.Nlp.Mappings = maps.Count;
        foreach (var m in maps)
            snap.Nlp.BySource[m.Source] = snap.Nlp.BySource.TryGetValue(m.Source, out var c) ? c + 1 : 1;
        snap.Nlp.AvgWorkedRate = maps.Count == 0 ? 0 : Math.Round(maps.Average(m => m.Reliability), 2);
        snap.Nlp.Recent = maps.OrderByDescending(m => m.UpdatedAt).Take(20).Select(m => new MappingRow
        {
            Code = m.Code, Keywords = string.Join(",", m.Keywords), Solution = m.Solution,
            Hits = m.Hits, Worked = m.Worked, Source = m.Source
        }).ToList();
        try { snap.Nlp.LexiconSignals = (await _lexicon.EntriesAsync()).Count; } catch { }

        // ── Tokens / vectors (best-effort, per registered project root) ──
        foreach (var root in ProjectRoots.ToArray())
        {
            try
            {
                var pj = Path.Combine(root, ".syncro_db", "Projects", "projects.json");
                if (!File.Exists(pj)) continue;
                var arr = JArray.Parse(await File.ReadAllTextAsync(pj));
                foreach (var o in arr)
                {
                    var name = (string?)(o["name"] ?? o["project_id"]) ?? "project";
                    int ast = (int?)(o["astNodeCount"] ?? o["ast_node_count"]) ?? 0;
                    int vec = (int?)(o["vectorCount"] ?? o["vector_count"]) ?? 0;
                    snap.Tokens.Projects.Add(new ProjectTokens { Name = name, AstNodeCount = ast, VectorCount = vec });
                    snap.Tokens.TotalAstNodes += ast;
                    snap.Tokens.TotalVectors += vec;
                }
            }
            catch { /* tolerant: skip unreadable project */ }
        }

        // ── Audit tail ──
        try
        {
            var audit = await _db.ReadJsonlAsync<AuditEntry>(_db.Resolve("intelligence", "audit.jsonl"));
            snap.Audit = audit.AsEnumerable().Reverse().Take(15).Select(a => new AuditRow
            {
                At = a.At.ToString("HH:mm:ss"), Action = a.Action, Note = a.Note
            }).ToList();
        }
        catch { }

        return snap;
    }

    private static async Task<T> Safe<T>(Task<T> task, T fallback)
    {
        try { return await task; } catch { return fallback; }
    }
}
