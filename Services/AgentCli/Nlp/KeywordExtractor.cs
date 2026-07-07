using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>
/// "Find keywords in errors": normalize → match the lexicon → extract an <see cref="ErrorSignature"/>
/// (error code + signal keywords). No ML.
/// </summary>
public class KeywordExtractor
{
    private static readonly Regex CodeRx = new(@"\b([A-Z]{2,5}\d{3,5})\b", RegexOptions.Compiled);

    private readonly TextNormalizer _normalizer;
    private readonly KeywordLexicon _lexicon;

    public KeywordExtractor(TextNormalizer normalizer, KeywordLexicon lexicon)
    {
        _normalizer = normalizer;
        _lexicon = lexicon;
    }

    public async Task<ErrorSignature> SignatureAsync(string rawError)
    {
        var normalized = _normalizer.Normalize(rawError);

        // Error codes are uppercase (CS1061, TS2307) — read from the raw text before lowercasing.
        string? code = null;
        var m = CodeRx.Match(rawError);
        if (m.Success) code = m.Groups[1].Value.ToUpperInvariant();

        var keywords = new List<string>();
        foreach (var entry in await _lexicon.EntriesAsync())
        {
            if (entry.Pattern.IsMatch(rawError) || entry.Pattern.IsMatch(normalized))
                if (!keywords.Contains(entry.Signal))
                    keywords.Add(entry.Signal);
        }

        return new ErrorSignature(code, keywords, normalized);
    }
}
