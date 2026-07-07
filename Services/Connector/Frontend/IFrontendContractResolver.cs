using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Frontend
{
    /// <summary>
    /// Extracts the API-call surface of a frontend repo into a normalized <see cref="FrontendContract"/>.
    /// Detects the frontend stack (Flutter / Next / React / Vite / Angular / Blazor) and runs the
    /// matching extractor; falls back to trying every extractor when the stack is unclear.
    /// </summary>
    public interface IFrontendContractResolver
    {
        Task<FrontendContract> ResolveAsync(string frontendPath, CancellationToken ct = default);
    }
}
