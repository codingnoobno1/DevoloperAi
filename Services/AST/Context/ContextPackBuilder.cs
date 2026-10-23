using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Context
{
    /// <summary>
    /// Projects <see cref="AstProjectMap"/>s into tiered, token-costed context and fits them under a
    /// hard budget (ast-context-engine.md §2). L0 (skeleton) is emitted for every project first — the
    /// universe overview always survives — then the remaining budget upgrades the most relevant
    /// projects to richer tiers. Never returns more than the budget; omissions become notes.
    /// </summary>
    public sealed class ContextPackBuilder
    {
        private readonly ITokenCounter _tok;

        // symbol node types worth a signature line (Class/Interface/Method/Property/Route/Dto)
        private static readonly HashSet<AstNodeType> SymbolTypes = new()
        {
            AstNodeType.Class, AstNodeType.Interface, AstNodeType.Method,
            AstNodeType.Property, AstNodeType.Route, AstNodeType.Dto
        };

        public ContextPackBuilder(ITokenCounter tok) => _tok = tok;

        // ── tier rendering ─────────────────────────────────────────────────────
        public string Render(AstProjectMap map, ContextTier tier)
        {
            var real = map.Nodes.Where(n => !n.IsExternalOrBoilerplate).ToList();
            var sb = new StringBuilder();

            sb.Append("# ").Append(map.ProjectName)
              .Append("  (").Append(map.Language).Append(" / ").Append(map.Framework).Append(")\n");
            sb.Append("symbols:").Append(real.Count(n => SymbolTypes.Contains(n.Type)))
              .Append(" endpoints:").Append(map.Endpoints.Count)
              .Append(" dtos:").Append(map.Dtos.Count)
              .Append(" deps:").Append(map.Dependencies.Count).Append('\n');

            if (map.Endpoints.Count > 0)
                sb.Append("endpoints: ")
                  .Append(string.Join(", ", map.Endpoints.Select(e => $"{e.Method} {e.Path}"))).Append('\n');

            var modules = real.Where(n => !string.IsNullOrEmpty(n.FilePath))
                              .Select(n => System.IO.Path.GetFileName(n.FilePath))
                              .Distinct().OrderBy(x => x).ToList();
            if (modules.Count > 0)
                sb.Append("modules: ").Append(string.Join(", ", modules)).Append('\n');

            if (tier == ContextTier.Skeleton) return sb.ToString();

            // L1 / L2: signatures
            sb.Append("symbols:\n");
            foreach (var n in real.Where(n => SymbolTypes.Contains(n.Type)))
            {
                sb.Append("  ").Append(Signature(n));
                if (tier == ContextTier.Detail && !string.IsNullOrWhiteSpace(n.Summary))
                    sb.Append("  — ").Append(OneLine(n.Summary!));
                sb.Append('\n');
            }

            if (tier == ContextTier.Detail && map.Dependencies.Count > 0)
                sb.Append("dependencies: ")
                  .Append(string.Join(", ", map.Dependencies.Select(d => d.Name).Distinct().Take(40))).Append('\n');

            return sb.ToString();
        }

        // ── fit to budget ──────────────────────────────────────────────────────
        public ContextPack Fit(IReadOnlyList<AstProjectMap> maps, int budget, string? focus = null)
        {
            var pack = new ContextPack { BudgetTokens = budget };
            if (maps.Count == 0) return pack;

            // focus project first, then the rest.
            var ordered = maps.OrderByDescending(m => string.Equals(m.ProjectName, focus, StringComparison.OrdinalIgnoreCase))
                              .ThenBy(m => m.ProjectName).ToList();

            int used = 0;

            // pass 1 — L0 for every project (skeleton always wins until the budget is exhausted).
            var contexts = new List<ProjectContext>();
            var byName = new Dictionary<string, AstProjectMap>(StringComparer.OrdinalIgnoreCase);
            int omitted = 0;
            foreach (var m in ordered)
            {
                var text = Render(m, ContextTier.Skeleton);
                var t = _tok.Count(text);
                if (used + t > budget) { omitted++; continue; }
                contexts.Add(new ProjectContext { Project = m.ProjectName, Tier = ContextTier.Skeleton, Text = text, Tokens = t });
                byName[m.ProjectName] = m;
                used += t;
            }
            if (omitted > 0) pack.Notes.Add($"{omitted} project(s) omitted — over the {budget}-token budget.");

            // pass 2 — upgrade to Symbols then Detail (focus first), best-effort within budget.
            foreach (var tier in new[] { ContextTier.Symbols, ContextTier.Detail })
            {
                for (int i = 0; i < contexts.Count; i++)
                {
                    var pc = contexts[i];
                    if (pc.Tier >= tier) continue;
                    var text = Render(byName[pc.Project], tier);
                    var t = _tok.Count(text);
                    var delta = t - pc.Tokens;
                    if (delta <= 0 || used + delta > budget) continue;
                    contexts[i] = new ProjectContext { Project = pc.Project, Tier = tier, Text = text, Tokens = t };
                    used += delta;
                }
            }

            pack.Projects.AddRange(contexts);
            pack.UsedTokens = used;
            return pack;
        }

        private static string Signature(AstNode n)
        {
            var pars = n.Parameters.Count > 0 ? "(" + string.Join(",", n.Parameters) + ")" : "";
            var ret = string.IsNullOrEmpty(n.ReturnType) ? "" : " " + n.ReturnType;
            var loc = string.IsNullOrEmpty(n.FilePath) ? "" : $"  {System.IO.Path.GetFileName(n.FilePath)}:{n.LineNumber}";
            return $"{n.Name}{pars}{ret}{loc}";
        }

        private static string OneLine(string s) =>
            s.Replace('\r', ' ').Replace('\n', ' ').Trim() is var t && t.Length > 100 ? t.Substring(0, 100) + "…" : s.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
