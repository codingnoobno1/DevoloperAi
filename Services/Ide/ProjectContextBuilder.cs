using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli;
using Syncro.Desktop.Services.AgentCli.Models;
using Syncro.Desktop.Services.Ide.Models;

namespace Syncro.Desktop.Services.Ide;

public interface IProjectContextBuilder
{
    Task<ProjectContext> BuildAsync(string rootPath, string projectId);
}

public class ProjectContextBuilder : IProjectContextBuilder
{
    public async Task<ProjectContext> BuildAsync(string rootPath, string projectId)
    {
        var ctx = new ProjectContext { ProjectId = projectId };
        
        // Use a project-specific SyncroDb instance so reads are isolated to this project's .syncro_db
        string projectDbPath = Path.Combine(rootPath, ".syncro_db");
        var projectDb = new SyncroDb(projectDbPath);
        
        // Load tasks
        var tasks = await projectDb.QueryTasksAsync(projectId);
        ctx.Tasks = tasks.ToList();

        // Check if AST exists
        string astDir = projectDb.Resolve("AST", projectId);
        ctx.HasAstIndex = Directory.Exists(astDir) && File.Exists(Path.Combine(astDir, "ast_index.json"));

        // Load project metadata
        var p = await projectDb.GetProjectAsync(projectId);
        if (p != null)
        {
            string lang = p.Language ?? "Unknown";
            string fw = p.Framework ?? "Unknown";
            string arch = "Unknown"; // Architecture is not in ProjectRecord
            ctx.Metadata = new ProjectMetadata(lang, fw, arch);
        }

        return ctx;
    }
}
