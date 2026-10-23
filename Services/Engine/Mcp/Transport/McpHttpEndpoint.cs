using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using STJ = System.Text.Json.JsonSerializer;   // the app project also global-usings Newtonsoft

namespace Syncro.Desktop.Services.Engine.Mcp.Transport
{
    /// <summary>
    /// Localhost HTTP endpoint that exposes the Syncro engine to the code-oss extension host
    /// (extension-system.md, lanes A+B).
    ///
    /// E0: <c>GET /health</c> — reachability probe for the <c>syncro-bridge</c> extension.
    /// E1: <c>POST /mcp</c> — a Model Context Protocol server (streamable-HTTP, JSON-RPC 2.0)
    /// over <see cref="McpOrchestrator"/>: <c>initialize</c>, <c>tools/list</c>, <c>tools/call</c>,
    /// <c>ping</c>. Every registered <see cref="IMcpTool"/> is surfaced; no per-tool porting.
    ///
    /// Security posture: binds 127.0.0.1 only. <c>/mcp</c> requires the per-session bearer token
    /// (injected into the code-oss server env as SYNCRO_BRIDGE_TOKEN and echoed back by the
    /// extension in the definition's headers). <c>/health</c> is token-free (no data; doubles as
    /// the reachability probe). Tools with <see cref="IMcpTool.RequiresAdminApproval"/> are refused
    /// here until Syncro grows an in-app approval flow — the editor never gets a bypass.
    /// </summary>
    public sealed class McpHttpEndpoint : IDisposable
    {
        private const string ProtocolVersion = "2025-03-26";

        private readonly object _gate = new();
        private readonly McpOrchestrator? _orchestrator;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;

        public McpHttpEndpoint(McpOrchestrator? orchestrator = null)
        {
            _orchestrator = orchestrator;
        }

        /// <summary>Bound port once started; 0 before.</summary>
        public int Port { get; private set; }

        /// <summary>Per-session bearer token (minted at construction).</summary>
        public string Token { get; } = Guid.NewGuid().ToString("N");

        public bool IsRunning => _listener is { IsListening: true };

        /// <summary>Start (or reuse) the listener. Scans a small port range so a stale process
        /// can't permanently block the bridge. Never throws — returns false when no port bound.</summary>
        public bool EnsureStarted()
        {
            lock (_gate)
            {
                if (IsRunning) return true;

                for (int port = 9877; port <= 9897; port++)
                {
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    try
                    {
                        listener.Start();
                        _listener = listener;
                        Port = port;
                        _cts = new CancellationTokenSource();
                        _ = Task.Run(() => AcceptLoopAsync(listener, _cts.Token));
                        return true;
                    }
                    catch (HttpListenerException)
                    {
                        try { listener.Close(); } catch { }
                    }
                }
                return false;
            }
        }

        private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }

                _ = Task.Run(() => HandleAsync(ctx), ct);
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var path = (ctx.Request.Url?.AbsolutePath ?? "").TrimEnd('/');

                if (ctx.Request.HttpMethod == "GET" && path.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonAsync(ctx.Response, 200,
                        $"{{\"app\":\"Syncro.Desktop\",\"status\":\"ok\",\"bridge\":\"e1\",\"tools\":{_orchestrator?.Tools.Count ?? 0}}}");
                    return;
                }

                if (path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsAuthorized(ctx.Request))
                    {
                        await WriteJsonAsync(ctx.Response, 401, "{\"error\":\"missing or invalid bearer token\"}");
                        return;
                    }
                    if (ctx.Request.HttpMethod != "POST")
                    {
                        // No standalone SSE stream — streamable-HTTP with plain JSON responses.
                        await WriteJsonAsync(ctx.Response, 405, "{\"error\":\"POST only\"}");
                        return;
                    }

                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        body = await reader.ReadToEndAsync();

