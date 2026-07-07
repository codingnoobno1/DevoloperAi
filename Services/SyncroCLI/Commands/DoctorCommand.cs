using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands;

/// <summary>
/// Checks the real health of the development environment by probing
/// dotnet, node, python, git, flutter on PATH and the LLM service.
/// </summary>
public class DoctorCommand : ICliCommand
{
    private readonly Action<string> _logger;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(3) };

    public DoctorCommand(Action<string> logger)
    {
        _logger = logger;
    }

    public string Name => "doctor";
    public string Description => "Check the health of your development environment.";

    public async Task Execute(CommandContext context)
    {
        _logger("╔══════════════════════════════════════════╗");
        _logger("║       SYNCRO CLI — ENVIRONMENT DOCTOR    ║");
        _logger("╚══════════════════════════════════════════╝");
        _logger("");

        // SDK / runtime checks
        CheckTool("dotnet", "--version", ".NET SDK");
        CheckTool("node", "--version", "Node.js");
        CheckTool("npm", "--version", "npm");
        CheckTool("python", "--version", "Python");
        CheckTool("git", "--version", "Git");
        CheckTool("flutter", "--version", "Flutter");
        CheckTool("java", "-version", "Java JDK");

        _logger("");

        // Service probes
        await ProbeHttp("LLM Express (3020)", "http://localhost:3020/");
        await ProbeHttp("Monitor   (3030)", "http://localhost:3030/");
        await ProbeHttp("Ollama    (11434)", "http://localhost:11434/");

        _logger("");
        _logger("Doctor check complete.");
    }

    private void CheckTool(string exe, string args, string label)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) { _logger($"  [✗] {label,-22} not found"); return; }

            string output = proc.StandardOutput.ReadToEnd().Trim();
            string errOut = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit(5000);

            string version = !string.IsNullOrEmpty(output) ? output : errOut;
            // Take first line only
            if (version.Contains('\n')) version = version.Split('\n')[0].Trim();
            if (version.Length > 60) version = version[..60];

            _logger($"  [✓] {label,-22} {version}");
        }
        catch
        {
            _logger($"  [✗] {label,-22} not found in PATH");
        }
    }

    private async Task ProbeHttp(string label, string url)
    {
        try
        {
            var resp = await _http.GetAsync(url);
            _logger(resp.IsSuccessStatusCode
                ? $"  [✓] {label,-22} ONLINE"
                : $"  [!] {label,-22} HTTP {(int)resp.StatusCode}");
        }
        catch
        {
            _logger($"  [✗] {label,-22} OFFLINE");
        }
    }
}
