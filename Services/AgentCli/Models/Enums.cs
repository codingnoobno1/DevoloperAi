using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>Control-flow status of a task in the loop. Serialized as snake_case.</summary>
public enum LoopStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Diagnosing,
    Resolving,
    Patched,
    NeedsHuman,
    Done
}

/// <summary>What kind of work a task represents.</summary>
public enum TaskKind
{
    RunScript,
    EditCode,
    Analyse,
    Generate,
    LlmFix,
    Clone,
    Report,
    Show,
    Custom
}

/// <summary>
/// Canonical solution labels (data-driven; also produced by the NLP mappings table).
/// SAFE labels may auto-loop; the rest require approval.
/// </summary>
public static class Solutions
{
    public const string InstallDep = "install_dep";
    public const string ChangePort = "change_port";
    public const string AddUsing = "add_using";
    public const string FixSignature = "fix_signature";
    public const string CreateFile = "create_file";
    public const string Rerun = "rerun";
    public const string RunAnother = "run_another";
    public const string LlmFix = "llm_fix";
    public const string EditCode = "edit_code";
    public const string Human = "human";
    public const string None = "none";

    private static readonly HashSet<string> SafeLabels =
        new(StringComparer.OrdinalIgnoreCase) { InstallDep, ChangePort, Rerun, RunAnother };

    /// <summary>SAFE solutions can be auto-applied/looped without human approval.</summary>
    public static bool IsSafe(string? solution) =>
        !string.IsNullOrEmpty(solution) && SafeLabels.Contains(solution);
}
