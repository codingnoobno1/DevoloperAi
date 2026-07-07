using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Llm;
using Syncro.Desktop.Services.AgentCli.Models;
using Syncro.Desktop.Services.AgentCli.Nlp;

namespace Syncro.Desktop.Services.AgentCli.Loop;

/// <summary>The decision for how to resolve a failed task.</summary>
public record Resolution(string Solution, string? ScriptId, string? MappingId, bool NeedsApproval, string Why);

/// <summary>
/// Decides the solution for a failed task: extract keywords → match the mappings table; if the
/// match is confident, use it; else (LLM on) let the teacher label it and insert a new row; else
/// hand off to a human. Pure DB lookup + optional LLM labeling — no ML.
/// </summary>
public class ResolutionPolicy
{
    private const double HighConfidence = 0.60;
    private static readonly string[] Candidates =
    {
        Solutions.InstallDep, Solutions.ChangePort, Solutions.AddUsing, Solutions.FixSignature,
        Solutions.CreateFile, Solutions.Rerun, Solutions.RunAnother, Solutions.Human
    };

    private readonly KeywordExtractor _extractor;
    private readonly ErrorMatcher _matcher;
    private readonly LlmTeacher _teacher;
    private readonly ILlmGateway _llm;

    public ResolutionPolicy(KeywordExtractor extractor, ErrorMatcher matcher, LlmTeacher teacher, ILlmGateway llm)
    {
        _extractor = extractor;
        _matcher = matcher;
        _teacher = teacher;
        _llm = llm;
    }

    public async Task<Resolution> DecideAsync(TaskRecord task, string rawError, CancellationToken ct = default)
    {
        var sig = await _extractor.SignatureAsync(rawError);
        task.Keywords = new System.Collections.Generic.List<string>(sig.Keywords);
        task.ErrorType = sig.Keywords.Count > 0 ? string.Join(",", sig.Keywords) : (sig.Code ?? "unknown");
        task.Source = Truncate(rawError, 240);

        var match = await _matcher.MatchAsync(sig);
        if (match != null && match.Confidence >= HighConfidence)
        {
            task.MatchedMapping = match.Row.Id;
            task.MatchConfidence = match.Confidence;
            task.ClassifiedBy = "db";
            return new Resolution(match.Row.Solution, match.Row.ScriptId, match.Row.Id,
                                  NeedsApproval: !Solutions.IsSafe(match.Row.Solution),
                                  $"db match: {match.Explanation}");
        }

        // Low/no confidence → learn from the LLM (if available): it labels, we store a new row.
        if (_llm.IsAvailable)
        {
            var row = await _teacher.TeachAsync(sig, Candidates, ct);
            if (row != null)
            {
                task.LlmInvolved = true;
                task.MatchedMapping = row.Id;
                task.MatchConfidence = 0.70;
                task.ClassifiedBy = "llm";
                return new Resolution(row.Solution, row.ScriptId, row.Id,
                                      NeedsApproval: !Solutions.IsSafe(row.Solution),
                                      "llm taught a new mapping row");
            }
        }

        task.MatchConfidence = match?.Confidence ?? 0;
        task.ClassifiedBy = match?.Row.Source ?? "none";
        return new Resolution(Solutions.Human, null, match?.Row.Id, true, "no confident match; needs human");
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
