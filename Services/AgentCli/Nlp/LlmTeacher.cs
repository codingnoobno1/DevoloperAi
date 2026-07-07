using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Llm;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>
/// Learn-from-LLM, DB-style: when the matcher is unsure AND the LLM is available, ask the LLM to
/// pick a label from a fixed candidate set, then INSERT a new mappings row. Next time the same
/// class of error is handled offline by the table. The LLM only classifies — it never executes.
/// </summary>
public class LlmTeacher
{
    private static readonly Regex SecretRx = new(
        @"(?i)(api[_-]?key|token|secret|password|authorization)\s*[=:]\s*\S+", RegexOptions.Compiled);

    private readonly ILlmGateway _llm;
    private readonly ErrorMappingStore _store;

    public LlmTeacher(ILlmGateway llm, ErrorMappingStore store)
    {
        _llm = llm;
        _store = store;
    }

    public async Task<ErrorMapping?> TeachAsync(
        ErrorSignature sig, IReadOnlyList<string> candidateLabels, CancellationToken ct = default)
    {
        if (!_llm.IsAvailable) return null;

        var redacted = SecretRx.Replace(sig.NormalizedText, "$1=<redacted>");
        var system = "You classify build/runtime errors. Choose exactly ONE label from the candidate " +
                     "list and reply with only that label.";
        var user = $"Error (normalized, secrets redacted):\n<untrusted_context>\n{redacted}\n</untrusted_context>\n" +
                   $"Candidates: {string.Join(", ", candidateLabels)}";

        var resp = await _llm.CompleteAsync(new LlmRequest(system, user, candidateLabels), ct);
        if (!resp.Ok) return null;

        var label = (resp.Label ?? resp.Text)?.Trim();
        // Constrained: the answer MUST be one of the candidates.
        if (string.IsNullOrEmpty(label) || !candidateLabels.Contains(label)) return null;

        var row = new ErrorMapping
        {
            Code = sig.Code,
            Keywords = new List<string>(sig.Keywords),
            Label = label,
            Solution = label,
            Source = "llm"
        };
        await _store.UpsertAsync(row);
        return row;
    }
}
