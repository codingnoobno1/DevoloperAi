using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands;

/// <summary>
/// Opens the Diagnostics Monitor (localhost:3030) in the default system browser.
/// </summary>
public class MonitorCommand : ICliCommand
{
    private readonly Action<string> _logger;

    public MonitorCommand(Action<string> logger) => _logger = logger;

    public string Name => "monitor";
    public string Description => "Open the Syncro AI Diagnostics Monitor in your browser.";

    public async Task Execute(CommandContext context)
    {
        string url = "http://localhost:3030";
        _logger($"  Opening {url} in default browser...");

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _logger("  [✓] Diagnostics Monitor opened.");
        }
        catch (Exception ex)
        {
            _logger($"  [✗] Could not open browser: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}
