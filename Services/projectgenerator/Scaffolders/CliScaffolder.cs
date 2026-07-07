using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;
using CliWrap.EventStream;

namespace Syncro.Desktop.Services.projectgenerator.Scaffolders
{
    public class CliScaffolder : IStackScaffolder
    {
        public async Task<bool> ScaffoldAsync(ScaffolderContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(context.Archetype.Cli))
            {
                return false;
            }

            try
            {
                string folderName = context.ProjectName.ToLower().Replace(" ", "_");
                string parentDir = Directory.GetParent(context.TargetPath)?.FullName ?? context.TargetPath;

                // Replace {folder} placeholder in CLI string
                string fullCommand = context.Archetype.Cli.Replace("{folder}", folderName);
                
                // Split command from args (e.g. "npm create vite@latest..." -> "npm", "create vite@latest...")
                int firstSpace = fullCommand.IndexOf(' ');
                string command = firstSpace > -1 ? fullCommand.Substring(0, firstSpace) : fullCommand;
                string args = firstSpace > -1 ? fullCommand.Substring(firstSpace + 1) : "";

                context.OnLog?.Invoke($"[Native CLI] Running Command: {command} {args}");

                var cmd = Cli.Wrap(command)
                    .WithArguments(args)
                    .WithWorkingDirectory(parentDir)
                    .WithEnvironmentVariables(env => env
                        .Set("CI", "true")
                        .Set("npm_config_progress", "false")
                        .Set("FORCE_COLOR", "0")
                    )
                    .WithValidation(CommandResultValidation.None);

                // Guard against a stalled install. Native scaffolders (create-next-app, etc.) run
                // `npm install`, which emits NO newline-terminated output while piped — so the log
                // goes silent for minutes and looks frozen even when it's working. We: (1) cap the
                // whole run with a timeout so a genuine stall fails cleanly into the template
                // fallback rather than hanging forever (bug B10); (2) emit a heartbeat so the silent
                // install phase reads as "working", not "dead".
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var startedAt = DateTime.UtcNow;
                using var heartbeat = new Timer(_ =>
                {
                    int secs = (int)(DateTime.UtcNow - startedAt).TotalSeconds;
                    context.OnLog?.Invoke($"[Native CLI] …still working ({secs}s) — installing dependencies, this can take a few minutes.");
                }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

                try
                {
                    await foreach (var cmdEvent in cmd.ListenAsync(linkedCts.Token))
                    {
                        switch (cmdEvent)
                        {
                            case StandardOutputCommandEvent stdOut:
                                if (!string.IsNullOrWhiteSpace(stdOut.Text))
                                    context.OnLog?.Invoke($"[Native CLI] {stdOut.Text}");
                                break;
                            case StandardErrorCommandEvent stdErr:
                                if (!string.IsNullOrWhiteSpace(stdErr.Text))
                                    context.OnLog?.Invoke($"[Native CLI ERR] {stdErr.Text}");
                                break;
                            case ExitedCommandEvent exited:
                                if (exited.ExitCode == 0)
                                {
                                    // Fix output folder mismatch if tool generated a folder named `folderName` instead of `targetPath`
                                    string generatedPath = Path.Combine(parentDir, folderName);
                                    if (Directory.Exists(generatedPath) && generatedPath != context.TargetPath)
                                    {
                                        if (Directory.Exists(context.TargetPath)) Directory.Delete(context.TargetPath, true);
                                        Directory.Move(generatedPath, context.TargetPath);
                                    }
                                    return true;
                                }
                                context.OnLog?.Invoke($"[Native CLI] Exited with code {exited.ExitCode}. Falling back to templates.");
                                return false;
                        }
                    }
                    return true;
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    context.OnLog?.Invoke("[Native CLI] Timed out after 10 min — the dependency install appears stalled. Falling back to local templates.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                context.OnLog?.Invoke($"[Native CLI Warning] Command failed: {ex.Message}. Falling back to templates.");
                return false;
            }
        }
    }
}