                    var (status, json) = await HandleJsonRpcAsync(body);
                    if (json == null)
                    {
                        ctx.Response.StatusCode = status;   // notification → 202, no body
                        ctx.Response.Close();
                    }
                    else
                    {
                        await WriteJsonAsync(ctx.Response, status, json);
                    }
                    return;
                }

                await WriteJsonAsync(ctx.Response, 404, "{\"error\":\"unknown endpoint\"}");
            }
            catch { /* connection dropped — nothing to do */ }
        }

        private bool IsAuthorized(HttpListenerRequest req)
        {
            var auth = req.Headers["Authorization"];
            return auth != null && auth.Equals($"Bearer {Token}", StringComparison.Ordinal);
        }

        // ── MCP JSON-RPC ──────────────────────────────────────────────────────────
        private async Task<(int status, string? json)> HandleJsonRpcAsync(string body)
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(body); }
            catch { return (400, RpcError(null, -32700, "parse error")); }

            using (doc)
            {
                var root = doc.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
                var hasId = root.TryGetProperty("id", out var idEl);
                var id = hasId ? idEl.Clone() : default;

                // Notifications (no id) → acknowledge with 202, no body.
                if (!hasId)
                    return (202, null);

                switch (method)
                {
                    case "initialize":
                        return (200, RpcResult(id,
                            $"{{\"protocolVersion\":\"{ProtocolVersion}\"," +
                            "\"capabilities\":{\"tools\":{}}," +
                            "\"serverInfo\":{\"name\":\"syncro-engine\",\"version\":\"1.0.0\"}}"));

                    case "ping":
                        return (200, RpcResult(id, "{}"));

                    case "tools/list":
                        return (200, RpcResult(id, BuildToolsList()));

                    case "tools/call":
                        return (200, await BuildToolCallAsync(id, root));

                    default:
                        return (200, RpcError(id, -32601, $"method not found: {method}"));
                }
            }
        }

        private string BuildToolsList()
        {
            var sb = new StringBuilder("{\"tools\":[");
            bool first = true;
            foreach (var tool in _orchestrator?.Tools ?? System.Array.Empty<IMcpTool>())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":").Append(STJ.Serialize(tool.Name))
                  .Append(",\"description\":").Append(STJ.Serialize(DescribeTool(tool)))
                  .Append(",\"inputSchema\":").Append(ToJsonSchema(tool.InputSchema))
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private async Task<string> BuildToolCallAsync(JsonElement id, JsonElement root)
        {
            string name = "";
            string argsJson = "{}";
            if (root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                if (p.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    name = n.GetString() ?? "";
                if (p.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                    argsJson = a.GetRawText();
            }

            if (_orchestrator == null || !_orchestrator.TryGetTool(name, out var tool))
                return RpcResult(id, ToolText($"Error: tool '{name}' is not registered in the Syncro engine.", isError: true));

            // Approval gate: write/exec tools stay locked until Syncro grows an in-app approval flow.
            if (tool.RequiresAdminApproval)
                return RpcResult(id, ToolText(
                    $"Tool '{name}' modifies state and requires approval in Syncro Desktop. " +
                    "It is not callable from the editor yet.", isError: true));

            try
            {
                // Even tools without the approval flag (WriteFile, DeleteFile, ApplyPatch…) go through
                // Agent Settings; requests from the editor never run without the user in the loop.
                var result = await _orchestrator.ExecuteToolAsync(name, argsJson, "editor");
                var isError = result.StartsWith("{\"error\"", StringComparison.OrdinalIgnoreCase)
                              || result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase);
                return RpcResult(id, ToolText(result, isError));
            }
            catch (Exception ex)
            {
                return RpcResult(id, ToolText($"Error: {ex.Message}", isError: true));
            }
        }

        private static string DescribeTool(IMcpTool tool) =>
            tool.RequiresAdminApproval ? $"{tool.Description} (requires approval in Syncro Desktop)" : tool.Description;

        /// <summary>
        /// Syncro tools declare loose schemas like <c>{ "path": "string" }</c>. MCP requires JSON
        /// Schema — wrap: known-shape objects become <c>{type:object, properties:{…}}</c> with
        /// primitive-name values mapped to types; anything unparseable becomes a permissive object.
        /// </summary>
        internal static string ToJsonSchema(string declared)
        {
            try
            {
                using var doc = JsonDocument.Parse(declared);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return "{\"type\":\"object\"}";

                // Already a JSON Schema? Pass through.
                if (root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    return root.GetRawText();

                var sb = new StringBuilder("{\"type\":\"object\",\"properties\":{");
                bool first = true;
                foreach (var prop in root.EnumerateObject())
                {
                    if (!first) sb.Append(',');
                    first = false;
                    var hint = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
                    var type = hint.Contains("number", StringComparison.OrdinalIgnoreCase) || hint.Contains("int", StringComparison.OrdinalIgnoreCase) ? "number"
                             : hint.Contains("bool", StringComparison.OrdinalIgnoreCase) ? "boolean"
                             : hint.Contains("array", StringComparison.OrdinalIgnoreCase) || hint.Contains("[]", StringComparison.Ordinal) ? "array"
                             : "string";
                    sb.Append(STJ.Serialize(prop.Name))
                      .Append(":{\"type\":\"").Append(type).Append("\"");
                    if (hint.Length > 0 && hint != type)
                        sb.Append(",\"description\":").Append(STJ.Serialize(hint));
                    sb.Append('}');
                }
                sb.Append("}}");
                return sb.ToString();
            }
            catch
            {
                return "{\"type\":\"object\"}";
            }
        }

        private static string ToolText(string text, bool isError) =>
            "{\"content\":[{\"type\":\"text\",\"text\":" + STJ.Serialize(text) + "}]" +
            (isError ? ",\"isError\":true}" : "}");

        private static string RpcResult(JsonElement id, string resultJson) =>
            "{\"jsonrpc\":\"2.0\",\"id\":" + id.GetRawText() + ",\"result\":" + resultJson + "}";

        private static string RpcError(JsonElement? id, int code, string message) =>
            "{\"jsonrpc\":\"2.0\",\"id\":" + (id?.GetRawText() ?? "null") +
            ",\"error\":{\"code\":" + code + ",\"message\":" + STJ.Serialize(message) + "}}";

        private static async Task WriteJsonAsync(HttpListenerResponse res, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            res.StatusCode = status;
            res.ContentType = "application/json";
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.OutputStream.Close();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                try { _cts?.Cancel(); } catch { }
                try { _listener?.Stop(); _listener?.Close(); } catch { }
                _listener = null;
            }
        }
    }
}
