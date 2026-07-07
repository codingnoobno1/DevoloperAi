using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Engine.Workspace
{
    public class WorkspaceManager : IWorkspaceManager
    {
        public string ActiveWorkspacePath { get; private set; } = string.Empty;

        public void SetWorkspace(string path)
        {
            ActiveWorkspacePath = path;
        }

        public Task<string> GetWorkspaceSummaryAsync()
        {
            if (string.IsNullOrEmpty(ActiveWorkspacePath) || !Directory.Exists(ActiveWorkspacePath))
            {
                return Task.FromResult("No active workspace or directory does not exist.");
            }

            // Simple basic summary for now
            string summary = $"Active Workspace: {ActiveWorkspacePath}\n";
            int fileCount = Directory.GetFiles(ActiveWorkspacePath, "*.*", SearchOption.AllDirectories).Length;
            summary += $"Total Files: {fileCount}\n";
            
            return Task.FromResult(summary);
        }
    }
}
