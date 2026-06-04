using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core.Platform;

namespace Syncro.Desktop.Services.SyncroCLI.Execution
{
    public class ProcessRunner
    {
        private readonly IPlatformService _platform;
        public event Action<string>? OnOutput;

        public ProcessRunner(IPlatformService platform)
        {
            _platform = platform;
        }

        public async Task<int> Run(string command, string workingDir, string? input = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _platform.Shell,
                Arguments = _platform.WrapCommand(command),
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = input != null,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Enhance PATH for Syncro-specific tools
            ApplyEnvironmentEnhancements(psi);

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null) OnOutput?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) OnOutput?.Invoke($"ERR: {e.Data}");
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (input != null)
                {
                    await process.StandardInput.WriteLineAsync(input);
                    await process.StandardInput.FlushAsync();
                    process.StandardInput.Close();
                }

                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                OnOutput?.Invoke($"CRITICAL ERROR: Failed to start process: {ex.Message}");
                return -1;
            }
        }

        private void ApplyEnvironmentEnhancements(ProcessStartInfo psi)
        {
            if (_platform.Type == PlatformType.Windows)
            {
                var flutterBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SyncroFlutter", "flutter", "bin");
                if (Directory.Exists(flutterBin))
                {
                    var currentPath = psi.EnvironmentVariables["PATH"] ?? "";
                    if (!currentPath.Contains(flutterBin))
                    {
                        psi.EnvironmentVariables["PATH"] = flutterBin + ";" + currentPath;
                    }
                }
            }
        }
    }
}
