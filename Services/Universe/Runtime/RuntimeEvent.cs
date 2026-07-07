using System.Collections.Generic;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>What a background worker observed (runtime.md §4). Workers publish these to the
    /// <see cref="IEventBus"/>; the graph updater and dashboard both consume them.</summary>
    public enum RuntimeEventKind
    {
        ProcessStarted,
        ProcessHealthy,
        ProcessCrashed,
        ProcessStopped,
        PortOpened,
        Metric,             // cpu/mem/latency sample for a process
        ApiDiscovered,      // an endpoint/call worker found something (indexer owns the node)
        MappingChanged,
        TestCompleted,
        BuildCompleted,
        ContainerStatus,
        SchemaChanged,
        DependencyMissing,
        DependencyInstalled,
        GitChanged
    }

    /// <summary>
    /// One immutable fact from a worker. <see cref="TargetNodeId"/> is the graph node this event is
    /// about (may be newly created by the updater); <see cref="Data"/> carries status/metrics/etc as
    /// flat strings so it maps straight into a node's Meta.
    /// </summary>
    public sealed class RuntimeEvent
    {
        public string WorkerId { get; init; } = "";
        public string WorkspaceId { get; init; } = "";
        public RuntimeEventKind Kind { get; init; }
        public string TargetNodeId { get; init; } = "";
        public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();

        public static RuntimeEvent Create(
            string workerId, string workspaceId, RuntimeEventKind kind, string targetNodeId,
            IReadOnlyDictionary<string, string>? data = null) => new()
            {
                WorkerId = workerId,
                WorkspaceId = workspaceId,
                Kind = kind,
                TargetNodeId = targetNodeId,
                Data = data ?? new Dictionary<string, string>()
            };
    }
}
