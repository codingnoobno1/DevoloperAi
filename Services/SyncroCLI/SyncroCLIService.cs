using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;
using Syncro.Desktop.Services.SyncroCLI.Commands;
using Syncro.Desktop.Services.SyncroCLI.Providers;
using Syncro.Desktop.Services.SyncroCLI.Execution;
using Syncro.Desktop.Services.SyncroCLI.Providers.Git;
using Syncro.Desktop.Services.SyncroCLI.Core.Platform;
using DeveloperAI.BusinessLogic;

namespace Syncro.Desktop.Services.SyncroCLI
{
    public class SyncroCLIService
    {
        public readonly CliEngine Engine;
        public readonly ProcessRunner ProcessRunner;
        public readonly ScriptRunner ScriptRunner;
        public readonly MarketplaceService Marketplace;
        public readonly ElevationService Elevation;
        public readonly IPlatformService Platform;
        private readonly AIClient _aiClient;

        public event Action<string>? OnLog;

        public SyncroCLIService(AIClient aiClient)
        {
            _aiClient = aiClient;
            
            // Phase 1: Platform Detection
            var platformType = PlatformDetector.Detect();
            Platform = platformType switch
            {
                PlatformType.Windows => new WindowsPlatform(),
                PlatformType.Linux => new LinuxPlatform(),
                PlatformType.Mac => new MacPlatform(),
                PlatformType.WSL => new WslPlatform(),
                _ => new WindowsPlatform() // Fallback
            };

            ProcessRunner = new ProcessRunner(Platform);
            ScriptRunner = new ScriptRunner(Platform, ProcessRunner);
            Marketplace = new MarketplaceService();
            Elevation = new ElevationService(Platform);
            Engine = new CliEngine();

            ProcessRunner.OnOutput += Log;
            Engine.OnOutput += Log;

            Log($"System: Initialized execution engine for {Platform.Type}");
            
            // Background load marketplace
            _ = Marketplace.LoadMarketplace();
            
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            // Register Providers
            var providers = new List<IProjectProvider>
            {
                new PythonProvider(ProcessRunner),
                new MernProvider(ProcessRunner),
                new JavaGradleProvider(ProcessRunner),
                new DartProvider(ProcessRunner),
                new FlutterProvider(ProcessRunner),
                new GoProvider(ProcessRunner)
            };

            var gitProvider = new GitProvider(ProcessRunner);

            // Register Commands
            Engine.RegisterCommand(new InitCommand(providers, _aiClient, Log));
            Engine.RegisterCommand(new GitCommand(gitProvider, Log));
            Engine.RegisterCommand(new DoctorCommand(Log));
            Engine.RegisterCommand(new ScriptCommand(ScriptRunner, Marketplace, Log));
            // Add Help Command
            Engine.RegisterCommand(new HelpCommand(Engine, Log));
        }

        public void Log(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public async Task InitializeCLI()
        {
            Log("SyncroCLI Engine Started.");
            await AddToPath(AppDomain.CurrentDomain.BaseDirectory);
        }

        private async Task AddToPath(string path)
        {
            await Task.Run(() =>
            {
                try
                {
                    const string pathName = "PATH";
                    var scope = EnvironmentVariableTarget.User;
                    var oldValue = Environment.GetEnvironmentVariable(pathName, scope);
                    
                    if (oldValue == null) return;

                    var paths = oldValue.Split(';').Select(p => p.Trim()).ToList();
                    
                    if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        Log($"System: Adding binary path to PATH...");
                        var newValue = oldValue + (oldValue.EndsWith(";") ? "" : ";") + path;
                        Environment.SetEnvironmentVariable(pathName, newValue, scope);
                    }
                }
                catch (Exception ex)
                {
                    Log($"System: Error updating PATH: {ex.Message}");
                }
            });
        }

        public async Task ExecuteCommand(string input, string? password = null)
        {
            await Engine.ProcessInput(input, password);
        }

        public async Task<bool> CreateProject(string stack, string name, string path)
        {
            try
            {
                var runner = new ProcessRunner(Platform);
                runner.OnOutput += Log;
                
                IProjectProvider? provider = stack.ToLower() switch
                {
                    "flutter" => new FlutterProvider(runner),
                    "python" => new PythonProvider(runner),
                    "mern" => new MernProvider(runner),
                    "java" => new JavaGradleProvider(runner),
                    "dart" => new DartProvider(runner),
                    "go" => new GoProvider(runner),
                    _ => null
                };

                if (provider != null)
                {
                    Log($"CLI: Creating {stack} project '{name}' at {path}...");
                    await provider.Create(name, path);
                    return true;
                }
                
                Log($"CLI: Error - No provider for {stack}");
                return false;
            }
            catch (Exception ex)
            {
                Log($"CLI: Error creating project: {ex.Message}");
                return false;
            }
        }
    }

    public class HelpCommand : ICliCommand
    {
        private readonly CliEngine _engine;
        private readonly Action<string> _logger;

        public HelpCommand(CliEngine engine, Action<string> logger)
        {
            _engine = engine;
            _logger = logger;
        }

        public string Name => "help";
        public string Description => "List all available commands.";

        public async Task Execute(CommandContext context)
        {
            await Task.Run(() => {
                _logger("Available Commands:");
                foreach (var cmd in _engine.GetCommands())
                {
                    _logger($"  {cmd.Name,-10} - {cmd.Description}");
                }
            });
        }
    }
}
