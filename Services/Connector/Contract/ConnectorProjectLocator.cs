using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Storage;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>Reads <c>connector.json</c> via <see cref="IConnectorProjectStore"/> and returns the
    /// backend/frontend paths, resolved to absolute against the workspace root.</summary>
    public sealed class ConnectorProjectLocator : IContractProjectLocator
    {
        private readonly IConnectorProjectStore _store;

        public ConnectorProjectLocator(IConnectorProjectStore store) => _store = store;

        public async Task<(string? BackendPath, string? FrontendPath)> LocateAsync(string workspaceRoot)
        {
            var project = await _store.LoadAsync(workspaceRoot);
            if (project == null) return (null, null);
            return (Resolve(workspaceRoot, project.Backend?.Path), Resolve(workspaceRoot, project.Frontend?.Path));
        }

        private static string? Resolve(string root, string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        }
    }
}
