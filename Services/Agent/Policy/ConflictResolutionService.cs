using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Git;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>Result of one verification gate. Status is "passed", "failed" or "not run".</summary>
    public sealed record GateResult(string Gate, string Status, string Detail)
    {
        public bool Passed => Status == "passed";
        public static GateResult NotRun(string gate, string why) => new(gate, "not run", why);
    }

    public sealed class SimulatedFileOutcome
    {
        public string FilePath { get; init; } = "";
        public string GitCode { get; init; } = "UU";
        public string ConflictTypeId { get; init; } = "";
        public string ConflictTypeLabel { get; init; } = "";
        public string Severity { get; init; } = "medium";
        public string RuleSummary { get; init; } = "";
        public string ChainSummary { get; init; } = "";
        public List<GateResult> Gates { get; init; } = new();
        public int RiskScore { get; init; }
        public Decision Decision { get; init; }
        public string DecisionBadge { get; init; } = "";
        public List<string> Factors { get; init; } = new();
        public string? Floor { get; init; }
    }

    /// <summary>Outcome of a zero-write merge simulation (<c>git merge-tree</c>).</summary>
    public sealed class SimulationReport
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public string? Repo { get; init; }
        public string SourceRef { get; init; } = "";
        public string TargetRef { get; init; } = "";
        public List<SimulatedFileOutcome> Outcomes { get; init; } = new();
    }

    public sealed class LearnedRuleSuggestion
    {
        public string Pattern { get; init; } = "";
        public string ConflictTypeId { get; init; } = "";
        public int ObservedCount { get; init; }
        public int MergeCount { get; init; }
        public string SuggestedChain { get; init; } = "";
        public List<string> ChainIds { get; init; } = new();
        public List<string> Gates { get; init; } = new();
        public string Summary { get; init; } = "";
    }

    public sealed class HistoryScanResult
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public int MergesScanned { get; init; }
        public int MergesWithConflicts { get; init; }
        public List<LearnedRuleSuggestion> Suggestions { get; init; } = new();
    }

    public sealed class GitAttributesImport
    {
        public string Message { get; init; } = "";
        public List<ConflictTypeDef> Types { get; init; } = new();
        public List<TechniqueDef> Techniques { get; init; } = new();
        public List<ResolutionRule> Rules { get; init; } = new();
    }

    public sealed class ResolutionRunResult
    {
        public string OperationId { get; init; } = "";
        public string BackupRef { get; init; } = "";
        public string Operation { get; init; } = "";
        public List<string> Resolved { get; init; } = new();
        public List<string> Deferred { get; init; } = new();
        public List<string> Notes { get; init; } = new();
    }

    /// <summary>Risk assessment for one conflicted file, explained factor by factor.</summary>
    public sealed class ConflictAssessment
    {
        public ConflictTypeDef Type { get; init; } = new();
        public ResolutionRule Rule { get; init; } = new();
        public int Score { get; init; }
        public Decision Decision { get; init; }
        public string Badge { get; init; } = "";
        public List<string> Factors { get; init; } = new();
        public string? Floor { get; init; }
    }

    /// <summary>
    /// Conflict classification, rule matching, zero-write simulation, history learning,
    /// .gitattributes import and the resolution runner behind Agent Settings and Conflict MCP.
    /// Everything here reports what git actually says; checks that were not executed are reported
    /// as "not run", never as passed.
    /// </summary>
    public sealed class ConflictResolutionService
    {
        private readonly IGitEngine _git;

        public ConflictResolutionService(IGitEngine git)
        {
            _git = git;
        }

        // ───────────────────────── classification ─────────────────────────

        private static readonly Regex ImportLine = new(
            @"^\s*(using\s|import\s|from\s+\S+\s+import\s|export\s.*\sfrom\s|#include\s|require\(|part\s+'|library\s|@using\s|global using\s)",
            RegexOptions.Compiled);

        /// <summary>
        /// Picks the conflict type for a file. Glob-only types match on the path. Types that are
        /// defined by what changed (whitespace-only, imports, version-bump, custom types with a
        /// content regex) only match when the conflict hunks are available and pass the check —
        /// otherwise e.g. "whitespace-only" (glob **/*) would swallow every file.
        /// </summary>
        public ConflictTypeDef ClassifyFile(string filePath, AgentPolicy policy, string? conflictedContent = null)
        {
            var hunks = conflictedContent == null ? null : ParseHunks(conflictedContent);

            foreach (var ct in policy.Conflicts.ConflictTypes.OrderByDescending(t => t.Priority))
            {
                bool glob = ct.Globs.Any(g => PermissionChecker.GlobMatch(filePath, g));
                switch (ct.Id)
                {
                    case "whitespace-only":
                        if (hunks != null && hunks.Value.Ours.Count + hunks.Value.Theirs.Count > 0
                            && Squash(hunks.Value.Ours) == Squash(hunks.Value.Theirs)) return ct;
                        continue;
                    case "formatting-only":
                        continue; // needs a formatter run to detect; never assumed from the path
                    case "imports":
                        if (glob && hunks != null && AllChanged(hunks.Value, l => ImportLine.IsMatch(l))) return ct;
                        continue;
                    case "version-bump":
                        if (glob && hunks != null && !string.IsNullOrEmpty(ct.ContentRegex)
                            && AllChanged(hunks.Value, l => SafeMatch(l, ct.ContentRegex!))) return ct;
                        continue;
                    case "generated":
                        if (glob) return ct;
                        if (conflictedContent != null && !string.IsNullOrEmpty(ct.ContentRegex)
                            && SafeMatch(string.Join("\n", conflictedContent.Split('\n').Take(5)), ct.ContentRegex!)) return ct;
                        continue;
                }

                if (!glob) continue;
                if (ct.IsBuiltin || string.IsNullOrEmpty(ct.ContentRegex)) return ct;
                // Custom type with a content regex: some conflicting line must match it.
                if (hunks != null && hunks.Value.Ours.Concat(hunks.Value.Theirs).Any(l => SafeMatch(l, ct.ContentRegex!))) return ct;
            }

            return policy.Conflicts.ConflictTypes.FirstOrDefault(t => t.Id == "code-logic")
                ?? new ConflictTypeDef { Id = "unclassified", Label = "Unclassified", Severity = "medium", Priority = 0 };
        }

        /// <summary>First rule (by Order) whose type, git code, branch and operation all match.</summary>
        public ResolutionRule MatchRule(string conflictTypeId, string gitCode, AgentPolicy policy, string? branch = null, string? operation = null)
        {
            foreach (var r in policy.Conflicts.Rules.OrderBy(r => r.Order))
            {
                bool typeMatch = r.ConflictTypes.Contains("*") ||
                                 r.ConflictTypes.Any(c => string.Equals(c, conflictTypeId, StringComparison.OrdinalIgnoreCase));
                bool codeMatch = r.GitCodes.Count == 0 || r.GitCodes.Contains("*") ||
                                 r.GitCodes.Any(c => string.Equals(c, gitCode, StringComparison.OrdinalIgnoreCase));
                bool branchMatch = branch == null || r.Branches.Count == 0 ||
                                   r.Branches.Any(g => g == "*" || PermissionChecker.GlobMatch(branch, g));
                bool opMatch = operation == null || r.Operations.Count == 0 ||
                               r.Operations.Any(o => string.Equals(o, operation, StringComparison.OrdinalIgnoreCase));
                if (typeMatch && codeMatch && branchMatch && opMatch) return r;
            }

            // Nothing matched: hand it to a human rather than borrowing an unrelated rule.
            return new ResolutionRule
            {
                Order = 0, Id = "no-rule", Name = "no matching rule → human review",
                ConflictTypes = new() { conflictTypeId }, Chain = new(), VerifyGates = new(), ElseAction = "H1", ApprovalMode = "Ask",
            };
        }

        public string DescribeChain(IEnumerable<string> chainIds, AgentPolicy policy)
        {
            var ids = chainIds?.ToList() ?? new List<string>();
            if (ids.Count == 0) return "None → Force Human Review";
            var labels = ids.Select(id =>
            {
                var tech = policy.Conflicts.Techniques.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                return tech != null ? $"{tech.Id} {tech.Label}" : id;
            });
            return string.Join(" → ", labels);
        }

        // ───────────────────────── risk assessment ─────────────────────────

        /// <summary>
        /// One scoring path for simulate / list / explain, mirroring BypassDecider's bands and
        /// autonomy clamp. Nothing is assumed to have passed: gates that didn't run add no bonus.
        /// </summary>
        public ConflictAssessment Assess(string filePath, string gitCode, AgentPolicy policy,
            string? conflictedContent = null, string? branch = null, string? operation = null)
        {
            var ct = ClassifyFile(filePath, policy, conflictedContent);
            var rule = MatchRule(ct.Id, gitCode, policy, branch, operation);
            var w = policy.Bypass.Weights ?? new RiskFactorWeights();
            var factors = new List<string>();
            int score = 10; factors.Add("Local write +10");

            int sev = ct.Severity switch { "low" => 5, "medium" => 15, "high" => 20, "critical" => 30, _ => 15 };
            score += sev; factors.Add($"Severity {ct.Severity} +{sev}");

            if (!string.Equals(gitCode, "UU", StringComparison.OrdinalIgnoreCase))
            { score += 10; factors.Add($"{gitCode} add/delete conflict +10"); }

            if (rule.Chain.Any(c => c.StartsWith("A", StringComparison.OrdinalIgnoreCase)))
            { score += w.LowAiConfidence; factors.Add($"AI step in chain, confidence unknown until it runs +{w.LowAiConfidence}"); }

            Decision? floorDecision = null;
            string? floor = null;
            void Floor(Decision d, string why) { if (floorDecision == null || d > floorDecision) { floorDecision = d; floor = why; } }

            if (!string.IsNullOrEmpty(branch))
            {
                var b = branch.StartsWith("origin/", StringComparison.OrdinalIgnoreCase) ? branch[7..] : branch;
                if (policy.Bypass.ProtectedBranches.Any(g => PermissionChecker.GlobMatch(b, g)))
                { score += w.ProtectedBranch; factors.Add($"Protected branch '{b}' +{w.ProtectedBranch}"); Floor(Decision.AskDoubleConfirm, $"protected branch '{b}'"); }
                if (BypassDecider.EnvironmentFor(b, policy) == "production")
                { score += w.ProductionEnvironment; factors.Add($"Production +{w.ProductionEnvironment}"); Floor(Decision.AskDoubleConfirm, $"'{b}' maps to production"); }
            }
            score = Math.Clamp(score, 0, 100);

            bool humanOnly = ct.Id == "secrets/env" || BypassDecider.IsSecretPath(filePath)
                             || rule.ApprovalMode == "Locked Floor" || rule.Chain.Count == 0;
            if (humanOnly)
            {
                var humanWhy = ct.Id == "secrets/env" || BypassDecider.IsSecretPath(filePath) ? "secrets file — human only"
                        : rule.Chain.Count == 0 ? $"rule '{rule.Name}' has no technique — human review"
                        : $"rule '{rule.Name}' is a locked floor";
                return new ConflictAssessment { Type = ct, Rule = rule, Score = score, Decision = Decision.Block, Badge = "Human only", Factors = factors, Floor = humanWhy };
            }

            var bp = policy.Bypass;
            Decision d;
            string? why = null;
            if (bp.Level == AutonomyLevel.Observe) { d = Decision.Block; why = "autonomy level is Observe"; }
            else if (bp.Level == AutonomyLevel.Suggest) { d = score > bp.AskMax ? Decision.AskDoubleConfirm : Decision.Ask; why = "autonomy level Suggest asks before every change"; }
            else
            {
                d = score <= bp.AutoMax ? Decision.Auto
                  : score <= bp.NotifyMax && bp.Level != AutonomyLevel.GuardedAuto ? Decision.AutoNotify
                  : score <= bp.AskMax ? Decision.Ask
                  : Decision.AskDoubleConfirm;
            }

            if (rule.ApprovalMode == "Ask" && d < Decision.Ask) { d = Decision.Ask; why = $"rule '{rule.Name}' is set to Ask"; }
            else if (rule.ApprovalMode == "Auto + Notify" && d == Decision.Auto) d = Decision.AutoNotify;
            if (floorDecision is { } fd && d != Decision.Block && fd >= d) { d = fd; why = floor; }
            floor = why;

            var badge = d switch
            {
                Decision.Auto => $"Score {score}: Auto",
                Decision.AutoNotify => $"Score {score}: Auto + notify",
                Decision.Ask => $"Score {score}: Ask",
                Decision.AskDoubleConfirm => $"Score {score}: Ask + confirm",
                _ => $"Score {score}: Blocked",
            };
            return new ConflictAssessment { Type = ct, Rule = rule, Score = score, Decision = d, Badge = badge, Factors = factors, Floor = floor };
        }

        // ───────────────────────── zero-write simulation ─────────────────────────

        /// <summary>
        /// Simulates merging <paramref name="sourceRef"/> into <paramref name="targetRef"/> with
        /// <c>git merge-tree --write-tree</c>: git computes the real result in its object store and
        /// touches neither the working tree, the index nor any ref.
        /// </summary>
        public async Task<SimulationReport> SimulateReplayAsync(string? repoPath, string sourceRef, string targetRef, AgentPolicy policy)
        {
            SimulationReport Fail(string msg) => new() { Ok = false, Message = msg, Repo = repoPath, SourceRef = sourceRef, TargetRef = targetRef };

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return Fail("Choose a repository folder to simulate in.");
            if (!await _git.IsRepoAsync(repoPath)) return Fail($"'{repoPath}' is not a git repository.");
            sourceRef = (sourceRef ?? "").Trim();
            targetRef = (targetRef ?? "").Trim();
            if (sourceRef.Length == 0 || targetRef.Length == 0) return Fail("Enter a source and a target branch or commit.");

            foreach (var r in new[] { sourceRef, targetRef })
            {
                var v = await _git.RawAsync(repoPath, new[] { "rev-parse", "--verify", "--quiet", r + "^{commit}" });
                if (!v.Ok) return Fail($"'{r}' is not a branch or commit in this repository.");
            }

            var mt = await MergeTreeAsync(repoPath, targetRef, sourceRef);
            if (mt.Error != null) return Fail(mt.Error);
            if (mt.Files.Count == 0)
                return new SimulationReport { Ok = true, Repo = repoPath, SourceRef = sourceRef, TargetRef = targetRef, Message = $"Merging {sourceRef} into {targetRef} has no conflicts." };

            var outcomes = new List<SimulatedFileOutcome>();
            foreach (var (path, code) in mt.Files)
            {
                var content = code is "UU" or "AA" ? await ShowAsync(repoPath, $"{mt.Tree}:{path}") : null;
                var a = Assess(path, code, policy, content, targetRef, "merge");
                var gates = a.Rule.VerifyGates.Select(g => GateResult.NotRun(g, "simulation only — nothing was built or tested")).ToList();
                outcomes.Add(Outcome(path, code, a, gates, policy));
            }

            return new SimulationReport
            {
                Ok = true, Repo = repoPath, SourceRef = sourceRef, TargetRef = targetRef, Outcomes = outcomes,
                Message = $"{outcomes.Count} file(s) would conflict when merging {sourceRef} into {targetRef}. Computed with git merge-tree — nothing in the repository was changed.",
            };
        }

        private SimulatedFileOutcome Outcome(string path, string code, ConflictAssessment a, List<GateResult> gates, AgentPolicy policy) => new()
        {
            FilePath = path,
            GitCode = code,
            ConflictTypeId = a.Type.Id,
            ConflictTypeLabel = a.Type.Label,
            Severity = a.Type.Severity,
            RuleSummary = a.Rule.Order > 0 ? $"Rule #{a.Rule.Order}: {a.Rule.Name}" : a.Rule.Name,
            ChainSummary = DescribeChain(a.Rule.Chain, policy),
            Gates = gates,
            RiskScore = a.Score,
            Decision = a.Decision,
            DecisionBadge = a.Badge,
            Factors = a.Factors,
            Floor = a.Floor,
        };

        // ───────────────────────── learning from history ─────────────────────────

        /// <summary>
        /// Re-runs the last <paramref name="maxMerges"/> merge commits with git merge-tree to find the
        /// files that really conflicted, then groups them by conflict type. Git doesn't record how a
        /// conflict was resolved, so suggestions point at the rule that would handle it today.
        /// </summary>
        public async Task<HistoryScanResult> LearnFromHistoryAsync(string? repoPath, AgentPolicy policy, int maxMerges = 40)
        {
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath) || !await _git.IsRepoAsync(repoPath))
                return new HistoryScanResult { Ok = false, Message = "Choose a git repository to scan." };

            var log = await _git.RawAsync(repoPath, new[] { "log", "--merges", "--format=%H %P", "-n", maxMerges.ToString() });
            if (!log.Ok) return new HistoryScanResult { Ok = false, Message = "git log failed: " + log.Error.Trim() };

            var merges = log.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(p => p.Length >= 3).ToList();

            var byType = new Dictionary<string, (int Files, HashSet<string> Merges, Dictionary<string, int> Paths)>();
            int withConflicts = 0, contentReads = 0;
            foreach (var m in merges)
            {
                var mt = await MergeTreeAsync(repoPath, m[1], m[2]);
                if (mt.Error != null || mt.Files.Count == 0) continue;
                withConflicts++;
                foreach (var (path, code) in mt.Files)
                {
                    string? content = null;
                    if (code is "UU" or "AA" && contentReads++ < 300) content = await ShowAsync(repoPath, $"{mt.Tree}:{path}");
                    var ct = ClassifyFile(path, policy, content);
                    if (!byType.TryGetValue(ct.Id, out var e)) e = (0, new HashSet<string>(), new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
                    e.Files++;
                    e.Merges.Add(m[0]);
                    e.Paths[path] = e.Paths.TryGetValue(path, out var n) ? n + 1 : 1;
                    byType[ct.Id] = e;
                }
            }

            var suggestions = new List<LearnedRuleSuggestion>();
            foreach (var (typeId, e) in byType.OrderByDescending(kv => kv.Value.Files))
            {
                var top = e.Paths.OrderByDescending(p => p.Value).First();
                string pattern = top.Value >= 2 || e.Paths.Count == 1
                    ? top.Key
                    : "**/*" + (e.Paths.Keys.Select(Path.GetExtension).GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key ?? "");
                var rule = MatchRule(typeId, "UU", policy);
                suggestions.Add(new LearnedRuleSuggestion
                {
                    Pattern = pattern,
                    ConflictTypeId = typeId,
                    ObservedCount = e.Files,
                    MergeCount = e.Merges.Count,
                    ChainIds = new List<string>(rule.Chain),
                    Gates = new List<string>(rule.VerifyGates),
                    SuggestedChain = DescribeChain(rule.Chain, policy),
                    Summary = $"Conflicted {e.Files} time(s) in {e.Merges.Count} of the last {merges.Count} merges. " +
                              $"Today it would be handled by {(rule.Order > 0 ? $"Rule #{rule.Order} ({rule.Name})" : "no rule (human review)")}. " +
                              "Git doesn't record how it was resolved back then.",
                });
            }

            return new HistoryScanResult
            {
                Ok = true, MergesScanned = merges.Count, MergesWithConflicts = withConflicts, Suggestions = suggestions,
                Message = merges.Count == 0 ? "No merge commits in this repository's history."
                        : $"Replayed {merges.Count} merge(s) with git merge-tree; {withConflicts} had conflicts.",
            };
        }

        // ───────────────────────── .gitattributes import ─────────────────────────

        /// <summary>Turns <c>merge=union</c> / <c>merge=ours</c> lines into a conflict type + rule per glob.</summary>
        public GitAttributesImport ImportFromGitAttributes(string? repoPath, int startOrder)
        {
            var types = new List<ConflictTypeDef>();
            var techs = new List<TechniqueDef>();
            var rules = new List<ResolutionRule>();
            if (string.IsNullOrWhiteSpace(repoPath)) return new GitAttributesImport { Message = "Choose a repository first." };

            var attrPath = Path.Combine(repoPath, ".gitattributes");
            if (!File.Exists(attrPath)) return new GitAttributesImport { Message = "No .gitattributes file in this repository." };

            int n = 0;
            foreach (var raw in File.ReadAllLines(attrPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var glob = parts[0];
                string? driver = parts.Skip(1).FirstOrDefault(p => p.StartsWith("merge=", StringComparison.OrdinalIgnoreCase))?[6..];
                if (driver is not ("union" or "ours")) continue;

                n++;
                var typeId = $"gitattr-{driver}-{n}";
                var fullGlob = glob.Contains('/') ? glob.TrimStart('/') : "**/" + glob;
                types.Add(new ConflictTypeDef { Id = typeId, Label = $".gitattributes {driver}: {glob}", Globs = new() { fullGlob }, Priority = 85, Severity = "low", Description = $"Imported from .gitattributes ({line})" });

                string techId = "T10";
                if (driver == "ours")
                {
                    techId = "GA-ours";
                    if (!techs.Any(t => t.Id == techId))
                        techs.Add(new TechniqueDef { Id = techId, Label = "Keep Our Whole File", Category = "git-native", CommandOrHint = "git checkout --ours -- <file>", Risk = "medium", Reversible = true });
                }
                rules.Add(new ResolutionRule
                {
                    Order = startOrder++, Id = $"import-{typeId}", Name = $".gitattributes {driver} ({glob})",
                    ConflictTypes = new() { typeId }, GitCodes = new() { "UU", "AA" }, Chain = new() { techId },
                    VerifyGates = new() { "no-markers" }, ElseAction = "H1", ApprovalMode = "Auto + Notify",
                });
            }

            return new GitAttributesImport
            {
                Types = types, Techniques = techs, Rules = rules,
                Message = rules.Count == 0 ? "No merge=union or merge=ours entries in .gitattributes." : $"Imported {rules.Count} rule(s) from .gitattributes.",
            };
        }

        // ───────────────────────── live conflicts ─────────────────────────

        public async Task<List<SimulatedFileOutcome>> ListAndClassifyConflictsAsync(string repoPath, AgentPolicy policy)
        {
            var results = new List<SimulatedFileOutcome>();
            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath)) return results;

            var st = await _git.StatusAsync(repoPath);
            var op = DetectOperation(repoPath);
            foreach (var f in st.Files.Where(f => f.IsConflicted))
            {
                var code = f.ConflictCode ?? "UU";
                var a = Assess(f.Path, code, policy, ReadText(Path.Combine(repoPath, f.Path)), st.Branch, op);
                var gates = a.Rule.VerifyGates.Select(g => GateResult.NotRun(g, "runs after resolution")).ToList();
                results.Add(Outcome(f.Path, code, a, gates, policy));
            }
            return results;
        }

        public object ExplainDecision(string filePath, string gitCode, AgentPolicy policy)
        {
            var content = File.Exists(filePath) ? ReadText(filePath) : null;
            var a = Assess(filePath, gitCode, policy, content);
            return new
            {
                filePath,
                gitCode,
                contentChecked = content != null,
                conflictType = new { a.Type.Id, a.Type.Label, a.Type.Severity, a.Type.Priority },
                matchedRule = new { a.Rule.Order, a.Rule.Id, a.Rule.Name, a.Rule.Chain, a.Rule.VerifyGates, a.Rule.ElseAction, a.Rule.ApprovalMode, a.Rule.ShadowMode },
                chainDescription = DescribeChain(a.Rule.Chain, policy),
                riskScore = a.Score,
                factors = a.Factors,
                floor = a.Floor,
                decision = a.Decision.ToString(),
                thresholds = new { policy.Bypass.AutoMax, policy.Bypass.NotifyMax, policy.Bypass.AskMax, autonomy = policy.Bypass.Level.ToString() },
            };
        }

        // ───────────────────────── resolution runner ─────────────────────────

        /// <summary>
        /// Runs each conflicted file's rule chain. Only techniques that are really implemented are
        /// applied (T2/T13 re-merge with histogram diff, T10 union, take-whole-file ours/theirs);
        /// anything else is reported as not implemented and the file is left for a human. A file is
        /// only staged after a real no-conflict-markers check.
        /// </summary>
        public async Task<ResolutionRunResult> ExecuteResolutionChainAsync(string repoPath, AgentPolicy policy)
        {
            var opId = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var resolved = new List<string>();
            var deferred = new List<string>();
            var notes = new List<string>();

            if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
                return new ResolutionRunResult { OperationId = opId, Notes = { "Repository path does not exist." } };

            var backup = await _git.BackupRefAsync(repoPath, opId);
            notes.Add(backup.Ok
                ? $"Backup ref refs/syncro/backup/{opId} saved at HEAD. It holds committed work only — uncommitted changes are not in it."
                : "Could not create a backup ref: " + backup.Error.Trim());

            var st = await _git.StatusAsync(repoPath);
            var op = DetectOperation(repoPath);
            foreach (var f in st.Files.Where(f => f.IsConflicted))
            {
                var code = f.ConflictCode ?? "UU";
                var full = Path.Combine(repoPath, f.Path);
                var a = Assess(f.Path, code, policy, ReadText(full), st.Branch, op);
                var rule = a.Rule;

                if (a.Decision == Decision.Block) { deferred.Add($"{f.Path} — {a.Floor ?? "human only"}"); continue; }
                if (rule.ShadowMode) { deferred.Add($"{f.Path} — rule '{rule.Name}' is in shadow mode; would run {string.Join(" → ", rule.Chain)}"); continue; }

                var tried = new List<string>();
                string? doneBy = null;
                foreach (var techId in rule.Chain)
                {
                    var (ok, note) = await ApplyTechniqueAsync(repoPath, f.Path, code, techId, policy);
                    tried.Add($"{techId}: {note}");
                    if (ok) { doneBy = $"{techId} ({note})"; break; }
                }

                if (doneBy != null)
                {
                    var gates = rule.VerifyGates.Where(g => g != "no-markers").ToList();
                    resolved.Add($"{f.Path} via {doneBy}, rule #{rule.Order} · no markers ✓" +
                                 (gates.Count > 0 ? $" · {string.Join(", ", gates)} not run — run Build & Verify before committing" : ""));
                }
                else deferred.Add($"{f.Path} — {string.Join("; ", tried)} → {rule.ElseAction}");
            }

            return new ResolutionRunResult
            {
                OperationId = opId, BackupRef = $"refs/syncro/backup/{opId}", Operation = op ?? "unknown",
                Resolved = resolved, Deferred = deferred, Notes = notes,
            };
        }

        /// <summary>Restores HEAD to the backup ref. Warning: this is reset --hard — uncommitted changes are discarded.</summary>
        public Task<GitResult> UndoResolutionAsync(string repoPath, string operationId) =>
            _git.RestoreBackupRefAsync(repoPath, operationId);

        /// <summary>True when the resolver really executes this technique (see ApplyTechniqueAsync).</summary>
        public static bool IsImplemented(TechniqueDef t)
        {
            var id = (t.Id ?? "").ToUpperInvariant();
            if (id is "T2" or "T13" or "T10") return true;
            var hint = t.CommandOrHint ?? "";
            return hint.Contains("checkout --ours", StringComparison.OrdinalIgnoreCase)
                || hint.Contains("checkout --theirs", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<(bool Ok, string Note)> ApplyTechniqueAsync(string repo, string path, string code, string techId, AgentPolicy policy)
        {
            var tech = policy.Conflicts.Techniques.FirstOrDefault(t => string.Equals(t.Id, techId, StringComparison.OrdinalIgnoreCase));
            var hint = tech?.CommandOrHint ?? "";
            bool sideOurs = hint.Contains("checkout --ours", StringComparison.OrdinalIgnoreCase);
            bool sideTheirs = hint.Contains("checkout --theirs", StringComparison.OrdinalIgnoreCase);

            if (sideOurs || sideTheirs)
            {
                if (code is not ("UU" or "AA")) return (false, $"whole-file pick not supported for {code} (add/delete) conflicts");
                var r = await _git.ResolveConflictSideAsync(repo, path, takeTheirs: sideTheirs);
                if (!r.Ok) return (false, r.Error.Trim());
                return HasNoConflictMarkers(Path.Combine(repo, path)) ? (true, sideTheirs ? "took their whole file" : "kept our whole file") : (false, "markers remain");
            }

            switch (techId.ToUpperInvariant())
            {
                case "T2":
                case "T13":
                    return await MergeFileAsync(repo, path, code, "--diff-algorithm=histogram", "re-merged with histogram diff");
                case "T10":
                    return await MergeFileAsync(repo, path, code, "--union", "kept both sides' lines (union)");
                case "T6":
                case "T12":
                    return (false, "applies to git config, not a single file");
                default:
                    return (false, "not implemented yet");
            }
        }

        /// <summary>Real 3-way re-merge of one file from its index stages using git merge-file.</summary>
        private async Task<(bool Ok, string Note)> MergeFileAsync(string repo, string path, string code, string flag, string label)
        {
            if (code is not ("UU" or "AA")) return (false, $"not possible for {code} (add/delete) conflicts");
            var co = await _git.RawAsync(repo, new[] { "checkout-index", "--stage=all", "--temp", "--", path });
            if (!co.Ok) return (false, co.Error.Trim());

            // Output: "<stage1> <stage2> <stage3>\t<path>", "." for a missing stage.
            var line = co.Output.Split('\n').FirstOrDefault(l => l.Contains('\t')) ?? "";
            var names = line.Split('\t')[0].Split(' ');
            var temps = names.Where(n => n != ".").Select(n => Path.Combine(repo, n)).ToList();
            string? emptyBase = null;
            try
            {
                if (names.Length < 3 || names[1] == "." || names[2] == ".") return (false, "missing our or their version");
                string basePath = names[0] == "." ? (emptyBase = Path.GetTempFileName()) : Path.Combine(repo, names[0]);
                string ours = Path.Combine(repo, names[1]), theirs = Path.Combine(repo, names[2]);

                var mf = await _git.RawAsync(repo, new[] { "merge-file", flag, "-L", "ours", "-L", "base", "-L", "theirs", ours, basePath, theirs });
                if (mf.ExitCode < 0 || mf.ExitCode > 127) return (false, "git merge-file failed: " + mf.Error.Trim());
                if (mf.ExitCode > 0) return (false, $"still {mf.ExitCode} conflict(s)");

                var target = Path.Combine(repo, path);
                File.Copy(ours, target, overwrite: true);
                if (!HasNoConflictMarkers(target)) return (false, "markers remain");
                var add = await _git.RawAsync(repo, new[] { "add", "--", path });
                return add.Ok ? (true, label) : (false, add.Error.Trim());
            }
            finally
            {
                foreach (var t in temps) { try { File.Delete(t); } catch { } }
                if (emptyBase != null) { try { File.Delete(emptyBase); } catch { } }
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        private sealed record MergeTreeResult(string? Tree, List<(string Path, string Code)> Files, string? Error);

        /// <summary>
        /// <c>git merge-tree --write-tree ours theirs</c> (git ≥ 2.38). Exit 0 = clean, 1 = conflicts.
        /// Conflicted entries are "mode oid stage\tpath"; the set of stages gives the git code.
        /// </summary>
        private async Task<MergeTreeResult> MergeTreeAsync(string repo, string ours, string theirs)
        {
            var r = await _git.RawAsync(repo, new[] { "merge-tree", "--write-tree", "--no-messages", ours, theirs });
            if (r.ExitCode is not (0 or 1))
                return new(null, new(), "git merge-tree failed (needs git 2.38+): " + r.Error.Trim());

            var lines = r.Output.Replace("\r", "").Split('\n');
            var tree = lines.FirstOrDefault()?.Trim();
            var stages = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var l in lines.Skip(1))
            {
                if (l.Length == 0) break;
                var tab = l.IndexOf('\t');
                if (tab < 0) continue;
                var meta = l[..tab].Split(' ');
                if (meta.Length < 3 || !int.TryParse(meta[2], out var stage)) continue;
                var p = Unquote(l[(tab + 1)..]);
                if (!stages.TryGetValue(p, out var set)) stages[p] = set = new HashSet<int>();
                set.Add(stage);
            }

            var files = stages.Select(kv => (kv.Key, CodeFor(kv.Value))).ToList();
            return new(tree, files, null);
        }

        private static string CodeFor(HashSet<int> s) =>
            s.SetEquals(new[] { 1, 2, 3 }) ? "UU" :
            s.SetEquals(new[] { 2, 3 }) ? "AA" :
            s.SetEquals(new[] { 1, 2 }) ? "UD" :
            s.SetEquals(new[] { 1, 3 }) ? "DU" :
            s.SetEquals(new[] { 2 }) ? "AU" :
            s.SetEquals(new[] { 3 }) ? "UA" : "DD";

        private async Task<string?> ShowAsync(string repo, string objectSpec)
        {
            var r = await _git.RawAsync(repo, new[] { "cat-file", "-p", objectSpec });
            return r.Ok && r.Output.Length < 2_000_000 ? r.Output : null;
        }

        private static string Unquote(string p) =>
            p.Length >= 2 && p[0] == '"' && p[^1] == '"' ? Regex.Unescape(p[1..^1]) : p;

        /// <summary>merge | rebase | cherry-pick | revert, from the state files git leaves in .git.</summary>
        private static string? DetectOperation(string repo)
        {
            var gitDir = Path.Combine(repo, ".git");
            if (File.Exists(gitDir))
            {
                var l = File.ReadAllText(gitDir).Trim();
                if (l.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                {
                    var g = l[7..].Trim();
                    gitDir = Path.IsPathRooted(g) ? g : Path.GetFullPath(Path.Combine(repo, g));
                }
            }
            if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) return "cherry-pick";
            if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))) return "revert";
            if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return "rebase";
            if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) return "merge";
            return null;
        }

        private static string? ReadText(string fullPath)
        {
            try { return File.Exists(fullPath) && new FileInfo(fullPath).Length < 2_000_000 ? File.ReadAllText(fullPath) : null; }
            catch { return null; }
        }

        /// <summary>Lines on our side and their side of every conflict block (zdiff3 base sections skipped).</summary>
        public static (List<string> Ours, List<string> Theirs)? ParseHunks(string content)
        {
            var ours = new List<string>();
            var theirs = new List<string>();
            int state = 0; bool any = false;
            foreach (var raw in content.Split('\n'))
            {
                var l = raw.TrimEnd('\r');
                if (l.StartsWith("<<<<<<<") && (l.Length == 7 || l[7] == ' ')) { state = 1; any = true; continue; }
                if (state == 1 && l.StartsWith("|||||||") && (l.Length == 7 || l[7] == ' ')) { state = 2; continue; }
                if (state is 1 or 2 && l == "=======") { state = 3; continue; }
                if (state == 3 && l.StartsWith(">>>>>>>") && (l.Length == 7 || l[7] == ' ')) { state = 0; continue; }
                if (state == 1) ours.Add(l);
                else if (state == 3) theirs.Add(l);
            }
            return any ? (ours, theirs) : null;
        }

        private static string Squash(IEnumerable<string> lines) => Regex.Replace(string.Concat(lines), @"\s+", "");

        private static bool AllChanged((List<string> Ours, List<string> Theirs) h, Func<string, bool> pred)
        {
            var changed = h.Ours.Concat(h.Theirs).Where(l => l.Trim().Length > 0).ToList();
            return changed.Count > 0 && changed.All(pred);
        }

        private static bool SafeMatch(string text, string pattern)
        {
            try { return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)); }
            catch { return false; }
        }

        /// <summary>True when no real conflict marker lines remain (exact "=======" only, so Markdown rules don't count).</summary>
        public static bool HasNoConflictMarkers(string fullPath)
        {
            if (!File.Exists(fullPath)) return true;
            try
            {
                foreach (var raw in File.ReadLines(fullPath))
                {
                    var line = raw.TrimEnd('\r');
                    if ((line.StartsWith("<<<<<<<") || line.StartsWith(">>>>>>>")) && (line.Length == 7 || line[7] == ' ')) return false;
                    if (line == "=======") return false;
                }
                return true;
            }
            catch { return false; }
        }

        // ───────────────────────── plain-English drafting ─────────────────────────

        /// <summary>Keyword-based draft (not an LLM call): recognises lockfiles, generated code and contracts.</summary>
        public ResolutionRule DraftRuleFromPlainEnglish(string prompt, AgentPolicy policy)
        {
            var lower = (prompt ?? "").ToLowerInvariant();
            int nextOrder = (policy.Conflicts.Rules.Count > 0 ? policy.Conflicts.Rules.Max(r => r.Order) : 15) + 1;

            if (lower.Contains("flutter") || lower.Contains(".g.dart") || lower.Contains("freezed") || lower.Contains("generated"))
                return new ResolutionRule
                {
                    Order = nextOrder, Id = "rule-nl-flutter-gen", Name = "flutter-generated (*.g.dart, *.freezed.dart)",
                    ConflictTypes = new() { "generated" }, GitCodes = new() { "UU", "AA" }, Chain = new() { "S2", "T8" },
                    VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Auto Bypass", ShadowMode = true,
                };

            if (lower.Contains("lock") || lower.Contains("package-lock") || lower.Contains("pubspec.lock") || lower.Contains("restore"))
                return new ResolutionRule
                {
                    Order = nextOrder, Id = "rule-nl-lockfile", Name = "lockfile auto-regenerate",
                    ConflictTypes = new() { "lockfile" }, GitCodes = new() { "UU" }, Chain = new() { "S1" },
                    VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Auto Bypass", ShadowMode = true,
                };

            if (lower.Contains("contract") || lower.Contains("openapi") || lower.Contains("swagger") || lower.Contains("controller"))
                return new ResolutionRule
                {
                    Order = nextOrder, Id = "rule-nl-contract", Name = "api-contract strict consumer check",
                    ConflictTypes = new() { "api-contract" }, GitCodes = new() { "UU" }, Chain = new() { "S6", "S9", "A1" },
                    VerifyGates = new() { "contract", "build", "tests" }, ElseAction = "H1", MinConfidence = 0.90, ApprovalMode = "Ask", ShadowMode = true,
                };

            return new ResolutionRule
            {
                Order = nextOrder, Id = "rule-nl-" + Guid.NewGuid().ToString("N")[..6],
                Name = string.IsNullOrWhiteSpace(prompt) ? "custom-nl-rule" : (prompt.Length > 44 ? prompt[..44] + "…" : prompt),
                ConflictTypes = new() { "code-logic" }, GitCodes = new() { "UU" }, Chain = new() { "T2", "S6", "A1" },
                VerifyGates = new() { "build", "tests" }, ElseAction = "H1", MinConfidence = 0.85, ApprovalMode = "Ask", ShadowMode = true,
            };
        }
    }
}
