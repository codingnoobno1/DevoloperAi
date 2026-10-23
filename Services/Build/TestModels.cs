using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>
    /// The uniform result of running a project's test suite. Mirrors <see cref="BuildResult"/> so the
    /// verification gate treats "the code compiles" and "the code still behaves" the same way.
    /// </summary>
    public sealed class TestResult
    {
        /// <summary>True when the runner exited 0 — i.e. the suite is green.</summary>
        public bool Ok { get; set; }

        public BuildStack Stack { get; set; }
        public string ProjectPath { get; set; } = "";
        public string Command { get; set; } = "";
        public int ExitCode { get; set; }
        public long DurationMs { get; set; }

        /// <summary>
        /// Names of tests the runner reported as failing. Empty with <see cref="Ok"/> false means the
        /// suite failed but names could not be parsed — callers must fall back to <see cref="Ok"/>.
        /// </summary>
        public List<string> FailedTests { get; init; } = new();

        public string RawOutput { get; set; } = "";

        /// <summary>Set when tests could not be attempted at all (no runner, no suite, bad path).</summary>
        public string? Error { get; set; }

        /// <summary>True when this project has no discoverable test suite — not a failure.</summary>
        public bool Skipped { get; set; }

        public int FailedCount => FailedTests.Count;

        /// <summary>Total number of passing tests reported by the runner (e.g. 148 in '148/148 passed').</summary>
        public int PassedCount { get; set; }

        /// <summary>Total number of executed tests reported by the runner.</summary>
        public int TotalCount { get; set; }

        public string Summary =>
            Skipped ? "No test suite detected — tests skipped."
            : Error is { } e ? $"Tests could not run: {e}"
            : $"{Stack} tests {(Ok ? "passed" : "FAILED")} ({(TotalCount > 0 ? $"{PassedCount}/{TotalCount}" : "suite")}) in {DurationMs}ms" +
              (FailedCount > 0 ? $" — {FailedCount} failing: {string.Join(", ", FailedTests.Take(5))}" : ".");
    }
}
