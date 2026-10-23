using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Syncro.Desktop.Services.Agent.Policy
{
    public enum PermissionVerdict { Allowed, NeedsApproval, Blocked }

    public sealed record PermissionResult(PermissionVerdict Verdict, string Why)
    {
        public static readonly PermissionResult Ok = new(PermissionVerdict.Allowed, "");
    }

    /// <summary>
    /// Shell / file-path / network checks from the Permissions tab (agentsettings.md D3–D5).
    /// Deny always wins. Anything not explicitly allowed needs approval — it is never silently run.
    /// </summary>
    public static class PermissionChecker
    {
        private static readonly string[] ShellBuiltins = { "cd", "cd ", "pushd", "popd", "@echo off", "echo off", "setlocal", "endlocal", "exit /b" };

        // ── shell ──
        public static PermissionResult CheckShell(string script, PermissionsSettings p)
        {
            var needs = new List<string>();
            foreach (var raw in script.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("::") || line.StartsWith("#")
                    || line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || line.Equals("rem", StringComparison.OrdinalIgnoreCase))
                    continue;

                var deny = p.ShellDeny.FirstOrDefault(d => Matches(line, d, prefixOnly: false));
                if (deny != null) return new(PermissionVerdict.Blocked, $"shell line matches deny rule '{deny}': {Trim(line)}");

                // Every chained segment must be allowed on its own.
                foreach (var seg in Regex.Split(line, @"\s*(?:&&|\|\||&|;|\|)\s*").Select(s => s.Trim()).Where(s => s.Length > 0))
                {
                    if (ShellBuiltins.Any(b => seg.Equals(b.Trim(), StringComparison.OrdinalIgnoreCase) || seg.StartsWith(b, StringComparison.OrdinalIgnoreCase))) continue;
                    if (!p.ShellAllow.Any(a => Matches(seg, a, prefixOnly: true))) needs.Add(Trim(seg));
                }
            }
            return needs.Count == 0
                ? PermissionResult.Ok
                : new(PermissionVerdict.NeedsApproval, $"not on shell allowlist: {string.Join(" · ", needs.Distinct().Take(3))}{(needs.Count > 3 ? " …" : "")}");
        }

        // ── file paths ──
        public static PermissionResult CheckPaths(IEnumerable<string> paths, PermissionsSettings p)
        {
            var list = paths.ToList();
            foreach (var path in list)
            {
                var deny = p.PathDeny.FirstOrDefault(g => GlobMatch(path, g));
                if (deny != null) return new(PermissionVerdict.Blocked, $"path matches deny rule '{deny}': {path}");
            }
            if (p.PathAllow.Count > 0)
            {
                var outside = list.Where(path => !p.PathAllow.Any(g => GlobMatch(path, g))).ToList();
                if (outside.Count > 0) return new(PermissionVerdict.NeedsApproval, $"outside allowed paths: {outside[0]}");
            }
            return PermissionResult.Ok;
        }

        // ── network ──
        public static PermissionResult CheckUrls(IEnumerable<string> urls, PermissionsSettings p)
        {
            foreach (var u in urls)
            {
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri))
                {
                    // scp-style git remotes: git@github.com:owner/repo.git
                    var m = Regex.Match(u, @"^[\w.-]+@([\w.-]+):");
                    if (!m.Success) return new(PermissionVerdict.NeedsApproval, $"unrecognised address: {u}");
                    if (!HostAllowed(m.Groups[1].Value, p)) return new(PermissionVerdict.NeedsApproval, $"host not on network allowlist: {m.Groups[1].Value}");
                    continue;
                }
                if (!HostAllowed(uri.Host, p)) return new(PermissionVerdict.NeedsApproval, $"host not on network allowlist: {uri.Host}");
            }
            return PermissionResult.Ok;
        }

        private static bool HostAllowed(string host, PermissionsSettings p) =>
            p.NetworkAllow.Any(a => host.Equals(a, StringComparison.OrdinalIgnoreCase)
                                    || host.EndsWith("." + a, StringComparison.OrdinalIgnoreCase));

        // ── matching helpers ──

        /// <summary>Rule is a prefix (or substring for deny), or a /regex/.</summary>
        public static bool Matches(string text, string rule, bool prefixOnly)
        {
            if (string.IsNullOrWhiteSpace(rule)) return false;
            rule = rule.Trim();
            if (rule.Length > 2 && rule.StartsWith('/') && rule.EndsWith('/'))
            {
                try { return Regex.IsMatch(text, rule[1..^1], RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
                catch { return false; }
            }
            var t = Regex.Replace(text, @"\s+", " ");
            return prefixOnly
                ? t.StartsWith(rule, StringComparison.OrdinalIgnoreCase)
                : t.Contains(rule, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Glob with ** (any dirs), * (within a segment), ?. Case-insensitive, / and \ equal.</summary>
        public static bool GlobMatch(string text, string glob)
        {
            if (string.IsNullOrWhiteSpace(glob)) return false;
            var t = text.Replace('\\', '/');
            var g = glob.Trim().Replace('\\', '/');
            var rx = "^" + Regex.Escape(g)
                .Replace(@"\*\*/", "(?:.*/)?")
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", "[^/]*")
                .Replace(@"\?", ".") + "$";
            try
            {
                if (Regex.IsMatch(t, rx, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100))) return true;
                // Relative globs ("src/**") also match anywhere below a root.
                if (!g.StartsWith("**") && !g.Contains(':') && !g.StartsWith('/'))
                    return Regex.IsMatch(t, "(?:^|/)" + rx[1..], RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch { }
            return false;
        }

        private static string Trim(string s) => s.Length > 80 ? s[..80] + "…" : s;
    }
}
