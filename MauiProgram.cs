using Microsoft.Extensions.Logging;
using DeveloperAI.BusinessLogic;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Syncro.Desktop.Services.AST;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Parsers;
using Syncro.Desktop.Services.AST.Scanners;
using Syncro.Desktop.Services.AST.Analyzers;
using Syncro.Desktop.Services.AST.Graph;
using Syncro.Desktop.Services.AST.Storage;
using Syncro.Desktop.Services.AST.Reporters;
using Syncro.Desktop.Services.AST.Git;
using Syncro.Desktop.Services.AST.Env;

namespace Syncro.Desktop;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
        DotEnv.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Git.FetchProject>();
		builder.Services.AddSingleton<DeveloperAI.BusinessLogic.AIClient>();
		builder.Services.AddSingleton<DeveloperAI.BusinessLogic.EnvironmentManager>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProjectService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.PixelService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.FlutterService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.SyncroCLI.SyncroCLIService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.SyncroCLI.AgentBridgeServer>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.SyncroCLI.AgentOrchestrator>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.SyncroCLI.AgentMonitorServer>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.AST.Env.EnvironmentConfigurator>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.AST.AstService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Git.GitAutomationService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Mcp.LocalMcpServer>();

		// Syncro .NET AI Engine
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Core.ILLMProvider, Syncro.Desktop.Services.Engine.LLM.GroqProvider>();
		// Universe backend (universe.md U1) — persistent cross-workspace graph store + registry.
		Syncro.Desktop.Services.Universe.UniverseServiceCollectionExtensions.AddSyncroUniverse(builder.Services);
		// Real knowledge engine backed by the Universe graph (replaces the no-op KnowledgeEngineStub).
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Core.IKnowledgeEngine, Syncro.Desktop.Services.Universe.UniverseKnowledgeEngine>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Core.ISessionManager, Syncro.Desktop.Services.Engine.Session.SessionManager>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Core.IWorkspaceManager, Syncro.Desktop.Services.Engine.Workspace.WorkspaceManager>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Context.ContextBuilder>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.McpOrchestrator>();

		// 10 Priority V1 MCP Servers
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.WorkspaceMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.FileSystemMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.RepositoryMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.GitMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.AstMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.ApiMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.JsonIntelligenceMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.KnowledgeMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.PatchMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.PlanningMcp>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Knowledge.RepairDatabaseService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Engine.Mcp.Servers.RepairKnowledgeMcp>(); // The 11th Server (Repair OS)

		// Connector Engine — backend understanding (swagger/route-scan) + batch file ops; the 12th MCP server
		Syncro.Desktop.Services.Connector.ConnectorServiceCollectionExtensions.AddSyncroConnector(builder.Services);

		// AgentCli DB core (task loop, NLP mappings, script store, DB status snapshot)
		Syncro.Desktop.Services.AgentCli.AgentCliServiceCollectionExtensions.AddAgentCli(builder.Services);

		// Hindsight retrieval (vector-DB read + with/without-LLM demo)
		builder.Services.AddSingleton<Syncro.Desktop.Services.AST.Hindsight.HindsightVectorStore>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.AST.Hindsight.HindsightQueryService>();

		// Syncro IDE (Phase 0/1: shell, explorer, editor, workspace state)
		Syncro.Desktop.Services.Ide.SyncroIdeServiceCollectionExtensions.AddSyncroIde(builder.Services);
		builder.Services.AddScoped<Syncro.Desktop.SyncroUI.Theme.ThemeEngine>();

		// AST services registration
		builder.Services.AddSingleton<IAstParser, CSharpAstParser>();
		builder.Services.AddSingleton<IAstParser, TypeScriptAstParser>();
		builder.Services.AddSingleton<IAstParser, JavaScriptAstParser>();
		builder.Services.AddSingleton<IAstParser, PythonAstParser>();
		builder.Services.AddSingleton<IAstParser, ConfigFileParser>();

		builder.Services.AddSingleton<PortScanner>();
		builder.Services.AddSingleton<ApiEndpointScanner>();
		builder.Services.AddSingleton<SwaggerScanner>();
		builder.Services.AddSingleton<RouteScanner>();
		builder.Services.AddSingleton<ConfigScanner>();

		builder.Services.AddSingleton<FrameworkDetector>();
		builder.Services.AddSingleton<ProjectAnalyzer>();
		builder.Services.AddSingleton<DtoAnalyzer>();
		builder.Services.AddSingleton<DependencyAnalyzer>();
		builder.Services.AddSingleton<ComplexityAnalyzer>();
		builder.Services.AddSingleton<GraphExporter>();
		builder.Services.AddSingleton<DagBuilder>();

		builder.Services.AddSingleton<AstRegistry>();
		builder.Services.AddSingleton<AstEngine>();
		builder.Services.AddSingleton<AstStorageService>();
		builder.Services.AddSingleton<AstCacheManager>();
		builder.Services.AddSingleton<PdfReporter>();
		builder.Services.AddSingleton<JsonReporter>();
		builder.Services.AddSingleton<GitRepoService>();

		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.FolderCreationService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.BatchFileExecutionService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.CommandExecutionService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.MERNProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.SpringBootProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.PhpProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.DotNetProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.INLPServices, Syncro.Desktop.Services.ProgramLogic.SimpleNLPServices>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.DocumentationGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.GitCommandService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.QuickStartService>();
		// Phase 4 — database archetype + compose generator (solves projectgenerator.md B7)
		builder.Services.AddSingleton<Syncro.Desktop.Services.Database.DatabaseScaffolder>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Database.ComposeGenerator>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Flutter.FlutterBlocScaffolder>();
		builder.Services.AddHttpClient();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Auth.AuthService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.NetlifyService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.projectgenerator.ProjectGenerator>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.projectgenerator.Registry.StackRegistry>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.projectgenerator.Orchestration.PortAllocator>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.projectgenerator.Orchestration.ProjectWiringService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.projectgenerator.Orchestration.GroupOrchestrator>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.projectgenerator.IProjectCreationService, Syncro.Desktop.Services.projectgenerator.DefaultProjectCreationService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.VSTools.VscodeLauncherService>();
		// DevHub secondary window — shared fleet state between UniverseConnector and DevHub window
		builder.Services.AddSingleton<Syncro.Desktop.Services.Connector.DevHub.DevHubState>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Auto-start Named Pipe IPC server and browser monitor server on port 3030
		try
		{
			var bridgeServer = app.Services.GetRequiredService<Syncro.Desktop.Services.SyncroCLI.AgentBridgeServer>();
			var monitorServer = app.Services.GetRequiredService<Syncro.Desktop.Services.SyncroCLI.AgentMonitorServer>();
			bridgeServer.Start();
			monitorServer.Start();
		}
		catch {}

		// Start the Universe Runtime Engine (runtime.md R1) — the always-on event bus + graph updater.
		// Harmless with no workers yet (R1); R2 adds Run/Health workers that publish live state.
		try
		{
			app.Services.GetRequiredService<Syncro.Desktop.Services.Universe.Runtime.RuntimeCoordinator>().Start();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Runtime] Coordinator failed to start: {ex.Message}");
		}

		// Activate the MCP layer: every server below is registered in DI above, but the
		// orchestrator only learns about a server's tools once RegisterServerAsync runs.
		// Without this, the agent loop has no tools at all.
		try
		{
			var orchestrator = app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.McpOrchestrator>();
			Syncro.Desktop.Services.Engine.Mcp.IMcpServer[] mcpServers =
			{
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.WorkspaceMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.FileSystemMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.RepositoryMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.GitMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.AstMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.ApiMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.JsonIntelligenceMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.KnowledgeMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.PatchMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.PlanningMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Engine.Mcp.Servers.RepairKnowledgeMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Connector.Mcp.ConnectorMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Universe.Mcp.UniverseMcp>(),
				app.Services.GetRequiredService<Syncro.Desktop.Services.Universe.Mcp.RuntimeMcp>(),
			};

			foreach (var server in mcpServers)
				orchestrator.RegisterServerAsync(server).GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[MCP] Server registration failed: {ex.Message}");
		}

		// safeupgrade.md Phase 2 — characterization harness. Opt-in only: without
		// --characterize=baseline|verify on the command line this block is a no-op, so normal
		// app startup is unaffected (Phase 2 is "tests only, no product change").
		try
		{
			var cliArgs = Environment.GetCommandLineArgs();
			var characterizeArg = cliArgs.FirstOrDefault(a => a.StartsWith("--characterize", StringComparison.OrdinalIgnoreCase));
			if (characterizeArg != null)
			{
				var mode = characterizeArg.Contains('=') ? characterizeArg.Split('=', 2)[1] : "verify";
				var creationService = app.Services.GetRequiredService<Syncro.Desktop.Services.projectgenerator.IProjectCreationService>();
				var results = Syncro.Desktop.Services.projectgenerator.Testing.CharacterizationHarness
					.RunAsync(creationService).GetAwaiter().GetResult();
				var json = Syncro.Desktop.Services.projectgenerator.Testing.CharacterizationHarness.Serialize(results);

				string baselinePath = Path.Combine(AppContext.BaseDirectory, "characterization-baseline.json");
				if (mode.Equals("baseline", StringComparison.OrdinalIgnoreCase))
				{
					File.WriteAllText(baselinePath, json);
					System.Diagnostics.Debug.WriteLine($"[Characterization] Baseline saved to {baselinePath}");
				}
				else
				{
					if (File.Exists(baselinePath))
					{
						var baseline = Syncro.Desktop.Services.projectgenerator.Testing.CharacterizationHarness
							.Deserialize(File.ReadAllText(baselinePath));
						var diffs = Syncro.Desktop.Services.projectgenerator.Testing.CharacterizationHarness.Diff(baseline, results);
						System.Diagnostics.Debug.WriteLine(diffs.Count == 0
							? "[Characterization] No drift from baseline."
							: "[Characterization] DRIFT DETECTED:\n" + string.Join("\n", diffs));
					}
					else
					{
						System.Diagnostics.Debug.WriteLine("[Characterization] No baseline found. Run with --characterize=baseline first.");
					}
				}
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[Characterization] Harness failed: {ex.Message}");
		}

		return app;
	}
}
