using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Agent.Policy
{
    public sealed record DecisionFactor(string Factor, int Points, string Why);

    public sealed class DecisionResult
    {
        public Decision Decision { get; init; }
        public int Score { get; init; }
        public ActionClass ActionClass { get; init; }
        public bool Reversible { get; init; }
        public List<DecisionFactor> Factors { get; init; } = new();
        /// <summary>The hard floor or block rule that forced the result, if any.</summary>
        public string? Floor { get; init; }
        public string Reason => Floor ?? (Factors.Count == 0 ? "read-only" : $"score {Score}");
    }

    /// <summary>
    /// Decides, for one tool call, whether the agent may go ahead (Auto / Auto + notify), must ask
    /// (Ask / Ask with second confirm) or may not do it at all (Block). Pure function of the call
    /// and the policy — no I/O beyond reading .git/HEAD — so every decision can be explained and
    /// replayed (agentsettings.md §C.8).
    /// </summary>
    public sealed class BypassDecider
    {
        private static readonly string[] SecretFileNames = { "id_rsa", "id_ed25519", "id_ecdsa", "secrets.json", ".npmrc", ".pypirc", "credentials" };
        private static readonly string[] SecretExtensions = { ".pem", ".key", ".p12", ".pfx", ".keystore", ".jks" };

        public DecisionResult Decide(string toolName, ToolCallFacts facts, string origin, AgentPolicy policy)
        {
            var cls = ToolCatalog.Classify(toolName, facts);
            var factors = new List<DecisionFactor>();
            Decision? minimum = null;          // hard floors raise the result to at least this
            string? floor = null;

            void Floor(Decision d, string why)
            {
                if (minimum == null || d > minimum) { minimum = d; floor = why; }
            }

            DecisionResult Blocked(string why) => new()
            {
                Decision = Decision.Block, ActionClass = cls, Floor = why, Factors = factors,
                Score = factors.Sum(f => f.Points),
            };

            // ── tool override (Tools & Capabilities) ──
            var ov = Override(policy, toolName);
            // "Auto-accept setup jobs for my tasks" (Sync & Pull) acts as an Auto-if-safe override for that one job type.
            bool autoSetup = ov == ToolOverride.Default
                             && string.Equals(toolName, "SetupTaskWorkspace", StringComparison.OrdinalIgnoreCase)
                             && policy.PlatformAgent?.AutoAcceptSetupJobs == true;
            if (autoSetup) ov = ToolOverride.AutoIfSafe;
            if (ov == ToolOverride.Block) return Blocked($"'{toolName}' is blocked in Tools & Capabilities");

            bool readOnly = cls is ActionClass.Read or ActionClass.Internal;

            // ── permissions: these apply to reads too (a read can still exfiltrate via URL) ──
            if (facts.Urls.Count > 0)
            {
                var net = PermissionChecker.CheckUrls(facts.Urls, policy.Permissions);
                if (net.Verdict == PermissionVerdict.NeedsApproval) Floor(Decision.Ask, net.Why);
            }

            if (!readOnly)
            {
                var paths = PermissionChecker.CheckPaths(facts.Paths, policy.Permissions);
                if (paths.Verdict == PermissionVerdict.Blocked) return Blocked(paths.Why);
                if (paths.Verdict == PermissionVerdict.NeedsApproval) Floor(Decision.Ask, paths.Why);

                foreach (var script in facts.Scripts)
                {
                    var sh = PermissionChecker.CheckShell(script, policy.Permissions);
                    if (sh.Verdict == PermissionVerdict.Blocked) return Blocked(sh.Why);
                    if (sh.Verdict == PermissionVerdict.NeedsApproval) Floor(Decision.Ask, sh.Why);
                }
            }

            if (readOnly && minimum == null && ov != ToolOverride.AlwaysAsk)
                return new DecisionResult { Decision = Decision.Auto, ActionClass = cls, Reversible = true, Factors = factors };

            // ── score ──
            var w = policy.Bypass.Weights ?? new RiskFactorWeights();
            int basePts = cls == ActionClass.Outward ? w.OutwardAction : ToolCatalog.BasePoints(cls);
            if (basePts > 0) factors.Add(new("Action class", basePts, ToolCatalog.Label(cls)));

            bool reversible = cls switch
            {
                ActionClass.Destructive => false,
                ActionClass.LocalWrite => facts.RepoRoot != null,   // git can undo it
                _ => true,
            };
            if (cls == ActionClass.Destructive) factors.Add(new("Reversibility", 40, "cannot be undone"));
            else if (cls == ActionClass.LocalWrite && !reversible) factors.Add(new("Reversibility", 15, "files are not in a git repo"));
            else if (cls is ActionClass.Outward or ActionClass.Execute) factors.Add(new("Reversibility", 10, cls == ActionClass.Outward ? "needs a remote revert" : "process side effects"));

            if (facts.FileCount > 20) factors.Add(new("Blast radius", w.ConsumerBlastRadius, $"{facts.FileCount} files"));
            else if (facts.FileCount > 3) factors.Add(new("Blast radius", Math.Max(5, w.ConsumerBlastRadius / 3), $"{facts.FileCount} files"));

            if (!string.IsNullOrEmpty(facts.Branch) && facts.Branch != "(detached)")
            {
                if (policy.Bypass.ProtectedBranches.Any(g => PermissionChecker.GlobMatch(facts.Branch, g)))
                {
                    factors.Add(new("Branch", w.ProtectedBranch, $"'{facts.Branch}' is protected"));
                    Floor(Decision.AskDoubleConfirm, $"write on protected branch '{facts.Branch}'");
                }

                var env = EnvironmentFor(facts.Branch, policy);
                if (env == "production")
                {
                    factors.Add(new("Environment", w.ProductionEnvironment, "production"));
                    Floor(Decision.AskDoubleConfirm, $"branch '{facts.Branch}' maps to production");
                }
                else if (env == "staging") factors.Add(new("Environment", 10, "staging"));
            }

            var secret = facts.Paths.FirstOrDefault(IsSecretPath);
            if (secret != null)
            {
                factors.Add(new("Secrets", 30, System.IO.Path.GetFileName(secret)));
                Floor(Decision.AskDoubleConfirm, $"touches a secrets file ({System.IO.Path.GetFileName(secret)})");
            }

            if (policy.Environments.FreezeNow)
                Floor(Decision.Ask, "change freeze is on" + (string.IsNullOrWhiteSpace(policy.Environments.FreezeReason) ? "" : $": {policy.Environments.FreezeReason}"));

            // Requests from outside the in-app agent ask — except a platform job the member explicitly
            // allowed (Auto-if-safe for that tool). Every other floor still applies to it.
            bool platformAllowed = string.Equals(origin, "platform", StringComparison.OrdinalIgnoreCase) && ov == ToolOverride.AutoIfSafe;
            if (!string.Equals(origin, "agent", StringComparison.OrdinalIgnoreCase) && !platformAllowed)
                Floor(Decision.Ask, $"request came from {origin}, not the in-app agent");

            if (facts.ParseFailed) Floor(Decision.Ask, "tool arguments could not be read");
            if (!ToolCatalog.IsKnown(toolName)) Floor(Decision.Ask, $"'{toolName}' is not in the tool catalog");
            if (ov == ToolOverride.AlwaysAsk) Floor(Decision.Ask, $"'{toolName}' is set to Always ask");

            int score = Math.Clamp(factors.Sum(f => f.Points), 0, 100);

            // ── bands, clamped by autonomy level ──
            var b = policy.Bypass;
            var level = b.Level;
            if (level == AutonomyLevel.FullAutoSandbox && !InSandbox(facts, b)) level = AutonomyLevel.AutoNotify;
            if (ov == ToolOverride.AutoIfSafe && level < AutonomyLevel.AutoNotify) level = AutonomyLevel.AutoNotify;

            Decision d;
            if (level == AutonomyLevel.Observe) return Blocked("autonomy level is Observe (read-only)");
            else if (level == AutonomyLevel.Suggest) d = score > b.AskMax ? Decision.AskDoubleConfirm : Decision.Ask;
            else
            {
                int autoMax = level == AutonomyLevel.FullAutoSandbox ? b.AskMax : b.AutoMax;
                d = score <= autoMax ? Decision.Auto
                  : score <= b.NotifyMax && level != AutonomyLevel.GuardedAuto ? Decision.AutoNotify
                  : score <= b.AskMax ? Decision.Ask
                  : Decision.AskDoubleConfirm;
            }

            // Principle 2: only reversible actions may skip approval. Then apply hard floors.
            string? why = null;
            if (!reversible && d < Decision.Ask) { d = Decision.Ask; why = "action cannot be undone"; }
            if (minimum is { } m && m > d) { d = m; why = floor; }
            if (why == null && minimum == d) why = floor;
            if (why == null && level == AutonomyLevel.Suggest) why = "autonomy level Suggest asks before every change";

            return new DecisionResult
            {
                Decision = d, Score = score, ActionClass = cls, Reversible = reversible,
                Factors = factors, Floor = why,
            };
        }

        public static string EnvironmentFor(string branch, AgentPolicy policy) =>
            policy.Environments.Branches.FirstOrDefault(r => PermissionChecker.GlobMatch(branch, r.Pattern))?.Environment ?? "local";

        public static bool IsSecretPath(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase)) return !name.EndsWith(".example", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".sample", StringComparison.OrdinalIgnoreCase);
            return SecretFileNames.Any(s => name.Equals(s, StringComparison.OrdinalIgnoreCase))
                || SecretExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }

        private static ToolOverride Override(AgentPolicy p, string tool)
        {
            foreach (var kv in p.Bypass.ToolOverrides)
                if (string.Equals(kv.Key, tool, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            return ToolOverride.Default;
        }

        private static bool InSandbox(ToolCallFacts f, BypassSettings b) =>
            b.SandboxRoots.Count > 0 && f.Paths.Count > 0 &&
            f.Paths.All(p => b.SandboxRoots.Any(r => !string.IsNullOrWhiteSpace(r) &&
                p.Replace('\\', '/').StartsWith(r.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)));
    }
}
