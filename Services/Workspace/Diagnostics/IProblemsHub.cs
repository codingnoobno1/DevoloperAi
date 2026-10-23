using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Workspace.Diagnostics
{
    /// <summary>
    /// The DiagnosticEngine seam (idearchitecture.md Phase 2). Every diagnostic producer reports into
    /// one hub; every consumer (the IDE Problems panel, the AI context builder, CI gates) reads one
    /// stream instead of querying Roslyn, the security scanner and the contract engine separately.
    ///
    /// Reporting is bucketed by (source, scope) so a source can refresh its own findings — e.g. one
    /// file's compiler errors — without disturbing anything else.
    /// </summary>
    public interface IProblemsHub
    {
        /// <summary>Replace the findings in one bucket. <paramref name="scope"/> is a per-source key —
        /// a document URI for <see cref="ProblemSource.Language"/>, a workspace path for
        /// <see cref="ProblemSource.Security"/>.</summary>
        void Report(ProblemSource source, string scope, IEnumerable<Problem> problems);

        /// <summary>Drop one bucket (e.g. a closed document).</summary>
        void Clear(ProblemSource source, string scope);

        /// <summary>Drop every bucket belonging to a source.</summary>
        void ClearSource(ProblemSource source);

        /// <summary>Every current problem, sorted most-severe first.</summary>
        IReadOnlyList<Problem> All { get; }

        /// <summary>Problems from one source only.</summary>
        IReadOnlyList<Problem> BySource(ProblemSource source);

        int ErrorCount { get; }
        int WarningCount { get; }

        /// <summary>Raised after any mutation, so the UI can re-render. Fired on the caller's thread.</summary>
        event Action? Changed;
    }
}
