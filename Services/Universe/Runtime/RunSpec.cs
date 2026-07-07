using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>What to run for one stack (runtime.md R2). Built from a stack's <c>run:</c> command
    /// (stacks.json / template front-matter) with <c>{PORT}</c> already substituted.</summary>
    public sealed class RunSpec
    {
        public string WorkspaceId { get; set; } = "";
        public string Label { get; set; } = "";            // e.g. "server", "mobile"
        public string WorkingDirectory { get; set; } = "";
        public string Command { get; set; } = "";          // executable, e.g. "npm"
        public string Arguments { get; set; } = "";         // e.g. "run dev -- -p 5000"
        public int Port { get; set; }
        public Dictionary<string, string?> Env { get; set; } = new();

        /// <summary>Optional explicit install command; if null the bootstrapper detects one from the manifest.</summary>
        public string? InstallCommand { get; set; }

        /// <summary>Skip the pre-run dependency bootstrap (e.g. when the caller already installed).</summary>
        public bool SkipBootstrap { get; set; }

        /// <summary>Dependency wave for group runs: 0 = database/infra, 1 = backend, 2 = frontend.
        /// Lower waves start (and become healthy) before higher ones.</summary>
        public int Wave { get; set; } = 1;

        /// <summary>Split a full command line ("npm run dev -- -p 5000") into a spec.</summary>
        public static RunSpec FromCommandLine(string workspaceId, string label, string workingDir, string fullCommand, int port)
        {
            var trimmed = (fullCommand ?? "").Trim();
            int space = trimmed.IndexOf(' ');
            return new RunSpec
            {
                WorkspaceId = workspaceId,
                Label = label,
                WorkingDirectory = workingDir,
                Command = space > -1 ? trimmed.Substring(0, space) : trimmed,
                Arguments = space > -1 ? trimmed.Substring(space + 1) : "",
                Port = port
            };
        }
    }

    /// <summary>A snapshot of one running (or recently-exited) process — what the dashboard tile shows.</summary>
    public sealed class RunStatus
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string WorkspaceId { get; set; } = "";
        public string State { get; set; } = "starting"; // starting | running | healthy | crashed | stopped
        public int? Pid { get; set; }
        public int Port { get; set; }
        public DateTime StartedAt { get; set; }
        public double CpuPercent { get; set; }
        public double MemoryMb { get; set; }
    }
}
