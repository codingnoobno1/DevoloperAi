using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

public record ProjectMetadata(string Language, string Framework, string Architecture);

public record AstContext(IReadOnlyList<string> ExistingSymbols, string MissingLayerGuess);

public class TaskRoutingContext
{
    public string RawGoal { get; }
    public string NormalizedGoal { get; }
    public string ProjectPath { get; }
    
    public ProjectMetadata Metadata { get; set; } = new("Unknown", "Unknown", "Unknown");
    public AstContext Ast { get; set; } = new(new List<string>(), "");
    public IReadOnlyList<string> HindsightKnowledge { get; set; } = new List<string>();

    public TaskRoutingContext(string rawGoal, string normalizedGoal, string projectPath)
    {
        RawGoal = rawGoal;
        NormalizedGoal = normalizedGoal;
        ProjectPath = projectPath;
    }
}
