using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Storage
{
    /// <summary>
    /// Reads and writes <c>connector.json</c> (the machine-readable contract) and a generated
    /// <c>connector.md</c> human summary, both in the connected project's root — see connector.md
    /// ("connector.json — the project contract").
    /// </summary>
    public interface IConnectorProjectStore
    {
        Task<ConnectorProject?> LoadAsync(string projectRoot, CancellationToken ct = default);

        Task SaveAsync(string projectRoot, ConnectorProject project, CancellationToken ct = default);

        /// <summary>Loads (or creates) the project at <paramref name="projectRoot"/> and fills its
        /// backend section from a freshly resolved <see cref="BackendContract"/>, preserving any
        /// existing frontend section untouched. Saves connector.json + connector.md afterward.</summary>
        Task<ConnectorProject> UpsertBackendAsync(string projectRoot, BackendContract contract, string? runCommand = null, int? port = null, CancellationToken ct = default);

        Task WriteMarkdownSummaryAsync(string projectRoot, ConnectorProject project, CancellationToken ct = default);
    }
}
