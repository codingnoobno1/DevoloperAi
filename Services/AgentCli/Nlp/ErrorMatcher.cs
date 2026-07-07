using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>
/// Maps an error to an action by scanning the mappings table and scoring each row:
/// <c>score = 0.45*codeMatch + 0.30*keywordOverlap + 0.10*phraseHit + 0.15*reliability</c>.
/// Pure set math + a ratio — no ML.
/// </summary>
public class ErrorMatcher
{
    private readonly ErrorMappingStore _store;

    public ErrorMatcher(ErrorMappingStore store) => _store = store;

    public async Task<ErrorMatch?> MatchAsync(ErrorSignature sig)
    {
        var rows = await _store.AllAsync();
        ErrorMapping? best = null;
        double bestScore = 0;
        string why = "";

        foreach (var row in rows)
        {
            double codeMatch = row.Code != null && sig.Code != null &&
                               string.Equals(row.Code, sig.Code, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            double overlap = row.Keywords.Count == 0 ? 0 : (double)Intersect(row.Keywords, sig.Keywords) / row.Keywords.Count;
            double phrase = 0;
            foreach (var p in row.MatchPhrases)
                if (sig.NormalizedText.Contains(p.ToLowerInvariant())) { phrase = 1; break; }

            double score = 0.45 * codeMatch + 0.30 * overlap + 0.10 * phrase + 0.15 * row.Reliability;
            if (score > bestScore)
            {
                bestScore = score;
                best = row;
                why = $"code={codeMatch:0.0} overlap={overlap:0.00} phrase={phrase:0.0} reliability={row.Reliability:0.00}";
            }
        }

        return best == null ? null : new ErrorMatch(best, bestScore, why);
    }

    private static int Intersect(IEnumerable<string> a, IEnumerable<string> b)
    {
        var set = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var x in a) if (set.Contains(x)) count++;
        return count;
    }
}
