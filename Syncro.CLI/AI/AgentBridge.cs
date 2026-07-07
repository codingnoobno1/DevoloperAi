using System.IO.Pipes;

namespace Syncro.CLI;

/// <summary>
/// Named Pipe client — sends JSON status/progress/result packets
/// to the Syncro Desktop agent UI. Fails silently if Desktop is not running.
/// </summary>
public sealed class AgentBridge : IDisposable
{
    private const string PipeName = "syncro-agent-bridge";

    private NamedPipeClientStream? _pipe;
    private StreamWriter?          _writer;
    private bool                   _connected;

    public bool IsConnected => _connected;

    /// <summary>Attempts to connect to the Desktop Named Pipe server.</summary>
    public async Task<bool> TryConnectAsync(int timeoutMs = 1500)
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", PipeName,
                PipeDirection.Out, PipeOptions.Asynchronous);

            await _pipe.ConnectAsync(timeoutMs);
            _writer    = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };
            _connected = true;
            return true;
        }
        catch
        {
            _connected = false;
            return false;  // Desktop not running — CLI continues independently
        }
    }

    // ── Typed send helpers ───────────────────────────────────────────────────

    public Task SendStatusAsync(string message, string level = "info", string? taskId = null)
        => SendAsync(new { type = "status", level, message, taskId });

    public Task SendProgressAsync(int percent, int step, int total, string? taskId = null)
        => SendAsync(new { type = "progress", percent, step, total, taskId });

    public Task SendErrorAsync(string code, string message, string? file = null, int? line = null)
        => SendAsync(new { type = "error", code, message, file, line });

    public Task SendResultAsync(string resultType, object data, long durationMs = 0)
        => SendAsync(new { type = "result", resultType, data, durationMs });

    public Task SendHeartbeatAsync()
        => SendAsync(new { type = "heartbeat", pid = Environment.ProcessId, timestamp = DateTime.UtcNow });

    // ── Raw send ─────────────────────────────────────────────────────────────

    public async Task SendAsync(object packet)
    {
        if (!_connected || _writer == null) return;
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(packet);
            await _writer.WriteLineAsync(json);
        }
        catch
        {
            // Desktop disconnected — CLI keeps running
            _connected = false;
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
