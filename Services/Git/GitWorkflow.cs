using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>One step in a workflow: a git command or an arbitrary shell command.</summary>
    public sealed class GitWorkflowStep
    {
        /// <summary>"git" | "shell".</summary>
        public string Kind { get; set; } = "git";
        /// <summary>For git: the args after "git" (e.g. "fetch --all --prune"). For shell: the command line.</summary>
        public string Command { get; set; } = "";
        /// <summary>Keep going if this step fails (default: stop the workflow).</summary>
        public bool ContinueOnError { get; set; }
    }

    /// <summary>An ordered, named, reusable sequence of git + shell steps.</summary>
    public sealed class GitWorkflow
    {
        public string Name { get; set; } = "workflow";
        public List<GitWorkflowStep> Steps { get; set; } = new();
        /// <summary>Repo the workflow runs in. {{placeholders}} in commands are resolved at run time.</summary>
        public string Repo { get; set; } = "";
    }

    /// <summary>Outcome of materializing + running a workflow.</summary>
    public sealed class GitWorkflowResult
    {
        public bool Ok { get; set; }
        public int ExitCode { get; set; }
        public string ScriptPath { get; set; } = "";
        public string Output { get; set; } = "";
        public string Error { get; set; } = "";
        /// <summary>Destructive patterns the safety scanner flagged (blocks the run unless approved).</summary>
        public List<string> Flags { get; set; } = new();
        public bool Blocked => Flags.Count > 0;
    }

    /// <summary>
    /// Static safety scan for a workflow's commands. Mirrors ScriptSafetyScanner's intent for the git
    /// domain: destructive/outward patterns (hard reset, force push, history rewrite, recursive delete)
    /// are flagged and require explicit approval before a run.
    /// </summary>
    public static class GitWorkflowSafety
    {
        private static readonly (string pattern, string why)[] Rules =
        {
            ("reset --hard", "hard reset discards uncommitted work"),
            ("push --force", "force push can overwrite remote history"),
            ("push -f", "force push can overwrite remote history"),
            ("push --force-with-lease", "force push can overwrite remote history"),
            ("clean -fd", "recursive clean deletes untracked files"),
            ("rebase", "rebase rewrites history"),
            ("filter-branch", "history rewrite"),
            ("branch -D", "force branch delete"),
            ("rm -rf", "recursive delete"),
            ("rmdir /s", "recursive delete"),
        };

        public static List<string> Scan(GitWorkflow wf)
        {
            var flags = new List<string>();
            foreach (var step in wf.Steps)
            {
                var cmd = step.Command.ToLowerInvariant();
                foreach (var (pattern, why) in Rules)
                    if (cmd.Contains(pattern))
                        flags.Add($"\"{step.Command.Trim()}\" — {why}");
            }
            return flags.Distinct().ToList();
        }
    }
}
