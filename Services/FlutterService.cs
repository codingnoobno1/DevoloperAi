using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services
{
    public class FlutterService
    {
        private Process? _activeProcess;

        public event Action<string>? OnOutput;

        public async Task<bool> CreateProject(string name, string path)
        {
            // ... (keep create logic)
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "flutter",
                    Arguments = $"create --org com.syncro {name}",
                    WorkingDirectory = path,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch { return false; }
        }

        public async Task<string?> RunProject(string projectPath, string mode)
        {
            await Task.Yield();
            KillExistingProcesses();

            string arguments;
            string? url = null;

            if (mode.ToLower() == "web")
            {
                arguments = "run -d web-server --web-port=5000";
                url = "http://localhost:5000";
            }
            else
            {
                arguments = mode.ToLower() switch
                {
                    "adb" => "run -d android",
                    "emulator" => "run",
                    _ => "run"
                };
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c flutter {arguments}",
                WorkingDirectory = projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true, // Added for hot reload
                CreateNoWindow = true
            };

            _activeProcess = new Process { StartInfo = startInfo };
            _activeProcess.OutputDataReceived += (s, e) => { if (e.Data != null) OnOutput?.Invoke(e.Data); };
            _activeProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) OnOutput?.Invoke($"ERR: {e.Data}"); };
            
            _activeProcess.Start();
            _activeProcess.BeginOutputReadLine();
            _activeProcess.BeginErrorReadLine();

            return url;
        }

        public async Task HotReload()
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                OnOutput?.Invoke("🔥 Triggering Hot Reload...");
                await _activeProcess.StandardInput.WriteLineAsync("r");
            }
        }

        public async Task HotRestart()
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                OnOutput?.Invoke("🔄 Triggering Hot Restart...");
                await _activeProcess.StandardInput.WriteLineAsync("R");
            }
        }

        public async Task SmartReload(List<string> changedFiles)
        {
            bool needsRestart = changedFiles.Any(f => 
                f.Contains("main.dart", StringComparison.OrdinalIgnoreCase) || 
                f.Contains("routes", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("provider", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("bloc", StringComparison.OrdinalIgnoreCase) ||
                f.Contains("state", StringComparison.OrdinalIgnoreCase));

            if (needsRestart)
            {
                OnOutput?.Invoke("💡 Structural changes detected. Performing Hot Restart...");
                await HotRestart();
            }
            else
            {
                OnOutput?.Invoke("⚡ UI changes detected. Performing Hot Reload...");
                await HotReload();
            }
        }

        public void StopProject()
        {
            if (_activeProcess != null && !_activeProcess.HasExited)
            {
                _activeProcess.Kill(true);
            }
            KillExistingProcesses();
        }

        private void KillExistingProcesses()
        {
            // ... (keep existing kill logic)
            try
            {
                var killInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/F /IM flutter.exe /T",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(killInfo)?.WaitForExit();
            }
            catch { }
        }

        public async Task CleanProject(string projectPath)
        {
            // ... (keep existing clean logic)
            OnOutput?.Invoke("Running flutter clean...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c flutter clean",
                WorkingDirectory = projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync();
            OnOutput?.Invoke("Clean completed.");
        }

        public async Task BuildWeb(string projectPath)
        {
            // ... (keep existing build logic)
            var startInfo = new ProcessStartInfo
            {
                FileName = "flutter",
                Arguments = "build web",
                WorkingDirectory = projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync();
        }
    }
}
