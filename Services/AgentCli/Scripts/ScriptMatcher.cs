using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Scripts;

/// <summary>
/// Finds a reusable script for a purpose + error keywords on a platform. Same DB-lookup scoring
/// style as the NLP matcher: <c>0.4*purpose + 0.3*overlap + 0.15*platform + 0.15*reliability</c>.
/// Flagged scripts are excluded.
/// </summary>
public class ScriptMatcher
{
    private readonly ScriptStore _store;

    public ScriptMatcher(ScriptStore store) => _store = store;

    public async Task<ScriptMatch?> FindForAsync(string purpose, IReadOnlyList<string> keywords, string platform)
    {
        var all = await _store.AllAsync();
        ScriptRecord? best = null;
        double bestScore = 0;
        string why = "";

        foreach (var s in all)
        {
            if (s.Flagged) continue;
            double purposeMatch = string.Equals(s.Purpose, purpose, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            double overlap = s.Keywords.Count == 0 ? 0 : (double)Intersect(s.Keywords, keywords) / s.Keywords.Count;
            double plat = s.Platform.Count == 0 ? 1
                : (s.Platform.Exists(p => string.Equals(p, platform, StringComparison.OrdinalIgnoreCase)) ? 1 : 0);
            double score = 0.40 * purposeMatch + 0.30 * overlap + 0.15 * plat + 0.15 * s.Reliability;
            if (score > bestScore)
            {
                bestScore = score;
                best = s;
                why = $"purpose={purposeMatch:0.0} overlap={overlap:0.00} platform={plat:0.0} reliability={s.Reliability:0.00}";
            }
        }

        return best == null ? null : new ScriptMatch(best, bestScore, why);
    }

    private static int Intersect(IEnumerable<string> a, IEnumerable<string> b)
    {
        var set = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var x in a) if (set.Contains(x)) count++;
        return count;
    }
}
