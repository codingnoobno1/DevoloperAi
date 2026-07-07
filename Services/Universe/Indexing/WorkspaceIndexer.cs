using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST;
using Syncro.Desktop.Services.AST.Models;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Connector.Matching;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Universe.Models;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe.Indexing
{
    public sealed class IndexResult
    {
        public string WorkspaceId { get; set; } = "";
        public string WorkspacePath { get; set; } = "";
        public int Files { get; set; }
        public int Symbols { get; set; }
        public int Endpoints { get; set; }
        public int Calls { get; set; }
        public int Edges { get; set; }
        public List<string> Stacks { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>
    /// Populates the Universe graph for one workspace (universe.md U2) by driving the indexers that
    /// already exist — the <see cref="AstService"/> for files/symbols and the Connector resolvers for
    /// endpoints/calls — and writing their output as graph nodes/edges. Intra-workspace call↔route
    /// matches become <c>Consumes</c> edges here; cross-workspace <c>Consumes</c> is U3.
    ///
    /// Re-index is upsert-based (deterministic ids), so running it again refreshes in place. Stale
    /// nodes from deleted files are U4's (incremental) concern.
    /// </summary>
    public sealed class WorkspaceIndexer
    {
        private readonly UniverseGraphStore _store;
        private readonly UniverseRegistry _registry;
        private readonly AstService _ast;
        private readonly IBackendContractResolver _backend;
        private readonly IFrontendContractResolver _frontend;

        private static readonly HashSet<AstNodeType> SymbolTypes = new()
        {
            AstNodeType.Class, AstNodeType.Interface, AstNodeType.Method, AstNodeType.Property, AstNodeType.Dto
        };

        public WorkspaceIndexer(
            UniverseGraphStore store,
            UniverseRegistry registry,
            AstService ast,
            IBackendContractResolver backend,
            IFrontendContractResolver frontend)
        {
            _store = store;
            _registry = registry;
            _ast = ast;
            _backend = backend;
            _frontend = frontend;
        }

        public async Task<IndexResult> IndexAsync(string workspacePath, CancellationToken ct = default)
        {
            var ws = _registry.Register(workspacePath);
            var wsId = ws.Id;
            var result = new IndexResult { WorkspaceId = wsId, WorkspacePath = workspacePath };

            var nodes = new List<GraphNode>();
            var edges = new List<GraphEdge>();
            var stacks = new List<string>();

            // ── AST: files + symbols ──────────────────────────────────────────────────────
            AstProjectMap? map = null;
            try { map = await _ast.ScanProjectAsync(workspacePath, ct); }
            catch (Exception ex) { result.Notes.Add($"AST scan failed: {ex.Message}"); }

            if (map != null)
            {
                if (!string.IsNullOrWhiteSpace(map.Language)) stacks.Add(map.Language);
                if (!string.IsNullOrWhiteSpace(map.Framework)) stacks.Add(map.Framework);

                var fileNodeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in map.Nodes
                             .Where(n => !n.IsExternalOrBoilerplate && !string.IsNullOrWhiteSpace(n.FilePath))
                             .Select(n => n.FilePath)
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var id = $"file:{wsId}:{path}";
                    fileNodeIds[path] = id;
                    nodes.Add(new GraphNode
                    {
                        Id = id, Kind = NodeKind.File, WorkspaceId = wsId,
                        Path = path, Name = System.IO.Path.GetFileName(path), Language = map.Language
                    });
                }
                result.Files = fileNodeIds.Count;

                foreach (var n in map.Nodes.Where(n => !n.IsExternalOrBoilerplate && SymbolTypes.Contains(n.Type)))
                {
                    var symId = $"sym:{wsId}:{n.Id}";
                    nodes.Add(new GraphNode
                    {
                        Id = symId, Kind = NodeKind.Symbol, WorkspaceId = wsId,
                        Path = n.FilePath, Name = n.Name, Language = map.Language,
                        Signature = BuildSignature(n),
                        Meta = { ["astType"] = n.Type.ToString(), ["line"] = n.LineNumber.ToString() }
                    });
                    result.Symbols++;

                    if (!string.IsNullOrWhiteSpace(n.FilePath) && fileNodeIds.TryGetValue(n.FilePath, out var fileId))
                        edges.Add(new GraphEdge { FromId = fileId, ToId = symId, Kind = EdgeKind.Contains, Source = "ast" });
                }
            }

            // ── Connector: endpoints (backend) ────────────────────────────────────────────
            BackendContract? backend = null;
            try { backend = await _backend.ResolveAsync(workspacePath, ct); }
            catch (Exception ex) { result.Notes.Add($"Backend resolve failed: {ex.Message}"); }

            if (backend != null && backend.Routes.Count > 0)
            {
                if (backend.Stack != BackendStack.Unknown) stacks.Add(backend.Stack.ToString());

                var svcId = $"svc:{wsId}";
                nodes.Add(new GraphNode
                {
                    Id = svcId, Kind = NodeKind.Service, WorkspaceId = wsId,
                    Path = workspacePath, Name = ws.Name, Meta = { ["stack"] = backend.Stack.ToString() }
                });

                foreach (var r in backend.Routes)
                {
                    var epId = EndpointId(wsId, r.Method, r.PathTemplate);
                    nodes.Add(new GraphNode
                    {
                        Id = epId, Kind = NodeKind.Endpoint, WorkspaceId = wsId,
                        Path = r.FilePath, Name = $"{r.Method.ToString().ToUpperInvariant()} {r.PathTemplate}",
                        Meta = { ["method"] = r.Method.ToString().ToUpperInvariant(), ["path"] = r.PathTemplate }
                    });
                    edges.Add(new GraphEdge { FromId = svcId, ToId = epId, Kind = EdgeKind.Exposes, Source = "connector" });
                    result.Endpoints++;
                }
            }

            // ── Connector: calls (frontend) + intra-workspace consumes ────────────────────
            FrontendContract? frontend = null;
            try { frontend = await _frontend.ResolveAsync(workspacePath, ct); }
            catch (Exception ex) { result.Notes.Add($"Frontend resolve failed: {ex.Message}"); }

            if (frontend != null && frontend.Calls.Count > 0)
            {
                if (frontend.Stack != FrontendStack.Unknown) stacks.Add(frontend.Stack.ToString());

                foreach (var c in frontend.Calls)
                {
                    var callId = CallId(wsId, c.Method, c.PathTemplate);
                    nodes.Add(new GraphNode
                    {
                        Id = callId, Kind = NodeKind.ApiCall, WorkspaceId = wsId,
                        Path = c.FilePath, Name = $"{c.Method.ToString().ToUpperInvariant()} {c.PathTemplate}",
                        Meta = { ["method"] = c.Method.ToString().ToUpperInvariant(), ["path"] = c.PathTemplate, ["client"] = c.Client }
                    });
                    result.Calls++;
                }

                // A workspace can be both caller and provider (e.g. a Next.js app with app/api routes).
                if (backend != null && backend.Routes.Count > 0)
                {
                    var report = ContractMatcher.Match(frontend.Calls, backend.Routes);
                    foreach (var e in report.Edges.Where(e => e.Route != null))
                    {
                        var callId = CallId(wsId, e.Call.Method, e.Call.PathTemplate);
                        var epId = EndpointId(wsId, e.Route!.Method, e.Route.PathTemplate);
                        edges.Add(new GraphEdge
                        {
                            FromId = callId, ToId = epId, Kind = EdgeKind.Consumes,
                            Confidence = e.State == MatchState.Matched ? 1.0 : 0.6,
                            Source = "connector-intra"
                        });
                    }
                }
            }

            _store.UpsertNodes(nodes);
            _store.UpsertEdges(edges);
            result.Edges = edges.Count;
            result.Stacks = stacks.Distinct().ToList();
            _registry.MarkIndexed(wsId, result.Stacks);

            return result;
        }

        private static string EndpointId(string wsId, HttpVerb method, string path) =>
            $"ep:{wsId}:{method.ToString().ToUpperInvariant()}:{path}";

        private static string CallId(string wsId, HttpVerb method, string path) =>
            $"call:{wsId}:{method.ToString().ToUpperInvariant()}:{path}";

        private static string BuildSignature(Syncro.Desktop.Services.AST.Core.AstNode n)
        {
            var ret = string.IsNullOrWhiteSpace(n.ReturnType) ? "" : n.ReturnType + " ";
            var pars = n.Parameters.Count > 0 ? "(" + string.Join(", ", n.Parameters) + ")" : "";
            return (ret + n.Name + pars).Trim();
        }
    }
}
