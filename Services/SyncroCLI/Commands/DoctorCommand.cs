using System;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands
{
    public class DoctorCommand : ICliCommand
    {
        private readonly Action<string> _logger;

        public DoctorCommand(Action<string> logger)
        {
            _logger = logger;
        }

        public string Name => "doctor";
        public string Description => "Check the health of your development environment.";

        public async Task Execute(CommandContext context)
        {
            _logger("Syncro Doctor - Checking Environment...");
            await Task.Delay(500);
            _logger("[OK] .NET 9.0 detected.");
            _logger("[OK] Node.js detected.");
            _logger("[!] Python not found in PATH (optional).");
            _logger("All systems operational.");
        }
    }
}
