using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;
using Syncro.Desktop.Services.AgentCli.Nlp.Pipeline;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>
/// Replaces the old TaskMapper. Orchestrates the Software Understanding Engine pipeline.
/// </summary>
public class TaskRouter
{
    private readonly TextNormalizer _normalizer;
    private readonly IProjectAnalyzer _projectAnalyzer;
    private readonly IAstFeatureExtractor _astExtractor;
    private readonly IHindsightRetriever _hindsight;
    private readonly IIntentClassifier _classifier;
    private readonly ITaskPlanner _planner;

    public TaskRouter(
        TextNormalizer normalizer,
        IProjectAnalyzer projectAnalyzer,
        IAstFeatureExtractor astExtractor,
        IHindsightRetriever hindsight,
        IIntentClassifier classifier,
        ITaskPlanner planner)
    {
        _normalizer = normalizer;
        _projectAnalyzer = projectAnalyzer;
        _astExtractor = astExtractor;
        _hindsight = hindsight;
        _classifier = classifier;
        _planner = planner;
    }

    public async Task<TaskPlan> RouteAsync(string goal, string projectPath)
    {
        var norm = _normalizer.Normalize(goal);
        var ctx = new TaskRoutingContext(goal, norm, projectPath);

        await _projectAnalyzer.AnalyzeAsync(ctx);
        await _astExtractor.ExtractAsync(ctx);
        await _hindsight.RetrieveAsync(ctx);
        
        var (intent, confidence, reasons) = await _classifier.ClassifyAsync(ctx);
        var plan = await _planner.PlanAsync(ctx, intent, confidence, reasons);

        return plan;
    }
}
