using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Verification
{
    /// <summary>
    /// idearchitecture.md Decision 8 / guardrail #5 — "Nothing applies AI output unverified".
    /// The only sanctioned write path for machine-generated edits: apply → build → compare against a
    /// pre-patch baseline → keep or roll back. AI agents call this instead of <c>ApplyPatch</c>.
    /// </summary>
    public interface IVerificationEngine
    {
        /// <summary>
        /// Verify and (only on success) persist a patch. On any failure the file is restored to its
        /// exact prior bytes before returning.
        /// </summary>
        Task<VerificationResult> VerifyAndApplyAsync(PatchRequest request, CancellationToken ct = default);

        /// <summary>Verify without keeping the change — a dry run for "would this compile?".</summary>
        Task<VerificationResult> DryRunAsync(PatchRequest request, CancellationToken ct = default);
    }
}
