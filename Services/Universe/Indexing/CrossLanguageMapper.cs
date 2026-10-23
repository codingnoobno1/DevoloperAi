using System;
using System.Collections.Generic;
using System.Linq;
using Syncro.Desktop.Services.Universe.Models;
using Syncro.Desktop.Services.Universe.Store;

namespace Syncro.Desktop.Services.Universe.Indexing
{
    /// <summary>The architectural layer a symbol plays in a feature, inferred from its name suffix.</summary>
    public enum ConceptRole
    {
        Controller,   // ProductController — backend HTTP surface
        Service,      // ProductService    — backend business logic
        Repository,   // ProductRepository — data access (either side)
        Bloc,         // ProductBloc/Cubit — frontend state machine
        State,        // ProductState      — frontend state shape
        Event,        // ProductEvent      — frontend intent
        Screen,       // ProductScreen/Page/View — frontend UI
        Dto,          // ProductDto/Model/Entity — the shared data shape
        ViewModel     // ProductViewModel
    }

    /// <summary>One symbol participating in a feature cluster.</summary>
    public sealed record ConceptMember(string NodeId, string Name, ConceptRole Role, string? Language, string? Path);

    /// <summary>
    /// All symbols across every language that model one concept, e.g. "Product" →
    /// { ProductController(C#), ProductRepository(C#), ProductBloc(Dart), ProductScreen(Dart) }.
    /// </summary>
    public sealed class ConceptCluster
    {
        public string Concept { get; init; } = "";
        public List<ConceptMember> Members { get; init; } = new();

        /// <summary>Distinct layers present — a cluster spanning backend and frontend is the interesting case.</summary>
        public IReadOnlyCollection<ConceptRole> Roles => Members.Select(m => m.Role).Distinct().ToList();

        public bool SpansBackendAndFrontend =>
            Members.Any(m => IsBackendRole(m.Role)) && Members.Any(m => !IsBackendRole(m.Role));

        internal static bool IsBackendRole(ConceptRole r) =>
            r is ConceptRole.Controller or ConceptRole.Service or ConceptRole.Repository;
    }

    public sealed class CrossLanguageMapReport
    {
        public string WorkspaceId { get; init; } = "";
        public List<ConceptCluster> Clusters { get; init; } = new();
        public int EdgesWritten { get; set; }

        public int FullStackConcepts => Clusters.Count(c => c.SpansBackendAndFrontend);

        public string Summary =>
            $"{Clusters.Count} concept(s), {FullStackConcepts} spanning backend↔frontend, {EdgesWritten} edge(s).";
    }

    /// <summary>
    /// Phase 3 — the Cross-Language Mapper (idearchitecture.md Decision 3 / your #14).
    ///
    /// Reads Symbol nodes already in the Universe graph and clusters them by *concept* — the symbol
    /// name minus its architectural suffix — so `ProductController` (C#), `ProductRepository` (C#),
    /// `ProductBloc` (Dart) and `ProductScreen` (Dart) are recognized as one feature. Each cluster
    /// becomes a set of <see cref="EdgeKind.Mirrors"/> edges, giving navigation, AI context and impact
    /// analysis a language-independent notion of "the Product feature".
    ///
    /// Works entirely over the graph — no filesystem access, no language branching (guardrail #4).
    /// </summary>
    public sealed class CrossLanguageMapper
    {
        private readonly UniverseGraphStore _store;

        public CrossLanguageMapper(UniverseGraphStore store) => _store = store;

