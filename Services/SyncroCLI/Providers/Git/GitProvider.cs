using System;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers.Git
{
    public class GitProvider
    {
        private readonly ProcessRunner _runner;
        public GitRepoService Repo { get; }
        public GitChangeService Changes { get; }
        public GitBranchService Branches { get; }
        public GitErrorHandler ErrorHandler { get; }

        public GitProvider(ProcessRunner runner)
        {
            _runner = runner;
            Repo = new GitRepoService(runner);
            Changes = new GitChangeService(runner);
            Branches = new GitBranchService(runner);
            ErrorHandler = new GitErrorHandler();
        }

        public async Task ExecuteCustom(string cmd, string path)
        {
            await _runner.Run($"git {cmd}", path);
        }
    }
}
