using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;
using Syncro.Desktop.Services.Universe.Models;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe
{
    /// <summary>
    /// The real <see cref="IKnowledgeEngine"/> — replaces <c>KnowledgeEngineStub</c> (which did
    /// nothing) and persists hindsight into the Universe graph. This is the seam universe.md §1
    /// identified: the interface was already wired into <c>ContextBuilder</c>, so backing it with
    /// real storage lights up memory everywhere the engine is already consumed, with no caller change.
    ///
    /// Hindsight is stored as <see cref="NodeKind.Hindsight"/> nodes keyed to a workspace/project id;
    /// retrieval returns a compact summary (decisions + graph stats), which is exactly the
    /// "budgeted digest, not raw dump" retrieval style (§7). Robust by design — a storage hiccup
    /// never breaks the calling agent.
    /// </summary>
    public sealed class UniverseKnowledgeEngine : IKnowledgeEngine
    {
        private readonly UniverseGraphStore _store;

        public UniverseKnowledgeEngine(UniverseGraphStore store)
        {
            _store = store;
        }

        public Task StoreHindsightAsync(string projectId, string decision, string filesChanged, string architectureImpact)
        {
            try
            {
                var node = new GraphNode
                {
                    Id = $"hindsight:{projectId}:{Guid.NewGuid():N}",
                    Kind = NodeKind.Hindsight,
                    WorkspaceId = projectId ?? "",
                    Name = Truncate(decision, 80),
                    Meta =
                    {
                        ["decision"] = decision ?? "",
                        ["files"] = filesChanged ?? "",
                        ["impact"] = architectureImpact ?? ""
                    }
                };
                _store.UpsertNode(node);
            }
            catch
            {
                // Memory must never break the agent loop — swallow storage errors.
            }

            return Task.CompletedTask;
        }

        public Task<string> RetrieveProjectKnowledgeAsync(string projectId)
        {
            try
            {
                var hindsight = _store.NodesByKindInWorkspace(projectId ?? "", NodeKind.Hindsight);
                if (hindsight.Count == 0)
                    return Task.FromResult("No stored hindsight found for this project yet. This is a fresh index.");

                var (nodes, edges, _) = _store.Stats();
                var sb = new StringBuilder();
                sb.AppendLine($"Project knowledge — {hindsight.Count} recorded decision(s); universe graph: {nodes} nodes, {edges} edges.");
                foreach (var h in hindsight.OrderByDescending(h => h.UpdatedAt).Take(8))
                {
                    var impact = h.Meta.TryGetValue("impact", out var im) ? im : "";
                    sb.AppendLine($"- {h.Name}{(string.IsNullOrWhiteSpace(impact) ? "" : $"  (impact: {Truncate(impact, 100)})")}");
                }

                return Task.FromResult(sb.ToString().TrimEnd());
            }
            catch
            {
                return Task.FromResult("No stored hindsight found for this project yet. This is a fresh index.");
            }
        }

        private static string Truncate(string? s, int max)
        {
            s ??= "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
