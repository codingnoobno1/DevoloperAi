using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Syncro.Desktop.Services.AgentCli.Llm;
using Syncro.Desktop.Services.AgentCli.Loop;
using Syncro.Desktop.Services.AgentCli.Nlp;
using Syncro.Desktop.Services.AgentCli.Nlp.Pipeline;
using Syncro.Desktop.Services.AgentCli.Scripts;
using Syncro.Desktop.Services.AgentCli.Status;

namespace Syncro.Desktop.Services.AgentCli;

/// <summary>
/// One-call DI registration for the whole AgentCli core. Call from MauiProgram:
/// <c>builder.Services.AddAgentCli();</c>
/// The LLM defaults to <see cref="NullLlmGateway"/> (offline) — register a real
/// <see cref="ILlmGateway"/> before this call to enable LLM enrichment.
/// </summary>
public static class AgentCliServiceCollectionExtensions
{
    public static IServiceCollection AddAgentCli(this IServiceCollection services)
    {
        // Central DB facade (default base dir = %LOCALAPPDATA%/SyncroDesktop)
        services.AddSingleton<SyncroDb>();

        // LLM boundary — offline by default; TryAdd so a real gateway registered earlier wins.
        services.TryAddSingleton<ILlmGateway, NullLlmGateway>();

        // NLP (DB-based, no ML)
        services.AddSingleton<TextNormalizer>();
        services.AddSingleton<KeywordLexicon>();
        services.AddSingleton<KeywordExtractor>();
        services.AddSingleton<ErrorMappingStore>();
        services.AddSingleton<ErrorMatcher>();
        services.AddSingleton<IProjectAnalyzer, ProjectAnalyzer>();
        services.AddSingleton<IAstFeatureExtractor, AstFeatureExtractor>();
        services.AddSingleton<IHindsightRetriever, HindsightRetriever>();
        services.AddSingleton<IIntentClassifier, HeuristicIntentClassifier>();
        services.AddSingleton<ITaskPlanner, TaskPlanner>();
        services.AddSingleton<TaskRouter>();
        services.AddSingleton<LlmTeacher>();

        // Scripts (store + reuse)
        services.AddSingleton<IProcessExecutor, ProcessExecutor>();
        services.AddSingleton<ScriptSafetyScanner>();
        services.AddSingleton<ScriptStore>();
        services.AddSingleton<ScriptMatcher>();
        services.AddSingleton<ScriptRunnerAdapter>();

        // Loop
        services.AddSingleton<TaskStore>();
        services.AddSingleton<ResolutionPolicy>();
        services.AddSingleton<TaskLoopEngine>();

        // DB status aggregator (powers the diagnostics monitor)
        services.AddSingleton<DbStatusService>();

        return services;
    }
}
