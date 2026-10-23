using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Connector.Matching;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <inheritdoc cref="IContractEngine"/>
    public sealed class ContractEngine : IContractEngine
    {
        private readonly IFrontendContractResolver _frontend;
        private readonly IBackendContractResolver _backend;
        private readonly ContractSnapshotStore _store;
        private readonly Workspace.Events.IEventBus? _bus;

        // The event bus is optional: the engine works standalone (tests, CLI) and, when the Workspace
        // kernel is registered, publishes ContractDrift so diagnostics/AI/UI can react. DI supplies the
        // default (null) when IEventBus isn't registered.
        public ContractEngine(
            IFrontendContractResolver frontend,
            IBackendContractResolver backend,
            ContractSnapshotStore store,
            Workspace.Events.IEventBus? bus = null)
        {
            _frontend = frontend;
            _backend = backend;
            _store = store;
            _bus = bus;
        }

        public async Task<ConnectionReport> MapAsync(string frontendPath, string backendPath, CancellationToken ct = default)
        {
            var fe = await _frontend.ResolveAsync(frontendPath, ct);
            var be = await _backend.ResolveAsync(backendPath, ct);
            return ContractMatcher.Match(fe.Calls, be.Routes);
        }

        public async Task<ContractSnapshot> SnapshotAsync(string backendPath, CancellationToken ct = default)
        {
            var be = await _backend.ResolveAsync(backendPath, ct);
            return ToSnapshot(backendPath, be);
        }

        public async Task<ContractVerifyResult> VerifyAsync(
            string projectRoot,
            string? frontendPath,
            string backendPath,
            bool updateBaseline = false,
            CancellationToken ct = default)
        {
            // Resolve the backend once; reuse it for both the snapshot and the mapping.
            var be = await _backend.ResolveAsync(backendPath, ct);
            var current = ToSnapshot(backendPath, be);

            var result = new ContractVerifyResult();

            // Temporal: diff against the stored baseline.
            var baseline = await _store.LoadAsync(projectRoot);
            result.HasBaseline = baseline != null;
            if (baseline != null)
                result.Drift = ContractDiffer.Diff(baseline, current);

            // Spatial: map the frontend's calls onto the backend's routes.
            if (!string.IsNullOrWhiteSpace(frontendPath))
            {
                var fe = await _frontend.ResolveAsync(frontendPath!, ct);
                result.Connection = ContractMatcher.Match(fe.Calls, be.Routes);
            }

            var breakingDrift = result.Drift?.HasBreakingChanges == true;
            var brokenCalls = result.Connection != null &&
                              (result.Connection.Missing > 0 || result.Connection.MethodMismatch > 0);
            result.Ok = !breakingDrift && !brokenCalls;

            if (updateBaseline)
            {
                await _store.SaveAsync(projectRoot, current);
                result.BaselineUpdated = true;
            }

            // Announce breaking drift on the bus (when the Workspace kernel is wired). Subscribers —
            // the Problems Hub, the AI agents, the UI — react without depending on the Connector types.
            if (breakingDrift && _bus != null)
            {
                var summaries = result.Drift!.Changes.Where(c => c.Breaking).Select(c => c.Detail).ToList();
                _bus.Publish(new Workspace.Events.ContractDriftDetected(
                    projectRoot, result.Drift.BreakingCount, result.Drift.Total, summaries));
            }

            result.Summary = BuildSummary(result);
            return result;
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        private static ContractSnapshot ToSnapshot(string backendPath, BackendContract be) => new()
        {
            BackendPath = backendPath,
            Stack = be.Stack.ToString(),
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Routes = be.Routes.Select(r => new RouteSnapshot
            {
                Method = r.Method.ToString().ToUpperInvariant(),
                PathTemplate = r.PathTemplate,
                Request = r.RequestSchema.Select(ToField).ToList(),
                Response = r.ResponseSchema.Select(ToField).ToList()
            }).ToList()
        };

        private static FieldSnapshot ToField(ApiField f) => new()
        {
            Name = f.Name,
            Type = f.Type,
            Required = f.Required
        };

        private static string BuildSummary(ContractVerifyResult r)
        {
            if (!r.HasBaseline)
                return "No baseline captured yet — run with updateBaseline=true to record the current contract.";

            var parts = new System.Collections.Generic.List<string>();
            if (r.Drift != null)
                parts.Add(r.Drift.HasBreakingChanges
                    ? $"{r.Drift.BreakingCount} breaking change(s) of {r.Drift.Total} since baseline"
                    : $"{r.Drift.Total} non-breaking change(s) since baseline");
            if (r.Connection != null)
                parts.Add($"frontend: {r.Connection.Matched} matched, {r.Connection.Missing} missing, {r.Connection.MethodMismatch} method-mismatch");
            parts.Add(r.Ok ? "OK" : "CONTRACT BROKEN");
            return string.Join(" · ", parts);
        }
    }
}
