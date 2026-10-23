using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>
    /// Pure, dependency-free differ that classifies the change between two <see cref="ContractSnapshot"/>s
    /// as breaking or non-breaking, using API-semver rules:
    ///   • Removing a route / changing its method / removing or retyping a RESPONSE field → BREAKING
    ///     (the classic "backend changed UserDto but the Flutter side still compiles, then fails at runtime").
    ///   • Adding a required request field / making an existing one required / retyping a request field → BREAKING.
    ///   • Adding a route, adding an optional request field, adding a response field, removing a request
    ///     field → non-breaking.
    /// Static and side-effect-free so it is trivially unit-testable.
    /// </summary>
    public static class ContractDiffer
    {
        public static ContractDriftReport Diff(ContractSnapshot baseline, ContractSnapshot current)
        {
            var report = new ContractDriftReport();
            if (baseline == null || current == null) return report;

            // First-wins de-dup so a malformed snapshot with duplicate ids can't throw.
            var baseById = Index(baseline.Routes);
            var curById = Index(current.Routes);

            var added = curById.Keys.Where(id => !baseById.ContainsKey(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var removed = baseById.Keys.Where(id => !curById.ContainsKey(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Collapse "removed {GET /x} + added {POST /x}" into a single MethodChanged on /x.
            foreach (var rid in removed.ToList())
            {
                var br = baseById[rid];
                var moved = added.Select(aid => curById[aid])
                                 .FirstOrDefault(cr => PathEquals(cr.PathTemplate, br.PathTemplate));
                if (moved != null)
                {
                    report.Changes.Add(new ContractChange
                    {
                        Kind = ContractChangeKind.MethodChanged,
                        Target = br.PathTemplate,
                        Detail = $"Method changed {br.Method.ToUpperInvariant()} → {moved.Method.ToUpperInvariant()} for {br.PathTemplate}.",
                        Breaking = true
                    });
                    removed.Remove(rid);
                    added.Remove(moved.Id);
                }
            }

            foreach (var id in removed)
                report.Changes.Add(new ContractChange
                {
                    Kind = ContractChangeKind.RouteRemoved,
                    Target = id,
                    Detail = $"Route {id} was removed — existing callers will 404.",
                    Breaking = true
                });

            foreach (var id in added)
                report.Changes.Add(new ContractChange
                {
                    Kind = ContractChangeKind.RouteAdded,
                    Target = id,
                    Detail = $"Route {id} was added.",
                    Breaking = false
                });

            // Field-level diffs for routes present in both (same method + path).
            foreach (var id in baseById.Keys.Where(curById.ContainsKey))
                DiffFields(id, baseById[id], curById[id], report);

            return report;
        }

        private static void DiffFields(string routeId, RouteSnapshot bRoute, RouteSnapshot cRoute, ContractDriftReport report)
        {
            var bReq = Fields(bRoute.Request);
            var cReq = Fields(cRoute.Request);
            var bResp = Fields(bRoute.Response);
            var cResp = Fields(cRoute.Response);

            // ---- request ----
            foreach (var name in cReq.Keys.Where(n => !bReq.ContainsKey(n)))
            {
                var f = cReq[name];
                report.Changes.Add(new ContractChange
                {
                    Kind = ContractChangeKind.RequestFieldAdded,
                    Target = $"{routeId} → {name}",
                    Detail = f.Required
                        ? $"Backend now requires request field '{name}' — old clients don't send it."
                        : $"Optional request field '{name}' added.",
                    Breaking = f.Required
                });
            }
            foreach (var name in bReq.Keys.Where(n => !cReq.ContainsKey(n)))
                report.Changes.Add(new ContractChange
                {
                    Kind = ContractChangeKind.RequestFieldRemoved,
                    Target = $"{routeId} → {name}",
                    Detail = $"Backend no longer reads request field '{name}'.",
                    Breaking = false
                });
            foreach (var name in bReq.Keys.Where(cReq.ContainsKey))
            {
                var b = bReq[name]; var c = cReq[name];
                if (!TypeEquals(b.Type, c.Type))
                    report.Changes.Add(new ContractChange
                    {
                        Kind = ContractChangeKind.RequestFieldTypeChanged,
                        Target = $"{routeId} → {name}",
                        Detail = $"Request field '{name}' type changed {b.Type} → {c.Type}.",
                        Breaking = true
                    });
                if (!b.Required && c.Required)
                    report.Changes.Add(new ContractChange
                    {
                        Kind = ContractChangeKind.RequestFieldNowRequired,
                        Target = $"{routeId} → {name}",
                        Detail = $"Request field '{name}' is now required.",
                        Breaking = true
                    });
            }

            // ---- response (the runtime-break-the-compiler-misses cases) ----
            foreach (var name in bResp.Keys.Where(n => !cResp.ContainsKey(n)))
                report.Changes.Add(new ContractChange
                {
                    Kind = ContractChangeKind.ResponseFieldRemoved,
                    Target = $"{routeId} → {name}",
                    Detail = $"Response field '{name}' removed — frontend code that reads it will fail at runtime.",
                    Breaking = true
                });
            foreach (var name in cResp.Keys.Where(n => !bResp.ContainsKey(n)))
                report.Changes.Add(new ContractChange
                {
                    Kind = ContractChangeKind.ResponseFieldAdded,
                    Target = $"{routeId} → {name}",
                    Detail = $"Response field '{name}' added.",
                    Breaking = false
                });
            foreach (var name in bResp.Keys.Where(cResp.ContainsKey))
            {
                var b = bResp[name]; var c = cResp[name];
                if (!TypeEquals(b.Type, c.Type))
                    report.Changes.Add(new ContractChange
                    {
                        Kind = ContractChangeKind.ResponseFieldTypeChanged,
                        Target = $"{routeId} → {name}",
                        Detail = $"Response field '{name}' type changed {b.Type} → {c.Type} — frontend deserialization may break.",
                        Breaking = true
                    });
            }
        }

        private static Dictionary<string, RouteSnapshot> Index(IEnumerable<RouteSnapshot> routes)
        {
            var d = new Dictionary<string, RouteSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in routes)
                if (!d.ContainsKey(r.Id)) d[r.Id] = r;
            return d;
        }

        private static Dictionary<string, FieldSnapshot> Fields(IEnumerable<FieldSnapshot> fields)
        {
            var d = new Dictionary<string, FieldSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields)
                if (!string.IsNullOrWhiteSpace(f.Name) && !d.ContainsKey(f.Name)) d[f.Name] = f;
            return d;
        }

        private static bool PathEquals(string a, string b) =>
            string.Equals(a?.TrimEnd('/'), b?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

        private static bool TypeEquals(string a, string b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
