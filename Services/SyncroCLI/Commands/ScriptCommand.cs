using System;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;
using Syncro.Desktop.Services.SyncroCLI.Execution;
using Syncro.Desktop.Services.SyncroCLI.Providers;

namespace Syncro.Desktop.Services.SyncroCLI.Commands
{
    public class ScriptCommand : ICliCommand
    {
        private readonly ScriptRunner _scriptRunner;
        private readonly MarketplaceService _marketplace;
        private readonly Action<string> _logger;

        public ScriptCommand(ScriptRunner scriptRunner, MarketplaceService marketplace, Action<string> logger)
        {
            _scriptRunner = scriptRunner;
            _marketplace = marketplace;
            _logger = logger;
        }

        public string Name => "run";
        public string Description => "Run a script from the Syncro Marketplace. Usage: run <script_id>";

        public async Task Execute(CommandContext context)
        {
            if (context.Args.Count == 0)
            {
                _logger("Usage: run <script_id>");
                _logger("Available scripts:");
                foreach (var s in _marketplace.GetAllScripts())
                {
                    _logger($"  {s.Id,-15} - {s.Name}");
                }
                return;
            }

            var scriptId = context.Args[0];
            var script = _marketplace.GetScript(scriptId);

            if (script == null)
            {
                _logger($"Error: Script '{scriptId}' not found in marketplace.");
                return;
            }

            _logger($"Starting: {script.Name}...");
            
            try
            {
                var exitCode = await _scriptRunner.RunScript(scriptId, password: context.Password);
                if (exitCode == 0)
                {
                    _logger($"Success: {script.Name} completed successfully.");
                }
                else
                {
                    _logger($"Warning: {script.Name} exited with code {exitCode}.");
                }
            }
            catch (Exception ex)
            {
                _logger($"Error: Failed to execute script: {ex.Message}");
            }
        }
    }
}
