using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Build;
using Syncro.Desktop.Services.Verification;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    /// <summary>
    /// Phase 4 exposed to the agent: one uniform Build tool across every stack, and the
    /// verification-gated write path that replaces raw ApplyPatch for machine-generated edits.
    /// </summary>
    public sealed class BuildMcp : IMcpServer
    {
        public string Name => "Build MCP";

        public string Description =>
            "Build/type-check any stack (dotnet, flutter, node, python) with structured diagnostics, " +
            "and apply code patches only after verifying they introduce no new compiler errors.";

        private readonly IBuildEngine _build;
        private readonly ITestEngine _tests;
        private readonly IVerificationEngine _verify;

        public BuildMcp(IBuildEngine build, ITestEngine tests, IVerificationEngine verify)
        {
            _build = build;
            _tests = tests;
            _verify = verify;
        }

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new BuildProjectTool(_build);
            yield return new RunTestsTool(_tests);
            yield return new VerifiedPatchTool(_verify);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    /// <summary>Builds or analyzes a project and returns structured diagnostics.</summary>
    public sealed class BuildProjectTool : IMcpTool
    {
        private readonly IBuildEngine _build;

        public BuildProjectTool(IBuildEngine build) => _build = build;

        public string Name => "BuildProject";

        public string Description =>
            "Builds or type-checks a project and returns structured compiler diagnostics (file, line, " +
            "code, message). Detects the stack automatically: dotnet build for .NET, flutter analyze " +
            "for Flutter, tsc --noEmit or npm run build for Node, compileall for Python. Use this to " +
            "confirm a change compiles before reporting success.";

        public string InputSchema =>
            "{ \"type\": \"object\", \"properties\": { " +
            "\"projectPath\": { \"type\": \"string\", \"description\": \"absolute path to the project root\" } }, " +
            "\"required\": [\"projectPath\"] }";

        public bool RequiresAdminApproval => false;

        private sealed class ToolInput { public string ProjectPath { get; set; } = ""; }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ToolInput>(inputJson);
                if (string.IsNullOrWhiteSpace(input?.ProjectPath))
                    return JsonConvert.SerializeObject(new { error = "projectPath is required." });

                var r = await _build.BuildAsync(input.ProjectPath);

                return JsonConvert.SerializeObject(new
                {
                    ok = r.Ok,
                    stack = r.Stack.ToString(),
                    summary = r.Summary,
                    command = r.Command,
                    durationMs = r.DurationMs,
                    error = r.Error,
                    errorCount = r.ErrorCount,
                    warningCount = r.WarningCount,
                    // Cap so a catastrophically broken build can't blow the model's context window.
                    diagnostics = r.Diagnostics.Take(50).Select(d => new
                    {
                        severity = d.Severity.ToString().ToLowerInvariant(),
                        message = d.Message,
                        file = d.FilePath,
                        line = d.Line,
                        column = d.Column,
                        code = d.Code
                    })
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }

    /// <summary>Runs the suite on its own, without patching anything.</summary>
    public sealed class RunTestsTool : IMcpTool
    {
        private readonly ITestEngine _tests;

        public RunTestsTool(ITestEngine tests) => _tests = tests;

        public string Name => "RunTests";

        public string Description =>
            "Runs a project's test suite (dotnet test, flutter test, npm test, or pytest — detected " +
            "automatically) and returns which tests failed. Returns skipped=true when the project has " +
            "no discoverable suite. Use this to check the current state of the suite; use VerifiedPatch " +
            "when you want an edit gated on it.";

        public string InputSchema =>
            "{ \"type\": \"object\", \"properties\": { " +
            "\"projectPath\": { \"type\": \"string\", \"description\": \"absolute path to the project root\" } }, " +
            "\"required\": [\"projectPath\"] }";

        public bool RequiresAdminApproval => false;

        private sealed class ToolInput { public string ProjectPath { get; set; } = ""; }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ToolInput>(inputJson);
                if (string.IsNullOrWhiteSpace(input?.ProjectPath))
                    return JsonConvert.SerializeObject(new { error = "projectPath is required." });

                var r = await _tests.TestAsync(input.ProjectPath);

                return JsonConvert.SerializeObject(new
                {
                    ok = r.Ok,
                    skipped = r.Skipped,
                    stack = r.Stack.ToString(),
                    summary = r.Summary,
                    command = r.Command,
                    durationMs = r.DurationMs,
                    error = r.Error,
                    failedCount = r.FailedCount,
                    failedTests = r.FailedTests.Take(50)
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }

    /// <summary>The gated write path — apply a patch only if it does not break the build.</summary>
    public sealed class VerifiedPatchTool : IMcpTool
    {
        private readonly IVerificationEngine _verify;

        public VerifiedPatchTool(IVerificationEngine verify) => _verify = verify;

        public string Name => "VerifiedPatch";

        public string Description =>
            "Replaces searchTarget with replacement in a file, then rebuilds the project AND runs the " +
            "test suite, keeping the change only if it introduces no new compiler errors and breaks no " +
            "tests — otherwise the file is automatically rolled back and the failures are returned so " +
            "you can correct the patch. searchTarget must match exactly and appear exactly once. " +
            "Set dryRun=true to test a patch without keeping it, or runTests=false to gate on " +
            "compilation only when the suite is slow.";

        public string InputSchema =>
            "{ \"type\": \"object\", \"properties\": { " +
            "\"filePath\": { \"type\": \"string\" }, " +
            "\"searchTarget\": { \"type\": \"string\", \"description\": \"exact text to replace; empty means replace whole file\" }, " +
            "\"replacement\": { \"type\": \"string\" }, " +
            "\"projectPath\": { \"type\": \"string\", \"description\": \"optional; inferred from filePath if omitted\" }, " +
            "\"dryRun\": { \"type\": \"boolean\" }, " +
            "\"runTests\": { \"type\": \"boolean\", \"description\": \"default true; false gates on compilation only\" } }, " +
            "\"required\": [\"filePath\", \"replacement\"] }";

        // Writes to disk, but only after proving it compiles — the gate IS the safety mechanism.
        public bool RequiresAdminApproval => false;

        private sealed class ToolInput
        {
            public string FilePath { get; set; } = "";
            public string SearchTarget { get; set; } = "";
            public string Replacement { get; set; } = "";
            public string? ProjectPath { get; set; }
            public bool DryRun { get; set; }
            public bool? RunTests { get; set; }
        }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ToolInput>(inputJson);
                if (string.IsNullOrWhiteSpace(input?.FilePath))
                    return JsonConvert.SerializeObject(new { error = "filePath is required." });

                var req = new PatchRequest
                {
                    FilePath = input.FilePath,
                    SearchTarget = input.SearchTarget ?? "",
                    Replacement = input.Replacement ?? "",
                    ProjectPath = input.ProjectPath,
                    RunTests = input.RunTests ?? true
                };

                var r = input.DryRun
                    ? await _verify.DryRunAsync(req)
                    : await _verify.VerifyAndApplyAsync(req);

                return JsonConvert.SerializeObject(new
                {
                    ok = r.Ok,
                    applied = r.Ok && !input.DryRun,
                    failure = r.Failure.ToString(),
                    message = r.Message,
                    summary = r.Summary,
                    buildRan = r.BuildRan,
                    errorsBefore = r.ErrorsBefore,
                    errorsAfter = r.ErrorsAfter,
                    introducedErrors = r.IntroducedErrors.Take(15),
                    testsRan = r.TestsRan,
                    testsSkippedReason = r.TestsSkippedReason,
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
