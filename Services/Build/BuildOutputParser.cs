using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>
    /// Turns raw toolchain output into <see cref="BuildDiagnostic"/>s. Pure and static so it can be
    /// unit-verified against captured real output without running a build.
    /// </summary>
    public static class BuildOutputParser
    {
        // MSBuild / tsc share a shape:
        //   D:\p\File.cs(30,40): error CS0118: 'X' is a namespace [D:\p\proj.csproj]
        //   src/foo.ts(12,5): error TS2304: Cannot find name 'x'.
        private static readonly Regex MsBuildLike = new(
            @"^\s*(?<file>[^(]+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?<sev>error|warning)\s+(?<code>[A-Za-z]+[0-9]+)\s*:\s*(?<msg>.+?)\s*(?:\[[^\]]*\])?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // dart analyze:
        //     error • Undefined name 'x' • lib/main.dart:10:5 • undefined_identifier
        private static readonly Regex DartAnalyze = new(
            @"^\s*(?<sev>error|warning|info)\s+•\s+(?<msg>.+?)\s+•\s+(?<file>[^•]+?):(?<line>\d+):(?<col>\d+)\s+•\s+(?<code>\S+)\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // python / eslint-compact / flake8:
        //   foo.py:12:5: E999 SyntaxError: invalid syntax
        private static readonly Regex ColonTriple = new(
            @"^\s*(?<file>[A-Za-z]:[^:]+|[^:\s][^:]*?):(?<line>\d+):(?<col>\d+):\s*(?<code>[A-Za-z]\d+)?\s*(?<msg>.+?)\s*$",
            RegexOptions.Compiled);

        // python traceback form:
        //   File "foo.py", line 12
        private static readonly Regex PyFile = new(
            @"^\s*File\s+""(?<file>[^""]+)"",\s+line\s+(?<line>\d+)",
            RegexOptions.Compiled);

        public static List<BuildDiagnostic> Parse(string? rawOutput, BuildStack stack)
        {
            var results = new List<BuildDiagnostic>();
            if (string.IsNullOrWhiteSpace(rawOutput)) return results;

            // De-dup: MSBuild repeats each diagnostic once per target framework / project pass.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in rawOutput.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0) continue;

                var d = ParseLine(line, stack);
                if (d == null) continue;

                var key = $"{d.FilePath}|{d.Line}|{d.Column}|{d.Code}|{d.Message}";
                if (seen.Add(key)) results.Add(d);
            }

            return results;
        }

        private static BuildDiagnostic? ParseLine(string line, BuildStack stack)
        {
            // Dart's bullet format is unambiguous — try it first for Flutter.
            if (stack == BuildStack.Flutter)
            {
                var dm = DartAnalyze.Match(line);
                if (dm.Success)
                    return new BuildDiagnostic(
                        MapSeverity(dm.Groups["sev"].Value),
                        dm.Groups["msg"].Value.Trim(),
                        dm.Groups["file"].Value.Trim(),
                        ToInt(dm.Groups["line"].Value),
                        ToInt(dm.Groups["col"].Value),
                        dm.Groups["code"].Value);
            }

            var m = MsBuildLike.Match(line);
            if (m.Success)
                return new BuildDiagnostic(
                    MapSeverity(m.Groups["sev"].Value),
                    m.Groups["msg"].Value.Trim(),
                    m.Groups["file"].Value.Trim(),
                    ToInt(m.Groups["line"].Value),
                    ToInt(m.Groups["col"].Value),
                    m.Groups["code"].Value);

            if (stack is BuildStack.Python or BuildStack.Node)
            {
                var cm = ColonTriple.Match(line);
                if (cm.Success)
                {
                    var msg = cm.Groups["msg"].Value.Trim();
                    // Guard against matching ordinary "note: 12:34" prose.
                    if (msg.Length > 0)
                        return new BuildDiagnostic(
                            InferSeverity(msg),
                            msg,
                            cm.Groups["file"].Value.Trim(),
                            ToInt(cm.Groups["line"].Value),
                            ToInt(cm.Groups["col"].Value),
                            cm.Groups["code"].Success ? cm.Groups["code"].Value : null);
                }

                var pm = PyFile.Match(line);
                if (pm.Success)
                    return new BuildDiagnostic(
                        BuildSeverity.Error,
                        "Syntax error",
                        pm.Groups["file"].Value.Trim(),
                        ToInt(pm.Groups["line"].Value),
                        0,
                        "SyntaxError");
            }

            return null;
        }

        private static BuildSeverity MapSeverity(string s) => s.ToLowerInvariant() switch
        {
            "error" => BuildSeverity.Error,
            "warning" or "warn" => BuildSeverity.Warning,
            _ => BuildSeverity.Info
        };

        /// <summary>For tools that don't label severity, infer from the message text.</summary>
        private static BuildSeverity InferSeverity(string msg) =>
            msg.Contains("error", StringComparison.OrdinalIgnoreCase) ? BuildSeverity.Error
            : msg.Contains("warn", StringComparison.OrdinalIgnoreCase) ? BuildSeverity.Warning
            : BuildSeverity.Info;

        private static int ToInt(string? s) => int.TryParse(s, out var v) ? v : 0;
    }
}
