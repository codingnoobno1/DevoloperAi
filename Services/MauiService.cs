using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services
{
    public class MauiService
    {
        private Process? _activeProcess;
        public event Action<string>? OnOutput;

        public async Task<string?> RunProject(string projectPath, string mode)
        {
            await Task.Yield();
            KillActive();

            string arguments;
            string? url = null;

            if (mode.ToLower() == "windows")
            {
                arguments = "run -f net8.0-windows10.0.19041.0";
            }
            else if (mode.ToLower() == "android")
            {
                arguments = "run -f net8.0-android";
            }
            else
            {
                arguments = "run";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
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

        public async Task SmartReload(List<string> changedFiles)
        {
            OnOutput?.Invoke("⚡ MAUI/Blazor changes detected. Restarting or hot reloading via dotnet...");
            await Task.CompletedTask;
        }

        public void StopProject() => KillActive();

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
        
        public async Task CleanProject(string projectPath)
        {
            OnOutput?.Invoke("Running dotnet clean...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "clean",
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
    }
}
