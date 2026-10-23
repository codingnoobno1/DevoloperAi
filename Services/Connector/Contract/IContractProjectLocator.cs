using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>Resolves a workspace root to its connected backend/frontend paths (absolute), or
    /// (null, null) when the workspace has no connector.json. Kept as a seam so the reverify subscriber
    /// is testable without touching disk.</summary>
    public interface IContractProjectLocator
    {
        Task<(string? BackendPath, string? FrontendPath)> LocateAsync(string workspaceRoot);
    }
}
