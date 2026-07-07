using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Syncro.Desktop.Services
{
    public record FlutterDevice(string Id, string Name, string Platform, bool IsEmulator);

    public class FlutterService
    {
        private Process? _activeProcess;

        public event Action<string>? OnOutput;

        public async Task<bool> CreateProject(string name, string path)
        {
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
                if (process.ExitCode != 0) return false;

                // FB0: impose BLoC architecture on the freshly-created project (flutter-bloc-aimapper.md).
                var projectRoot = Path.Combine(path, name);
                var scaffolder = new Syncro.Desktop.Services.Flutter.FlutterBlocScaffolder();
                await scaffolder.ScaffoldBlocAsync(projectRoot, name, msg => OnOutput?.Invoke(msg));

                return true;
            }
            catch { return false; }
        }

        public async Task<string?> RunProject(string projectPath, string mode)
        {
            await Task.Yield();
            KillActive();   // clear only a previously-tracked run, not every flutter.exe

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

        public void StopProject() => KillActive();

        /// <summary>
        /// Kill ONLY this run's process tree (F3). The old implementation ran
        /// <c>taskkill /F /IM flutter.exe /T</c>, which killed every Flutter process on the
        /// machine — including unrelated apps the user was running.
        /// </summary>
        private void KillActive()
        {
            try
            {
                if (_activeProcess != null && !_activeProcess.HasExited)
                    _activeProcess.Kill(entireProcessTree: true);
            }
            catch { }
            finally { _activeProcess = null; }
        }

        /// <summary>Enumerate connected devices/emulators (F4) via <c>flutter devices --machine</c>.</summary>
        public async Task<List<FlutterDevice>> GetDevicesAsync()
        {
            var devices = new List<FlutterDevice>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c flutter devices --machine",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = new Process { StartInfo = psi };
                var sb = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                await p.WaitForExitAsync();

                var text = sb.ToString();
                int start = text.IndexOf('['), end = text.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    var arr = JArray.Parse(text.Substring(start, end - start + 1));
                    foreach (var d in arr)
                    {
                        devices.Add(new FlutterDevice(
                            (string?)d["id"] ?? "",
                            (string?)d["name"] ?? "",
                            (string?)d["targetPlatform"] ?? "",
                            (bool?)d["emulator"] ?? false));
                    }
                }
            }
            catch (Exception ex) { OnOutput?.Invoke($"[devices] {ex.Message}"); }
            return devices;
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
