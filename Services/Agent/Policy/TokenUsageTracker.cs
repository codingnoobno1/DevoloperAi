using System;
using System.IO;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// Counts LLM tokens used today (the "42K used today" bar in Model &amp; Provider). Persisted so a
    /// restart doesn't reset the daily budget. Resets at local midnight.
    /// </summary>
    public sealed class TokenUsageTracker
    {
        private readonly object _gate = new();
        private readonly string _path = Path.Combine(AgentPolicyStore.AgentDataDir, "usage.json");
        private DateOnly _day;
        private long _tokens;

        public event Action? Changed;

        public TokenUsageTracker()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var j = JObject.Parse(File.ReadAllText(_path));
                    if (DateOnly.TryParse((string?)j["day"], out var d) && d == Today)
                    {
                        _day = d;
                        _tokens = (long?)j["tokens"] ?? 0;
                    }
                }
            }
            catch { /* start from zero */ }
            if (_day != Today) { _day = Today; _tokens = 0; }
        }

        private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

        public long UsedToday
        {
            get { lock (_gate) { Roll(); return _tokens; } }
        }

        public void Add(long tokens)
        {
            if (tokens <= 0) return;
            lock (_gate)
            {
                Roll();
                _tokens += tokens;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, new JObject { ["day"] = _day.ToString("yyyy-MM-dd"), ["tokens"] = _tokens }.ToString());
                }
                catch { }
            }
            Changed?.Invoke();
        }

        private void Roll()
        {
            if (_day != Today) { _day = Today; _tokens = 0; }
        }
    }
}
