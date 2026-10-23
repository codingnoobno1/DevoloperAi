using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Syncro.Desktop.Services.Agent.Policy
{
    public sealed class AuditEntry
    {
        public DateTime Time { get; set; } = DateTime.Now;
        public string Origin { get; set; } = "agent";
        public string Tool { get; set; } = "";
        public ActionClass ActionClass { get; set; }
        public Decision Decision { get; set; }
        public int Score { get; set; }
        public string? Floor { get; set; }
        public List<string> Factors { get; set; } = new();
        /// <summary>auto | user | run-allowance | timeout | policy</summary>
        public string Approver { get; set; } = "auto";
        /// <summary>ok | error | blocked | denied | timeout | saved</summary>
        public string Outcome { get; set; } = "ok";
        public long DurationMs { get; set; }
        public string Target { get; set; } = "";
        public string InputPreview { get; set; } = "";
        public string? Branch { get; set; }
    }

    /// <summary>
    /// Append-only record of every agent decision and every policy save (agentsettings.md §C.13,
    /// D9). One JSON line per entry at %LOCALAPPDATA%\SyncroDesktop\agent\audit.jsonl; the last
    /// 500 are kept in memory for the Audit Log tab. Inputs are truncated and secrets redacted.
    /// </summary>
    public sealed class AgentAuditLog
    {
        private const int Keep = 500;
        private static readonly JsonSerializerSettings Json = new() { Converters = { new StringEnumConverter() } };
        private static readonly Regex SecretRx = new(
            @"(gsk_|ghp_|gho_|ghs_|github_pat_|sk-|xox[bpa]-)[A-Za-z0-9_\-]{8,}|AKIA[0-9A-Z]{16}|(""(?:password|passwd|secret|token|apiKey|api_key)""\s*:\s*"")[^""]*",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly object _gate = new();
        private readonly LinkedList<AuditEntry> _recent = new();

        public string FilePath { get; } = Path.Combine(AgentPolicyStore.AgentDataDir, "audit.jsonl");

        public event Action<AuditEntry>? Appended;

        public AgentAuditLog(AgentPolicyStore store)
        {
            LoadTail();
            store.OnSaved = (reason, path) => Append(new AuditEntry
            {
                Origin = "settings", Tool = "policy", Decision = Decision.Auto, Approver = "user",
                Outcome = "saved", Target = reason, InputPreview = path,
            });
        }

        public IReadOnlyList<AuditEntry> Recent
        {
            get { lock (_gate) return _recent.Reverse().ToList(); }   // newest first
        }

        public void Append(AuditEntry e)
        {
            e.InputPreview = Redact(e.InputPreview, 400);
            e.Target = Redact(e.Target, 200);
            lock (_gate)
            {
                _recent.AddLast(e);
                while (_recent.Count > Keep) _recent.RemoveFirst();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                    File.AppendAllText(FilePath, JsonConvert.SerializeObject(e, Json) + Environment.NewLine);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Audit] write failed: {ex.Message}"); }
            }
            Appended?.Invoke(e);
        }

        public static string Redact(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var r = SecretRx.Replace(s, m => m.Groups[2].Success ? m.Groups[2].Value + "[redacted]" : "[redacted]");
            return r.Length > max ? r[..max] + "…" : r;
        }

        private void LoadTail()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadLines(FilePath).TakeLast(Keep))
                {
                    try { if (JsonConvert.DeserializeObject<AuditEntry>(line, Json) is { } e) _recent.AddLast(e); }
                    catch { }
                }
            }
            catch { }
        }
    }
}
