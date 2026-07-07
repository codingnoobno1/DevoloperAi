using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Connector.Frontend
{
    public class FrontendRunnerService : IFrontendRunnerService, IDisposable
    {
        public event Action<string>? OnOutput;
        public event Action<string>? OnReady;

        private Process? _process;
        private string? _stack;
        private string? _projectPath;

        public Task<bool> StartAsync(string projectPath, string stack)
        {
            if (_process != null && !_process.HasExited)
            {
                return Task.FromResult(true);
            }

            _projectPath = projectPath;
            _stack = stack.ToLowerInvariant();

            string fileName = "";
            string arguments = "";

            if (_stack == "flutter")
            {
                fileName = "flutter.bat";
                arguments = "run -d web-server";
            }
            else if (_stack == "nextjs" || _stack == "react" || _stack == "node")
            {
                fileName = "npm.cmd";
                arguments = "run dev";
            }
            else if (_stack == "blazor")
            {
                fileName = "dotnet.exe";
                arguments = "watch run";
            }
            else
            {
                OnOutput?.Invoke($"ERR: Unsupported stack '{_stack}' for live preview.");
                return Task.FromResult(false);
            }

            try
            {
                _process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        WorkingDirectory = projectPath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                _process.OutputDataReceived += HandleOutputData;
                _process.ErrorDataReceived += HandleErrorData;

                bool started = _process.Start();
                if (started)
                {
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                    OnOutput?.Invoke($"Started {_stack} dev server in {projectPath}");
                }
                return Task.FromResult(started);
            }
            catch (Exception ex)
            {
                OnOutput?.Invoke($"ERR: Failed to start {_stack} dev server: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private void HandleOutputData(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;

            OnOutput?.Invoke(e.Data);

            // Look for localhost URLs to detect readiness
            string dataLower = e.Data.ToLowerInvariant();
            var match = Regex.Match(dataLower, @"http://(localhost|127\.0\.0\.1):(\d+)");
            if (match.Success)
            {
                OnReady?.Invoke(match.Value);
            }
        }

        private void HandleErrorData(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            OnOutput?.Invoke($"ERR: {e.Data}");
        }

        public Task StopAsync()
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch { }
            }
            _process = null;
            return Task.CompletedTask;
        }

        public async Task RestartAsync()
        {
            if (_stack == "flutter" && _process != null && !_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteLineAsync("R");
                    OnOutput?.Invoke("Restart requested (hot restart)...");
                    return;
                }
                catch { }
            }
            
            await StopAsync();
            if (_projectPath != null && _stack != null)
            {
                await StartAsync(_projectPath, _stack);
            }
        }

        public async Task ReloadAsync()
        {
            if (_stack == "flutter" && _process != null && !_process.HasExited)
            {
                try
                {
                    await _process.StandardInput.WriteLineAsync("r");
                    OnOutput?.Invoke("Reload requested (hot reload)...");
                }
                catch { }
            }
        }

        public void Dispose()
        {
            StopAsync().Wait();
        }
    }
}
