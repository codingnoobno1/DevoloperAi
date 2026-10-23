using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Agent.Policy;
using Syncro.Desktop.Services.Git;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    /// <summary>
    /// Checklist #21 — Exposes the 2-layer Conflict Classifier, Ordered Resolution Engine,
    /// Decision Explainer, and Cherry-Pick Engine (`C1–C12`) as first-class MCP tools.
    /// </summary>
    public sealed class ConflictMcp : IMcpServer
    {
        public string Name => "Conflict & Cherry-Pick MCP";

        public string Description =>
            "2-layer Git + semantic conflict classification, ordered resolution rule execution with backup refs, " +
            "risk-score bypass explanation, and traceable cherry-pick backporting.";

        private readonly ConflictResolutionService _conflicts;
        private readonly AgentPolicyStore _policyStore;
        private readonly IGitEngine _git;

        public ConflictMcp(ConflictResolutionService conflicts, AgentPolicyStore policyStore, IGitEngine git)
        {
            _conflicts = conflicts;
            _policyStore = policyStore;
            _git = git;
        }

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new ListConflictsTool(_conflicts, _policyStore);
            yield return new ClassifyConflictsTool(_conflicts, _policyStore);
            yield return new ResolveConflictsTool(_conflicts, _policyStore);
            yield return new ExplainConflictDecisionTool(_conflicts, _policyStore);
            yield return new CherryPickCommitTool(_git, _policyStore);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    public sealed class ListConflictsTool : IMcpTool
    {
        private readonly ConflictResolutionService _conflicts;
        private readonly AgentPolicyStore _store;

        public ListConflictsTool(ConflictResolutionService conflicts, AgentPolicyStore store)
        {
            _conflicts = conflicts;
            _store = store;
        }

        public string Name => "list_conflicts";
        public string Description => "Lists all unmerged/conflicted files in a Git repository along with their 2-layer semantic classification and matched resolution rule.";
        public string InputSchema => "{ \"type\": \"object\", \"properties\": { \"repoPath\": { \"type\": \"string\" } }, \"required\": [\"repoPath\"] }";
        public bool RequiresAdminApproval => false;

        private sealed class Input { public string RepoPath { get; set; } = ""; }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var input = JsonConvert.DeserializeObject<Input>(inputJson);
            if (string.IsNullOrWhiteSpace(input?.RepoPath))
                return JsonConvert.SerializeObject(new { error = "repoPath is required." });

            var items = await _conflicts.ListAndClassifyConflictsAsync(input.RepoPath, _store.Current);
            return JsonConvert.SerializeObject(new { count = items.Count, conflicts = items });
        }
    }

    public sealed class ClassifyConflictsTool : IMcpTool
    {
        private readonly ConflictResolutionService _conflicts;
        private readonly AgentPolicyStore _store;

        public ClassifyConflictsTool(ConflictResolutionService conflicts, AgentPolicyStore store)
        {
            _conflicts = conflicts;
            _store = store;
        }

        public string Name => "classify_conflicts";
        public string Description => "Zero-write simulation of merging sourceRef into targetRef (git merge-tree): lists the files that would conflict, their type, matching rule and risk. Nothing is built or tested.";
        public string InputSchema => "{ \"type\": \"object\", \"properties\": { \"repoPath\": { \"type\": \"string\" }, \"sourceRef\": { \"type\": \"string\" }, \"targetRef\": { \"type\": \"string\" } } }";
        public bool RequiresAdminApproval => false;

        private sealed class Input
        {
            public string? RepoPath { get; set; }
            public string SourceRef { get; set; } = "HEAD";
            public string TargetRef { get; set; } = "origin/main";
        }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var input = JsonConvert.DeserializeObject<Input>(inputJson) ?? new Input();
            var report = await _conflicts.SimulateReplayAsync(input.RepoPath, input.SourceRef, input.TargetRef, _store.Current);
            return report.Ok
                ? JsonConvert.SerializeObject(new { message = report.Message, count = report.Outcomes.Count, outcomes = report.Outcomes })
                : JsonConvert.SerializeObject(new { error = report.Message });
        }
    }

    public sealed class ResolveConflictsTool : IMcpTool
    {
        private readonly ConflictResolutionService _conflicts;
        private readonly AgentPolicyStore _store;

        public ResolveConflictsTool(ConflictResolutionService conflicts, AgentPolicyStore store)
        {
            _conflicts = conflicts;
            _store = store;
        }

        public string Name => "resolve_conflicts";
        public string Description => "Saves a backup ref at HEAD, then runs each conflicted file's rule chain. Only T2/T13 (histogram re-merge), T10 (union) and whole-file ours/theirs are implemented; other files are left for a human. Build/tests are not run.";
        public string InputSchema => "{ \"type\": \"object\", \"properties\": { \"repoPath\": { \"type\": \"string\" } }, \"required\": [\"repoPath\"] }";
        public bool RequiresAdminApproval => false;

        private sealed class Input { public string RepoPath { get; set; } = ""; }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var input = JsonConvert.DeserializeObject<Input>(inputJson);
            if (string.IsNullOrWhiteSpace(input?.RepoPath))
                return JsonConvert.SerializeObject(new { error = "repoPath is required." });

            var run = await _conflicts.ExecuteResolutionChainAsync(input.RepoPath, _store.Current);
            return JsonConvert.SerializeObject(new
            {
                operationId = run.OperationId,
                backupRef = run.BackupRef,
                operation = run.Operation,
                resolvedCount = run.Resolved.Count,
                deferredCount = run.Deferred.Count,
                resolved = run.Resolved,
                deferredToHuman = run.Deferred,
                notes = run.Notes,
                nextStep = "Build and test before committing — only the no-conflict-markers check was run."
            });
        }
    }

    public sealed class ExplainConflictDecisionTool : IMcpTool
    {
        private readonly ConflictResolutionService _conflicts;
        private readonly AgentPolicyStore _store;

        public ExplainConflictDecisionTool(ConflictResolutionService conflicts, AgentPolicyStore store)
        {
            _conflicts = conflicts;
            _store = store;
        }

        public string Name => "explain_decision";
        public string Description => "Explains why Syncro chose a specific conflict rule, technique chain, verification gate, and bypass decision for a file.";
        public string InputSchema => "{ \"type\": \"object\", \"properties\": { \"filePath\": { \"type\": \"string\" }, \"gitCode\": { \"type\": \"string\" } }, \"required\": [\"filePath\"] }";
        public bool RequiresAdminApproval => false;

        private sealed class Input
        {
            public string FilePath { get; set; } = "";
            public string GitCode { get; set; } = "UU";
        }

        public Task<string> ExecuteAsync(string inputJson)
        {
            var input = JsonConvert.DeserializeObject<Input>(inputJson) ?? new Input();
            var explanation = _conflicts.ExplainDecision(input.FilePath, string.IsNullOrWhiteSpace(input.GitCode) ? "UU" : input.GitCode, _store.Current);
            return Task.FromResult(JsonConvert.SerializeObject(explanation));
        }
    }

    public sealed class CherryPickCommitTool : IMcpTool
    {
        private readonly IGitEngine _git;
        private readonly AgentPolicyStore _store;

        public CherryPickCommitTool(IGitEngine git, AgentPolicyStore store)
        {
            _git = git;
            _store = store;
        }

        public string Name => "cherry_pick";
        public string Description => "Cherry-picks a commit SHA or range after saving a backup ref, using the -x traceability and mainline settings from Agent Settings.";
        public string InputSchema => "{ \"type\": \"object\", \"properties\": { \"repoPath\": { \"type\": \"string\" }, \"commitOrRange\": { \"type\": \"string\" }, \"mainlineParent\": { \"type\": \"integer\" } }, \"required\": [\"repoPath\", \"commitOrRange\"] }";
        public bool RequiresAdminApproval => false;

        private sealed class Input
        {
            public string RepoPath { get; set; } = "";
            public string CommitOrRange { get; set; } = "";
            public int? MainlineParent { get; set; }
        }

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var input = JsonConvert.DeserializeObject<Input>(inputJson);
            if (string.IsNullOrWhiteSpace(input?.RepoPath) || string.IsNullOrWhiteSpace(input.CommitOrRange))
                return JsonConvert.SerializeObject(new { error = "repoPath and commitOrRange are required." });

            var cp = _store.Current.CherryPick;
            var opId = "cp-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            await _git.BackupRefAsync(input.RepoPath, opId);

            var r = await _git.CherryPickAsync(
                input.RepoPath,
                input.CommitOrRange,
                traceable: cp.Traceable,
                threeWay: cp.ThreeWayBlobFallback,
                mainlineParent: input.MainlineParent ?? cp.MainlineParent);

            return JsonConvert.SerializeObject(new
            {
                ok = r.Ok,
                exitCode = r.ExitCode,
                backupRef = $"refs/syncro/backup/{opId}",
                output = r.Output,
                error = r.Error
            });
        }
    }
}
