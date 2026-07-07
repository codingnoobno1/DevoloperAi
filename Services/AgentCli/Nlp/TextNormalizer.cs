using System.Text.RegularExpressions;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>
/// Makes error text comparable by stripping volatile bits (absolute paths, GUIDs, numbers,
/// timestamps) and lowercasing. No ML — pure regex normalization.
/// </summary>
public class TextNormalizer
{
    private static readonly Regex Guid = new(
        @"\b[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex WinPath = new(
        @"[a-zA-Z]:\\[^\s:*?""<>|]+", RegexOptions.Compiled);
    private static readonly Regex UnixPath = new(
        @"(?:/[^/\s]+){2,}", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Replace('\r', ' ').Replace('\n', ' ');
        s = Guid.Replace(s, "<guid>");
        s = WinPath.Replace(s, "<path>");
        s = UnixPath.Replace(s, "<path>");
        s = Number.Replace(s, "<n>");
        s = s.ToLowerInvariant();
        s = Whitespace.Replace(s, " ").Trim();
        return s;
    }
}
