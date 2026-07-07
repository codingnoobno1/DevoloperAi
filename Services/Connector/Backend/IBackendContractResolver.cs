using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Resolves the API surface of a backend repo into a normalized <see cref="BackendContract"/>.
    /// Resolution order: a swagger/openapi contract if present, otherwise a source route-scan,
    /// otherwise an empty contract (the connector will offer to AI-generate routes in a later phase).
    /// </summary>
    public interface IBackendContractResolver
    {
        Task<BackendContract> ResolveAsync(string backendPath, CancellationToken ct = default);
    }
}
