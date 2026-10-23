using System;

namespace Syncro.Desktop.Services.Workspace.Diagnostics
{
    /// <summary>Where a problem came from. Each source owns its own bucket in the hub, so refreshing
    /// one never drops another's findings.</summary>
    public enum ProblemSource
    {
        /// <summary>Roslyn / LSP compiler diagnostics for an open document.</summary>
        Language,

        /// <summary>SAST findings from <c>SecurityScanner</c>.</summary>
        Security,

        /// <summary>Breaking backend↔frontend drift from the Contract Engine.</summary>
        Contract,

        /// <summary>Compiler/tool output from a build run.</summary>
        Build,

        /// <summary>Anything an AI agent flagged.</summary>
        Agent
    }

    /// <summary>Unified severity across every source. Ordered so <c>&gt;=</c> comparisons work.</summary>
    public enum ProblemSeverity
    {
        Hint = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// One entry in the Problems Hub — the single shape every diagnostic source is normalized into
    /// (idearchitecture.md: "Unify Roslyn + LSP + build + analyzer + git + security + AI into one
    /// stream"). Carries primitives only so the UI needs no dependency on Roslyn/Connector/Security types.
    /// </summary>
    public sealed record Problem(
        ProblemSource Source,
        ProblemSeverity Severity,
        string Message,
        string? FilePath = null,
        int Line = 0,
        int Column = 0,
        string? Code = null)
    {
        /// <summary>Short source tag for the UI ("language", "security", …).</summary>
        public string SourceTag => Source.ToString().ToLowerInvariant();

        /// <summary>"file.cs:42" or just the message location-free.</summary>
        public string Location =>
            string.IsNullOrEmpty(FilePath)
                ? ""
                : $"{System.IO.Path.GetFileName(FilePath)}{(Line > 0 ? $":{Line}" : "")}";
    }
}
