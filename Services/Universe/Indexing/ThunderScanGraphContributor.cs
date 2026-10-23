using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Syncro.Desktop.Services.Connector;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Universe.Models;

namespace Syncro.Desktop.Services.Universe.Indexing
{
    /// <summary>
    /// Enriches the Universe graph with structure only a Reverse-AST scan (<c>thunder scan</c>) knows,
    /// beyond the flat call list the connector already contributes:
    /// <list type="bullet">
    ///   <item><b>Flutter service classes</b> (from <c>service.json</c> / the <c>service</c> field on each
    ///   call) as <see cref="NodeKind.Service"/> nodes, each with a <see cref="EdgeKind.Calls"/> edge to
    ///   the <see cref="NodeKind.ApiCall"/> nodes it issues.</item>
    ///   <item><b>Remote servers</b> (from <c>connector/server.json</c>) as cross-cutting
    ///   <see cref="NodeKind.Service"/> nodes keyed by host, with a <see cref="EdgeKind.Consumes"/> edge
    ///   from each call — the seed a later cross-workspace mapper resolves to a real backend workspace.</item>
    /// </list>
    /// Reads the scan artifacts under <c>&lt;workspace&gt;/.thunder/reverse/</c>; no-op (and never throws)
    /// when they are absent or malformed, so it's safe to call for every workspace.
    /// </summary>
    internal static class ThunderScanGraphContributor
    {
        public sealed class Contribution
        {
            public int ServiceClasses { get; set; }
            public int RemoteServers { get; set; }
            public int Edges { get; set; }
            public bool ScanFound { get; set; }
        }

        public static Contribution Contribute(
            string workspacePath, string wsId, List<GraphNode> nodes, List<GraphEdge> edges)
        {
            var c = new Contribution();
            if (string.IsNullOrWhiteSpace(workspacePath)) return c;

            var reverseDir = Path.Combine(workspacePath, ".thunder", "reverse");
            var apiJson = Path.Combine(reverseDir, "connector", "api.json");
            if (!File.Exists(apiJson)) return c;
            c.ScanFound = true;

            var calls = ReadArray(apiJson, "calls");
            if (calls.Count == 0) return c;

            // service.json → service name → declaring file (for node Path/provenance).
            var serviceFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in ReadArray(Path.Combine(reverseDir, "service.json"), "services"))
            {
                var name = Str(s, "name");
                if (name.Length == 0) continue;
                if (s.TryGetProperty("provenance", out var p) && p.ValueKind == JsonValueKind.Object)
                    serviceFiles[name] = Str(p, "file");
            }

            // server.json → server id → (host, baseUrl, stackGuess) for remote-server nodes.
            var servers = new Dictionary<string, (string host, string baseUrl, string stack)>(StringComparer.OrdinalIgnoreCase);
            foreach (var sv in ReadArray(Path.Combine(reverseDir, "connector", "server.json"), "servers"))
            {
                var sid = Str(sv, "id");
                if (sid.Length == 0) continue;
                var baseUrl = Str(sv, "baseUrl");
                servers[sid] = (HostOf(baseUrl), baseUrl, Str(sv, "stackGuess"));
            }

            var emittedServiceNodes = new HashSet<string>();
            var emittedRemoteNodes = new HashSet<string>();

            foreach (var call in calls)
            {
                var endpoint = Str(call, "endpoint");
                if (endpoint.Length == 0) continue;

                var method = Str(call, "method").ToHttpVerb();
                var path = PathNormalizer.NormalizeCallPath(endpoint);
                var callId = GraphIds.Call(wsId, method, path);   // matches WorkspaceIndexer's ApiCall node id

                // ── service class → Calls → apiCall ──
                var serviceName = Str(call, "service");
                if (serviceName.Length > 0)
                {
                    var svcId = GraphIds.ServiceClass(wsId, serviceName);
                    if (emittedServiceNodes.Add(svcId))
                    {
                        serviceFiles.TryGetValue(serviceName, out var file);
                        nodes.Add(new GraphNode
                        {
                            Id = svcId, Kind = NodeKind.Service, WorkspaceId = wsId,
                            Path = string.IsNullOrWhiteSpace(file) ? null : Path.Combine(workspacePath, file),
                            Name = serviceName, Meta = { ["kind"] = "flutter-service", ["source"] = "reverse-scan" }
                        });
                        c.ServiceClasses++;
                    }
                    edges.Add(new GraphEdge
                    {
                        FromId = svcId, ToId = callId, Kind = EdgeKind.Calls, Source = "reverse-scan"
                    });
                    c.Edges++;
                }

                // ── apiCall → Consumes → remote server (cross-cutting seed) ──
                var serverId = Str(call, "server");
                if (serverId.Length > 0 && servers.TryGetValue(serverId, out var srv) && srv.host.Length > 0)
                {
                    var remoteId = GraphIds.RemoteServer(srv.host);
                    if (emittedRemoteNodes.Add(remoteId))
                    {
                        nodes.Add(new GraphNode
                        {
                            Id = remoteId, Kind = NodeKind.Service, WorkspaceId = "",   // cross-cutting
                            Name = srv.host,
                            Meta = { ["baseUrl"] = srv.baseUrl, ["stack"] = srv.stack, ["source"] = "reverse-scan" }
                        });
                        c.RemoteServers++;
                    }
                    edges.Add(new GraphEdge
                    {
                        FromId = callId, ToId = remoteId, Kind = EdgeKind.Consumes,
                        Confidence = 0.5, Source = "reverse-scan-remote"
                    });
                    c.Edges++;
                }
            }

            return c;
        }

        private static List<JsonElement> ReadArray(string file, string prop)
        {
            var list = new List<JsonElement>();
            try
            {
                if (!File.Exists(file)) return list;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray()) list.Add(e.Clone());
            }
            catch { /* malformed scan → contribute nothing */ }
            return list;
        }

        private static string Str(JsonElement obj, string prop) =>
            obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "") : "";

        private static string HostOf(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return "";
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host)) return u.Host;
            return "";
        }
    }
}
