using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Status;

/// <summary>
/// A point-in-time, read-only view of everything in the local DB — assembled from the durable
/// <c>.syncro_db</c> files, so it reflects real persisted state and survives app restarts.
/// Serialized to the diagnostics monitor (/api/db and /api/status).
/// </summary>
public class DbSnapshot
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public TasksSummary Tasks { get; set; } = new();
    public ScriptsSummary Scripts { get; set; } = new();
    public NlpSummary Nlp { get; set; } = new();
    public TokensSummary Tokens { get; set; } = new();
    public List<AuditRow> Audit { get; set; } = new();
}

public class TasksSummary
{
    public int Total { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public List<TaskRow> Recent { get; set; } = new();
}

public class TaskRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public int Attempts { get; set; }
    public int ErrorCount { get; set; }
    public string? Solution { get; set; }
    public string? ClassifiedBy { get; set; }
    public string? ScriptId { get; set; }
    public bool TemplateScriptExecuted { get; set; }
    public bool Error { get; set; }
    public string? Source { get; set; }
    public string? ErrorType { get; set; }
    public string? NextAction { get; set; }
    public bool LlmInvolved { get; set; }
    public string ProjectId { get; set; } = "";
    public string Intent { get; set; } = "";
    public int MaxAttempts { get; set; }
    public bool NeedsApproval { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public int HistoryCount { get; set; }
}

public class ScriptsSummary
{
    public int Total { get; set; }
    public int Safe { get; set; }
    public int Flagged { get; set; }
    public int TotalRuns { get; set; }
    public int TotalSucceeded { get; set; }
    public double ReuseRate { get; set; }
    public List<ScriptRow> Recent { get; set; } = new();
}

public class ScriptRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Purpose { get; set; } = "";
    public int Runs { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public bool Safe { get; set; }
    public bool Flagged { get; set; }
    public string Source { get; set; } = "";
}

public class NlpSummary
{
    public int Mappings { get; set; }
    public Dictionary<string, int> BySource { get; set; } = new();
    public int LexiconSignals { get; set; }
    public double AvgWorkedRate { get; set; }
    public List<MappingRow> Recent { get; set; } = new();
}

public class MappingRow
{
    public string? Code { get; set; }
    public string Keywords { get; set; } = "";
    public string Solution { get; set; } = "";
    public int Hits { get; set; }
    public int Worked { get; set; }
    public string Source { get; set; } = "";
}

public class TokensSummary
{
    public List<ProjectTokens> Projects { get; set; } = new();
    public int TotalVectors { get; set; }
    public int TotalAstNodes { get; set; }
}

public class ProjectTokens
{
    public string Name { get; set; } = "";
    public int AstNodeCount { get; set; }
    public int VectorCount { get; set; }
}

public class AuditRow
{
    public string At { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Note { get; set; }
}
