using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Universe.Indexing
{
    /// <summary>
    /// Single source of truth for deterministic Universe-graph node ids that more than one indexer
    /// needs to agree on. The Reverse-AST contributor and the <see cref="WorkspaceIndexer"/> both
    /// derive <c>ApiCall</c>/<c>Endpoint</c> ids from here so their nodes and edges line up exactly.
    /// </summary>
    internal static class GraphIds
    {
        public static string Endpoint(string wsId, HttpVerb method, string path) =>
            $"ep:{wsId}:{method.ToString().ToUpperInvariant()}:{path}";

        public static string Call(string wsId, HttpVerb method, string path) =>
            $"call:{wsId}:{method.ToString().ToUpperInvariant()}:{path}";

        /// <summary>Flutter/service class node (distinct from the backend <c>svc:{wsId}</c> service node).</summary>
        public static string ServiceClass(string wsId, string name) => $"svc:{wsId}:{name}";

        /// <summary>Cross-cutting remote server keyed by host, so many frontends can point at one backend.</summary>
        public static string RemoteServer(string host) => $"remote:{host}";
    }
}
