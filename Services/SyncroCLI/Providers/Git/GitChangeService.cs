using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers.Git
{
    public class GitChangeService
    {
        private readonly ProcessRunner _runner;

        public GitChangeService(ProcessRunner runner)
        {
            _runner = runner;
        }

        public async Task Add(string pattern, string path)
        {
            await _runner.Run($"git add {pattern}", path);
        }

        public async Task Commit(string message, string path)
        {
            await _runner.Run($"git commit -m \"{message}\"", path);
        }

        public async Task Push(string remote, string branch, string path)
        {
            await _runner.Run($"git push {remote} {branch}", path);
        }

        public async Task Pull(string remote, string branch, string path)
        {
            await _runner.Run($"git pull {remote} {branch}", path);
        }
    }
}
