using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;
using Syncro.Desktop.Services.AgentCli.Nlp;
using Syncro.Desktop.Services.AgentCli.Scripts;

namespace Syncro.Desktop.Services.AgentCli.Loop;

/// <summary>
/// The loopable state machine. One <see cref="LoopStatus"/> change → one action:
/// pending→run script, failed→diagnose (DB lookup, LLM if on), resolving→pick a reusable script,
/// patched→loop back, succeeded→finish. Bounded by MaxAttempts → NeedsHuman. Crash-resumable.
/// </summary>
public class TaskLoopEngine
{
    private readonly TaskStore _tasks;
    private readonly ResolutionPolicy _policy;
    private readonly ScriptMatcher _scriptMatcher;
    private readonly ScriptStore _scripts;
    private readonly ScriptRunnerAdapter _runner;
    private readonly ErrorMappingStore _mappings;
    private readonly SyncroDb _db;

    private readonly string _platform =
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";

    /// <summary>When true, SAFE solutions auto-loop without prompting. Mutating ones still gate.</summary>
    public bool Autonomous { get; set; }

    public TaskLoopEngine(TaskStore tasks, ResolutionPolicy policy, ScriptMatcher scriptMatcher,
                          ScriptStore scripts, ScriptRunnerAdapter runner, ErrorMappingStore mappings, SyncroDb db)
    {
        _tasks = tasks;
        _policy = policy;
        _scriptMatcher = scriptMatcher;
        _scripts = scripts;
        _runner = runner;
        _mappings = mappings;
        _db = db;
    }

    /// <summary>Advance one task by exactly one state.</summary>
    public Task<TaskRecord> StepAsync(TaskRecord t, CancellationToken ct = default) => t.Status switch
    {
        LoopStatus.Pending => RunStep(t, ct),
        LoopStatus.Failed => DiagnoseStep(t, ct),
        LoopStatus.Resolving => ResolveStep(t, ct),
        LoopStatus.Patched => Reenqueue(t),
        LoopStatus.Succeeded => FinishStep(t),
        LoopStatus.Running => DiagnoseStep(MarkInterrupted(t), ct), // crash-resume safety
        _ => Task.FromResult(t) // Done / NeedsHuman / Diagnosing are terminal here
    };

    /// <summary>Drain a task to a terminal state (Done or NeedsHuman), bounded.</summary>
    public async Task<TaskRecord> RunToCompletionAsync(TaskRecord t, CancellationToken ct = default)
    {
        int guard = 0;
        while (t.Status != LoopStatus.Done && t.Status != LoopStatus.NeedsHuman && guard++ < 50)
            t = await StepAsync(t, ct);
        return t;
    }

    private async Task<TaskRecord> RunStep(TaskRecord t, CancellationToken ct)
    {
        t.Transition(LoopStatus.Running, "executing");
        await _tasks.SaveAsync(t);

        if (t.Intent != TaskKind.RunScript || t.ScriptId == null)
        {
            t.Transition(LoopStatus.NeedsHuman, $"no executable script for intent '{t.Intent}'");
            await _tasks.SaveAsync(t);
            return t;
        }

        var script = await _scripts.GetAsync(t.ScriptId);
        if (script == null)
        {
            t.Error = true;
            t.Transition(LoopStatus.Failed, "script not found");
            await _tasks.SaveAsync(t);
            return t;
        }

        // Approval gate: unsafe/unproven scripts require approval unless autonomous mode is on.
        if (!script.Safe && !Autonomous)
        {
            t.NeedsApproval = true;
            t.Transition(LoopStatus.NeedsHuman, $"approval required to run script {script.Id}");
            await _tasks.SaveAsync(t);
            return t;
        }

        var result = await _runner.RunAsync(script, BuildArgs(t), Environment.CurrentDirectory, ct);
        t.TemplateScriptExecuted = true;
        await _scripts.BumpAsync(script.Id, result.Ok);
        await _db.AppendAuditAsync(new AuditEntry
        {
            Action = "task.run",
            Note = $"{t.TaskId} script={script.Id} exit={result.ExitCode}"
        });

        if (result.Ok)
        {
            t.Error = false;
            t.Transition(LoopStatus.Succeeded, "exit 0");
        }
        else
        {
            t.Error = true;
            t.ErrorCount++;
            t.Source = Truncate(result.StdErr, 240);
            t.Transition(LoopStatus.Failed, $"exit {result.ExitCode}");
        }
        await _tasks.SaveAsync(t);
        return t;
    }

