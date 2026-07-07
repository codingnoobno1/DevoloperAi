using System;
using System.Net.Http;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands;

/// <summary>
/// Probes LLM (:3020), Monitor (:3030), and Ollama (:11434) services and prints live health.
/// </summary>
public class StatusCommand : ICliCommand
{
    private readonly Action<string> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public StatusCommand(Action<string> logger) => _logger = logger;

    public string Name => "status";
    public string Description => "Show live status of LLM, Monitor, and connected services.";

    public async Task Execute(CommandContext context)
    {
        _logger("╔══════════════════════════════════════════╗");
        _logger("║        SYNCRO CLI — SERVICE STATUS       ║");
        _logger("╚══════════════════════════════════════════╝");
        _logger("");

        // LLM Express (:3020)
        await ProbeService("LLM (Gemini Express)", "http://localhost:3020/");

        // Diagnostics Monitor (:3030)
        await ProbeService("Diagnostics Monitor", "http://localhost:3030/");

        // Ollama (:11434)
        await ProbeService("Ollama (Local LLM)", "http://localhost:11434/");

        // Workspace info
        var cwd = System.IO.Directory.GetCurrentDirectory();
        _logger($"  Workspace: {cwd}");
        _logger("");
        _logger("Status check complete.");
    }

    private async Task ProbeService(string name, string url)
    {
        try
        {
            var resp = await _http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync();
                string extra = body.Length > 80 ? body[..80] + "…" : body;
                _logger($"  [✓] {name,-28} ONLINE   {url}");
            }
            else
            {
                _logger($"  [✗] {name,-28} ERROR    HTTP {(int)resp.StatusCode}");
            }
        }
        catch
        {
            _logger($"  [✗] {name,-28} OFFLINE  {url}");
        }
    }
}
