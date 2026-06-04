using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class CommandExecutionService
    {
        public async Task<(bool success, string output, string error)> ExecuteCommand(
            string command, string workingDirectory, bool runInShell = true)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return (false, "", "Command cannot be empty.");
            }
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return (false, "", "Working directory cannot be empty.");
            }
            if (!Directory.Exists(workingDirectory))
            {
                return (false, "", $"Working directory does not exist: {workingDirectory}");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = runInShell ? "cmd.exe" : "", // Use cmd.exe for shell execution on Windows
                Arguments = runInShell ? $"/C \"{command}\"" : command, // /C executes and terminates
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // For non-shell commands, set FileName directly
            if (!runInShell)
            {
                // This part would need more sophisticated parsing to determine the actual executable
                // For now, assume the command itself is the executable and its arguments
                string[] parts = command.Split(' ', 2);
                startInfo.FileName = parts[0];
                startInfo.Arguments = parts.Length > 1 ? parts[1] : "";
            }

            try
            {
                Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return (false, "", "Failed to start command execution process.");
                }

                using (process)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        return (true, output, error);
                    }
                    else
                    {
                        return (false, output, error);
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, "", $"Error executing command: {ex.Message}");
            }
        }

        public Task<(bool success, string message)> OpenTerminalInDirectory(string workingDirectory, string? initialCommand = null)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return Task.FromResult((false, "Working directory cannot be empty."));
            }
            if (!Directory.Exists(workingDirectory))
            {
                return Task.FromResult((false, $"Working directory does not exist: {workingDirectory}"));
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe", // For Windows, open command prompt
                    Arguments = initialCommand != null ? $"/K cd /d \"{workingDirectory}\" & {initialCommand}" : $"/K cd /d \"{workingDirectory}\"",
                    UseShellExecute = true, // To open a new window
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
                return Task.FromResult((true, $"Opened terminal in {workingDirectory}"));
            }
            catch (Exception ex)
            {
                return Task.FromResult((false, $"Failed to open terminal: {ex.Message}"));
            }
        }
    }
}