    private async Task<TaskRecord> DiagnoseStep(TaskRecord t, CancellationToken ct)
    {
        t.Transition(LoopStatus.Diagnosing, "classifying error");
        await _tasks.SaveAsync(t);

        var res = await _policy.DecideAsync(t, t.Source ?? "", ct);
        t.Solution = res.Solution;
        t.NeedsApproval = res.NeedsApproval;
        t.NextAction = res.Why;
        if (res.MappingId != null) t.MatchedMapping = res.MappingId;
        if (res.ScriptId != null) t.ScriptId = res.ScriptId; // adopt the mapping's script

        if (res.Solution == Solutions.Human)
        {
            t.Transition(LoopStatus.NeedsHuman, res.Why);
            await _tasks.SaveAsync(t);
            return t;
        }

        t.Transition(LoopStatus.Resolving, res.Why);
        await _tasks.SaveAsync(t);
        return t;
    }

    private async Task<TaskRecord> ResolveStep(TaskRecord t, CancellationToken ct)
    {
        if (t.Attempts >= t.MaxAttempts)
        {
            if (t.MatchedMapping != null) await _mappings.BumpAsync(t.MatchedMapping, false);
            t.Transition(LoopStatus.NeedsHuman, "max attempts reached");
            await _tasks.SaveAsync(t);
            return t;
        }

        var solution = t.Solution ?? Solutions.None;
        if (!Solutions.IsSafe(solution) && !Autonomous)
        {
            t.NeedsApproval = true;
            t.Transition(LoopStatus.NeedsHuman, $"solution '{solution}' needs approval");
            await _tasks.SaveAsync(t);
            return t;
        }

        if (solution == Solutions.Rerun)
        {
            t.Attempts++;
            t.Transition(LoopStatus.Patched, "rerun same script");
            await _tasks.SaveAsync(t);
            return t;
        }

        // Reuse a stored script for this solution + the error keywords (no regeneration).
        var match = await _scriptMatcher.FindForAsync(solution, t.Keywords, _platform);
        if (match != null)
        {
            t.ScriptId = match.Script.Id;
            t.Attempts++;
            t.Transition(LoopStatus.Patched, $"using stored script {match.Script.Id} ({match.Explanation})");
            await _tasks.SaveAsync(t);
            return t;
        }

        t.Transition(LoopStatus.NeedsHuman, $"no stored script for solution '{solution}'");
        await _tasks.SaveAsync(t);
        return t;
    }

    private async Task<TaskRecord> Reenqueue(TaskRecord t)
    {
        t.Error = false;
        t.Transition(LoopStatus.Pending, "re-enqueued after resolve");
        await _tasks.SaveAsync(t);
        return t;
    }

    private async Task<TaskRecord> FinishStep(TaskRecord t)
    {
        if (t.MatchedMapping != null) await _mappings.BumpAsync(t.MatchedMapping, true);
        t.Transition(LoopStatus.Done, "completed");
        await _tasks.SaveAsync(t);
        await _db.AppendAuditAsync(new AuditEntry { Action = "task.done", Note = t.TaskId });
        return t;
    }

    private static TaskRecord MarkInterrupted(TaskRecord t)
    {
        t.Error = true;
        if (string.IsNullOrEmpty(t.Source)) t.Source = "interrupted while running";
        t.Status = LoopStatus.Failed;
        return t;
    }

    private static Dictionary<string, string> BuildArgs(TaskRecord t) => new()
    {
        ["ProjectPath"] = Environment.CurrentDirectory,
        ["ProjectId"] = t.ProjectId
    };

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
