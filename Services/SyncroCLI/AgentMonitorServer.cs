using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AgentCli.Status;

namespace Syncro.Desktop.Services.SyncroCLI
{
    public class AgentMonitorServer : IDisposable
    {
        private const int Port = 3030;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;
        private readonly AgentOrchestrator _orchestrator;
        private readonly AgentBridgeServer _bridgeServer;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(1) };
        private readonly List<string> _logRingBuffer = new();
        private readonly object _logLock = new();
        private readonly DbStatusService _dbStatus;
        private readonly ProjectService _projectService;
        private bool _llmOnlineCached;
        private DateTime _llmCheckedAt;
        private static readonly TimeSpan LlmCacheTtl = TimeSpan.FromSeconds(5);

        public AgentMonitorServer(AgentOrchestrator orchestrator, AgentBridgeServer bridgeServer, DbStatusService dbStatus, ProjectService projectService)
        {
            _orchestrator = orchestrator;
            _bridgeServer = bridgeServer;
            _dbStatus = dbStatus;
            _projectService = projectService;

            _orchestrator.OnLogUpdated += AddLog;
            _bridgeServer.OnRawMessageReceived += msg => AddLog($"[IPC Raw] {msg}");

            // Register all project roots on start
            foreach (var p in _projectService.GetProjects())
            {
                _dbStatus.RegisterProjectRoot(p.Path);
            }
        }

        private void AddLog(string log)
        {
            lock (_logLock)
            {
                _logRingBuffer.Add($"[{DateTime.Now:HH:mm:ss}] {log}");
                if (_logRingBuffer.Count > 100)
                {
                    _logRingBuffer.RemoveAt(0);
                }
            }
        }

        public void Start()
        {
            if (_serverTask != null) return;

            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

            try
            {
                _listener.Start();
                _serverTask = Task.Run(() => ListenLoopAsync(_cts.Token));
                AddLog($"[Monitor Server] Started on http://localhost:{Port}");
            }
            catch (Exception ex)
            {
                AddLog($"[Monitor Server Error] Failed to start HTTP listener: {ex.Message}");
            }
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AddLog($"[Monitor Server Error] Request accept error: {ex.Message}");
                    await Task.Delay(500, token);
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            var req = context.Request;
            var resp = context.Response;

            try
            {
                string path = req.Url?.AbsolutePath ?? "/";
                if (path == "/api/status")
                {
                    resp.ContentType = "application/json";
                    resp.Headers.Add("Access-Control-Allow-Origin", "*");

                    // Dynamically refresh project roots
                    foreach (var p in _projectService.GetProjects())
                    {
                        _dbStatus.RegisterProjectRoot(p.Path);
                    }

                    bool llmOnline = await CheckLlmCachedAsync();

                    List<string> logsCopy;
                    lock (_logLock)
                    {
                        logsCopy = _logRingBuffer.ToList();
                    }

                    var sessionsCopy = new List<object>();
                    lock (_orchestrator.ActiveSessions)
                    {
                        sessionsCopy = _orchestrator.ActiveSessions.Select(s => (object)new {
                            id = s.Id,
                            agentName = s.AgentName,
                            projectId = s.ProjectId,
                            status = s.Status,
                            currentTask = s.CurrentTask
                        }).ToList();
                    }

                    var actionsCopy = new List<object>();
                    lock (_orchestrator.LoggedActions)
                    {
                        actionsCopy = _orchestrator.LoggedActions.Select(a => (object)new {
                            id = a.Id,
                            sessionId = a.SessionId,
                            actionType = a.ActionType,
                            input = a.Input,
                            output = a.Output,
                            success = a.Success
                        }).ToList();
                    }

                    var requestsCopy = new List<object>();
                    lock (_orchestrator.LoggedLlmRequests)
                    {
                        requestsCopy = _orchestrator.LoggedLlmRequests.Select(r => (object)new {
                            id = r.Id,
                            prompt = r.Prompt,
                            contextSource = r.ContextSource,
                            status = r.Status,
                            result = r.Result
                        }).ToList();
                    }

                    var statusData = new
                    {
                        pipeServer = "Active",
                        llmServer = llmOnline ? "Online" : "Offline",
                        sessionsCount = sessionsCopy.Count,
                        sessions = sessionsCopy,
                        logs = logsCopy,
                        totalTokens = _orchestrator.TotalTokensTracked,
                        actions = actionsCopy,
                        llmRequests = requestsCopy,
                        db = await _dbStatus.GetSnapshotAsync()
                    };

                    byte[] buffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(statusData));
                    resp.ContentLength64 = buffer.Length;
                    await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else if (path == "/api/db")
                {
                    resp.ContentType = "application/json";
                    resp.Headers.Add("Access-Control-Allow-Origin", "*");
                    var snapshot = await _dbStatus.GetSnapshotAsync();
                    byte[] dbBuffer = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot));
                    resp.ContentLength64 = dbBuffer.Length;
                    await resp.OutputStream.WriteAsync(dbBuffer, 0, dbBuffer.Length);
                }
                else if (path == "/ipc")
                {
                    resp.ContentType = "text/html";
                    byte[] buffer = Encoding.UTF8.GetBytes(GetIpcFullScreenHtml());
                    resp.ContentLength64 = buffer.Length;
                    await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else
                {
                    resp.ContentType = "text/html";
                    byte[] buffer = Encoding.UTF8.GetBytes(GetDashboardHtml());
                    resp.ContentLength64 = buffer.Length;
                    await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] buffer = Encoding.UTF8.GetBytes($"Server Error: {ex.Message}");
                resp.ContentLength64 = buffer.Length;
                await resp.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            finally
            {
                resp.Close();
            }
        }

