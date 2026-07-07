using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.Ide.Models;

public class ProjectContext
{
    public string ProjectId { get; set; } = "";
    public ProjectMetadata Metadata { get; set; } = new("Unknown", "Unknown", "Unknown");
    public bool HasAstIndex { get; set; }
    public List<TaskRecord> Tasks { get; set; } = new();
}
