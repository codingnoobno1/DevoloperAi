using System;
using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Ide;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands;

/// <summary>
/// Opens the Syncro IDE window, optionally targeting a specific workspace path.
/// </summary>
public class IdeCommand : ICliCommand
{
    private readonly IdeWindowService _ideWin;
    private readonly Action<string> _logger;

    public IdeCommand(IdeWindowService ideWin, Action<string> logger)
    {
        _ideWin = ideWin;
        _logger = logger;
    }

    public string Name => "ide";
    public string Description => "Open Syncro IDE (optionally with a workspace path).";

    public async Task Execute(CommandContext context)
    {
        string? path = null;

        if (context.Args.Count > 0 && !context.Args[0].StartsWith("-"))
        {
            path = Path.GetFullPath(context.Args[0]);
            if (!Directory.Exists(path))
            {
                _logger($"[✗] Directory not found: {path}");
                return;
            }
        }

        _logger($"  Opening Syncro IDE{(path != null ? $" → {path}" : "")}...");

        try
        {
            _ideWin.Open(path);
            _logger("  [✓] Syncro IDE window launched.");
        }
        catch (Exception ex)
        {
            _logger($"  [✗] Failed to open IDE: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}
