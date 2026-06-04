using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class JavaGradleProvider : IProjectProvider
    {
        private readonly ProcessRunner _runner;

        public JavaGradleProvider(ProcessRunner runner)
        {
            _runner = runner;
        }

        public string StackName => "java_gradle";

        public async Task<bool> Create(string name, string targetPath)
        {
            string projectDir = Path.Combine(targetPath, name);
            if (!Directory.Exists(projectDir)) Directory.CreateDirectory(projectDir);
            var result = await _runner.Run("gradle init --type java-application", projectDir);
            return result == 0;
        }
    }
}
