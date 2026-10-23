using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Build;

namespace Syncro.Desktop.Services.Verification
{
    /// <inheritdoc cref="IVerificationEngine"/>
    public sealed class VerificationEngine : IVerificationEngine
    {
        private readonly IBuildEngine _build;
        private readonly ITestEngine _tests;

        public VerificationEngine(IBuildEngine build, ITestEngine tests)
        {
            _build = build;
            _tests = tests;
        }

        public Task<VerificationResult> VerifyAndApplyAsync(PatchRequest request, CancellationToken ct = default)
            => RunAsync(request, keepOnSuccess: true, ct);

        public Task<VerificationResult> DryRunAsync(PatchRequest request, CancellationToken ct = default)
            => RunAsync(request, keepOnSuccess: false, ct);

        private async Task<VerificationResult> RunAsync(PatchRequest req, bool keepOnSuccess, CancellationToken ct)
        {
            var result = new VerificationResult { FilePath = req.FilePath };

            if (string.IsNullOrWhiteSpace(req.FilePath) || !File.Exists(req.FilePath))
            {
                result.Failure = VerificationFailure.FileMissing;
                result.Message = $"File does not exist: {req.FilePath}";
                return result;
            }

            string original;
            try { original = await File.ReadAllTextAsync(req.FilePath, ct); }
            catch (Exception ex)
            {
                result.Failure = VerificationFailure.Exception;
                result.Message = $"Could not read file: {ex.Message}";
                return result;
            }

            // ── 1. Structural checks on the anchor (cheap — do these before any build) ──────
            string patched;
            if (string.IsNullOrEmpty(req.SearchTarget))
            {
                patched = req.Replacement;                    // whole-file replacement
            }
            else
            {
                var occurrences = CountOccurrences(original, req.SearchTarget);
                if (occurrences == 0)
                {
                    result.Failure = VerificationFailure.AnchorNotFound;
                    result.Message =
                        "searchTarget was not found in the file. Match must be exact, including whitespace and indentation.";
                    return result;
                }
                if (occurrences > 1)
                {
                    result.Failure = VerificationFailure.AnchorAmbiguous;
                    result.Message =
                        $"searchTarget matches {occurrences} times — it must be unique. Include surrounding lines to disambiguate.";
                    return result;
                }
                patched = original.Replace(req.SearchTarget, req.Replacement);
            }

            if (string.Equals(patched, original, StringComparison.Ordinal))
            {
                result.Failure = VerificationFailure.NoChange;
                result.Message = "The patch would not change the file.";
                return result;
            }

            // ── 2. Resolve the project + establish a pre-patch baseline ────────────────────
            var projectPath = req.ProjectPath ?? FindProjectRoot(req.FilePath);
            if (projectPath == null || _build.Detect(projectPath) == BuildStack.Unknown)
            {
                // No toolchain to verify against. The structural checks above still passed, so this
                // is a legitimate (weaker) pass rather than a rejection — but say so plainly.
                if (keepOnSuccess) await File.WriteAllTextAsync(req.FilePath, patched, ct);
                result.Ok = true;
                result.BuildRan = false;
                result.Message = "No build stack detected — applied after structural verification only.";
                return result;
            }

            var before = await _build.BuildAsync(projectPath, ct);
            var baseline = ErrorSignatures(before);
            result.ErrorsBefore = baseline.Count;

            // ── 3. Apply, rebuild, compare ────────────────────────────────────────────────
            try
            {
                await File.WriteAllTextAsync(req.FilePath, patched, ct);

                var after = await _build.BuildAsync(projectPath, ct);
                var afterSigs = ErrorSignatures(after);
                result.ErrorsAfter = afterSigs.Count;
                result.BuildRan = true;

                // Compare by signature, not count: a patch can fix one error and add another.
                // Line numbers are excluded from the signature because patching shifts them.
                var introduced = afterSigs.Where(s => !baseline.Contains(s)).ToList();

                if (introduced.Count > 0)
                {
                    await File.WriteAllTextAsync(req.FilePath, original, ct);   // roll back
                    result.Failure = VerificationFailure.BuildBroken;
                    result.Message =
                        $"The patch introduced {introduced.Count} new compiler error(s); the file was rolled back.";
                    result.IntroducedErrors.AddRange(introduced);
                    return result;
                }

                // ── 4. It compiles. Does it still behave? ─────────────────────────────────
                // The file is currently in its PATCHED state, which is what we want to test.
                var brokeTests = await RunTestStageAsync(req, projectPath, original, patched, result, ct);

                if (brokeTests)
                {
                    await File.WriteAllTextAsync(req.FilePath, original, ct);   // roll back
                    result.Failure = VerificationFailure.TestsBroken;
                    result.Message =
                        $"The patch compiles but broke {result.IntroducedTestFailures.Count} test(s); the file was rolled back.";
                    return result;
                }

                if (!keepOnSuccess)
                    await File.WriteAllTextAsync(req.FilePath, original, ct);   // dry run

                result.Ok = true;
                var verified = result.TestsRan ? "no new compiler errors, no new test failures" : "no new compiler errors";
                result.Message = keepOnSuccess
                    ? $"Verified: {verified}. Patch applied."
                    : $"Verified: {verified}. Dry run — file left unchanged.";
                return result;
            }
            catch (Exception ex)
            {
                // Any failure past the write must restore the original.
                try { await File.WriteAllTextAsync(req.FilePath, original, CancellationToken.None); } catch { }
                result.Failure = VerificationFailure.Exception;
                result.Message = $"Verification failed, file rolled back: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Runs the suite against the already-written patched file and decides whether this patch is
        /// what turned any test red.
        ///
        /// The baseline run is LAZY: if the patched suite is green there is nothing to attribute, so
        /// the common case costs exactly one test run. Only when tests fail do we restore the original
        /// and run again to separate "this patch broke it" from "it was already broken".
        ///
        /// Leaves the file in its PATCHED state. Returns true if the patch must be rolled back.
        /// </summary>
        private async Task<bool> RunTestStageAsync(
            PatchRequest req, string projectPath, string original, string patched,
            VerificationResult result, CancellationToken ct)
        {
            if (!req.RunTests)
            {
                result.TestsSkippedReason = "caller opted out";
                return false;
            }

            if (!_tests.HasTests(projectPath))
            {
                result.TestsSkippedReason = "no test suite detected";
                return false;
            }

            var after = await _tests.TestAsync(projectPath, ct);

            if (after.Skipped)
            {
                result.TestsSkippedReason = after.Error ?? "no test suite detected";
                return false;
            }

            result.TestsRan = true;
            if (after.Ok) return false;                       // green — no baseline needed

            // Red. Restore the original and re-run to find out whether these failures pre-existed.
            await File.WriteAllTextAsync(req.FilePath, original, ct);
            TestResult before;
            try { before = await _tests.TestAsync(projectPath, ct); }
            finally { await File.WriteAllTextAsync(req.FilePath, patched, ct); }

            var baseline = before.FailedTests.ToHashSet(StringComparer.Ordinal);
            var introduced = after.FailedTests.Where(t => !baseline.Contains(t)).ToList();

            if (introduced.Count > 0)
            {
                result.IntroducedTestFailures.AddRange(introduced);
                return true;
            }

            // No names parsed on either side — fall back to the runner's own verdict.
            if (after.FailedTests.Count == 0)
            {
                if (before.Ok)
                {
                    result.IntroducedTestFailures.Add(
                        $"Test suite went from passing to failing (exit code {after.ExitCode}); " +
                        "individual test names could not be parsed from the runner output.");
                    return true;
                }

                // Both runs are red and unparseable — this patch cannot be blamed, but say so
                // rather than reporting a clean pass.
                result.TestsSkippedReason =
                    "suite was already failing before the patch and test names could not be parsed — " +
                    "regression could not be ruled out";
                result.TestsRan = false;
            }

            return false;
        }

        /// <summary>
        /// Error identity WITHOUT the line number — patching shifts lines, so including them would
        /// report every surviving downstream error as "introduced".
        /// </summary>
        private static HashSet<string> ErrorSignatures(BuildResult r) =>
            r.Diagnostics
                .Where(d => d.Severity == BuildSeverity.Error)
                .Select(d => $"{NormalizePath(d.FilePath)}|{d.Code}|{d.Message}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static string NormalizePath(string? p) =>
            string.IsNullOrEmpty(p) ? "" : Path.GetFileName(p);

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return 0;
            int count = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                count++;
                i += needle.Length;
                if (count > 1) break;   // caller only needs 0 / 1 / many
            }
            return count;
        }

        /// <summary>Walk up from the file to the nearest directory carrying a build marker.</summary>
        private static string? FindProjectRoot(string filePath)
        {
            try
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
                while (dir != null)
                {
                    if (dir.EnumerateFiles("*.csproj").Any() ||
                        dir.EnumerateFiles("*.sln").Any() ||
                        dir.EnumerateFiles("pubspec.yaml").Any() ||
                        dir.EnumerateFiles("package.json").Any() ||
                        dir.EnumerateFiles("pyproject.toml").Any() ||
                        dir.EnumerateFiles("requirements.txt").Any())
                        return dir.FullName;

                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }
    }
}
