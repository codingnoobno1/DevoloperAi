using System.Collections.Generic;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Scripts;

/// <summary>
/// Deny-list scanner run before a script is stored AND before it runs. A hit flags the script
/// as never-auto-run (requires a human). Defense in depth, not a substitute for approval.
/// </summary>
public class ScriptSafetyScanner
{
    private static readonly (Regex Rx, string Desc)[] Deny =
    {
        (new(@"rm\s+-rf\s+(/|~|\$HOME|\*)", RegexOptions.IgnoreCase), "recursive force delete of root/home"),
        (new(@"\bdel\s+/[sq]\b", RegexOptions.IgnoreCase), "recursive del"),
        (new(@"\brmdir\s+/s\b", RegexOptions.IgnoreCase), "recursive rmdir"),
        (new(@"remove-item\s+.*-recurse.*-force", RegexOptions.IgnoreCase), "recursive force Remove-Item"),
        (new(@"\bmkfs\b|\bformat\s+[a-z]:|diskpart", RegexOptions.IgnoreCase), "disk format"),
        (new(@"\bshutdown\b|\breboot\b", RegexOptions.IgnoreCase), "power state change"),
        (new(@">\s*/dev/sd[a-z]", RegexOptions.IgnoreCase), "raw disk write"),
        (new(@":\(\)\s*\{\s*:\|:&\s*\};:", RegexOptions.IgnoreCase), "fork bomb"),
        (new(@"\b(curl|wget)\b[^\n|]*\|\s*(sudo\s+)?(ba)?sh", RegexOptions.IgnoreCase), "pipe remote script to shell"),
        (new(@"reg\s+delete\b", RegexOptions.IgnoreCase), "registry delete")
    };

    public ScanResult Scan(string body, string shell)
    {
        var hits = new List<string>();
        foreach (var (rx, desc) in Deny)
            if (rx.IsMatch(body)) hits.Add(desc);
        return new ScanResult(hits.Count == 0, hits);
    }
}
