using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Security;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Workspace.Diagnostics
{
    /// <summary>
    /// Feeds the <see cref="IProblemsHub"/> from the event bus — the aggregation half of the
    /// DiagnosticEngine (idearchitecture.md Phase 2).
    ///
    ///   WorkspaceIndexed      → run the SAST scanner → Security problems
    ///   ContractDriftDetected → breaking-change summaries → Contract problems
    ///   WorkspaceClosed       → drop every workspace-scoped bucket
    ///
    /// Roslyn/LSP diagnostics are pushed in directly by the editor shell (it already has them
    /// per-keystroke), so this subscriber deliberately does not poll the language service.
    /// </summary>
    public sealed class DiagnosticsSubscriber : IDisposable
    {
        private readonly IProblemsHub _hub;
        private readonly SecurityScanner _scanner;
        private readonly List<IDisposable> _subs = new();
        private CancellationTokenSource? _scanCts;

        public DiagnosticsSubscriber(IEventBus bus, IProblemsHub hub, SecurityScanner scanner)
        {
            _hub = hub;
            _scanner = scanner;

            _subs.Add(bus.Subscribe<WorkspaceIndexed>(e => _ = Task.Run(() => ScanSecurityAsync(e.WorkspacePath))));
            _subs.Add(bus.Subscribe<ContractDriftDetected>(OnContractDrift));
            _subs.Add(bus.Subscribe<BuildDone>(OnBuildDone));
            _subs.Add(bus.Subscribe<WorkspaceClosed>(e =>
            {
                _hub.Clear(ProblemSource.Security, e.WorkspacePath);
                _hub.Clear(ProblemSource.Contract, e.WorkspacePath);
            }));
        }

        private async Task ScanSecurityAsync(string workspacePath)
        {
            // A new index supersedes any in-flight scan for the previous state.
            var cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _scanCts, cts)?.Cancel();

            try
            {
                var report = await _scanner.ScanAsync(workspacePath, Severity.High, cts.Token);
                if (cts.Token.IsCancellationRequested) return;

                _hub.Report(ProblemSource.Security, workspacePath,
                    report.Findings.Select(f => new Problem(
                        Source: ProblemSource.Security,
                        Severity: MapSeverity(f.Severity),
                        Message: $"{f.Title} — {f.Why}",
                        FilePath: f.File,
                        Line: f.Line,
                        Code: f.RuleId)));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Diagnostics] Security scan failed: {ex.Message}");
            }
        }

        private void OnContractDrift(ContractDriftDetected e)
        {
            _hub.Report(ProblemSource.Contract, e.WorkspacePath,
                e.BreakingSummaries.Select(s => new Problem(
                    Source: ProblemSource.Contract,
                    Severity: ProblemSeverity.Error,
                    Message: $"Breaking contract drift: {s}",
                    FilePath: e.WorkspacePath,
                    Code: "CONTRACT_DRIFT")));
        }

        /// <summary>
        /// A build result reports one summary line rather than every diagnostic: the full per-file
        /// detail lives in the <c>BuildResult</c> the caller already holds, and duplicating it here
        /// would double-report errors the language service is showing per-document.
        /// </summary>
        private void OnBuildDone(BuildDone e)
        {
            if (e.Ok && e.ErrorCount == 0)
            {
                _hub.Clear(ProblemSource.Build, e.WorkspacePath);
                return;
            }

            _hub.Report(ProblemSource.Build, e.WorkspacePath, new[]
            {
                new Problem(
                    Source: ProblemSource.Build,
                    Severity: e.Ok ? ProblemSeverity.Warning : ProblemSeverity.Error,
                    Message: e.Summary,
                    FilePath: e.WorkspacePath,
                    Code: $"BUILD_{e.Stack.ToUpperInvariant()}")
            });
        }

        /// <summary>SAST severity → unified severity. Critical/High are hard errors; Medium warns.</summary>
        private static ProblemSeverity MapSeverity(Severity s) => s switch
        {
            Severity.Critical or Severity.High => ProblemSeverity.Error,
            Severity.Medium => ProblemSeverity.Warning,
            Severity.Low => ProblemSeverity.Info,
            _ => ProblemSeverity.Hint
        };

        public void Dispose()
        {
            _scanCts?.Cancel();
            foreach (var s in _subs) s.Dispose();
        }
    }
}
