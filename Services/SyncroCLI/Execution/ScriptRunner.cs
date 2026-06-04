using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core.Platform;

namespace Syncro.Desktop.Services.SyncroCLI.Execution
{
    public class ScriptRunner
    {
        private readonly IPlatformService _platform;
        private readonly ProcessRunner _runner;

        public ScriptRunner(IPlatformService platform, ProcessRunner runner)
        {
            _platform = platform;
            _runner = runner;
        }

        public async Task<int> RunScript(string scriptName, string? subDir = null, string? password = null)
        {
            var platformDir = _platform.Type.ToString(); // Windows, Linux, Mac
            var fileName = $"{scriptName}{_platform.ScriptExtension}";
            
            var baseDir = AppContext.BaseDirectory;
            var projectRoot = Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.FullName ?? baseDir;
            
            // Path structure: Marketplace/Scripts/{platform}/{fileName}
            var basePath = Path.Combine(projectRoot, "Marketplace", "Scripts", platformDir);
            
            var scriptPath = Path.Combine(basePath, fileName);

            if (!File.Exists(scriptPath))
            {
                // Try subDir if provided
                if (!string.IsNullOrEmpty(subDir))
                {
                    scriptPath = Path.Combine(basePath, subDir, fileName);
                }
            }

            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Script not found: {scriptPath}");
            }

            return await _runner.Run(scriptPath, Directory.GetCurrentDirectory(), password);
        }
    }
}
