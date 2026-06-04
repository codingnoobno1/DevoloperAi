using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers.Git
{
    public class GitBranchService
    {
        private readonly ProcessRunner _runner;

        public GitBranchService(ProcessRunner runner)
        {
            _runner = runner;
        }

        public async Task CreateBranch(string name, string path)
        {
            await _runner.Run($"git branch {name}", path);
        }

        public async Task Checkout(string name, string path)
        {
            await _runner.Run($"git checkout {name}", path);
        }

        public async Task Merge(string source, string path)
        {
            await _runner.Run($"git merge {source}", path);
        }
    }
}
