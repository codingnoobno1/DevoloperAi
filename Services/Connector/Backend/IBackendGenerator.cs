using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Drafts backend files to satisfy a set of endpoints the backend doesn't yet expose. This is
    /// generation only — it never touches disk. The caller reviews <see cref="BackendGenerationResult.Files"/>
    /// (a Monaco diff in the workbench UI, once built) and applies them explicitly via
    /// <c>WriteBackendFilesTool</c>. Keeping "draft" and "apply" as separate tools means the AI can
    /// never write a file the user didn't approve.
    /// </summary>
    public interface IBackendGenerator
    {
        Task<BackendGenerationResult> GenerateAsync(BackendGenerationRequest request, CancellationToken ct = default);
    }
}
