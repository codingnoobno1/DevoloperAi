using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>The toolchain a project builds with. Detected from marker files, never branched on
    /// by feature code — callers ask <see cref="IBuildEngine"/> and get a uniform result.</summary>
    public enum BuildStack
    {
        Unknown,
        DotNet,     // *.sln / *.csproj      → dotnet build
        Flutter,    // pubspec.yaml + flutter → flutter analyze
        Node,       // package.json           → npm run build (or tsc --noEmit)
        Python      // requirements.txt / pyproject.toml → python -m compileall
    }

    public enum BuildSeverity { Info, Warning, Error }

    /// <summary>One structured compiler/analyzer message parsed out of raw tool output.</summary>
    public sealed record BuildDiagnostic(
        BuildSeverity Severity,
        string Message,
        string? FilePath,
        int Line,
        int Column,
        string? Code);

    /// <summary>
    /// The uniform result of building any stack. <see cref="Ok"/> reflects the process exit code;
    /// <see cref="Diagnostics"/> is what every consumer (Problems Hub, AI repair, CI gate) reads
    /// instead of re-parsing raw output.
    /// </summary>
    public sealed class BuildResult
    {
        public bool Ok { get; set; }
        public BuildStack Stack { get; set; }
        public string ProjectPath { get; set; } = "";
        public string Command { get; set; } = "";
        public int ExitCode { get; set; }
        public long DurationMs { get; set; }
        public List<BuildDiagnostic> Diagnostics { get; init; } = new();

        /// <summary>Full stdout+stderr, kept for the Output panel and for AI when parsing missed something.</summary>
        public string RawOutput { get; set; } = "";

        /// <summary>Set when the build could not even be attempted (no toolchain, bad path).</summary>
        public string? Error { get; set; }

        public int ErrorCount => Diagnostics.Count(d => d.Severity == BuildSeverity.Error);
        public int WarningCount => Diagnostics.Count(d => d.Severity == BuildSeverity.Warning);

        public string Summary => Error is { } e
            ? $"Build could not run: {e}"
            : $"{Stack} build {(Ok ? "succeeded" : "FAILED")} in {DurationMs}ms — " +
              $"{ErrorCount} error(s), {WarningCount} warning(s).";
    }
}
