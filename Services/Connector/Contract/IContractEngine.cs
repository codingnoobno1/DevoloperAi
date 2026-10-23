using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>
    /// First-class Contract Engine — the single owner of the frontend↔backend contract relationship.
    /// Consolidates the resolve→match logic that was duplicated across MapContractsTool /
    /// MultiProjectScanService / WorkspaceIndexer, and adds the temporal dimension: capture a baseline
    /// and detect <b>breaking drift</b> when the backend surface changes underneath the frontend.
    ///
    /// Two axes:
    ///   • <b>Spatial</b> (<see cref="MapAsync"/>) — does the frontend's current calls line up with the
    ///     backend's current routes? (matched / mismatch / missing / orphan).
    ///   • <b>Temporal</b> (<see cref="VerifyAsync"/>) — has the backend changed vs the agreed baseline
    ///     in a way that breaks consumers even though both sides still compile?
    /// </summary>
    public interface IContractEngine
    {
        /// <summary>Resolve both sides and map frontend calls onto backend routes (spatial check).</summary>
        Task<ConnectionReport> MapAsync(string frontendPath, string backendPath, CancellationToken ct = default);

        /// <summary>Capture the backend's current API surface as a comparable snapshot.</summary>
        Task<ContractSnapshot> SnapshotAsync(string backendPath, CancellationToken ct = default);

        /// <summary>
        /// The full contract verification for a project: diff the live backend against the stored
        /// baseline (temporal drift) and, when a frontend path is given, map calls onto routes (spatial).
        /// Optionally re-baseline. <see cref="ContractVerifyResult.Ok"/> is the CI/verification gate.
        /// </summary>
        Task<ContractVerifyResult> VerifyAsync(
            string projectRoot,
            string? frontendPath,
            string backendPath,
            bool updateBaseline = false,
            CancellationToken ct = default);
    }

    /// <summary>Combined verdict from <see cref="IContractEngine.VerifyAsync"/>.</summary>
    public sealed class ContractVerifyResult
    {
        /// <summary>Whether a baseline existed to diff against.</summary>
        public bool HasBaseline { get; set; }

        /// <summary>Temporal drift vs baseline (null when there was no baseline).</summary>
        public ContractDriftReport? Drift { get; set; }

        /// <summary>Spatial frontend↔backend mapping (null when no frontend path was supplied).</summary>
        public ConnectionReport? Connection { get; set; }

        /// <summary>True if the baseline was (re)written during this call.</summary>
        public bool BaselineUpdated { get; set; }

        /// <summary>The gate: no breaking drift AND no missing / method-mismatched frontend calls.</summary>
        public bool Ok { get; set; }

        public string Summary { get; set; } = "";
    }
}
