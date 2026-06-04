using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers.Git
{
    public class GitRepoService
    {
        private readonly ProcessRunner _runner;

        public GitRepoService(ProcessRunner runner)
        {
            _runner = runner;
        }

        public async Task Init(string path)
        {
            await _runner.Run("git init", path);
        }

        public async Task Clone(string url, string path)
        {
            await _runner.Run($"git clone {url}", path);
        }
    }
}
