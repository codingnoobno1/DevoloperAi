using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>
/// Normalized fingerprint of an error: an optional code, the extracted signal keywords,
/// and the normalized text. Produced by KeywordExtractor; consumed by ErrorMatcher.
/// </summary>
public record ErrorSignature(
    string? Code,
    IReadOnlyList<string> Keywords,
    string NormalizedText);
