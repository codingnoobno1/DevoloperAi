using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>
    /// idearchitecture.md Decision 2 — "Build is infrastructure, not logic". One interface over
    /// dotnet/flutter/npm/python producing structured diagnostics, so diagnostics aggregation,
    /// AI-repair, CI generation and verification all consume the same shape instead of each shelling
    /// out to <c>Process.Start("dotnet"…)</c> and re-parsing output.
    ///
    /// Implementations publish <c>BuildDone</c> on the event bus.
    /// </summary>
    public interface IBuildEngine
    {
        /// <summary>Which toolchain <paramref name="projectPath"/> builds with, from marker files.</summary>
        BuildStack Detect(string projectPath);

        /// <summary>
        /// Build (or, where a full build is prohibitively slow, type-check/analyze) the project.
        /// Never throws for a failing build — a failure is a <see cref="BuildResult"/> with
        /// <c>Ok == false</c> and diagnostics.
        /// </summary>
        Task<BuildResult> BuildAsync(string projectPath, CancellationToken ct = default);
    }
}
