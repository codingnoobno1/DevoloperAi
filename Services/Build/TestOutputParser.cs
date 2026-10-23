using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>
    /// Extracts failing test NAMES from raw runner output. Names (not counts) are what the
    /// verification gate compares, so that a patch which fixes one failure and causes another is
    /// still correctly rejected.
    ///
    /// Every runner is best-effort: when no name can be parsed the caller falls back to the exit
    /// code, so a format we do not recognize degrades to "the suite went red" rather than to
    /// "nothing broke".
    /// </summary>
    public static class TestOutputParser
    {
        /// <summary>Cap so a catastrophically red suite cannot blow out the model's context.</summary>
        private const int MaxNames = 100;

        // dotnet test / VSTest:  "  Failed CartTests.Add [12 ms]"   xunit console: "    X CartTests.Add [12 ms]"
        // The trailing "[12 ms]" is optional. Two deliberate guards:
        //  - '\s+' after the keyword means the summary line "Failed!  - Failed: 2, …" cannot match.
        //  - a REQUIRED leading indent (^\s+) means unindented prose such as "Failed to connect to
        //    server" cannot match; runners always indent individual failures under the summary.
        private static readonly Regex DotNetFailed = new(
            @"^\s+(?:Failed|X)\s+(?<name>[^\s\[][^\[]*?)\s*(?:\[[\d.,]+\s*\w*\])?\s*$",
            RegexOptions.Compiled);

        // flutter test:  "00:02 +5 -1: CartBloc emits loaded [E]"
        private static readonly Regex FlutterFailed = new(
            @"^\s*\d+:\d+\s+\+\d+(?:\s+-\d+)?\s*:\s*(?<name>.+?)\s*\[E\]\s*$",
            RegexOptions.Compiled);

        // jest/vitest:  "  ✕ renders cart items (5 ms)"  and  "  ● Cart › renders cart items"
        private static readonly Regex JestFailed = new(
            @"^\s*[✕✗×●]\s+(?<name>.+?)(?:\s+\(\d+\s*m?s\))?\s*$",
            RegexOptions.Compiled);

        // pytest:  "FAILED tests/test_cart.py::test_add - assert 1 == 2"
        private static readonly Regex PytestFailedPrefix = new(
            @"^\s*FAILED\s+(?<name>\S+)", RegexOptions.Compiled);

        // pytest verbose:  "tests/test_cart.py::test_add FAILED"
        private static readonly Regex PytestFailedSuffix = new(
            @"^\s*(?<name>\S+::\S+)\s+FAILED", RegexOptions.Compiled);

        public static List<string> ParseFailures(string? rawOutput, BuildStack stack)
        {
            var names = new List<string>();
            if (string.IsNullOrWhiteSpace(rawOutput)) return names;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rawLine in rawOutput.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (line.Length == 0) continue;

                var name = MatchLine(line, stack);
                if (string.IsNullOrWhiteSpace(name)) continue;

                name = name.Trim();
                // Runner chatter that matches the shape but names no test.
                if (name.Length < 2 || name.StartsWith("Test suite failed", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (seen.Add(name)) names.Add(name);
                if (names.Count >= MaxNames) break;
            }

            return names;
        }

        public static (int Passed, int Total) ParseCounts(string? rawOutput, BuildStack stack)
        {
            if (string.IsNullOrWhiteSpace(rawOutput)) return (0, 0);

            // dotnet test summary: "Passed!  - Failed:     0, Passed:   148, Skipped:     0, Total:   148"
            var dn = Regex.Match(rawOutput, @"Passed:\s*(?<passed>\d+).*?Total:\s*(?<total>\d+)", RegexOptions.IgnoreCase);
            if (dn.Success && int.TryParse(dn.Groups["passed"].Value, out int p1) && int.TryParse(dn.Groups["total"].Value, out int t1))
                return (p1, t1);

            // jest / vitest: "Tests: 148 passed, 148 total" or "Tests: 2 failed, 146 passed, 148 total"
            var jest = Regex.Match(rawOutput, @"Tests:.*?(?<passed>\d+)\s+passed.*?(?<total>\d+)\s+total", RegexOptions.IgnoreCase);
            if (jest.Success && int.TryParse(jest.Groups["passed"].Value, out int p2) && int.TryParse(jest.Groups["total"].Value, out int t2))
                return (p2, t2);

            // pytest: "148 passed in 1.23s" or "2 failed, 146 passed in 1.23s"
            var pyPass = Regex.Match(rawOutput, @"(?<passed>\d+)\s+passed", RegexOptions.IgnoreCase);
            var pyFail = Regex.Match(rawOutput, @"(?<failed>\d+)\s+failed", RegexOptions.IgnoreCase);
            if (pyPass.Success && int.TryParse(pyPass.Groups["passed"].Value, out int p3))
            {
                int f3 = pyFail.Success && int.TryParse(pyFail.Groups["failed"].Value, out int ff) ? ff : 0;
                return (p3, p3 + f3);
            }

            // flutter test: "+148: All tests passed!" or "+146 -2: Some tests failed."
            var flMatches = Regex.Matches(rawOutput, @"\+(?<passed>\d+)(?:\s+-(?<failed>\d+))?:");
            if (flMatches.Count > 0)
            {
                var last = flMatches[^1];
                int.TryParse(last.Groups["passed"].Value, out int p4);
                int.TryParse(last.Groups["failed"].Value, out int f4);
                return (p4, p4 + f4);
            }

            return (0, 0);
        }

        private static string? MatchLine(string line, BuildStack stack)
        {
            switch (stack)
            {
                case BuildStack.Flutter:
                {
                    var m = FlutterFailed.Match(line);
                    return m.Success ? m.Groups["name"].Value : null;
                }
                case BuildStack.Node:
                {
                    var m = JestFailed.Match(line);
                    return m.Success ? m.Groups["name"].Value : null;
                }
                case BuildStack.Python:
                {
                    var m = PytestFailedPrefix.Match(line);
                    if (m.Success) return m.Groups["name"].Value;
                    var m2 = PytestFailedSuffix.Match(line);
                    return m2.Success ? m2.Groups["name"].Value : null;
                }
                case BuildStack.DotNet:
                {
                    var m = DotNetFailed.Match(line);
                    return m.Success ? m.Groups["name"].Value : null;
                }
                default:
                    return null;
            }
        }
    }
}
