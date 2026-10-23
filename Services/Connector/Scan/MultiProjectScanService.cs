using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Frontend;
using Syncro.Desktop.Services.Connector.Matching;
using Syncro.Desktop.Services.Connector.Models;
using Syncro.Desktop.Services.Connector.ProjectDomain;

namespace Syncro.Desktop.Services.Connector.Scan
{
    /// <summary>
    /// Runtime multi-project scan: point it at a root folder, it discovers every project inside,
    /// runs Dual-Role (Producer + Consumer) contract resolution on each, and maps every project's
    /// outbound calls against the union of all discovered routes. Zero-GPU, deterministic.
    /// </summary>
    public sealed class MultiProjectScanService
    {
        private readonly WorkspaceDiscoveryService _discovery;
        private readonly IBackendContractResolver _backend;
        private readonly IFrontendContractResolver _frontend;

        public MultiProjectScanService(
            WorkspaceDiscoveryService discovery,
            IBackendContractResolver backend,
            IFrontendContractResolver frontend)
        {
            _discovery = discovery;
            _backend = backend;
            _frontend = frontend;
        }

        public async Task<MultiProjectScanReport> ScanAsync(string root, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var report = new MultiProjectScanReport { Root = root ?? "" };

            var nodes = _discovery.Discover(root ?? "");
            if (nodes.Count == 0)
            {
                report.ElapsedMs = sw.ElapsedMilliseconds;
                return report;
            }

            // Resolve every project concurrently in Dual-Role mode (both producer routes & consumer calls).
            var scans = await Task.WhenAll(nodes.Select(n => ResolveNodeAsync(n, ct)));
            report.Projects = scans.OrderBy(p => (int)DomainWave(p.Domain)).ThenBy(p => p.Name).ToList();

            // Cross-map: each consumer's calls against the pooled routes of every producer.
            var backends = report.Projects.Where(p => p.Backend is { Routes.Count: > 0 }).ToList();
            var pooledRoutes = backends.SelectMany(b => b.Backend!.Routes).ToList();
            var consumedRouteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fe in report.Projects.Where(p => p.Frontend is { Calls.Count: > 0 }))
            {
                var match = ContractMatcher.Match(fe.Frontend!.Calls, pooledRoutes);
                foreach (var edge in match.Edges)
                {
                    var owner = edge.Route == null ? null
                        : backends.FirstOrDefault(b => b.Backend!.Routes.Any(r => r.Id == edge.Route.Id))?.Name;
                    if (edge.Route != null) consumedRouteIds.Add(edge.Route.Id);

                    report.Connections.Add(new CrossEdge
                    {
                        FromProject = fe.Name,
                        Method = edge.Call.Method.ToString().ToUpperInvariant(),
                        CallPath = edge.Call.PathTemplate,
                        ToProject = owner,
                        RoutePath = edge.Route?.PathTemplate,
                        State = edge.State.ToString()
                    });

                    if (edge.State == MatchState.Matched) report.Matched++;
                    else if (edge.State == MatchState.Missing) report.Missing++;
                }
            }

            report.TotalRoutes = report.Projects.Sum(p => p.Routes);
            report.TotalCalls = report.Projects.Sum(p => p.Calls);
            report.OrphanRoutes = pooledRoutes.Count(r => !consumedRouteIds.Contains(r.Id));
            report.ElapsedMs = sw.ElapsedMilliseconds;
            return report;
        }

        private async Task<ProjectScan> ResolveNodeAsync(ConnectorWorkspaceNode node, CancellationToken ct)
        {
            var scan = new ProjectScan
            {
                Name = node.Label,
                Path = node.Path,
                Domain = node.Domain,
                Stack = node.Stack
            };

            try
            {
                if (node.Domain != DomainKind.Database)
                {
                    // Dual-Role (Producer + Consumer) resolution: any service can both expose routes
                    // (e.g. Next.js API routes, BFF, microservice) and make outbound HTTP calls.
                    var beTask = _backend.ResolveAsync(node.Path, ct);
                    var feTask = _frontend.ResolveAsync(node.Path, ct);
                    await Task.WhenAll(beTask, feTask);

                    scan.Backend = beTask.Result;
                    scan.Routes = scan.Backend.Routes.Count;
                    scan.RouteSource = scan.Routes > 0 ? scan.Backend.PrimarySource.ToString() : "none";
                    scan.Notes.AddRange(scan.Backend.Notes);

                    scan.Frontend = feTask.Result;
                    scan.Calls = scan.Frontend.Calls.Count;
                    scan.Notes.AddRange(scan.Frontend.Notes);
                }

                // Data layer — models, DTOs, SQL tables / Mongo collections — for every project.
                scan.Data = DataLayerScanner.Scan(node.Path);
            }
            catch (Exception ex)
            {
                scan.Notes.Add($"Resolve failed: {ex.Message}");
            }

            return scan;
        }

        private static int DomainWave(DomainKind d) => DomainDetector.GetDefaultWave(d);
    }
}
