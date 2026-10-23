using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Ide.Host
{
    /// <summary>
    /// Backs the IDE with the real VS Code OSS checkout (<c>vscode-main</c>) served over localhost via
    /// its <c>scripts/code-web</c> launcher, then shown in a WebView. This is the "code-oss abstraction"
    /// backend: Syncro drives VS Code OSS as a child process instead of re-implementing the workbench.
    ///
    /// It never throws — if the checkout is missing or not compiled it returns an
    /// <see cref="IdeHostMode.Unavailable"/> session carrying the exact remedy, so the shell can fall
    /// back to Monaco and show the user what to run.
    /// </summary>
    public sealed class CodeOssIdeHost : IIdeHost
    {
        private readonly IdeHostOptions _opts;
        private readonly Engine.Mcp.Transport.McpHttpEndpoint? _bridge;
        private Process? _proc;
        private string? _url;

        /// <summary>How many IDE windows are currently using this shared server.</summary>
        private int _refs;

        public CodeOssIdeHost(IdeHostOptions opts, Engine.Mcp.Transport.McpHttpEndpoint? bridge = null)
        {
            _opts = opts;
            _bridge = bridge;
        }

        public IdeHostKind Kind => IdeHostKind.CodeOss;

        public async Task<IdeHostSession> StartAsync(string? workspacePath, CancellationToken ct = default)
        {
            // Reuse a live server.
            if (_proc is { HasExited: false } && _url != null)
                return IdeHostSession.Web(Kind, WithFolder(_url, workspacePath), "code-oss (reused)");

            var root = ResolveCheckout();
            if (root == null)
                return IdeHostSession.Unavailable(Kind,
                    "VS Code OSS checkout not found.",
                    "Set IdeHostOptions.CodeOssPath to your 'vscode-main' folder.");

            // Must be compiled — the server needs out/server-main.js.
            if (!File.Exists(Path.Combine(root, "out", "server-main.js")))
                return IdeHostSession.Unavailable(Kind,
                    "VS Code OSS is present but not compiled.",
                    $"In \"{root}\" run:  npm install  then  npm run compile  (or  npm run watch  for dev). First build is large and one-time.");

            // The server runs under the checkout's own downloaded node (.build/node/...). One-time:
            // `npm run gulp node` fetches it (scripts/code-server.bat does this automatically).
            var serverNode = FindRemoteNode(root);
            if (serverNode == null)
                return IdeHostSession.Unavailable(Kind,
                    "VS Code OSS server node runtime not downloaded yet.",
                    $"In \"{root}\" run:  npm run gulp node   (also run once:  npm run download-builtin-extensions).");

            // Bring up the Syncro bridge endpoint so the syncro-bridge extension can reach the engine.
            var bridgeUp = _bridge?.EnsureStarted() == true;

            try
            {
                _proc = StartWebServer(root, serverNode, _opts.Port,
                    bridgeUp ? _bridge!.Port : 0, bridgeUp ? _bridge!.Token : null);
            }
            catch (Exception ex)
            {
                return IdeHostSession.Unavailable(Kind, $"Failed to launch code-oss: {ex.Message}", null);
            }

            var ready = await WaitForPortAsync(_opts.Port, _opts.StartTimeoutSeconds, ct);
            if (!ready)
            {
                await StopAsync();
                return IdeHostSession.Unavailable(Kind,
                    $"code-oss did not start on port {_opts.Port} within {_opts.StartTimeoutSeconds}s.",
                    "Try building first (npm run compile) or check the vscode-main logs.");
            }

            _url = $"http://127.0.0.1:{_opts.Port}/";
            return IdeHostSession.Web(Kind, WithFolder(_url, workspacePath), "code-oss serve-web");
        }

        public void Acquire() => Interlocked.Increment(ref _refs);

        /// <summary>
        /// Release one window's claim. The server only dies when the last IDE window closes —
        /// otherwise closing one window would kill the editor in every other open window, since
        /// they all share this singleton and its single localhost port.
        /// </summary>
        public async Task ReleaseAsync()
        {
            if (Interlocked.Decrement(ref _refs) <= 0)
            {
                Interlocked.Exchange(ref _refs, 0);   // clamp: never let an unpaired release go negative
                await StopAsync();
            }
        }

        public Task StopAsync()
        {
            try
            {
                if (_proc is { HasExited: false })
                    _proc.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
            finally { _proc = null; _url = null; }
            return Task.CompletedTask;
        }

        // ── helpers ──────────────────────────────────────────────────────────────
        private string? ResolveCheckout()
        {
            if (!string.IsNullOrWhiteSpace(_opts.CodeOssPath) && Directory.Exists(_opts.CodeOssPath))
                return _opts.CodeOssPath;

            // Walk up from the app base looking for a sibling 'vscode-main'.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "vscode-main");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        /// <summary>The node runtime the VS Code server runs under: <c>.build/node/&lt;ver&gt;/&lt;platform&gt;/node(.exe)</c>.</summary>
        private static string? FindRemoteNode(string root)
        {
            var nodeDir = Path.Combine(root, ".build", "node");
            if (!Directory.Exists(nodeDir)) return null;
            var exe = OperatingSystem.IsWindows() ? "node.exe" : "node";
            try
            {
                return Directory.EnumerateFiles(nodeDir, exe, SearchOption.AllDirectories).FirstOrDefault();
            }
            catch { return null; }
        }

        private static Process StartWebServer(string root, string serverNode, int port, int bridgePort, string? bridgeToken)
        {
            // Launch the server flavor directly (out/server-main.js via scripts/code-server.js) — the
            // same thing scripts/code-server.bat does after its one-time bootstrap, minus the electron
            // download preLaunch would trigger.
            var psi = new ProcessStartInfo(serverNode,
                $"\"{Path.Combine(root, "scripts", "code-server.js")}\" --host 127.0.0.1 --port {port} --without-connection-token")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["NODE_ENV"] = "development";
            psi.Environment["VSCODE_DEV"] = "1";
            psi.Environment["VSCODE_SKIP_PRELAUNCH"] = "1";

            // Syncro bridge — inherited down to the extension host, read by syncro-bridge.
            if (bridgePort > 0)
            {
                psi.Environment["SYNCRO_BRIDGE_PORT"] = bridgePort.ToString();
                if (!string.IsNullOrEmpty(bridgeToken))
                    psi.Environment["SYNCRO_BRIDGE_TOKEN"] = bridgeToken;
            }

            var proc = Process.Start(psi)!;
            // Drain the pipes: the dev server logs heavily, and an unread redirected pipe fills its
            // buffer and blocks node mid-response (server "hangs" after the port opens).
            proc.OutputDataReceived += (_, _) => { };
            proc.ErrorDataReceived += (_, _) => { };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }

        private static async Task<bool> WaitForPortAsync(int port, int timeoutSeconds, CancellationToken ct)
        {
            var deadline = Environment.TickCount64 + timeoutSeconds * 1000L;
            while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
            {
                try
                {
                    using var client = new TcpClient();
                    var connect = client.ConnectAsync("127.0.0.1", port);
                    var done = await Task.WhenAny(connect, Task.Delay(1000, ct));
                    if (done == connect && client.Connected) return true;
                }
                catch { /* not up yet */ }
                await Task.Delay(500, ct);
            }
            return false;
        }

        private static string WithFolder(string baseUrl, string? workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath)) return baseUrl;
            return $"{baseUrl}?folder={Uri.EscapeDataString(workspacePath)}";
        }
    }
}
