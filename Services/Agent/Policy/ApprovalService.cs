using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Agent.Policy
{
    public enum ApprovalAnswer { Approved, AllowForRun, Denied, TimedOut }

    public sealed class ApprovalRequest
    {
        public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime CreatedAt { get; } = DateTime.Now;
        public DateTime ExpiresAt { get; init; }
        public string ToolName { get; init; } = "";
        public string Origin { get; init; } = "agent";
        public string? RunId { get; init; }
        public string Target { get; init; } = "";
        public string? Branch { get; init; }
        public string InputPreview { get; init; } = "";
        public DecisionResult Result { get; init; } = new();
        public bool DoubleConfirm => Result.Decision == Decision.AskDoubleConfirm;
        /// <summary>What the user must type to approve a double-confirm request.</summary>
        public string ConfirmWord => !string.IsNullOrEmpty(Branch) ? Branch! : ToolName;

        internal TaskCompletionSource<ApprovalAnswer> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// The approval inbox (agentsettings.md D6, D10, D11). The agent awaits <see cref="RequestAsync"/>;
    /// any open window (main app or IDE) shows the pending list and answers it. Unanswered requests
    /// are denied at timeout — never approved.
    /// </summary>
    public sealed class ApprovalService
    {
        private readonly ConcurrentDictionary<string, ApprovalRequest> _pending = new();
        private readonly ConcurrentDictionary<string, byte> _runAllowances = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Pending list changed (added, answered, expired).</summary>
        public event Action? Changed;
        /// <summary>An Auto + notify action ran — show a toast.</summary>
        public event Action<string>? Notified;

        public IReadOnlyList<ApprovalRequest> Pending =>
            _pending.Values.OrderByDescending(r => r.Result.Score).ThenBy(r => r.CreatedAt).ToList();

        public int PendingCount => _pending.Count;

        public async Task<ApprovalAnswer> RequestAsync(ApprovalRequest req, TimeSpan timeout)
        {
            _pending[req.Id] = req;
            Changed?.Invoke();
            try
            {
                var done = await Task.WhenAny(req.Tcs.Task, Task.Delay(timeout)).ConfigureAwait(false);
                return done == req.Tcs.Task ? await req.Tcs.Task.ConfigureAwait(false) : ApprovalAnswer.TimedOut;
            }
            finally
            {
                _pending.TryRemove(req.Id, out _);
                req.Tcs.TrySetResult(ApprovalAnswer.TimedOut);
                Changed?.Invoke();
            }
        }

        /// <summary>Approve one request. Double-confirm requests need <paramref name="typed"/> to match.</summary>
        public bool Approve(string id, bool forRun = false, string? typed = null)
        {
            if (!_pending.TryGetValue(id, out var r)) return false;
            if (r.DoubleConfirm && !string.Equals(typed?.Trim(), r.ConfirmWord, StringComparison.OrdinalIgnoreCase)) return false;
            if (forRun && r.RunId != null && !r.DoubleConfirm) _runAllowances[Key(r.RunId, r.ToolName)] = 1;
            return r.Tcs.TrySetResult(forRun ? ApprovalAnswer.AllowForRun : ApprovalAnswer.Approved);
        }

        public bool Deny(string id) => _pending.TryGetValue(id, out var r) && r.Tcs.TrySetResult(ApprovalAnswer.Denied);

        /// <summary>Approve every plain "Ask" request (never double-confirm ones). Returns how many.</summary>
        public int ApproveAllLowRisk()
        {
            int n = 0;
            foreach (var r in _pending.Values.Where(r => r.Result.Decision == Decision.Ask))
                if (r.Tcs.TrySetResult(ApprovalAnswer.Approved)) n++;
            return n;
        }

        public void DenyAll()
        {
            foreach (var r in _pending.Values) r.Tcs.TrySetResult(ApprovalAnswer.Denied);
        }

        public bool IsAllowedForRun(string? runId, string tool) =>
            runId != null && _runAllowances.ContainsKey(Key(runId, tool));

        public void EndRun(string runId)
        {
            foreach (var k in _runAllowances.Keys.Where(k => k.StartsWith(runId + "|", StringComparison.Ordinal)).ToList())
                _runAllowances.TryRemove(k, out _);
        }

        public void Notify(string message) => Notified?.Invoke(message);

        private static string Key(string run, string tool) => run + "|" + tool;
    }
}
