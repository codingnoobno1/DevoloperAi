using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Build;
using Syncro.Desktop.Services.Connector.Scan;
using Syncro.Desktop.Services.Git;
using Syncro.Desktop.Services.Security;
using Syncro.Desktop.Services.Verification;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// State behind the Incident AutoSRE panel in GitManager. Everything starts empty / "Not run":
    /// there is no incident source (alerts, telemetry) connected yet, so nothing here is invented.
    /// </summary>
    public sealed class IncidentContextModel
    {
        /// <summary>Empty until an incident source exists (SRE-1/SRE-3 in agentsettings.md).</summary>
        public string IncidentId { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Title { get; set; } = "No active incident";
        public string ServiceName { get; set; } = "";
        public string Elapsed { get; set; } = "";
        public string State { get; set; } = "IDLE";
        /// <summary>0 = no estimate. Nothing computes a confidence yet.</summary>
        public int ConfidencePercent { get; set; }
        public bool HasIncident => !string.IsNullOrEmpty(IncidentId);

        // Latest commit — NOT a correlated suspect: no telemetry is connected to correlate with.
        public string SuspectSha { get; set; } = "";
        public string SuspectAge { get; set; } = "";
        public string SuspectAuthor { get; set; } = "";
        public string SuspectSubject { get; set; } = "";
        public string CorrelationNote { get; set; } = "No incident source is connected, so this is the latest commit, not a correlated suspect.";

        public List<string> Evidence { get; set; } = new();

        // No patch generator exists yet.
        public string ProposedFixSummary { get; set; } = "";
        public string TargetFile { get; set; } = "";

        // Verification gates: "Not run" until they actually run.
        public bool BuildRan { get; set; }
        public string BuildStatus { get; set; } = "Not run";
        public string BuildSub { get; set; } = "";
        public bool BuildOk { get; set; }

        public bool TestsRan { get; set; }
        public string TestsStatus { get; set; } = "Not run";
        public string TestsSub { get; set; } = "";
        public bool TestsOk { get; set; }

        public bool SecurityRan { get; set; }
        public string SecurityStatus { get; set; } = "Not run";
        public string SecuritySub { get; set; } = "";
        public bool SecurityOk { get; set; }

        public bool RiskComputed { get; set; }
        public string RiskStatus { get; set; } = "Not computed";
        public string RiskSub { get; set; } = "";

        /// <summary>1=Detect, 2=Correlate, 3=Patch, 4=Verify, 5=PR. Steps before this one ran.</summary>
        public int ActiveStep { get; set; } = 1;
        public string SandboxLastResult { get; set; } = "";
        public bool SandboxVerified { get; set; }

        public int GatesRun => (BuildRan ? 1 : 0) + (TestsRan ? 1 : 0) + (SecurityRan ? 1 : 0);
        public int GatesPassed => (BuildRan && BuildOk ? 1 : 0) + (TestsRan && TestsOk ? 1 : 0) + (SecurityRan && SecurityOk ? 1 : 0);
    }

    /// <summary>
    /// Runs real verification gates (SAST, build, tests) on a repository and summarises its git
    /// state for the GitManager incident panel. It does not detect incidents, correlate telemetry
    /// or generate fixes — those need an incident source and a patch generator that don't exist yet.
    /// </summary>
    public sealed class IncidentAutoSreService
    {
        private readonly IGitEngine _git;
        private readonly IBuildEngine _build;
        private readonly ITestEngine _tests;
        private readonly IVerificationEngine _verify;
        private readonly SecurityScanner _security;
        private readonly MultiProjectScanService _contracts;

        public IncidentContextModel Current { get; private set; } = new();

        public IncidentAutoSreService(
            IGitEngine git,
            IBuildEngine build,
            ITestEngine tests,
            IVerificationEngine verify,
            SecurityScanner security,
            MultiProjectScanService contracts)
        {
            _git = git;
            _build = build;
            _tests = tests;
            _verify = verify;
            _security = security;
            _contracts = contracts;
        }

        /// <summary>Fresh state for a repo (gates reset to Not run) plus its real git facts.</summary>
        public async Task<IncidentContextModel> CorrelateRepoAsync(string repoPath, IReadOnlyList<GitCommit>? recentCommits = null)
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return Current;

            var keep = Current;
            Current = new IncidentContextModel
            {
                // keep incident fields if a future incident source set them
                IncidentId = keep.IncidentId, Severity = keep.Severity, Title = keep.Title, Elapsed = keep.Elapsed,
                ServiceName = Path.GetFileName(repoPath.TrimEnd('\\', '/')),
            };

            var commits = recentCommits ?? await _git.LogAsync(repoPath, 10);
            var latest = commits.FirstOrDefault();
            if (latest != null)
            {
                Current.SuspectSha = latest.ShortSha;
                Current.SuspectAuthor = latest.Author;
                Current.SuspectSubject = latest.Subject;
                if (DateTimeOffset.TryParse(latest.Date, out var when)) Current.SuspectAge = Ago(DateTimeOffset.Now - when);
                Current.Evidence.Add($"Latest commit {latest.ShortSha} by {latest.Author}: {latest.Subject}");
            }
            Current.Evidence.Add("No runtime telemetry or alerts are connected — evidence is from git only.");

            var status = await _git.StatusAsync(repoPath);
            Current.ActiveStep = 2;
            if (status.Files.Count > 0)
            {
                Current.TargetFile = status.Files[0].Path;
                int totalLoc = status.Files.Sum(f => f.Additions + f.Deletions);
                double riskVal = Math.Clamp(Math.Round((status.Files.Count * 0.06) + (totalLoc * 0.008), 2), 0.0, 1.0);
                string tier = riskVal <= 0.25 ? "Low" : riskVal <= 0.55 ? "Med" : "High";
                Current.RiskComputed = true;
                Current.RiskStatus = $"{tier} · {riskVal:0.00}";
                Current.RiskSub = $"{status.Files.Count} changed file{(status.Files.Count == 1 ? "" : "s")} · {totalLoc} LOC";
                Current.Evidence.Add($"{status.Files.Count} uncommitted file(s), {totalLoc} changed line(s).");
            }
            else
            {
                Current.RiskSub = "working tree is clean";
            }

            return Current;
        }

        /// <summary>Runs SAST, build and tests for real. A gate that can't run says so instead of passing.</summary>
        public async Task<IncidentContextModel> RunFullVerificationGatesAsync(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return Current;

            Current.State = "VERIFYING";
            Current.ActiveStep = 4;

            var sast = await _security.ScanAsync(repoPath);
            Current.SecurityRan = true;
            Current.SecurityOk = sast.GatePassed;
            Current.SecurityStatus = sast.GatePassed ? "Passed" : "Failed";
            Current.SecuritySub = $"{sast.Findings.Count} finding{(sast.Findings.Count == 1 ? "" : "s")} · {sast.ElapsedMs}ms";

            var buildRes = await _build.BuildAsync(repoPath);
            if (buildRes.Stack != BuildStack.Unknown)
            {
                Current.BuildRan = true;
                Current.BuildOk = buildRes.Ok;
                Current.BuildStatus = buildRes.Ok ? "Passing" : "Failed";
                Current.BuildSub = $"{buildRes.ErrorCount} errors · {(buildRes.DurationMs / 1000.0):0.1}s";
            }
            else
            {
                Current.BuildRan = false;
                Current.BuildStatus = "Not run";
                Current.BuildSub = "no build system detected";
            }

            var testRes = await _tests.TestAsync(repoPath);
            if (!testRes.Skipped && testRes.TotalCount > 0)
            {
                Current.TestsRan = true;
                Current.TestsOk = testRes.Ok;
                Current.TestsStatus = $"{testRes.PassedCount}/{testRes.TotalCount}";
                Current.TestsSub = $"{(testRes.Ok ? "passed" : "failed")} · {(testRes.DurationMs / 1000.0):0.1}s";
            }
            else
            {
                Current.TestsRan = false;
                Current.TestsStatus = "Not run";
                Current.TestsSub = "no tests found";
            }

            Current.SandboxVerified = Current.BuildRan && Current.BuildOk && Current.SecurityOk && (!Current.TestsRan || Current.TestsOk);
            Current.State = "VERIFIED";
            return Current;
        }

        /// <summary>
        /// There is no patch generator yet, so there is no proposed fix to apply. This says so
        /// plainly; the caller can still run the real gates on the current working tree.
        /// </summary>
        public Task<(bool Ok, string Message)> ApplyProposedFixInSandboxAsync(string repoPath, string? selectedFilePath)
        {
            if (string.IsNullOrWhiteSpace(Current.ProposedFixSummary))
            {
                Current.SandboxVerified = false;
                Current.SandboxLastResult = "No proposed fix: Syncro can't generate incident patches yet. Gates were run on the current working tree instead.";
                return Task.FromResult((false, Current.SandboxLastResult));
            }

            // A future patch generator will fill ProposedFixSummary and apply through IVerificationEngine.
            Current.SandboxLastResult = "Applying generated fixes is not implemented yet.";
            return Task.FromResult((false, Current.SandboxLastResult));
        }

        private static string Ago(TimeSpan t) =>
            t.TotalMinutes < 1 ? "just now" :
            t.TotalHours < 1 ? $"{(int)t.TotalMinutes}m ago" :
            t.TotalDays < 1 ? $"{(int)t.TotalHours}h ago" :
            $"{(int)t.TotalDays}d ago";
    }
}
