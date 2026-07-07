using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Universe.Runtime;

namespace Syncro.Desktop.Services.Universe.Mcp
{
    /// <summary>
    /// MCP server for the Runtime Engine (runtime.md R2). Lets the agent/UI start and stop stacks and
    /// read live status — but note the run manager runs and reports state *without* any LLM; these
    /// tools are just a control surface over it.
    /// </summary>
    public sealed class RuntimeMcp : IMcpServer
    {
        private readonly StackRunManager _runManager;
        private readonly EnvironmentBootstrapper _bootstrapper;
        private readonly Syncro.Desktop.Services.Universe.Dependencies.DependencyReconciler _reconciler;
        private readonly GroupRunOrchestrator _groupRun;
        private readonly IEventBus _bus;

        public RuntimeMcp(StackRunManager runManager, EnvironmentBootstrapper bootstrapper,
            Syncro.Desktop.Services.Universe.Dependencies.DependencyReconciler reconciler,
            GroupRunOrchestrator groupRun, IEventBus bus)
        {
            _runManager = runManager;
            _bootstrapper = bootstrapper;
            _reconciler = reconciler;
            _groupRun = groupRun;
            _bus = bus;
        }

        public string Name => "RuntimeMcp";
        public string Description =>
            "Starts/stops project stacks as managed processes (auto-installing deps first), and reports " +
            "live run status (state, port, cpu, memory). The engine tracks health continuously without the LLM.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new StartStackTool(_runManager);
            yield return new StopStackTool(_runManager);
            yield return new RunStatusTool(_runManager);
            yield return new BootstrapWorkspaceTool(_bootstrapper, _bus);
            yield return new ReconcileDependenciesTool(_reconciler, _bus);
            yield return new RunGroupTool(_groupRun, _bus);
            yield return new StopGroupTool(_groupRun);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    public sealed class RunGroupTool : IMcpTool
    {
        private readonly GroupRunOrchestrator _group;
        private readonly IEventBus _bus;
        public RunGroupTool(GroupRunOrchestrator group, IEventBus bus) { _group = group; _bus = bus; }

        public string Name => "RunGroup";
        public string Description =>
            "Runs a whole connected stack in dependency order (wave 0 databases → 1 backends → 2 " +
            "frontends), parallel within a wave and health-gated between waves. Each member installs " +
            "its deps first. One action to bring up the entire multi-project ecosystem.";
        public string InputSchema =>
            "{ \"members\": [ { \"workspaceId\": \"string\", \"label\": \"string\", \"workingDirectory\": \"string\", " +
            "\"command\": \"string\", \"port\": \"int?\", \"wave\": \"int (0=db,1=backend,2=frontend)\" } ] }";
        public bool RequiresAdminApproval => true; // spawns real processes

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeObject<RunGroupInput>(inputJson);
                if (input?.Members == null || input.Members.Count == 0)
                    return JsonConvert.SerializeObject(new { error = "members is required." });

                var specs = input.Members.Select(m =>
                {
                    var spec = RunSpec.FromCommandLine(m.WorkspaceId ?? "", string.IsNullOrWhiteSpace(m.Label) ? "app" : m.Label,
                        m.WorkingDirectory ?? "", m.Command ?? "", m.Port);
                    spec.Wave = m.Wave;
                    return spec;
                }).ToList();

                var started = await _group.RunGroupAsync(specs, _bus);
                return JsonConvert.SerializeObject(new
                {
                    started = started.Select(s => new { s.Id, s.Label, s.State, s.Port, s.Pid })
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        private sealed class RunGroupInput { public List<MemberInput> Members { get; set; } = new(); }
        private sealed class MemberInput
        {
            public string WorkspaceId { get; set; } = "";
            public string Label { get; set; } = "";
            public string WorkingDirectory { get; set; } = "";
            public string Command { get; set; } = "";
            public int Port { get; set; }
            public int Wave { get; set; } = 1;
        }
    }

    public sealed class StopGroupTool : IMcpTool
    {
        private readonly GroupRunOrchestrator _group;
        public StopGroupTool(GroupRunOrchestrator group) => _group = group;

        public string Name => "StopGroup";
        public string Description => "Stops every managed process (kill-tree) — the stop-all for a running stack group.";
        public string InputSchema => "{ }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try { _group.StopGroup(); return Task.FromResult(JsonConvert.SerializeObject(new { success = true })); }
            catch (Exception ex) { return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message })); }
        }
    }

    public sealed class ReconcileDependenciesTool : IMcpTool
    {
        private readonly Syncro.Desktop.Services.Universe.Dependencies.DependencyReconciler _reconciler;
        private readonly IEventBus _bus;
        public ReconcileDependenciesTool(Syncro.Desktop.Services.Universe.Dependencies.DependencyReconciler reconciler, IEventBus bus)
        {
            _reconciler = reconciler;
            _bus = bus;
        }

        public string Name => "ReconcileDependencies";
        public string Description =>
            "Detects packages a project imports but doesn't declare (e.g. `import cv2` with no " +
            "requirements entry → opencv-python), and — only when install=true — installs the missing, " +
            "registry-verified ones. Report-only by default. Never installs an unresolved/typo'd name.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"install\": \"bool (default false)\" }";
        public bool RequiresAdminApproval => true; // may install packages

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", install = false });
                if (string.IsNullOrWhiteSpace(input?.workspacePath))
                    return JsonConvert.SerializeObject(new { error = "workspacePath is required." });

                var r = await _reconciler.ReconcileAsync(input.workspacePath, input.install, _bus);
                return JsonConvert.SerializeObject(new
                {
                    ecosystem = r.Ecosystem,
                    missing = r.Missing.Select(m => new { m.Module, m.Package, m.Source }),
                    installed = r.Installed,
                    skipped = r.Skipped,
                    notes = r.Notes
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }

    public sealed class StartStackTool : IMcpTool
    {
        private readonly StackRunManager _run;
        public StartStackTool(StackRunManager run) => _run = run;

        public string Name => "StartStack";
        public string Description => "Starts a stack as a managed process from a run command. Returns its run status.";
        public string InputSchema => "{ \"workspaceId\": \"string\", \"label\": \"string\", \"workingDirectory\": \"string\", \"command\": \"string (full run command)\", \"port\": \"int?\" }";
        public bool RequiresAdminApproval => true; // spawns a real process

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspaceId = "", label = "", workingDirectory = "", command = "", port = 0 });
                if (string.IsNullOrWhiteSpace(input?.command) || string.IsNullOrWhiteSpace(input.workingDirectory))
                    return Task.FromResult(JsonConvert.SerializeObject(new { error = "command and workingDirectory are required." }));

                var spec = RunSpec.FromCommandLine(
                    input.workspaceId ?? "", string.IsNullOrWhiteSpace(input.label) ? "app" : input.label,
                    input.workingDirectory, input.command, input.port);

                var status = _run.Start(spec);
                return Task.FromResult(JsonConvert.SerializeObject(status));
            }
            catch (Exception ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }

    public sealed class StopStackTool : IMcpTool
    {
        private readonly StackRunManager _run;
        public StopStackTool(StackRunManager run) => _run = run;

        public string Name => "StopStack";
        public string Description => "Stops a managed process (kill-tree) by its run id (proc:{workspace}:{label}).";
        public string InputSchema => "{ \"id\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { id = "" });
                if (string.IsNullOrWhiteSpace(input?.id))
                    return Task.FromResult(JsonConvert.SerializeObject(new { error = "id is required." }));

                _run.Stop(input.id);
                return Task.FromResult(JsonConvert.SerializeObject(new { success = true, id = input.id }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }

    public sealed class BootstrapWorkspaceTool : IMcpTool
    {
        private readonly EnvironmentBootstrapper _bootstrapper;
        private readonly IEventBus _bus;
        public BootstrapWorkspaceTool(EnvironmentBootstrapper bootstrapper, IEventBus bus)
        {
            _bootstrapper = bootstrapper;
            _bus = bus;
        }

        public string Name => "BootstrapWorkspace";
        public string Description =>
            "Installs a workspace's dependencies deterministically (npm install / pip venv+install / " +
            "flutter pub get / dotnet restore), detected from its manifest. Skips if unchanged. No LLM.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"installCommand\": \"string?\" }";
        public bool RequiresAdminApproval => true; // installs packages

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", installCommand = "" });
                if (string.IsNullOrWhiteSpace(input?.workspacePath))
                    return JsonConvert.SerializeObject(new { error = "workspacePath is required." });

                var spec = new RunSpec
                {
                    WorkspaceId = input.workspacePath,
                    Label = "bootstrap",
                    WorkingDirectory = input.workspacePath,
                    InstallCommand = string.IsNullOrWhiteSpace(input.installCommand) ? null : input.installCommand
                };

                var ok = await _bootstrapper.EnsureAsync(spec, _bus, System.Threading.CancellationToken.None);
                return JsonConvert.SerializeObject(new { success = ok });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }

    public sealed class RunStatusTool : IMcpTool
    {
        private readonly StackRunManager _run;
        public RunStatusTool(StackRunManager run) => _run = run;

        public string Name => "RunStatus";
        public string Description => "Returns live status of all managed processes (state, port, cpu, memory). Read-only.";
        public string InputSchema => "{ }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var running = _run.Snapshot().Select(s => new
                {
                    s.Id, s.Label, s.WorkspaceId, s.State, s.Pid, s.Port,
                    cpu = s.CpuPercent, memoryMb = s.MemoryMb, startedAt = s.StartedAt
                });
                return Task.FromResult(JsonConvert.SerializeObject(new { running }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(JsonConvert.SerializeObject(new { error = ex.Message }));
            }
        }
    }
}