        /// <summary>Longest-first so "ViewModel" wins over "Model" and "Repository" over "Repo".</summary>
        private static readonly (string Suffix, ConceptRole Role)[] RoleSuffixes =
        {
            ("ViewModel",  ConceptRole.ViewModel),
            ("Controller", ConceptRole.Controller),
            ("Repository", ConceptRole.Repository),
            ("Provider",   ConceptRole.Bloc),
            ("Service",    ConceptRole.Service),
            ("Screen",     ConceptRole.Screen),
            ("Widget",     ConceptRole.Screen),
            ("Entity",     ConceptRole.Dto),
            ("Cubit",      ConceptRole.Bloc),
            ("State",      ConceptRole.State),
            ("Event",      ConceptRole.Event),
            ("Model",      ConceptRole.Dto),
            ("Page",       ConceptRole.Screen),
            ("View",       ConceptRole.Screen),
            ("Repo",       ConceptRole.Repository),
            ("Bloc",       ConceptRole.Bloc),
            ("Dto",        ConceptRole.Dto),
        };

        public CrossLanguageMapReport Map(string workspaceId)
        {
            var report = new CrossLanguageMapReport { WorkspaceId = workspaceId };

            var symbols = _store.NodesByKindInWorkspace(workspaceId, NodeKind.Symbol);
            var byConcept = new Dictionary<string, List<ConceptMember>>(StringComparer.OrdinalIgnoreCase);

            foreach (var n in symbols)
            {
                if (!TrySplit(n.Name, out var concept, out var role)) continue;

                if (!byConcept.TryGetValue(concept, out var list))
                    byConcept[concept] = list = new List<ConceptMember>();

                list.Add(new ConceptMember(n.Id, n.Name, role, n.Language, n.Path));
            }

            var edges = new List<GraphEdge>();

            foreach (var (concept, members) in byConcept)
            {
                // A single symbol is not a cross-layer mapping — and neither are two symbols playing
                // the same role (e.g. two Screens in different modules).
                if (members.Count < 2) continue;
                if (members.Select(m => m.Role).Distinct().Count() < 2) continue;

                var cluster = new ConceptCluster { Concept = concept, Members = members };
                report.Clusters.Add(cluster);

                // Mirrors is symmetric in meaning but stored directed; write one edge per ordered
                // pair so OutEdges(x) answers "what else models this concept?" for every member.
                foreach (var a in members)
                    foreach (var b in members)
                    {
                        if (ReferenceEquals(a, b)) continue;
                        edges.Add(new GraphEdge
                        {
                            Id = GraphEdge.MakeId(a.NodeId, EdgeKind.Mirrors, b.NodeId),
                            FromId = a.NodeId,
                            ToId = b.NodeId,
                            Kind = EdgeKind.Mirrors,
                            // Cross-language pairings are the heuristic ones; same-language is safer.
                            Confidence = SameLanguage(a, b) ? 0.9 : 0.75,
                            Source = "cross-language"
                        });
                    }
            }

            if (edges.Count > 0) _store.UpsertEdges(edges);
            report.EdgesWritten = edges.Count;

            report.Clusters.Sort((x, y) => string.Compare(x.Concept, y.Concept, StringComparison.OrdinalIgnoreCase));
            return report;
        }

        private static bool SameLanguage(ConceptMember a, ConceptMember b) =>
            !string.IsNullOrEmpty(a.Language) &&
            string.Equals(a.Language, b.Language, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// "ProductController" → ("Product", Controller). Returns false when the name carries no known
        /// suffix, or when stripping it would leave nothing (e.g. a class literally named "State").
        /// </summary>
        internal static bool TrySplit(string? symbolName, out string concept, out ConceptRole role)
        {
            concept = "";
            role = default;
            if (string.IsNullOrWhiteSpace(symbolName)) return false;

            var name = symbolName.Trim();

            // Dart/TS convention: strip a leading underscore on private types.
            name = name.TrimStart('_');

            // Abstract-interface convention: IProductRepository → ProductRepository.
            if (name.Length > 2 && name[0] == 'I' && char.IsUpper(name[1]))
                name = name[1..];

            foreach (var (suffix, r) in RoleSuffixes)
            {
                if (name.Length <= suffix.Length) continue;
                if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;

                var stem = name[..^suffix.Length].TrimEnd('_');
                if (stem.Length < 2) continue;      // "ADto" is noise, not a concept

                concept = stem;
                role = r;
                return true;
            }

            return false;
        }
    }
}
