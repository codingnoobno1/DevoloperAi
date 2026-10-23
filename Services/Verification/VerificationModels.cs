using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Verification
{
    /// <summary>One file edit to be verified before it is allowed to persist.</summary>
    public sealed class PatchRequest
    {
        public string FilePath { get; set; } = "";

        /// <summary>Exact text to find. Empty means "replace the whole file with <see cref="Replacement"/>".</summary>
        public string SearchTarget { get; set; } = "";

        public string Replacement { get; set; } = "";

        /// <summary>Project root to build for verification. Defaults to the file's nearest project.</summary>
        public string? ProjectPath { get; set; }

        /// <summary>
        /// Run the test suite as part of the gate. On by default — compiling only proves a patch is
        /// well-formed, not that it still behaves. Automatically a no-op when the project has no
        /// discoverable suite, so most repos pay nothing for it.
        /// </summary>
        public bool RunTests { get; set; } = true;
    }

    /// <summary>Which gate rejected a patch (or <see cref="None"/> when it passed).</summary>
    public enum VerificationFailure
    {
        None,
        FileMissing,
        AnchorNotFound,
        AnchorAmbiguous,
        NoChange,
        BuildBroken,      // the patch introduced NEW compiler errors
        TestsBroken,      // it compiles, but the patch introduced NEW test failures
        Exception
    }

    /// <summary>
    /// The verdict on one patch. A patch is applied only when <see cref="Ok"/> — otherwise the file
    /// on disk is byte-identical to what it was before verification started.
    /// </summary>
    public sealed class VerificationResult
    {
        public bool Ok { get; set; }
        public VerificationFailure Failure { get; set; } = VerificationFailure.None;
        public string Message { get; set; } = "";
        public string FilePath { get; set; } = "";

        /// <summary>Compiler errors that exist *because of* this patch — baseline errors are excluded.</summary>
        public List<string> IntroducedErrors { get; init; } = new();

        public int ErrorsBefore { get; set; }
        public int ErrorsAfter { get; set; }

        /// <summary>True when the build ran; false when verification was structural only (see remarks).</summary>
        public bool BuildRan { get; set; }

        /// <summary>Tests the patch broke — tests already failing before it are excluded.</summary>
        public List<string> IntroducedTestFailures { get; init; } = new();

        /// <summary>True when the suite actually ran; false when skipped (no suite, or opted out).</summary>
        public bool TestsRan { get; set; }

        /// <summary>Why tests did not run, when they didn't. Surfaced so a skip is never mistaken for a pass.</summary>
        public string? TestsSkippedReason { get; set; }

        public string Summary => Ok
            ? $"Patch verified and applied to {System.IO.Path.GetFileName(FilePath)}" +
              (BuildRan ? $" (errors {ErrorsBefore}→{ErrorsAfter}" : " (structural check only — no build stack detected") +
              (BuildRan ? TestsRan ? ", tests green)." : $", tests skipped: {TestsSkippedReason})." : ").")
            : $"Patch REJECTED [{Failure}]: {Message}" +
              (IntroducedErrors.Count > 0
                  ? "\nIntroduced errors:\n  " + string.Join("\n  ", IntroducedErrors.Take(10))
                  : "") +
              (IntroducedTestFailures.Count > 0
                  ? "\nBroke tests:\n  " + string.Join("\n  ", IntroducedTestFailures.Take(10))
                  : "");
    }
}
