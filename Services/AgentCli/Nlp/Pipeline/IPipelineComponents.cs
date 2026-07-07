using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Nlp.Pipeline;

public interface IProjectAnalyzer
{
    Task AnalyzeAsync(TaskRoutingContext ctx);
}

public interface IAstFeatureExtractor
{
    Task ExtractAsync(TaskRoutingContext ctx);
}

public interface IHindsightRetriever
{
    Task RetrieveAsync(TaskRoutingContext ctx);
}

public interface IIntentClassifier
{
    Task<(string Intent, double Confidence, string[] Reasons)> ClassifyAsync(TaskRoutingContext ctx);
}

public interface ITaskPlanner
{
    Task<TaskPlan> PlanAsync(TaskRoutingContext ctx, string intent, double confidence, string[] reasons);
}
