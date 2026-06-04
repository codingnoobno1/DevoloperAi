using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class BatchFileExecutionService
    {
        public async Task<(bool success, string message)> ExecuteBatchFile(
            string scriptContent, string targetDirectory, string fileName = "generated_script.bat")
        {
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                return (false, "Target directory cannot be empty.");
            }
            if (!Directory.Exists(targetDirectory))
            {
                return (false, $"Target directory does not exist: {targetDirectory}");
            }

            string scriptFilePath = Path.Combine(targetDirectory, fileName);
            try
            {
                await File.WriteAllTextAsync(scriptFilePath, scriptContent);

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"{scriptFilePath}\"", // /C executes the command and then terminates
                    WorkingDirectory = targetDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    return (false, "Failed to start script execution process.");
                }

                using (process)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    string error = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        return (true, $"Script executed successfully.\nOutput:\n{output}");
                    }
                    else
                    {
                        return (false, $"Script execution failed with exit code {process.ExitCode}.\nOutput:\n{output}\nError:\n{error}");
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error executing script: {ex.Message}");
            }
        }
    }
}
