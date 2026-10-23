using System;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Agent.Policy;
using Syncro.Desktop.Services.Universe.Runtime;

namespace Syncro.Desktop.Services.Agent.Platform
{
    /// <summary>
    /// Background worker that makes this PC a platform agent (platform-desktop-plan.md Phase 2):
    /// registers the device, heartbeats every 60 s, claims jobs every 30 s and runs them.
    /// Heartbeats keep going while a job waits for approval, so the platform keeps the job leased to
    /// this PC. Only "setup-task" jobs exist today; unknown types are reported as failed.
    /// </summary>
    public sealed class AgentJobWorker : IRuntimeWorker
    {
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ClaimEvery = TimeSpan.FromSeconds(30);

        private readonly PlatformAgentClient _client;
        private readonly DeviceIdentity _device;
        private readonly PlatformAgentStatus _status;
        private readonly AgentPolicyStore _store;
        private readonly SetupTaskJobHandler _setup;

        private string? _registeredToken;
        private string _deviceStatus = "idle";
        private string _deviceNote = "";
        private string _currentJobId = "";
        private DateTime _nextClaim = DateTime.MinValue;

        public AgentJobWorker(PlatformAgentClient client, DeviceIdentity device, PlatformAgentStatus status, AgentPolicyStore store, SetupTaskJobHandler setup)
        {
            _client = client; _device = device; _status = status; _store = store; _setup = setup;
        }

        public string Id => "platform-agent";
        public WorkerTier Tier => WorkerTier.Continuous;

        private string DeviceName => string.IsNullOrWhiteSpace(_store.Current.PlatformAgent.DeviceName)
            ? DeviceIdentity.DefaultName : _store.Current.PlatformAgent.DeviceName.Trim();

        /// <summary>Lets the settings page ask for an immediate claim instead of waiting up to 30 s.</summary>
        public void CheckNow() => _nextClaim = DateTime.MinValue;

        public async Task RunAsync(IEventBus bus, CancellationToken ct)
        {
            var heartbeat = Task.Run(() => HeartbeatLoopAsync(ct), ct);
            while (!ct.IsCancellationRequested)
            {
                try { await StepAsync(ct); }
                catch (Exception ex) { _status.Set("error", ex.Message); }
                try { await Task.Delay(Tick, ct); } catch (OperationCanceledException) { break; }
            }
            try { await heartbeat; } catch { }
        }

        private readonly SemaphoreSlim _registerGate = new(1, 1);

        private async Task<bool> EnsureRegisteredAsync(CancellationToken ct)
        {
            // Heartbeat and claim loops both call this; register once, not twice.
            await _registerGate.WaitAsync(ct);
            try { return await EnsureRegisteredCoreAsync(ct); }
            finally { _registerGate.Release(); }
        }

        private async Task<bool> EnsureRegisteredCoreAsync(CancellationToken ct)
        {
            var policy = _store.Current.PlatformAgent;
            if (!policy.Enabled) { _status.Set("disabled"); _registeredToken = null; return false; }
            if (!_client.SignedIn) { _status.Set("signed-out"); _registeredToken = null; return false; }
            if (_registeredToken != null && _registeredToken == _client.SessionKey) return true;

            var r = await _client.RegisterAsync(new
            {
                deviceId = _device.DeviceId,
                name = DeviceName,
                os = Environment.OSVersion.VersionString,
                appVersion = typeof(AgentJobWorker).Assembly.GetName().Version?.ToString() ?? "",
                workspaceRoot = SetupTaskJobHandler.WorkspaceRoot(_store.Current),
                enabled = true,
            }, ct);
            if (!r.Ok) { _status.Set("error", "register: " + r.Error); return false; }
            _registeredToken = _client.SessionKey;
            _status.Registered();
            _status.Add($"Registered as '{DeviceName}' for {_client.UserName}");
            return true;
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (await EnsureRegisteredAsync(ct))
                    {
                        var r = await _client.HeartbeatAsync(new { deviceId = _device.DeviceId, status = _deviceStatus, statusNote = _deviceNote, currentJobId = _currentJobId }, ct);
                        if (r.NeedsRegister) _registeredToken = null;
                        else if (r.Ok) _status.Beat((int?)r.Body?["queued"] ?? 0);
                        else _status.Set("error", "heartbeat: " + r.Error);
                    }
                }
                catch (Exception ex) { _status.Set("error", "heartbeat: " + ex.Message); }
                try { await Task.Delay(HeartbeatEvery, ct); } catch (OperationCanceledException) { break; }
            }
        }

        private async Task StepAsync(CancellationToken ct)
        {
            if (DateTime.Now < _nextClaim) return;
            _nextClaim = DateTime.Now + ClaimEvery;
            if (!await EnsureRegisteredAsync(ct)) return;

            var (call, job) = await _client.ClaimAsync(_device.DeviceId, ct);
            if (call.NeedsRegister) { _registeredToken = null; return; }
            if (!call.Ok) { _status.Set("error", "claim: " + call.Error); return; }
            if (job == null) return;

            await RunJobAsync(job, ct);
            _nextClaim = DateTime.MinValue;   // look for the next one right away
        }

        private async Task RunJobAsync(PlatformJob job, CancellationToken ct)
        {
            var title = (string?)job.Payload["title"] ?? job.Type;
            _currentJobId = job.Id;
            _status.Job($"{job.Type}: {title}");
            _status.Add($"Claimed {job.Type} '{title}' (attempt {job.Attempts})");

            try
            {
                if (job.Type != "setup-task")
                {
                    await Report(job, "failed", $"This version of Syncro Desktop can't run '{job.Type}' jobs.", null, ct);
                    return;
                }

                var preview = _setup.Preview(job);
                if (preview.Decision is Decision.Ask or Decision.AskDoubleConfirm)
                {
                    SetDevice("waiting-approval", $"approve '{title}' in Syncro");
                    await Report(job, "running", $"Waiting for approval on {DeviceName}", null, ct);
                }
                else
                {
                    SetDevice("busy", title);
                    await Report(job, "running", $"Setting up on {DeviceName}", null, ct);
                }

                SetupOutcome outcome;
                try { outcome = await _setup.RunAsync(job, DeviceName, ct); }
                catch (Exception ex) { outcome = new SetupOutcome(false, ex.Message, null); }

                await Report(job, outcome.Ok ? "done" : "failed", outcome.Message, outcome.Result, ct);
                _status.Add(outcome.Ok ? $"Set up '{title}': {outcome.Message}" : $"Failed '{title}': {outcome.Message}");
            }
            finally
            {
                _currentJobId = "";
                SetDevice("idle", "");
                _status.Job(null);
            }
        }

        private void SetDevice(string status, string note) { _deviceStatus = status; _deviceNote = note; }

        private async Task Report(PlatformJob job, string state, string note, JObject? result, CancellationToken ct)
        {
            var r = await _client.ReportAsync(job.Id, new
            {
                deviceId = _device.DeviceId,
                state,
                note,
                result,
                error = state == "failed" ? note : null,
            }, ct);
            if (!r.Ok) _status.Add($"Report '{state}' failed: {r.Error}");
        }
    }
}
