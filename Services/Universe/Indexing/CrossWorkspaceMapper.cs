using System;
using System.Collections.Generic;
using System.Linq;
using Syncro.Desktop.Services.Connector;
using Syncro.Desktop.Services.Universe.Models;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe.Indexing
{
    public sealed class EndpointConsumers
    {
        public string Endpoint { get; set; } = "";     // "GET /api/products"
        public string Workspace { get; set; } = "";     // the providing workspace
        public List<string> Consumers { get; set; } = new(); // consuming workspace names
    }

    public sealed class MapSummary
    {
        public int CrossEdges { get; set; }
        public int WorkspaceDependencies { get; set; }
        public List<EndpointConsumers> Endpoints { get; set; } = new();
    }

    /// <summary>
    /// The N-consumer mapper (runtime.md R5, universe.md U3). Works purely from the graph — no
    /// re-scanning: it reads every <see cref="NodeKind.ApiCall"/> and <see cref="NodeKind.Endpoint"/>
    /// node and links each call to the endpoint(s) it consumes *in other workspaces*, writing
    /// cross-workspace <c>Consumes</c> edges plus workspace-level <c>DependsOn</c> edges. That's the
    /// thing no single-workspace IDE has: "GET /api/products is consumed by Flutter ✓, Next ✓, Desktop ✗".
    /// Matching mirrors the Connector's: exact canonical path first, then base-path suffix.
    /// </summary>
    public sealed class CrossWorkspaceMapper
    {
        private readonly UniverseGraphStore _store;

        public CrossWorkspaceMapper(UniverseGraphStore store)
        {
            _store = store;
        }

        public MapSummary MapAll()
        {
            var summary = new MapSummary();

            var endpoints = _store.NodesByKind(NodeKind.Endpoint);
            var calls = _store.NodesByKind(NodeKind.ApiCall);
            if (endpoints.Count == 0 || calls.Count == 0)
                return summary;

            var epInfos = endpoints.Select(e => new EpInfo(
                e,
                Method(e),
                PathNormalizer.Canonical(Path(e)),
                PathNormalizer.CanonicalSegments(Path(e)))).ToList();

            var wsDeps = new HashSet<(string, string)>();

            foreach (var call in calls)
            {
                var cMethod = Method(call);
                var cCanon = PathNormalizer.Canonical(Path(call));
                var cSegs = PathNormalizer.CanonicalSegments(Path(call));

                foreach (var ep in epInfos)
                {
                    if (ep.Node.WorkspaceId == call.WorkspaceId) continue; // intra handled by the indexer
                    if (!string.Equals(ep.Method, cMethod, StringComparison.OrdinalIgnoreCase)) continue;

                    bool exact = ep.Canon == cCanon;
                    bool suffix = !exact && (PathNormalizer.SuffixAligned(cSegs, ep.Segs) || PathNormalizer.SuffixAligned(ep.Segs, cSegs));
                    if (!exact && !suffix) continue;

                    _store.UpsertEdge(new GraphEdge
                    {
                        FromId = call.Id,
                        ToId = ep.Node.Id,
                        Kind = EdgeKind.Consumes,
                        Confidence = exact ? 1.0 : 0.7,
                        Source = "xws"
                    });
                    summary.CrossEdges++;

                    if (wsDeps.Add((call.WorkspaceId, ep.Node.WorkspaceId)))
                    {
                        _store.UpsertEdge(new GraphEdge
                        {
                            FromId = $"ws:{call.WorkspaceId}",
                            ToId = $"ws:{ep.Node.WorkspaceId}",
                            Kind = EdgeKind.DependsOn,
                            Source = "xws"
                        });
                    }
                }
            }

            summary.WorkspaceDependencies = wsDeps.Count;

            // Build the per-endpoint consumer report (who calls each endpoint, across the universe).
            foreach (var ep in endpoints)
            {
                var consumers = _store.InEdges(ep.Id)
                    .Where(e => e.Kind == EdgeKind.Consumes)
                    .Select(e => _store.GetNode(e.FromId))
                    .Where(n => n != null)
                    .Select(n => WorkspaceName(n!.WorkspaceId))
                    .Distinct()
                    .ToList();

                if (consumers.Count > 0)
                {
                    summary.Endpoints.Add(new EndpointConsumers
                    {
                        Endpoint = ep.Name,
                        Workspace = WorkspaceName(ep.WorkspaceId),
                        Consumers = consumers
                    });
                }
            }

            return summary;
        }

        private string WorkspaceName(string workspaceId)
        {
            var ws = _store.GetWorkspace(workspaceId);
            return ws?.Name ?? workspaceId;
        }

        private static string Method(GraphNode n) => n.Meta.TryGetValue("method", out var m) ? m : "GET";
        private static string Path(GraphNode n) => n.Meta.TryGetValue("path", out var p) ? p : "/";

        private sealed record EpInfo(GraphNode Node, string Method, string Canon, string[] Segs);
    }
}
