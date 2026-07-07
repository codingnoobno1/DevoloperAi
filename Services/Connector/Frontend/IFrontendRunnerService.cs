using System;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Connector.Frontend
{
    public interface IFrontendRunnerService
    {
        event Action<string> OnOutput;
        event Action<string> OnReady; // Emits the localhost URL when detected
        
        Task<bool> StartAsync(string projectPath, string stack);
        Task StopAsync();
        Task RestartAsync();
        Task ReloadAsync();
    }
}
