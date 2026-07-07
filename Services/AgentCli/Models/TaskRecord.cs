using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>
/// The loopable unit of work shared between the agent and the CLI (persisted in tasks.jsonl).
/// A single <see cref="Status"/> change drives the next action in the loop.
/// </summary>
public class TaskRecord
{
    public string TaskId { get; set; } = "tsk_" + Guid.NewGuid().ToString("N")[..8];
    public string ProjectId { get; set; } = "";
    public string Title { get; set; } = "";
    public TaskKind Intent { get; set; } = TaskKind.RunScript;

    public string? ScriptId { get; set; }
    public bool TemplateScriptExecuted { get; set; }

    public LoopStatus Status { get; set; } = LoopStatus.Pending;

    // Error fields
    public bool Error { get; set; }
    public int ErrorCount { get; set; }
    public string? Source { get; set; }
    public string? ErrorType { get; set; }

    // Resolution
    public string? Solution { get; set; }
    public string? NextAction { get; set; }
    public bool LlmInvolved { get; set; }

    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 4;
    public bool NeedsApproval { get; set; }

    // NLP fields (db-based, no ML)
    public List<string> Keywords { get; set; } = new();
    public string? MatchedMapping { get; set; }
    public double MatchConfidence { get; set; }
    public string? ClassifiedBy { get; set; } // db | llm | human | seed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<TaskEvent> History { get; set; } = new();

    /// <summary>Record a status transition with a note (appends to history, bumps UpdatedAt).</summary>
    public void Transition(LoopStatus to, string? note = null)
    {
        Status = to;
        UpdatedAt = DateTime.UtcNow;
        History.Add(new TaskEvent { Status = to, At = UpdatedAt, Note = note });
    }
}

public class TaskEvent
{
    public LoopStatus Status { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}
