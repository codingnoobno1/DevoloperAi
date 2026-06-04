using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class DartProvider : IProjectProvider
    {
        private readonly ProcessRunner _runner;

        public DartProvider(ProcessRunner runner)
        {
            _runner = runner;
        }

        public string StackName => "dart";

        public async Task<bool> Create(string name, string targetPath)
        {
            var result = await _runner.Run($"dart create {name}", targetPath);
            return result == 0;
        }
    }
}
