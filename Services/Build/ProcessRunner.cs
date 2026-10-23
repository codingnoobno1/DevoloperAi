using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>
    /// Shared toolchain-process launcher for <see cref="BuildEngine"/> and <see cref="TestEngine"/>.
    /// Exists so the two non-obvious requirements — event-driven stream reads and a kill-tree timeout —
    /// are implemented once rather than copied.
    /// </summary>
    internal static class ProcessRunner
    {
        /// <summary>Hard ceiling so a hung toolchain can never wedge the reactor.</summary>
        public const int DefaultTimeoutMs = 5 * 60 * 1000;

        public static async Task<(int ExitCode, string Output)> RunAsync(
            string exe, string args, string workingDir, CancellationToken ct, int timeoutMs = DefaultTimeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var sb = new StringBuilder();

            // Read both streams via events — reading them sequentially deadlocks on a full pipe buffer.
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            string output;
            lock (sb) output = sb.ToString();
            return (proc.ExitCode, output);
        }
    }
}
