using System;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;
using Syncro.Desktop.Services.SyncroCLI.Providers.Git;

namespace Syncro.Desktop.Services.SyncroCLI.Commands
{
    public class GitCommand : ICliCommand
    {
        private readonly GitProvider _git;
        private readonly Action<string> _logger;

        public GitCommand(GitProvider git, Action<string> logger)
        {
            _git = git;
            _logger = logger;
        }

        public string Name => "git";
        public string Description => "Execute Git commands. Usage: syncro git <subcommand> [args]";

        public async Task Execute(CommandContext context)
        {
            if (context.Args.Count == 0)
            {
                _logger("Usage: syncro git <init|commit|push|pull|branch|checkout|merge> [args]");
                return;
            }

            string subCommand = context.Args[0].ToLower();
            string[] subArgs = context.Args.Skip(1).ToArray();
            string currentPath = Environment.CurrentDirectory; // Or workspace path

            try
            {
                switch (subCommand)
                {
                    case "init":
                        await _git.Repo.Init(currentPath);
                        break;
                    case "commit":
                        string msg = subArgs.Length > 0 ? subArgs[0] : "Automated commit from Syncro";
                        await _git.Changes.Commit(msg, currentPath);
                        break;
                    case "push":
                        string remotePush = subArgs.Length > 0 ? subArgs[0] : "origin";
                        string branchPush = subArgs.Length > 1 ? subArgs[1] : "main";
                        await _git.Changes.Push(remotePush, branchPush, currentPath);
                        break;
                    case "pull":
                        string remotePull = subArgs.Length > 0 ? subArgs[0] : "origin";
                        string branchPull = subArgs.Length > 1 ? subArgs[1] : "main";
                        await _git.Changes.Pull(remotePull, branchPull, currentPath);
                        break;
                    case "status":
                        await _git.ExecuteCustom("status", currentPath);
                        break;
                    default:
                        // Fallback for any other git command
                        string fullCmd = string.Join(" ", context.Args);
                        await _git.ExecuteCustom(fullCmd.Replace("git ", ""), currentPath);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger(_git.ErrorHandler.AnalyzeError(ex.Message));
            }
        }
    }
}
