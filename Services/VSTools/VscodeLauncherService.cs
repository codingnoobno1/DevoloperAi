using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.VSTools
{
    public class VscodeLauncherService
    {
        public Task<bool> LaunchAsync(string projectPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                {
                    return Task.FromResult(false);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c code \"{projectPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VSCode Launcher] Error: {ex.Message}");
                return Task.FromResult(false);
            }
        }
    }
}
