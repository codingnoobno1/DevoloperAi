using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// The one source of truth for agent settings (agentsettings.md D36). Every UI — the
    /// /settings/agent page, the IDE settings panel, GitManager — reads and writes through here.
    /// Saves are atomic (temp file + replace) and every save is written to the audit log (D9).
    /// </summary>
    public sealed class AgentPolicyStore
    {
        private static readonly JsonSerializerSettings Json = new()
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter() },
            ObjectCreationHandling = ObjectCreationHandling.Replace,   // don't append saved lists onto defaults
        };

        private readonly object _gate = new();
        private readonly string _path;
        private AgentPolicy _current;

        /// <summary>%LOCALAPPDATA%\SyncroDesktop\agent, or SYNCRO_AGENT_DATA_DIR when set (tests).</summary>
        public static string AgentDataDir { get; } =
            Environment.GetEnvironmentVariable("SYNCRO_AGENT_DATA_DIR") is { Length: > 0 } overrideDir
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncroDesktop", "agent");

        /// <summary>Raised after a successful save, with the new policy.</summary>
        public event Action<AgentPolicy>? Changed;

        /// <summary>Hook for the audit log (set by AgentAuditLog to avoid a constructor cycle).</summary>
        internal Action<string, string>? OnSaved;

        public AgentPolicyStore() : this(Path.Combine(AgentDataDir, "policy.json")) { }

        public AgentPolicyStore(string path)
        {
            _path = path;
            _current = Load();
        }

        public string FilePath => _path;

        /// <summary>A deep copy of the current policy — safe to edit in a form before saving.</summary>
        public AgentPolicy Snapshot()
        {
            lock (_gate) return Clone(_current);
        }

        /// <summary>The live policy for read-only use on hot paths (do not mutate).</summary>
        public AgentPolicy Current
        {
            get { lock (_gate) return _current; }
        }

        /// <summary>Replace the whole policy (from the settings page's Save button).</summary>
        public void Save(AgentPolicy policy, string reason = "settings saved")
        {
            AgentPolicy copy = Clone(policy);
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(copy, Json));
                File.Move(tmp, _path, overwrite: true);
                _current = copy;
            }
            OnSaved?.Invoke(reason, _path);
            Changed?.Invoke(copy);
        }

        /// <summary>Edit one part of the policy and save (for small programmatic changes).</summary>
        public void Update(Action<AgentPolicy> edit, string reason)
        {
            var p = Snapshot();
            edit(p);
            Save(p, reason);
        }

        public string ToJson(AgentPolicy p) => JsonConvert.SerializeObject(p, Json);

        private AgentPolicy Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var loaded = JsonConvert.DeserializeObject<AgentPolicy>(File.ReadAllText(_path), Json) ?? new AgentPolicy();
                    EnsureDefaults(loaded);
                    return loaded;
                }
            }
            catch (Exception ex)
            {
                // A corrupt file must not unlock the agent: keep the file for inspection, use safe defaults.
                System.Diagnostics.Debug.WriteLine($"[AgentPolicy] Failed to read {_path}: {ex.Message}. Using defaults.");
                try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch { }
            }
            return new AgentPolicy();
        }

        private static void EnsureDefaults(AgentPolicy p)
        {
            p.Conflicts ??= ConflictSettings.CreateDefaults();
            if (p.Conflicts.ConflictTypes.Count == 0) p.Conflicts.ConflictTypes = ConflictSettings.DefaultConflictTypes();
            if (p.Conflicts.Techniques.Count == 0) p.Conflicts.Techniques = ConflictSettings.DefaultTechniques();
            if (p.Conflicts.Gates.Count == 0) p.Conflicts.Gates = ConflictSettings.DefaultGates();
            if (p.Conflicts.Rules.Count == 0) p.Conflicts.Rules = ConflictSettings.DefaultRules();
            p.Bypass.Weights ??= new RiskFactorWeights();

            // Older defaults used tool names that don't exist. "ShellExec" never matched anything (the
            // shell tool is RunBatchScript, already governed by the shell allow/deny lists), so drop it;
            // "GitCherryPick" was meant for the real "cherry_pick" tool, so move its value there.
            var overrides = p.Bypass.ToolOverrides;
            foreach (var key in overrides.Keys.ToList())
            {
                if (string.Equals(key, "ShellExec", StringComparison.OrdinalIgnoreCase)) overrides.Remove(key);
                else if (string.Equals(key, "GitCherryPick", StringComparison.OrdinalIgnoreCase))
                {
                    var v = overrides[key];
                    overrides.Remove(key);
                    if (!overrides.Keys.Any(k => string.Equals(k, "cherry_pick", StringComparison.OrdinalIgnoreCase)))
                        overrides["cherry_pick"] = v;
                }
            }
        }

        private static AgentPolicy Clone(AgentPolicy p) =>
            JsonConvert.DeserializeObject<AgentPolicy>(JsonConvert.SerializeObject(p, Json), Json)!;
    }
}
