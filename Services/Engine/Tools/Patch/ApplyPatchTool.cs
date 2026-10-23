using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Verification;

namespace Syncro.Desktop.Services.Engine.Tools.Patch
{
    /// <summary>
    /// idearchitecture.md guardrail #5 — "nothing applies AI output unverified".
    ///
    /// This tool used to write straight to disk with <c>string.Replace</c>, which replaced EVERY
    /// occurrence of the search target and never checked that the result compiled. It now delegates
    /// to <see cref="IVerificationEngine"/>, so the guardrail holds no matter which tool name the
    /// model reaches for. The name is kept because it is what the agent has been trained to call.
    /// </summary>
    public class ApplyPatchTool : IMcpTool
    {
        private readonly IVerificationEngine _verify;

        public ApplyPatchTool(IVerificationEngine verify) => _verify = verify;

        public string Name => "ApplyPatch";

        public string Description =>
            "Modifies a file by replacing an exact search string, then rebuilds and runs the test " +
            "suite — the edit is KEPT only if it introduces no new compiler errors and breaks no " +
            "tests, otherwise the file is automatically rolled back and the failures are returned. " +
            "searchTarget must be unique in the file; include surrounding lines to disambiguate.";

        public string InputSchema =>
            "{ \"type\": \"object\", \"properties\": { " +
            "\"absolutePath\": { \"type\": \"string\" }, " +
            "\"searchTarget\": { \"type\": \"string\" }, " +
            "\"replacement\": { \"type\": \"string\" }, " +
            "\"runTests\": { \"type\": \"boolean\", \"description\": \"default true; set false to gate on compilation only\" } }, " +
            "\"required\": [\"absolutePath\", \"searchTarget\", \"replacement\"] }";

        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new
                {
                    absolutePath = "",
                    searchTarget = "",
                    replacement = "",
                    runTests = (bool?)null
                });

                if (string.IsNullOrEmpty(input?.absolutePath) || !File.Exists(input.absolutePath))
                    return JsonConvert.SerializeObject(new { error = "Target file does not exist." });

                if (string.IsNullOrEmpty(input.searchTarget))
                    return JsonConvert.SerializeObject(new { error = "Search target cannot be empty." });

                var r = await _verify.VerifyAndApplyAsync(new PatchRequest
                {
                    FilePath = input.absolutePath,
                    SearchTarget = input.searchTarget,
                    Replacement = input.replacement ?? "",
                    RunTests = input.runTests ?? true
                });

                return JsonConvert.SerializeObject(new
                {
                    success = r.Ok,
                    applied = r.Ok,
                    message = r.Message,
                    summary = r.Summary,
                    failure = r.Failure.ToString(),
                    buildRan = r.BuildRan,
                    testsRan = r.TestsRan,
                    testsSkippedReason = r.TestsSkippedReason,
                    introducedErrors = r.IntroducedErrors.Take(15),
                    brokeTests = r.IntroducedTestFailures.Take(15)
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
