using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class GoProvider : IProjectProvider
    {
        private readonly ProcessRunner _runner;

        public GoProvider(ProcessRunner runner)
        {
            _runner = runner;
        }

        public string StackName => "go";

        public async Task<bool> Create(string name, string targetPath)
        {
            try
            {
                string projectDir = Path.Combine(targetPath, name);
                if (!Directory.Exists(projectDir)) Directory.CreateDirectory(projectDir);
                await _runner.Run($"go mod init {name}", projectDir);
                await File.WriteAllTextAsync(Path.Combine(projectDir, "main.go"), "package main\n\nimport \"fmt\"\n\nfunc main() {\n    fmt.WriteLine(\"Hello from Go!\")\n}");
                return true;
            }
            catch { return false; }
        }
    }
}
