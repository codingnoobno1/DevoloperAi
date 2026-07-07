using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.AgentCli.Scripts;

public record RunResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Boundary over process execution (kept abstract so the core is testable / swappable).</summary>
public interface IProcessExecutor
{
    Task<RunResult> RunAsync(string fileName, string args, string workingDir,
                             IDictionary<string, string>? env, CancellationToken ct);
}

/// <summary>Default executor over System.Diagnostics.Process (no shell, no window, cancellable).</summary>
public sealed class ProcessExecutor : IProcessExecutor
{
    public async Task<RunResult> RunAsync(string fileName, string args, string workingDir,
                                          IDictionary<string, string>? env, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Environment.CurrentDirectory : workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (env != null)
            foreach (var (k, v) in env) psi.Environment[k] = v;

        using var proc = new Process { StartInfo = psi };
        var so = new StringBuilder();
        var se = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) so.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) se.AppendLine(e.Data); };

        try
        {
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return new RunResult(proc.ExitCode, so.ToString(), se.ToString());
        }
        catch (Exception ex)
        {
            return new RunResult(-1, so.ToString(), se.ToString() + ex.Message);
        }
    }
}
