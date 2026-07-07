using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Syncro.CLI;

/// <summary>Wraps PowerShell and Bash execution with streaming, async, and result capture.</summary>
public static class ShellRunner
{
    // ── Result record ────────────────────────────────────────────────────────
    public record ShellResult(int ExitCode, string Stdout, string Stderr)
    {
        public bool Success => ExitCode == 0;
        public string Output => string.IsNullOrWhiteSpace(Stderr)
            ? Stdout
            : $"{Stdout}\n[STDERR] {Stderr}";
    }

    // ── PowerShell ───────────────────────────────────────────────────────────

    /// <summary>Runs a PowerShell command and returns captured output.</summary>
    public static ShellResult RunPowershell(string command, string? workingDir = null)
    {
        string shell = FindPwsh();
        var psi = BuildPsi(shell,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{Escape(command)}\"",
            workingDir);

        return RunCapture(psi);
    }

    /// <summary>Runs a .ps1 file with optional arguments.</summary>
    public static ShellResult RunPs1File(string scriptPath, string? args = null, string? workingDir = null)
    {
        string shell = FindPwsh();
        string argStr = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"";
        if (!string.IsNullOrWhiteSpace(args)) argStr += $" {args}";
        return RunCapture(BuildPsi(shell, argStr, workingDir ?? Path.GetDirectoryName(scriptPath)));
    }

    // ── Bash ─────────────────────────────────────────────────────────────────

    /// <summary>Runs a bash command via Git Bash or WSL. Falls back to cmd.</summary>
    public static ShellResult RunBash(string command, string? workingDir = null)
    {
        (string shell, string prefix) = FindBash();
        string argStr = prefix + $"-c \"{Escape(command)}\"";
        return RunCapture(BuildPsi(shell, argStr, workingDir));
    }

    /// <summary>Runs a .sh file.</summary>
    public static ShellResult RunShFile(string scriptPath, string? args = null, string? workingDir = null)
    {
        (string shell, string prefix) = FindBash();
        string argStr = prefix + $"\"{scriptPath}\"";
        if (!string.IsNullOrWhiteSpace(args)) argStr += $" {args}";
        return RunCapture(BuildPsi(shell, argStr, workingDir ?? Path.GetDirectoryName(scriptPath)));
    }

    // ── Streaming ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a command and yields each output line as it arrives.
    /// Optionally pushes each line to an AgentBridge for Named Pipe streaming.
    /// </summary>
    public static async IAsyncEnumerable<string> StreamPowershell(
        string command,
        string? workingDir = null,
        AgentBridge? bridge = null)
    {
        string shell = FindPwsh();
        var psi = BuildPsi(shell,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{Escape(command)}\"",
            workingDir);

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError  = true;

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell.");

        string? line;
        while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
        {
            if (bridge != null) await bridge.SendAsync(new { type = "output", line });
            yield return line;
        }
        await proc.WaitForExitAsync();

        string errTail = await proc.StandardError.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(errTail))
            yield return $"[STDERR] {errTail.Trim()}";
    }

    // ── Elevated ─────────────────────────────────────────────────────────────

    /// <summary>Relaunches the given exe with verb=runas (UAC elevation).</summary>
    public static void RunElevated(string exe, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName        = exe,
            Arguments       = arguments,
            UseShellExecute = true,
            Verb            = "runas"
        };
        Process.Start(psi);
    }

    // ── Tool version helper ──────────────────────────────────────────────────

    /// <summary>Returns (found, version) for any CLI tool.</summary>
    public static (bool Found, string Version) CheckTool(string tool, string versionArg = "--version")
    {
        try
        {
            var r = RunCapture(BuildPsi(tool, versionArg, null, capture: true));
            if (r.Success)
                return (true, r.Stdout.Trim().Split('\n')[0]);
            return (false, string.Empty);
        }
        catch { return (false, string.Empty); }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private static ShellResult RunCapture(ProcessStartInfo psi)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError  = true;
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return new ShellResult(-1, "", "Failed to start process.");
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return new ShellResult(proc.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
        }
        catch (Exception ex)
        {
            return new ShellResult(-1, "", ex.Message);
        }
    }

    private static ProcessStartInfo BuildPsi(string file, string args, string? workingDir, bool capture = false)
        => new()
        {
            FileName               = file,
            Arguments              = args,
            WorkingDirectory       = workingDir ?? Directory.GetCurrentDirectory(),
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = capture,
            RedirectStandardError  = capture
        };

    private static string FindPwsh()
    {
        // Prefer PowerShell 7
        if (File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe"))
            return @"C:\Program Files\PowerShell\7\pwsh.exe";
        return "powershell.exe";
    }

    private static (string shell, string prefix) FindBash()
    {
        string gitBash = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Git", "bin", "bash.exe");
        if (!File.Exists(gitBash))
            gitBash = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Git", "bin", "bash.exe");

        if (File.Exists(gitBash)) return (gitBash, "");

        string wsl = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        if (File.Exists(wsl)) return (wsl, "exec bash ");

        return ("cmd.exe", "/c ");
    }

    // Escape double-quotes inside a command string for CLI argument passing
    private static string Escape(string s) => s.Replace("\"", "\\\"");

    // Legacy compat — used by older callers until refactored
    public static string RunPowerShellCommand(string command, string workingDir)
        => RunPowershell(command, workingDir).Output;

    public static string RunBashCommand(string command, string workingDir)
        => RunBash(command, workingDir).Output;
}
