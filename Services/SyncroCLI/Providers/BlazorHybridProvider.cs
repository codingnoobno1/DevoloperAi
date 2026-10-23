using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class BlazorHybridProvider : IProjectProvider
    {
        private readonly ProcessRunner _runner;

        public BlazorHybridProvider(ProcessRunner runner)
        {
            _runner = runner;
        }

        public string StackName => "blazor-hybrid";

        public async Task<bool> Create(string name, string targetPath)
        {
            // Sanitize name
            string sanitizedName = name.ToLower().Replace(" ", "-").Replace("_", "-");
            
            // Execute dotnet new maui-blazor command
            var result = await _runner.Run($"dotnet new maui-blazor -n {sanitizedName} -o {sanitizedName}", targetPath);
            return result == 0;
        }
    }
}
