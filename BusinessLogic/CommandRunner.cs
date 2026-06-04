using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DeveloperAI.BusinessLogic
{
    public static class CommandRunner
    {
        public static async Task<(string StdOut, string StdErr, int ExitCode)> RunCommand(string command, string? workingDir, bool logOutput = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using (var process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                if (logOutput)
                {
                    LogCommand(command, outputBuilder.ToString(), errorBuilder.ToString(), workingDir);
                }

                return (outputBuilder.ToString(), errorBuilder.ToString(), process.ExitCode);
            }
        }

        private static void LogCommand(string command, string stdout, string stderr, string? workingDir)
        {
            string logDir = Path.Combine(workingDir ?? Directory.GetCurrentDirectory(), ".projectmeta", "logs");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "command.log");
            var logEntry = $"[{DateTime.Now}] CMD: {command}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}\n----------------------\n";
            File.AppendAllText(logFile, logEntry);
        }
    }
} 