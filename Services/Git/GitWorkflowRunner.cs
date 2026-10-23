using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>
    /// Materializes a <see cref="GitWorkflow"/> into a dynamically generated script (a <c>.bat</c> on
    /// Windows, <c>.sh</c> elsewhere) and runs it, streaming combined output (git-sync-engine.md §6).
    /// The safety scanner runs first: a workflow containing destructive/outward patterns is blocked
    /// unless the caller passes <paramref name="approved"/>. Placeholders <c>{{key}}</c> in commands are
    /// resolved from <paramref name="args"/> at run time — secrets are never baked into the script.
    /// </summary>
    public sealed class GitWorkflowRunner
    {
        public async Task<GitWorkflowResult> RunAsync(
            GitWorkflow wf,
            IDictionary<string, string>? args = null,
            bool approved = false,
            CancellationToken ct = default)
        {
            var result = new GitWorkflowResult();

            if (string.IsNullOrWhiteSpace(wf.Repo) || !Directory.Exists(wf.Repo))
            {
                result.Error = $"workflow repo does not exist: '{wf.Repo}'";
                return result;
            }

            // Safety gate — destructive patterns require explicit approval.
            result.Flags = GitWorkflowSafety.Scan(wf);
            if (result.Flags.Count > 0 && !approved)
            {
                result.Error = "blocked by safety scanner (destructive steps require approval)";
                return result;
            }

            var isWin = OperatingSystem.IsWindows();
            var script = isWin ? BuildBat(wf, args) : BuildSh(wf, args);
            var ext = isWin ? ".bat" : ".sh";
            var scriptPath = Path.Combine(Path.GetTempPath(), $"syncro_wf_{wf.Name}_{Guid.NewGuid():N}{ext}");
            result.ScriptPath = scriptPath;

            try
            {
                await File.WriteAllTextAsync(scriptPath, script, ct);

                var psi = isWin
                    ? new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
                    : new ProcessStartInfo("bash", $"\"{scriptPath}\"");
                psi.WorkingDirectory = wf.Repo;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.Environment["GIT_TERMINAL_PROMPT"] = "0";   // never hang on a prompt

                using var proc = Process.Start(psi);
                if (proc == null) { result.Error = "failed to start script"; return result; }

                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(ct);
                result.Output = await outTask;
                result.Error = await errTask;
                result.ExitCode = proc.ExitCode;
                result.Ok = proc.ExitCode == 0;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }

        private static string BuildBat(GitWorkflow wf, IDictionary<string, string>? args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine($"cd /d \"{wf.Repo}\"");
            foreach (var step in wf.Steps)
            {
                var line = Line(step, args);
                sb.AppendLine($"echo [syncro] {line}");
                sb.AppendLine(line);
                if (!step.ContinueOnError)
                    sb.AppendLine("if errorlevel 1 exit /b %errorlevel%");
            }
            return sb.ToString();
        }

        private static string BuildSh(GitWorkflow wf, IDictionary<string, string>? args)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#!/usr/bin/env bash");
            if (!wf.Steps.TrueForAll(s => s.ContinueOnError)) sb.AppendLine("set -e");
            sb.AppendLine($"cd \"{wf.Repo}\"");
            foreach (var step in wf.Steps)
            {
                var line = Line(step, args);
                sb.AppendLine($"echo \"[syncro] {line}\"");
                sb.AppendLine(step.ContinueOnError ? $"{line} || true" : line);
            }
            return sb.ToString();
        }

        private static string Line(GitWorkflowStep step, IDictionary<string, string>? args)
        {
            var cmd = Resolve(step.Command, args);
            return step.Kind == "git" ? $"git {cmd}" : cmd;
        }

        private static string Resolve(string command, IDictionary<string, string>? args)
        {
            if (args == null) return command;
            foreach (var (k, v) in args)
                command = command.Replace("{{" + k + "}}", v);
            return command;
        }
    }
}
