using System;
using System.Collections.Generic;
using System.Linq;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Matching
{
    /// <summary>
    /// Pairs frontend <see cref="ApiCallSite"/>s against backend <see cref="BackendRoute"/>s and
    /// classifies each pairing — the heart of the connection graph. Matching is param-agnostic and
    /// tolerant of a differing base path (a call to <c>/users</c> maps to a route mounted at
    /// <c>/api/users</c>). Shape diffing only runs when both sides actually carry field info.
    /// </summary>
    public static class ContractMatcher
    {
        public static ConnectionReport Match(IReadOnlyList<ApiCallSite> calls, IReadOnlyList<BackendRoute> routes)
        {
            var report = new ConnectionReport();
            var usedRouteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pre-compute canonical segments once per route.
            var routeInfos = routes
                .Select(r => (route: r, canon: PathNormalizer.Canonical(r.PathTemplate), segs: PathNormalizer.CanonicalSegments(r.PathTemplate)))
                .ToList();

            foreach (var call in calls)
            {
                var callCanon = PathNormalizer.Canonical(call.PathTemplate);
                var callSegs = PathNormalizer.CanonicalSegments(call.PathTemplate);

                var edge = new ConnectionEdge { Call = call };

                // 1. Same path + same method → the happy path.
                var exact = routeInfos.FirstOrDefault(ri => ri.canon == callCanon && ri.route.Method == call.Method);
                if (exact.route != null)
                {
                    Classify(edge, exact.route, usedRouteIds);
                }
                else
                {
                    // 2. Same path, any method (with a base-path suffix fallback).
                    var samePath = routeInfos.FirstOrDefault(ri => ri.canon == callCanon)
                                   .route
                                   ?? routeInfos.FirstOrDefault(ri => SuffixMatch(callSegs, ri.segs)).route;

                    if (samePath != null)
                    {
                        if (samePath.Method == call.Method)
                        {
                            Classify(edge, samePath, usedRouteIds);
                            edge.Diffs.Add("Matched on path suffix (base path differs between frontend and backend).");
                        }
                        else
                        {
                            edge.State = MatchState.MethodMismatch;
                            edge.Route = samePath;
                            edge.Diffs.Add($"Backend exposes {samePath.Method.ToString().ToUpperInvariant()} for this path, frontend calls {call.Method.ToString().ToUpperInvariant()}.");
                            usedRouteIds.Add(samePath.Id);
                        }
                    }
                    else
                    {
                        edge.State = MatchState.Missing;
                        edge.Diffs.Add("No backend route matches this call.");
                    }
                }

                report.Edges.Add(edge);
            }

            // Orphan routes: backend routes nothing consumed.
            report.OrphanRoutes = routes.Where(r => !usedRouteIds.Contains(r.Id)).ToList();

            report.Matched = report.Edges.Count(e => e.State == MatchState.Matched);
            report.ShapeMismatch = report.Edges.Count(e => e.State == MatchState.ShapeMismatch);
            report.MethodMismatch = report.Edges.Count(e => e.State == MatchState.MethodMismatch);
            report.Missing = report.Edges.Count(e => e.State == MatchState.Missing);

            return report;
        }

        private static void Classify(ConnectionEdge edge, BackendRoute route, HashSet<string> usedRouteIds)
        {
            edge.Route = route;
            usedRouteIds.Add(route.Id);

            var diffs = ShapeDiff(edge.Call, route);
            if (diffs.Count > 0)
            {
                edge.State = MatchState.ShapeMismatch;
                edge.Diffs.AddRange(diffs);
            }
            else
            {
                edge.State = MatchState.Matched;
            }
        }

        // Only meaningful when BOTH sides carry field info; the regex extractor rarely does, so this
        // stays quiet until swagger-backed routes meet AST-derived call shapes.
        private static List<string> ShapeDiff(ApiCallSite call, BackendRoute route)
        {
            var diffs = new List<string>();

            if (call.RequestFields.Count > 0 && route.RequestSchema.Count > 0)
            {
                var callNames = call.RequestFields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var routeNames = route.RequestSchema.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var missing in callNames.Except(routeNames))
                    diffs.Add($"Request field '{missing}' sent by frontend but not in backend schema.");
                foreach (var extra in routeNames.Except(callNames))
                    diffs.Add($"Backend expects request field '{extra}' the frontend doesn't send.");
            }

            if (call.ResponseFields.Count > 0 && route.ResponseSchema.Count > 0)
            {
                var callNames = call.ResponseFields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var routeNames = route.ResponseSchema.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var missing in callNames.Except(routeNames))
                    diffs.Add($"Frontend reads response field '{missing}' the backend doesn't return.");
            }

            return diffs;
        }

        private static bool SuffixMatch(string[] callSegs, string[] routeSegs)
        {
            // Try both orientations — the frontend may include or omit the mount prefix.
            return PathNormalizer.SuffixAligned(callSegs, routeSegs)
                || PathNormalizer.SuffixAligned(routeSegs, callSegs);
        }
    }
}
