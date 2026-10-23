using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Universe;
using Syncro.Desktop.Services.Universe.Indexing;
using Syncro.Desktop.Services.Workspace.Diagnostics;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    /// <summary>
    /// Exposes the Phase 2/3 engines to the agent loop: the unified Problems Hub and the
    /// Cross-Language Mapper. Both are read-only, so no approval gate.
    /// </summary>
    public sealed class WorkspaceIntelligenceMcp : IMcpServer
    {
        public string Name => "Workspace Intelligence MCP";

        public string Description =>
            "Unified diagnostics (compiler + security + contract drift) and cross-language feature mapping.";

        private readonly IProblemsHub _problems;
        private readonly CrossLanguageMapper _mapper;
        private readonly UniverseRegistry _registry;

        public WorkspaceIntelligenceMcp(IProblemsHub problems, CrossLanguageMapper mapper, UniverseRegistry registry)
        {
            _problems = problems;
            _mapper = mapper;
            _registry = registry;
        }

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new GetProblemsTool(_problems);
            yield return new MapFeatureTool(_mapper, _registry);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    /// <summary>Reads the Problems Hub — one stream across compiler, SAST and contract drift.</summary>
    public sealed class GetProblemsTool : IMcpTool
    {
        private readonly IProblemsHub _hub;

        public GetProblemsTool(IProblemsHub hub) => _hub = hub;

        public string Name => "GetProblems";

        public string Description =>
            "Lists the current problems in the open workspace from ALL sources in one stream: " +
            "compiler/Roslyn diagnostics, SAST security findings, and breaking frontend<->backend " +
            "contract drift. Optionally filter by source (language|security|contract|build|agent) or " +
            "by minimum severity (hint|info|warning|error). Read-only.";

        public string InputSchema =>
            "{ \"type\": \"object\", \"properties\": { " +
            "\"source\": { \"type\": \"string\", \"description\": \"language|security|contract|build|agent\" }, " +
            "\"minSeverity\": { \"type\": \"string\", \"description\": \"hint|info|warning|error\" }, " +
            "\"limit\": { \"type\": \"integer\" } } }";

        public bool RequiresAdminApproval => false;

        private sealed class ToolInput
        {
            public string? Source { get; set; }
            public string? MinSeverity { get; set; }
            public int? Limit { get; set; }
        }

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = string.IsNullOrWhiteSpace(inputJson)
                    ? new ToolInput()
                    : JsonConvert.DeserializeObject<ToolInput>(inputJson) ?? new ToolInput();

                IEnumerable<Problem> items = _hub.All;

                if (!string.IsNullOrWhiteSpace(input.Source) &&
                    Enum.TryParse<ProblemSource>(input.Source, ignoreCase: true, out var src))
                    items = items.Where(p => p.Source == src);

                if (!string.IsNullOrWhiteSpace(input.MinSeverity) &&
                    Enum.TryParse<ProblemSeverity>(input.MinSeverity, ignoreCase: true, out var min))
                    items = items.Where(p => p.Severity >= min);

                var limit = input.Limit is > 0 ? input.Limit.Value : 100;
                var list = items.Take(limit).ToList();

                return Task.FromResult(JsonConvert.SerializeObject(new
                {
                    totalErrors = _hub.ErrorCount,
                    totalWarnings = _hub.WarningCount,
                    returned = list.Count,
                    problems = list.Select(p => new
                    {
                        source = p.SourceTag,
                        severity = p.Severity.ToString().ToLowerInvariant(),
                        message = p.Message,
                        file = p.FilePath,
                        line = p.Line,
                        code = p.Code
                    })
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }

    /// <summary>Answers "what implements the X feature?" across languages via the Universe graph.</summary>
    public sealed class MapFeatureTool : IMcpTool
    {
        private readonly CrossLanguageMapper _mapper;
        private readonly UniverseRegistry _registry;

        public MapFeatureTool(CrossLanguageMapper mapper, UniverseRegistry registry)
        {
            _mapper = mapper;
            _registry = registry;
        }

        public string Name => "MapFeature";

        public string Description =>
            "Maps a feature across languages and layers. Given a workspace path, clusters symbols by " +
            "concept so you can see that ProductController (C#), ProductRepository (C#), ProductBloc " +
            "(Dart) and ProductScreen (Dart) are ONE feature. Pass a concept name to get just that " +
            "feature, or omit it to list every full-stack concept. Use this before editing a feature " +
            "so you change every layer, not just one. Read-only.";

        public string InputSchema =>
            "{ \"type\": \"object\", \"properties\": { " +
            "\"workspacePath\": { \"type\": \"string\" }, " +
            "\"concept\": { \"type\": \"string\", \"description\": \"e.g. Product — omit to list all\" }, " +
            "\"fullStackOnly\": { \"type\": \"boolean\" } }, \"required\": [\"workspacePath\"] }";

        public bool RequiresAdminApproval => false;

        private sealed class ToolInput
        {
            public string WorkspacePath { get; set; } = "";
            public string? Concept { get; set; }
            public bool FullStackOnly { get; set; }
        }

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<ToolInput>(inputJson);
                if (string.IsNullOrWhiteSpace(input?.WorkspacePath))
                    return Task.FromResult(JsonConvert.SerializeObject(new { error = "workspacePath is required." }));

                var ws = _registry.Register(input.WorkspacePath);
                var report = _mapper.Map(ws.Id);

                IEnumerable<ConceptCluster> clusters = report.Clusters;

                if (!string.IsNullOrWhiteSpace(input.Concept))
                    clusters = clusters.Where(c =>
                        c.Concept.Contains(input.Concept, StringComparison.OrdinalIgnoreCase));

                if (input.FullStackOnly)
                    clusters = clusters.Where(c => c.SpansBackendAndFrontend);

                var list = clusters.ToList();

                return Task.FromResult(JsonConvert.SerializeObject(new
                {
                    summary = report.Summary,
                    conceptCount = list.Count,
                    concepts = list.Select(c => new
                    {
                        concept = c.Concept,
                        spansBackendAndFrontend = c.SpansBackendAndFrontend,
                        members = c.Members.Select(m => new
                        {
                            name = m.Name,
                            role = m.Role.ToString(),
                            language = m.Language,
                            file = m.Path
                        })
                    })
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }
}
