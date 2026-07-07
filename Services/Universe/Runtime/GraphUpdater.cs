using System.Threading.Tasks;
using Syncro.Desktop.Services.Universe.Models;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// Subscribes to the <see cref="IEventBus"/> and folds each <see cref="RuntimeEvent"/> into the
    /// persistent graph (runtime.md §4) — so the last-known runtime state survives restarts and the
    /// dashboard/LLM read it without re-observing. Generic by design: an event maps to a node kind +
    /// status, and its <c>Data</c> flows straight into the node's Meta.
    /// </summary>
    public sealed class GraphUpdater
    {
        private readonly UniverseGraphStore _store;

        public GraphUpdater(UniverseGraphStore store)
        {
            _store = store;
        }

        public void Attach(IEventBus bus) => bus.Subscribe(HandleAsync);

        public Task HandleAsync(RuntimeEvent evt)
        {
            var (kind, status) = Map(evt.Kind);
            if (kind == null)
                return Task.CompletedTask; // pure signal (e.g. ApiDiscovered) — indexer owns the node

            var node = _store.GetNode(evt.TargetNodeId) ?? new GraphNode
            {
                Id = evt.TargetNodeId,
                Kind = kind.Value,
                WorkspaceId = evt.WorkspaceId
            };

            node.Kind = kind.Value;
            if (!string.IsNullOrEmpty(evt.WorkspaceId)) node.WorkspaceId = evt.WorkspaceId;
            if (status != null) node.Meta["status"] = status;
            node.Meta["lastEvent"] = evt.Kind.ToString();

            foreach (var kv in evt.Data)
                node.Meta[kv.Key] = kv.Value;

            if (string.IsNullOrEmpty(node.Name))
                node.Name = evt.Data.TryGetValue("name", out var n) ? n : evt.TargetNodeId;

            _store.UpsertNode(node);

            // A process announcing a port gets a RunsOn edge to a lightweight port node.
            if (kind == NodeKind.Process && evt.Data.TryGetValue("port", out var port) && !string.IsNullOrEmpty(port))
            {
                var portId = $"port:{port}";
                _store.UpsertNode(new GraphNode { Id = portId, Kind = NodeKind.EnvVar, Name = $"port {port}" });
                _store.UpsertEdge(new GraphEdge { FromId = node.Id, ToId = portId, Kind = EdgeKind.RunsOn, Source = "runtime" });
            }

            return Task.CompletedTask;
        }

        private static (NodeKind? kind, string? status) Map(RuntimeEventKind kind) => kind switch
        {
            RuntimeEventKind.ProcessStarted => (NodeKind.Process, "starting"),
            RuntimeEventKind.ProcessHealthy => (NodeKind.Process, "healthy"),
            RuntimeEventKind.ProcessCrashed => (NodeKind.Process, "crashed"),
            RuntimeEventKind.ProcessStopped => (NodeKind.Process, "stopped"),
            RuntimeEventKind.PortOpened => (NodeKind.Process, null),
            RuntimeEventKind.Metric => (NodeKind.Process, null),
            RuntimeEventKind.TestCompleted => (NodeKind.TestRun, null),
            RuntimeEventKind.BuildCompleted => (NodeKind.Process, null),
            RuntimeEventKind.ContainerStatus => (NodeKind.Container, null),
            RuntimeEventKind.SchemaChanged => (NodeKind.SchemaObject, null),
            RuntimeEventKind.DependencyMissing => (NodeKind.Package, "missing"),
            RuntimeEventKind.DependencyInstalled => (NodeKind.Package, "installed"),
            RuntimeEventKind.GitChanged => (NodeKind.GitState, null),
            _ => (null, null) // ApiDiscovered / MappingChanged — handled by the indexer
        };
    }
}
