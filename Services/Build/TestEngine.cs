using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Build
{
    /// <inheritdoc cref="ITestEngine"/>
    public sealed class TestEngine : ITestEngine
    {
        /// <summary>Tests get a longer leash than a build, but still a hard ceiling.</summary>
        private const int TimeoutMs = 10 * 60 * 1000;

        private readonly IBuildEngine _build;

        public TestEngine(IBuildEngine build) => _build = build;

        // Directories that never contain a project's own tests but are expensive to walk.
        private static readonly string[] SkipDirs =
            { "node_modules", "bin", "obj", ".git", ".dart_tool", "build", "dist", ".venv", "venv", "__pycache__" };

        public bool HasTests(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return false;

            try
            {
                return _build.Detect(projectPath) switch
                {
                    BuildStack.DotNet => HasDotNetTests(projectPath),
                    BuildStack.Flutter => HasMatching(Path.Combine(projectPath, "test"), "*_test.dart"),
                    BuildStack.Node => HasNodeTestScript(projectPath),
                    BuildStack.Python => HasPythonTests(projectPath),
                    _ => false
                };
            }
            catch { return false; }
        }

        public async Task<TestResult> TestAsync(string projectPath, CancellationToken ct = default)
        {
            var stack = _build.Detect(projectPath);
            var result = new TestResult { ProjectPath = projectPath, Stack = stack };

            if (stack == BuildStack.Unknown || !HasTests(projectPath))
            {
                result.Skipped = true;
                result.Ok = true;      // "no suite" is a pass, not a failure — it blocks nothing.
                return result;
            }

            var (exe, args) = CommandFor(stack);
            result.Command = $"{exe} {args}";

            var sw = Stopwatch.StartNew();
            try
            {
                var (exitCode, output) = await ProcessRunner.RunAsync(exe, args, projectPath, ct, TimeoutMs);
                result.ExitCode = exitCode;
                result.RawOutput = output;
                result.Ok = exitCode == 0;
                result.FailedTests.AddRange(TestOutputParser.ParseFailures(output, stack));
                var (passed, total) = TestOutputParser.ParseCounts(output, stack);
                result.PassedCount = passed;
                result.TotalCount = total > 0 ? total : (passed + result.FailedCount);

                // pytest exits 5 for "no tests collected" — that is an empty suite, not a red one.
                if (stack == BuildStack.Python && exitCode == 5)
                {
                    result.Skipped = true;
                    result.Ok = true;
                    result.FailedTests.Clear();
                }

                // A missing runner is an infrastructure problem, not a test failure — saying
                // otherwise would make the gate reject patches for the wrong reason.
                if (!result.Ok && LooksLikeMissingRunner(output))
                {
                    result.Error = "Test runner is not installed for this project.";
                    result.Skipped = true;
                    result.Ok = true;
                    result.FailedTests.Clear();
                }
            }
            catch (OperationCanceledException)
            {
                result.Error = "Test run cancelled or timed out.";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;      // runner not on PATH
                result.Skipped = true;
                result.Ok = true;
            }
            finally
            {
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            return result;
        }

        /// <summary>
        /// The runner per stack. <c>dotnet test</c> is deliberately allowed to rebuild rather than
        /// passing <c>--no-build</c>: the speed win is not worth silently testing stale assemblies
        /// when the preceding build targeted a different project or configuration.
        /// </summary>
        private static (string Exe, string Args) CommandFor(BuildStack stack) => stack switch
        {
            BuildStack.DotNet => ("dotnet", "test --nologo -v q"),
            // 'expanded' reporter prints one line per test; 'compact' rewrites a single line with \r
            // and would lose every failure name when the output is split on newlines.
            BuildStack.Flutter => ("flutter", "test -r expanded"),
            BuildStack.Node => ("npm", "test"),
            BuildStack.Python => ("python", "-m pytest -q"),
            _ => ("", "")
        };

        private static bool LooksLikeMissingRunner(string output) =>
            output.Contains("No module named pytest", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("is not recognized as an internal", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("command not found", StringComparison.OrdinalIgnoreCase);

        private static bool HasDotNetTests(string projectPath)
        {
            foreach (var csproj in EnumerateFilesBounded(projectPath, "*.csproj", maxDepth: 4))
            {
                var name = Path.GetFileNameWithoutExtension(csproj);
                if (name.Contains("Test", StringComparison.OrdinalIgnoreCase)) return true;

                try
                {
                    var content = File.ReadAllText(csproj);
                    if (content.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("NUnit", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("MSTest", StringComparison.OrdinalIgnoreCase) ||
                        content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static bool HasNodeTestScript(string projectPath)
        {
            var pkg = Path.Combine(projectPath, "package.json");
            if (!File.Exists(pkg)) return false;

            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(pkg));
                var test = json["scripts"]?["test"]?.ToString();
                if (string.IsNullOrWhiteSpace(test)) return false;

                // `npm init` writes a placeholder that exits 1 — running it would fail every patch.
                return !test.Contains("no test specified", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool HasPythonTests(string projectPath) =>
            HasMatching(Path.Combine(projectPath, "tests"), "test_*.py") ||
            HasMatching(projectPath, "test_*.py") ||
            HasMatching(projectPath, "*_test.py");

        private static bool HasMatching(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) return false;
            try { return Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories).Any(); }
            catch { return false; }
        }

        /// <summary>
        /// Depth-limited walk that skips vendor directories — a naive AllDirectories enumeration
        /// walks node_modules and can take seconds on a large repo.
        /// </summary>
        private static IEnumerable<string> EnumerateFilesBounded(string root, string pattern, int maxDepth)
        {
            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();

                string[] files;
                try { files = Directory.GetFiles(dir, pattern); }
                catch { continue; }
                foreach (var f in files) yield return f;

                if (depth >= maxDepth) continue;

                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch { continue; }

                foreach (var sub in subs)
                {
                    var name = Path.GetFileName(sub);
                    if (name.StartsWith('.') || SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;
                    queue.Enqueue((sub, depth + 1));
                }
            }
        }
    }
}
