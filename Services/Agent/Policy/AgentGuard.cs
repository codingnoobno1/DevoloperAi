using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// The gate every agent tool call passes through (agentsettings.md A.4 / P1): decide → ask if
    /// needed → run → audit. Wired into <c>McpOrchestrator.ExecuteToolAsync</c>, so the in-app agent
    /// loop, GitCopilot and the editor bridge can no longer run a write/exec tool unchecked.
    /// </summary>
    public sealed class AgentGuard
    {
        private static readonly AsyncLocal<string?> CurrentRun = new();

        private readonly AgentPolicyStore _store;
        private readonly BypassDecider _decider;
        private readonly ApprovalService _approvals;
        private readonly AgentAuditLog _audit;

        public AgentGuard(AgentPolicyStore store, BypassDecider decider, ApprovalService approvals, AgentAuditLog audit)
        {
            _store = store;
            _decider = decider;
            _approvals = approvals;
            _audit = audit;
        }

        /// <summary>Marks the start of one agent task, so "allow for this run" approvals stay scoped to it.</summary>
        public IDisposable BeginRun()
        {
            var id = Guid.NewGuid().ToString("N")[..10];
            var previous = CurrentRun.Value;
            CurrentRun.Value = id;
            return new RunScope(() => { _approvals.EndRun(id); CurrentRun.Value = previous; });
        }

        /// <summary>Preview the decision without running anything (used by the settings page).</summary>
        public DecisionResult Preview(string toolName, string json, string origin = "agent") =>
            _decider.Decide(toolName, ToolCallFacts.From(json), origin, _store.Current);

        public async Task<string> ExecuteAsync(string toolName, string json, string origin, Func<Task<string>> run)
        {
            var policy = _store.Current;
            var facts = ToolCallFacts.From(json);
            var d = _decider.Decide(toolName, facts, origin, policy);
            var entry = new AuditEntry
            {
                Origin = origin, Tool = toolName, ActionClass = d.ActionClass, Decision = d.Decision,
                Score = d.Score, Floor = d.Floor, Target = facts.Target, Branch = facts.Branch,
                InputPreview = json ?? "",
                Factors = d.Factors.Select(f => $"{f.Factor} +{f.Points}: {f.Why}").ToList(),
            };

            if (d.Decision == Decision.Block)
            {
                entry.Approver = "policy"; entry.Outcome = "blocked";
                _audit.Append(entry);
                return $"Error: Blocked by Syncro Agent Settings — {d.Reason}. Do not retry this action; tell the user what you wanted to do and why.";
            }

            if (d.Decision is Decision.Ask or Decision.AskDoubleConfirm)
            {
                var runId = CurrentRun.Value;
                if (d.Decision == Decision.Ask && _approvals.IsAllowedForRun(runId, toolName))
                {
                    entry.Approver = "run-allowance";
                }
                else
                {
                    var req = new ApprovalRequest
                    {
                        ToolName = toolName, Origin = origin, RunId = runId, Target = facts.Target, Branch = facts.Branch,
                        InputPreview = AgentAuditLog.Redact(json, 1200), Result = d,
                        ExpiresAt = DateTime.Now.AddMinutes(Math.Max(1, policy.Approvals.TimeoutMinutes)),
                    };
                    var answer = await _approvals.RequestAsync(req, TimeSpan.FromMinutes(Math.Max(1, policy.Approvals.TimeoutMinutes)));
                    if (answer is ApprovalAnswer.Denied or ApprovalAnswer.TimedOut)
                    {
                        entry.Approver = answer == ApprovalAnswer.TimedOut ? "timeout" : "user";
                        entry.Outcome = answer == ApprovalAnswer.TimedOut ? "timeout" : "denied";
                        _audit.Append(entry);
                        return answer == ApprovalAnswer.TimedOut
                            ? $"Error: No approval for '{toolName}' within {policy.Approvals.TimeoutMinutes} min, so it was not run. Ask the user to approve it in Syncro."
                            : $"Error: The user declined '{toolName}' ({facts.Target}). Do not retry it; continue without it or ask the user.";
                    }
                    entry.Approver = "user";
                }
            }

            var sw = Stopwatch.StartNew();
            try
            {
                var result = await run();
                entry.DurationMs = sw.ElapsedMilliseconds;
                entry.Outcome = IsError(result) ? "error" : "ok";
                _audit.Append(entry);
                if (d.Decision == Decision.AutoNotify && policy.Approvals.NotifyOnAutoNotify)
                    _approvals.Notify($"Agent ran {toolName}: {facts.Target} (score {d.Score})");
                return result;
            }
            catch (Exception ex)
            {
                entry.DurationMs = sw.ElapsedMilliseconds;
                entry.Outcome = "error";
                entry.InputPreview = $"{ex.Message} | {entry.InputPreview}";
                _audit.Append(entry);
                throw;
            }
        }

        private static bool IsError(string? r) =>
            r != null && (r.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                          || r.TrimStart().StartsWith("{ \"error\"", StringComparison.OrdinalIgnoreCase)
                          || r.TrimStart().StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase));

        private sealed class RunScope : IDisposable
        {
            private Action? _end;
            public RunScope(Action end) => _end = end;
            public void Dispose() { _end?.Invoke(); _end = null; }
        }
    }
}