        private async Task<bool> CheckLlmCachedAsync()
        {
            if (DateTime.UtcNow - _llmCheckedAt < LlmCacheTtl) return _llmOnlineCached;
            bool online = false;
            try
            {
                var r = await _http.GetAsync("http://localhost:3020/");
                online = r.IsSuccessStatusCode;
            }
            catch { }
            _llmOnlineCached = online;
            _llmCheckedAt = DateTime.UtcNow;
            return online;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_listener != null)
            {
                try { _listener.Stop(); } catch {}
                try { _listener.Close(); } catch {}
                _listener = null;
            }

            _serverTask = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private string GetDashboardHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Syncro AI Diagnostics Monitor</title>
    <link href=""https://fonts.googleapis.com/css2?family=Inter:wght@300;400;500;600;700&family=Orbitron:wght@500;700;900&family=JetBrains+Mono:wght@400;500&display=swap"" rel=""stylesheet"">
    <link href=""https://fonts.googleapis.com/icon?family=Material+Icons"" rel=""stylesheet"">
    <style>
        :root {
            --bg-dark: #06090f;
            --glass-bg: rgba(10, 16, 32, 0.7);
            --glass-border: rgba(255, 255, 255, 0.08);
            --accent-cyan: #00f2fe;
            --accent-purple: #7f00ff;
            --accent-pink: #ff007f;
            --text-main: #f8fafc;
            --text-muted: #64748b;
            --success: #10b981;
            --warning: #f59e0b;
            --danger: #ef4444;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            background-color: var(--bg-dark);
            color: var(--text-main);
            font-family: 'Inter', sans-serif;
            min-height: 100vh;
            display: flex;
            flex-direction: column;
            overflow-x: hidden;
            background-image: radial-gradient(circle at 10% 20%, rgba(127, 0, 255, 0.06) 0%, transparent 40%),
                              radial-gradient(circle at 90% 80%, rgba(0, 242, 254, 0.06) 0%, transparent 40%),
                              radial-gradient(circle at 50% 50%, rgba(255, 0, 127, 0.02) 0%, transparent 60%);
        }
        header {
            padding: 20px 40px;
            border-bottom: 1px solid var(--glass-border);
            display: flex;
            justify-content: space-between;
            align-items: center;
            backdrop-filter: blur(16px);
            background: rgba(6, 9, 15, 0.85);
            position: sticky;
            top: 0;
            z-index: 10;
        }
        .logo-container { display: flex; align-items: center; gap: 12px; }
        .logo {
            font-family: 'Orbitron', sans-serif;
            font-weight: 900;
            font-size: 24px;
            background: linear-gradient(135deg, var(--accent-cyan), var(--accent-purple), var(--accent-pink));
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            letter-spacing: 2px;
        }
        .subtitle { font-size: 11px; text-transform: uppercase; letter-spacing: 2px; color: var(--text-muted); margin-top: 3px; }
        .live-badge {
            background: rgba(16, 185, 129, 0.1);
            border: 1px solid rgba(16, 185, 129, 0.3);
            color: #34d399;
            padding: 6px 14px;
            border-radius: 20px;
            font-size: 11px;
            font-weight: 600;
            display: flex;
            align-items: center;
            gap: 8px;
        }
        .live-pulse {
            width: 8px; height: 8px; background-color: var(--success); border-radius: 50%; animation: pulse 1.8s infinite;
        }
        @keyframes pulse {
            0% { transform: scale(0.9); box-shadow: 0 0 0 0 rgba(16, 185, 129, 0.7); }
            70% { transform: scale(1.1); box-shadow: 0 0 0 8px rgba(16, 185, 129, 0); }
            100% { transform: scale(0.9); box-shadow: 0 0 0 0 rgba(16, 185, 129, 0); }
        }
        .tabs-navigation {
            display: flex;
            justify-content: center;
            gap: 15px;
            margin: 25px 40px;
            border-bottom: 1px solid var(--glass-border);
            padding-bottom: 15px;
        }
        .tab-nav-btn {
            background: rgba(255, 255, 255, 0.02);
            border: 1px solid var(--glass-border);
            color: var(--text-muted);
            padding: 12px 24px;
            border-radius: 12px;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 10px;
            font-family: 'Orbitron', sans-serif;
            font-weight: 700;
            font-size: 12px;
            letter-spacing: 1px;
            transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
        }
        .tab-nav-btn:hover { background: rgba(255, 255, 255, 0.08); border-color: rgba(255, 255, 255, 0.15); color: white; }
        .tab-nav-btn.active {
            background: linear-gradient(135deg, rgba(0, 242, 254, 0.15) 0%, rgba(127, 0, 255, 0.15) 100%);
            border-color: var(--accent-cyan);
            color: var(--accent-cyan);
            box-shadow: 0 0 15px rgba(0, 242, 254, 0.2);
        }
        .tab-panel { display: none; width: 100%; }
        .tab-panel.active { display: flex; flex-direction: column; gap: 30px; }
        main {
            flex: 1; max-width: 1400px; width: 100%; margin: 0 auto; padding: 0 30px 40px 30px; display: flex; flex-direction: column;
        }
        .card {
            background: var(--glass-bg);
            border: 1px solid var(--glass-border);
            border-radius: 20px;
            backdrop-filter: blur(24px);
            padding: 28px;
            box-shadow: 0 12px 40px 0 rgba(0, 0, 0, 0.4);
            display: flex; flex-direction: column; transition: border-color 0.3s ease;
        }
        .card:hover { border-color: rgba(255, 255, 255, 0.12); }
        .card-title {
            font-family: 'Orbitron', sans-serif;
            font-size: 14px;
            font-weight: 700;
            letter-spacing: 1.5px;
            margin-bottom: 22px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            color: #fff;
            border-bottom: 1px solid var(--glass-border);
            padding-bottom: 12px;
            text-transform: uppercase;
        }
        .status-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; margin-bottom: 24px; }
        .status-box {
            background: rgba(255, 255, 255, 0.02);
            border: 1px solid var(--glass-border);
            border-radius: 14px;
            padding: 18px;
            display: flex; flex-direction: column; gap: 8px;
        }
        .status-label { font-size: 11px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 1.5px; }
        .status-value { font-family: 'Orbitron', sans-serif; font-size: 18px; font-weight: 700; display: flex; align-items: center; gap: 10px; }
        .dot { width: 9px; height: 9px; border-radius: 50%; display: inline-block; }
        .dot.online { background: var(--success); box-shadow: 0 0 12px var(--success); }
        .dot.offline { background: var(--danger); box-shadow: 0 0 12px var(--danger); }
        .dot.active { background: #a855f7; box-shadow: 0 0 12px #a855f7; }
        .terminal {
            background: rgba(4, 6, 12, 0.95);
            border: 1px solid var(--glass-border);
            border-radius: 14px;
            font-family: 'JetBrains Mono', monospace;
            font-size: 12.5px;
            line-height: 1.6;
            padding: 18px;
            height: 300px;
            overflow-y: auto;
            color: #cbd5e1;
            box-shadow: inset 0 4px 20px rgba(0, 0, 0, 0.6);
        }
        .terminal::-webkit-scrollbar { width: 6px; }
        .terminal::-webkit-scrollbar-thumb { background: rgba(255, 255, 255, 0.1); border-radius: 3px; }
        .terminal-line { margin-bottom: 6px; word-break: break-all; white-space: pre-wrap; }
        .terminal-line.ipc-err { color: #f87171; }
        .terminal-line.ipc-raw { color: #c084fc; }
        .terminal-line.monitor { color: #38bdf8; }
        .session-list, .action-list { display: flex; flex-direction: column; gap: 12px; overflow-y: auto; max-height: 400px; padding-right: 4px; }
        .session-list::-webkit-scrollbar, .action-list::-webkit-scrollbar { width: 5px; }
        .session-list::-webkit-scrollbar-thumb, .action-list::-webkit-scrollbar-thumb { background: rgba(255, 255, 255, 0.08); border-radius: 3px; }
        .session-item, .action-item {
            background: rgba(255, 255, 255, 0.015);
            border: 1px solid var(--glass-border);
            border-radius: 12px;
            padding: 14px 18px;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        .session-meta, .action-meta { display: flex; flex-direction: column; gap: 4px; flex: 1; }
        .session-name { font-weight: 600; font-size: 14px; color: #fff; }
        .session-id, .action-id { font-family: 'JetBrains Mono', monospace; font-size: 10px; color: var(--text-muted); }
        .session-task { font-size: 12px; color: var(--accent-cyan); font-weight: 500; }
        .action-title { font-weight: 600; font-size: 13.5px; color: #fff; display: flex; align-items: center; gap: 8px; }
        .action-io { font-size: 11.5px; color: var(--text-muted); word-break: break-all; margin-top: 2px; }
        .badge { font-size: 10px; padding: 3px 8px; border-radius: 6px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; }
        .badge.success { background: rgba(16, 185, 129, 0.15); color: #34d399; border: 1px solid rgba(16, 185, 129, 0.25); }
        .badge.fail { background: rgba(239, 68, 68, 0.15); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.25); }
        .badge.type { background: rgba(127, 0, 255, 0.15); color: #c084fc; border: 1px solid rgba(127, 0, 255, 0.25); }
        .empty-state { text-align: center; color: var(--text-muted); font-size: 13px; padding: 30px; border: 1px dashed var(--glass-border); border-radius: 12px; }
        .llm-card { background: rgba(255, 255, 255, 0.01); border: 1px solid var(--glass-border); border-radius: 14px; padding: 20px; display: flex; flex-direction: column; gap: 12px; }
        .llm-header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid rgba(255, 255, 255, 0.05); padding-bottom: 8px; }
        .llm-label { font-family: 'Orbitron', sans-serif; font-size: 12px; font-weight: 600; color: var(--accent-cyan); }
        .llm-payload-container { display: grid; grid-template-columns: 1fr 1fr; gap: 15px; }
        .payload-box {
            background: rgba(4, 6, 12, 0.8);
            border: 1px solid var(--glass-border);
            border-radius: 8px;
            padding: 14px;
            font-family: 'JetBrains Mono', monospace;
            font-size: 11.5px;
            max-height: 250px;
            overflow-y: auto;
        }
        .payload-box::-webkit-scrollbar { width: 4px; }
        .payload-box::-webkit-scrollbar-thumb { background: rgba(255, 255, 255, 0.1); }
        .payload-title { font-size: 10px; text-transform: uppercase; letter-spacing: 1px; color: var(--text-muted); margin-bottom: 6px; font-weight: 600; }
        .payload-content { color: #cbd5e1; white-space: pre-wrap; word-break: break-all; }
        .floating-robot-assistant {
            position: fixed; bottom: 30px; right: 30px; width: 100px; height: auto; z-index: 1000;
            filter: drop-shadow(0 0 25px rgba(0, 242, 254, 0.6));
            animation: floatRobot 4s ease-in-out infinite; pointer-events: none; transition: all 0.3s ease;
        }
        @keyframes floatRobot {
            0% { transform: translateY(0px) rotate(0deg); }
            50% { transform: translateY(-15px) rotate(6deg); }
            100% { transform: translateY(0px) rotate(0deg); }
        }
    </style>
</head>
<body>
    <header>
        <div class=""logo-container"">
            <div>
                <div class=""logo"">SYNCRO AI MONITOR</div>
                <div class=""subtitle"">Live Token & Action Diagnostics Orchestrator</div>
            </div>
        </div>
        <div class=""live-badge"">
            <span class=""live-pulse""></span> MONITOR ONLINE (PORT 3030)
        </div>
    </header>
    <div class=""tabs-navigation"">
        <button class=""tab-nav-btn active"" onclick=""switchTab(event, 'tasks-tab')"">
            <span class=""material-icons"">task_alt</span> Tasks Loop
        </button>
        <button class=""tab-nav-btn"" onclick=""switchTab(event, 'logs-tab')"">
            <span class=""material-icons"">terminal</span> Activity & IPC
        </button>
        <button class=""tab-nav-btn"" onclick=""switchTab(event, 'status-tab')"">
            <span class=""material-icons"">bar_chart</span> System Metrics
        </button>
        <button class=""tab-nav-btn"" onclick=""switchTab(event, 'llm-tab')"">
            <span class=""material-icons"">psychology</span> LLM Inspector
        </button>
    </div>
    <main>
        <div id=""tasks-tab"" class=""tab-panel active"">
            <div class=""card"">
                <div class=""card-title"">durable DB &mdash; Tasks Stats &amp; Audit (.syncro_db)</div>
                <div class=""status-grid"" id=""db-task-stats"">
                    <div class=""empty-state"">Loading database status&hellip;</div>
                </div>
                <div class=""card-title"" style=""margin-top:18px;"">Recent Loop Tasks</div>
                <div class=""action-list"" id=""db-task-list"">
                    <div class=""empty-state"">No tasks in the loop yet.</div>
                </div>
                <div class=""card-title"" style=""margin-top:18px;"">Durable Audit Trail</div>
                <div class=""terminal"" id=""db-audit"" style=""max-height:220px;""></div>
            </div>
        </div>
        <div id=""logs-tab"" class=""tab-panel"">
            <div style=""display: grid; grid-template-columns: 1fr 1fr; gap: 30px;"">
                <div class=""card"">
                    <div class=""card-title"">Live IPC Packet Stream <a href=""/ipc"" target=""_blank"" style=""color: var(--accent-cyan); text-decoration: none; font-size: 11.5px; margin-left: 10px; text-transform: none; font-family: 'Inter', sans-serif;"">[ Full Screen ]</a></div>
                    <div class=""terminal"" id=""terminal"" style=""height: 480px;""></div>
                </div>
                <div class=""card"">
                    <div class=""card-title"">CLI & Agent Actions History</div>
                    <div class=""action-list"" id=""action-list"" style=""max-height: 480px;"">
                        <div class=""empty-state"">No actions registered. Execute operations via CLI.</div>
                    </div>
                </div>
            </div>
        </div>
        <div id=""status-tab"" class=""tab-panel"">
            <div style=""display: grid; grid-template-columns: 1fr 1fr; gap: 30px;"">
                <div class=""card"">
                    <div class=""card-title"">Diagnostics & AI Metrics</div>
                    <div class=""status-grid"">
                        <div class=""status-box pipe"">
                            <span class=""status-label"">IPC Named Pipe</span>
                            <span class=""status-value""><span class=""dot active""></span> ACTIVE</span>
                        </div>
                        <div class=""status-box llm"">
                            <span class=""status-label"">Groq / Gemini (Port 3020)</span>
                            <span class=""status-value"" id=""llm-status""><span class=""dot offline""></span> OFFLINE</span>
                        </div>
                    </div>
                    <div class=""status-box tokens"" style=""margin-bottom: 24px;"">
                        <span class=""status-label"">Processed Token Capacity</span>
                        <span class=""status-value"" id=""token-counter"" style=""font-size: 36px; font-weight: 900; background: linear-gradient(135deg, var(--accent-cyan), var(--accent-purple)); -webkit-background-clip: text; -webkit-text-fill-color: transparent;"">0</span>
                        <span style=""font-size: 10px; color: var(--text-muted); margin-top: -3px; font-weight: 500;"">ESTIMATED TOTAL TOKENS</span>
                    </div>
                    <div class=""card-title"">Active Agent Sessions</div>
                    <div class=""session-list"" id=""session-list"">
                        <div class=""empty-state"">No active sessions mapped. Run 'syncro clone' or scan files.</div>
                    </div>
                </div>
                <div class=""card"">
                    <div class=""card-title"">NLP Mappings &middot; Scripts &middot; Tokens</div>
                    <div class=""status-grid"" id=""db-kpis"">
                        <div class=""empty-state"">Loading KPIs&hellip;</div>
                    </div>
                    <div class=""card-title"" style=""margin-top:18px;"">Learned Error &rarr; Fix Mappings</div>
                    <div class=""action-list"" id=""db-mappings"" style=""max-height: 200px;"">
                        <div class=""empty-state"">No mappings learned yet.</div>
                    </div>
                    <div class=""card-title"" style=""margin-top:18px;"">Stored &amp; Executed Scripts</div>
                    <div class=""action-list"" id=""db-scripts"" style=""max-height: 200px;"">
                        <div class=""empty-state"">No scripts stored yet.</div>
                    </div>
                </div>
            </div>
        </div>
        <div id=""llm-tab"" class=""tab-panel"">
            <div class=""card"">
                <div class=""card-title"">LLM Request & Response Payloads (Port 3020)</div>
                <div style=""display: flex; flex-direction: column; gap: 16px; max-height: 600px; overflow-y: auto; padding-right: 4px;"" id=""llm-exchanges"">
                    <div class=""empty-state"">No LLM requests tracked yet. Connect on Port 3020.</div>
                </div>
            </div>
        </div>
    </main>
    <img src=""https://cdn.prod.website-files.com/64354b8ce4872ad8cd1c7b04/64354b8ce4872a4f4e1c7c21_botty.png"" class=""floating-robot-assistant"" alt=""Syncro AI Bot"" />
    <script>
        const llmStatus = document.getElementById('llm-status');
        const tokenCounter = document.getElementById('token-counter');
        const sessionList = document.getElementById('session-list');
        const actionList = document.getElementById('action-list');
        const llmExchanges = document.getElementById('llm-exchanges');
        const terminal = document.getElementById('terminal');
        const dbTaskStats = document.getElementById('db-task-stats');
        const dbTaskList = document.getElementById('db-task-list');
        const dbAudit = document.getElementById('db-audit');
        const dbKpis = document.getElementById('db-kpis');
        const dbMappings = document.getElementById('db-mappings');
        const dbScripts = document.getElementById('db-scripts');

        function switchTab(evt, tabId) {
            document.querySelectorAll('.tab-nav-btn').forEach(btn => btn.classList.remove('active'));
            document.querySelectorAll('.tab-panel').forEach(panel => panel.classList.remove('active'));
            evt.currentTarget.classList.add('active');
            document.getElementById(tabId).classList.add('active');
        }

        async function updateDiagnostics() {
            try {
                const res = await fetch('/api/status');
                if (!res.ok) throw new Error('API server down');
                const data = await res.json();
                if (data.llmServer === 'Online') {
                    llmStatus.innerHTML = '<span class=""dot online""></span> ONLINE';
                } else {
                    llmStatus.innerHTML = '<span class=""dot offline""></span> OFFLINE';
                }
                tokenCounter.textContent = (data.totalTokens || 0).toLocaleString();
                if (data.sessions && data.sessions.length > 0) {
                    sessionList.innerHTML = data.sessions.map(s => `
                        <div class=""session-item"">
                            <div class=""session-meta"">
                                <span class=""session-name"">${escapeHtml(s.agentName)}</span>
                                <span class=""session-id"">ID: ${s.id}</span>
                                <span class=""session-task"">Current Task: ${escapeHtml(s.currentTask || 'Idle')}</span>
                            </div>
                            <span class=""badge success"">${escapeHtml(s.status || 'Active')}</span>
                        </div>
                    `).join('');
                } else {
                    sessionList.innerHTML = '<div class=""empty-state"">No active sessions mapped. Run \'syncro clone\' or scan files.</div>';
                }
                if (data.actions && data.actions.length > 0) {
                    actionList.innerHTML = data.actions.slice().reverse().map(a => `
                        <div class=""action-item"">
                            <div class=""action-meta"">
                                <span class=""action-title"">
                                    <span class=""badge type"">${escapeHtml(a.actionType || 'Action')}</span>
                                    <span>Session: ${a.sessionId ? a.sessionId.substring(0, 8) : 'Unknown'}</span>
                                </span>
                                <div class=""action-io""><strong>Input:</strong> ${escapeHtml(a.input || 'None')}</div>
                                ${a.output ? `<div class=""action-io""><strong>Output:</strong> ${escapeHtml(a.output)}</div>` : ''}
                            </div>
                            <span class=""badge ${a.success ? 'success' : 'fail'}"">${a.success ? 'SUCCESS' : 'FAILED'}</span>
                        </div>
                    `).join('');
                } else {
                    actionList.innerHTML = '<div class=""empty-state"">No actions registered. Execute operations via CLI.</div>';
                }
                if (data.llmRequests && data.llmRequests.length > 0) {
                    llmExchanges.innerHTML = data.llmRequests.slice().reverse().map(r => `
                        <div class=""llm-card"">
                            <div class=""llm-header"">
                                <span class=""llm-label"">Request Exchange</span>
                                <span class=""badge ${r.status === 'Completed' ? 'success' : 'warning'}"">${escapeHtml(r.status || 'Pending')}</span>
                            </div>
                            <div class=""llm-payload-container"">
                                <div class=""payload-box"">
                                    <div class=""payload-title"">PROMPT</div>
                                    <div class=""payload-content"">${escapeHtml(r.prompt || '')}</div>
                                </div>
                                <div class=""payload-box"">
                                    <div class=""payload-title"">RESPONSE</div>
                                    <div class=""payload-content"">${escapeHtml(r.result || 'No response payload yet...')}</div>
                                </div>
                            </div>
                        </div>
                    `).join('');
                }
                if (data.logs && data.logs.length > 0) {
                    const scrollAtBottom = terminal.scrollHeight - terminal.clientHeight <= terminal.scrollTop + 1;
                    terminal.innerHTML = data.logs.map(log => {
                        let className = 'monitor';
                        if (log.includes('[IPC Raw]')) className = 'ipc-raw';
                        else if (log.includes('[IPC ERROR]') || log.includes('Error')) className = 'ipc-err';
                        return `<div class=""terminal-line ${className}"">${escapeHtml(log)}</div>`;
                    }).join('');
                    if (scrollAtBottom) {
                        terminal.scrollTop = terminal.scrollHeight;
                    }
                }
                if (data.db) renderDb(data.db);
            } catch (err) {
                console.error(err);
            }
        }
        function box(label, value){
            return `<div class=""status-box""><span class=""status-label"">${escapeHtml(label)}</span><span class=""status-value"">${escapeHtml(String(value))}</span></div>`;
        }
        function renderDb(db){
            if(!db) return;
            const t = db.Tasks||{}, sc = db.Scripts||{}, np = db.Nlp||{}, tk = db.Tokens||{};
            const byStatus = t.ByStatus||{};
            const statusStr = Object.keys(byStatus).map(k=>`${k}:${byStatus[k]}`).join('  ')||'—';
            dbTaskStats.innerHTML = box('Total Tasks', t.Total||0) + box('By Status', statusStr);
            const rt = t.Recent||[];
            dbTaskList.innerHTML = rt.length ? rt.map(x=>`
                <div class=""action-item"" style=""flex-direction:column; align-items:flex-start; gap:8px; border-left: 4px solid ${x.Status==='Done'?'#10b981':(x.Status==='NeedsHuman'?'#ef4444':'#7f00ff')};"">
                    <div class=""action-meta"" style=""width:100%;"">
                        <span class=""action-title"" style=""display:flex; justify-content:space-between; width:100%; margin-bottom: 8px;"">
                            <span>
                                <strong style=""font-size: 15px; color: #fff;"">${escapeHtml(x.Title||x.Id)}</strong>
                                <span style=""font-size: 11px; color: var(--text-muted); margin-left: 8px; font-family: 'JetBrains Mono', monospace;"">ID: ${escapeHtml(x.Id)}</span>
                            </span>
                            <span class=""badge ${x.Status==='Done'?'success':(x.Status==='NeedsHuman'?'fail':'type')}"">${escapeHtml(x.Status)}</span>
                        </span>
                        <div style=""display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 10px; background: rgba(255,255,255,0.02); padding: 12px; border-radius: 8px; border: 1px solid var(--glass-border); font-size: 12px;"">
                            <div><strong style=""color: var(--accent-cyan);"">Script ID:</strong> ${escapeHtml(x.ScriptId || 'None')}</div>
                            <div><strong style=""color: var(--accent-cyan);"">Template Executed:</strong> ${x.TemplateScriptExecuted ? 'Yes' : 'No'}</div>
                            <div><strong style=""color: var(--accent-cyan);"">Error Encountered:</strong> ${x.Error ? 'Yes' : 'No'}</div>
                            <div><strong style=""color: var(--accent-cyan);"">Error Count:</strong> ${x.ErrorCount}</div>
                            <div><strong style=""color: var(--accent-cyan);"">Source:</strong> ${escapeHtml(x.Source || 'Unknown')}</div>
                            <div><strong style=""color: var(--accent-cyan);"">Error Type:</strong> ${escapeHtml(x.ErrorType || 'None')}</div>
                            <div><strong style=""color: var(--accent-cyan);"">LLM Involved:</strong> ${x.LlmInvolved ? 'Yes' : 'No'}</div>
                            <div><strong style=""color: var(--accent-cyan);"">Next Action:</strong> ${escapeHtml(x.NextAction || 'None')}</div>
                        </div>
                        ${x.Solution ? `
                        <div style=""margin-top: 8px; padding: 10px; background: rgba(16, 185, 129, 0.05); border-left: 3px solid var(--success); border-radius: 4px; font-size: 12.5px;"">
                            <strong style=""color: #34d399;"">Proposed Solution:</strong> ${escapeHtml(x.Solution)}
                        </div>` : ''}
                    </div>
                </div>`).join('') : '<div class=""empty-state"">No tasks in the loop yet.</div>';
            const au = db.Audit||[];
            dbAudit.innerHTML = au.length ? au.map(a=>`<div class=""terminal-line monitor"">[${escapeHtml(a.At)}] ${escapeHtml(a.Action)} ${escapeHtml(a.Note||'')}</div>`).join('') : '<div class=""empty-state"">No audit entries.</div>';
            const srcStr = Object.keys(np.BySource||{}).map(k=>`${k}:${np.BySource[k]}`).join('  ')||'—';
            dbKpis.innerHTML =
                box('Mappings', np.Mappings||0) + box('Map Sources', srcStr) +
                box('Avg Worked', np.AvgWorkedRate||0) + box('Lexicon', np.LexiconSignals||0) +
                box('Scripts', sc.Total||0) + box('Runs ok/total', `${sc.TotalSucceeded||0}/${sc.TotalRuns||0}`);
            const rm = np.Recent||[];
            dbMappings.innerHTML = rm.length ? rm.map(m=>`
                <div class=""action-item"">
                    <div class=""action-meta"">
                        <span class=""action-title""><span class=""badge type"">${escapeHtml(m.Solution)}</span><span>${escapeHtml(m.Code||m.Keywords||'—')}</span></span>
                    </div>
                    <span class=""badge ${m.Source==='llm'?'warning':'success'}"">${escapeHtml(m.Source)}</span>
                </div>`).join('') : '<div class=""empty-state"">No mappings learned yet.</div>';
            const rs = sc.Recent||[];
            dbScripts.innerHTML = rs.length ? rs.map(s=>`
                <div class=""action-item"">
                    <div class=""action-meta"">
                        <span class=""action-title""><span class=""badge type"">${escapeHtml(s.Purpose)}</span><span>${escapeHtml(s.Name||s.Id)}</span></span>
                    </div>
                    <span class=""badge ${s.Flagged?'fail':(s.Safe?'success':'warning')}"">${s.Flagged?'FLAGGED':(s.Safe?'SAFE':'GATED')}</span>
                </div>`).join('') : '<div class=""empty-state"">No scripts stored yet.</div>';
        }
        function escapeHtml(str) {
            if (!str) return '';
            return str.replace(/&/g, ""&amp;"").replace(/</g, ""&lt;"").replace(/>/g, ""&gt;"").replace(/""/g, ""&quot;"").replace(/'/g, ""&#039;"");
        }
        setInterval(updateDiagnostics, 1500);
        updateDiagnostics();
    </script>
</body>
</html>";
        }

        private string GetIpcFullScreenHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Live IPC Packet Stream</title>
    <link href=""https://fonts.googleapis.com/css2?family=JetBrains+Mono:wght@400;500;700&display=swap"" rel=""stylesheet"">
    <style>
        body {
            background-color: #04060c;
            color: #cbd5e1;
            font-family: 'JetBrains Mono', monospace;
            margin: 0;
            padding: 20px;
            height: 100vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }
        header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            border-bottom: 1px solid rgba(255, 255, 255, 0.1);
            padding-bottom: 12px;
            margin-bottom: 15px;
        }
        h1 {
            font-size: 18px;
            margin: 0;
            color: #a855f7;
            text-transform: uppercase;
            letter-spacing: 1.5px;
        }
        .live-badge {
            background: rgba(168, 85, 247, 0.1);
            border: 1px solid rgba(168, 85, 247, 0.3);
            color: #c084fc;
            padding: 4px 10px;
            border-radius: 4px;
            font-size: 11px;
            font-weight: bold;
        }
        #terminal {
            flex: 1;
            overflow-y: auto;
            background: rgba(0, 0, 0, 0.3);
            border: 1px solid rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            padding: 20px;
            font-size: 14px;
            line-height: 1.6;
        }
        .terminal-line { margin-bottom: 8px; word-break: break-all; white-space: pre-wrap; }
        .terminal-line.ipc-err { color: #f87171; }
        .terminal-line.ipc-raw { color: #c084fc; }
        .terminal-line.monitor { color: #38bdf8; }
    </style>
</head>
<body>
    <header>
        <h1>Live IPC Packet Stream</h1>
        <div class=""live-badge"">REAL-TIME PACKETS</div>
    </header>
    <div id=""terminal""></div>

    <script>
        const terminal = document.getElementById('terminal');
        async function updateLogs() {
            try {
                const res = await fetch('/api/status');
                if (!res.ok) throw new Error('API server offline');
                const data = await res.json();
                if (data.logs && data.logs.length > 0) {
                    const scrollAtBottom = terminal.scrollHeight - terminal.clientHeight <= terminal.scrollTop + 1;
                    
                    terminal.innerHTML = data.logs.map(log => {
                        let className = 'monitor';
                        if (log.includes('[IPC Raw]')) className = 'ipc-raw';
                        else if (log.includes('[IPC ERROR]') || log.includes('Error')) className = 'ipc-err';
                        return `<div class=""terminal-line ${className}"">${escapeHtml(log)}</div>`;
                    }).join('');
                    
                    if (scrollAtBottom) {
                        terminal.scrollTop = terminal.scrollHeight;
                    }
                }
            } catch (err) {
                console.error(err);
            }
        }
        function escapeHtml(str) {
            if (!str) return '';
            return str.replace(/&/g, ""&amp;"").replace(/</g, ""&lt;"").replace(/>/g, ""&gt;"").replace(/""/g, ""&quot;"").replace(/'/g, ""&#039;"");
        }
        setInterval(updateLogs, 1000);
        updateLogs();
    </script>
</body>
</html>";
        }
    }
}
