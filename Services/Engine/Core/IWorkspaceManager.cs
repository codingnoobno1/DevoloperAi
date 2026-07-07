using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Core
{
    public interface IWorkspaceManager
    {
        /// <summary>
        /// Gets the root path of the active project (Admin cloned or independent local).
        /// </summary>
        string ActiveWorkspacePath { get; }

        /// <summary>
        /// Sets the active workspace context.
        /// </summary>
        void SetWorkspace(string path);

        /// <summary>
        /// Scans the workspace and returns a high-level summary.
        /// </summary>
        Task<string> GetWorkspaceSummaryAsync();
    }
}
