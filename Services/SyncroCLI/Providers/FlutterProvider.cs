using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class FlutterProvider : IProjectProvider
    {
        private readonly ProcessRunner _runner;

        public FlutterProvider(ProcessRunner runner)
        {
            _runner = runner;
        }

        public string StackName => "flutter";

        public async Task<bool> Create(string name, string targetPath)
        {
            // Sanitize name for Flutter project-name (lowercase, underscores)
            string sanitizedName = name.ToLower().Replace(" ", "_").Replace("-", "_");
            
            // Proper Flutter create command with org and project name
            var result = await _runner.Run($"flutter create --org com.syncro --project-name {sanitizedName} {name}", targetPath);
            return result == 0;
        }
    }
}
