using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Agent.Policy;
using Syncro.Desktop.Services.Auth;

namespace Syncro.Desktop.Services.Agent.Platform
{
    /// <summary>Stable id for this install, created once in %LOCALAPPDATA%\SyncroDesktop\agent\device.json.</summary>
    public sealed class DeviceIdentity
    {
        public string DeviceId { get; }

        public DeviceIdentity()
        {
            var path = Path.Combine(AgentPolicyStore.AgentDataDir, "device.json");
            try
            {
                if (File.Exists(path))
                {
                    var id = (string?)JObject.Parse(File.ReadAllText(path))["deviceId"];
                    if (!string.IsNullOrWhiteSpace(id)) { DeviceId = id!; return; }
                }
            }
            catch { /* regenerate below */ }

            DeviceId = "pc-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, new JObject { ["deviceId"] = DeviceId, ["createdAt"] = DateTime.UtcNow }.ToString());
            }
            catch { }
        }

        public static string DefaultName => Environment.MachineName;
    }

    public sealed class PlatformJob
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public JObject Payload { get; set; } = new();
        public int Attempts { get; set; }
    }

    public sealed record PlatformCall(bool Ok, int Status, string Error, JObject? Body)
    {
        public bool NeedsRegister => Status == 404 && Body?["register"]?.Value<bool>() == true;
    }

    /// <summary>
    /// HTTP client for the platform's agent endpoints (/api/mobile/agents/*), authenticated with the
    /// member's mobile JWT from <see cref="AuthService"/>. Never throws; failures come back as data.
    /// </summary>
    public sealed class PlatformAgentClient
    {
        public const string BaseUrl = "https://project-syncroo.netlify.app";
        private static readonly HttpClient Http = CreateHttp();
        private readonly AuthService _auth;

        public PlatformAgentClient(AuthService auth) => _auth = auth;

        public bool SignedIn => !string.IsNullOrEmpty(_auth.Token);
        public string? UserName => _auth.CurrentUserName;
        /// <summary>Changes whenever a different member signs in (or the token is refreshed), so the worker re-registers.</summary>
        public string? SessionKey => string.IsNullOrEmpty(_auth.Token) ? null : $"{_auth.CurrentUserId}:{_auth.Token.GetHashCode()}";

        /// <summary>Override for tests / a local dev server (env SYNCRO_PLATFORM_URL).</summary>
        public static string Root => Environment.GetEnvironmentVariable("SYNCRO_PLATFORM_URL")?.TrimEnd('/') ?? BaseUrl;

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Syncro.Desktop-Agent/1.0");
            return h;
        }

        public Task<PlatformCall> RegisterAsync(object body, CancellationToken ct) => SendAsync(HttpMethod.Post, "/api/mobile/agents/register", body, ct);
        public Task<PlatformCall> HeartbeatAsync(object body, CancellationToken ct) => SendAsync(HttpMethod.Post, "/api/mobile/agents/heartbeat", body, ct);
        public Task<PlatformCall> ReportAsync(string jobId, object body, CancellationToken ct) => SendAsync(HttpMethod.Post, $"/api/mobile/agents/jobs/{Uri.EscapeDataString(jobId)}/report", body, ct);

        public async Task<(PlatformCall Call, PlatformJob? Job)> ClaimAsync(string deviceId, CancellationToken ct)
        {
            var call = await SendAsync(HttpMethod.Get, $"/api/mobile/agents/jobs?claim=1&deviceId={Uri.EscapeDataString(deviceId)}", null, ct);
            if (!call.Ok || call.Body?["job"] is not JObject j) return (call, null);
            return (call, new PlatformJob
            {
                Id = (string?)j["id"] ?? "",
                Type = (string?)j["type"] ?? "",
                ProjectId = (string?)j["projectId"] ?? "",
                TaskId = (string?)j["taskId"] ?? "",
                Payload = j["payload"] as JObject ?? new JObject(),
                Attempts = (int?)j["attempts"] ?? 0,
            });
        }

        private async Task<PlatformCall> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
        {
            var token = _auth.Token;
            if (string.IsNullOrEmpty(token)) return new(false, 401, "not signed in", null);
            try
            {
                using var req = new HttpRequestMessage(method, Root + path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body != null) req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                using var res = await Http.SendAsync(req, ct);
                var text = await res.Content.ReadAsStringAsync(ct);
                JObject? json = null;
                try { json = string.IsNullOrWhiteSpace(text) ? null : JObject.Parse(text); } catch { }
                var err = res.IsSuccessStatusCode ? "" : ((string?)json?["error"] ?? (string?)json?["message"] ?? $"HTTP {(int)res.StatusCode}");
                return new(res.IsSuccessStatusCode, (int)res.StatusCode, err, json);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new(false, 0, "cancelled", null); }
            catch (Exception ex) { return new(false, 0, "network: " + ex.Message, null); }
        }
    }

    /// <summary>What the Sync &amp; Pull card shows about this PC as an agent.</summary>
    public sealed class PlatformAgentStatus
    {
        private readonly object _gate = new();
        private readonly LinkedList<string> _log = new();

        public string State { get; private set; } = "starting";   // starting | disabled | signed-out | online | error
        public string? LastError { get; private set; }
        public DateTime? RegisteredAt { get; private set; }
        public DateTime? LastHeartbeatAt { get; private set; }
        public string? CurrentJob { get; private set; }
        public int QueuedOnPlatform { get; private set; }

        public event Action? Changed;

        public IReadOnlyList<string> Log { get { lock (_gate) return new List<string>(_log); } }

        internal void Set(string state, string? error = null)
        {
            State = state;
            if (error != null) LastError = error;
            Changed?.Invoke();
        }

        internal void Registered() { RegisteredAt = DateTime.Now; LastError = null; State = "online"; Changed?.Invoke(); }
        internal void Beat(int queued) { LastHeartbeatAt = DateTime.Now; QueuedOnPlatform = queued; if (State != "online") State = "online"; Changed?.Invoke(); }
        internal void Job(string? text) { CurrentJob = text; Changed?.Invoke(); }

        internal void Add(string line)
        {
            lock (_gate)
            {
                _log.AddFirst($"{DateTime.Now:HH:mm:ss}  {line}");
                while (_log.Count > 30) _log.RemoveLast();
            }
            Changed?.Invoke();
        }
    }
}
