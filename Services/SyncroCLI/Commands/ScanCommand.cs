using System;
using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST;
using Syncro.Desktop.Services.AST.Models;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands;

/// <summary>
/// Scans a codebase using the AST engine and fires an event so the UI can populate the Scan tab.
/// </summary>
public class ScanCommand : ICliCommand
{
    private readonly AstService _astService;
    private readonly Action<string> _logger;

    /// <summary>Fired after a successful scan with the resulting AstProjectMap.</summary>
    public event Action<AstProjectMap>? OnScanComplete;

    /// <summary>Last scan result, available for the UI to read.</summary>
    public AstProjectMap? LastScan { get; private set; }

    public ScanCommand(AstService astService, Action<string> logger)
    {
        _astService = astService;
        _logger = logger;
    }

    public string Name => "scan";
    public string Description => "Scan a codebase and show AST summary (nodes, routes, DTOs, complexity).";

    public async Task Execute(CommandContext context)
    {
        string projectPath = Directory.GetCurrentDirectory();

        if (context.Args.Count > 0 && !context.Args[0].StartsWith("-"))
        {
            projectPath = Path.GetFullPath(context.Args[0]);
        }

        if (!Directory.Exists(projectPath))
        {
            _logger($"[✗] Directory not found: {projectPath}");
            return;
        }

        _logger("╔══════════════════════════════════════════╗");
        _logger("║       SYNCRO CLI — CODEBASE SCAN         ║");
        _logger("╚══════════════════════════════════════════╝");
        _logger("");
        _logger($"  Target: {projectPath}");
        _logger("  Scanning...");

        try
        {
            var map = await _astService.ScanProjectAsync(projectPath);
            LastScan = map;

            _logger("");
            _logger($"  [✓] Scan complete");
            _logger($"      Project:    {map.ProjectName}");
            _logger($"      Language:   {map.Language}");
            _logger($"      Framework:  {map.Framework}");
            _logger($"      AST Nodes:  {map.Nodes.Count}");
            _logger($"      Endpoints:  {map.Endpoints.Count}");
            _logger($"      DTO Models: {map.Dtos.Count}");
            _logger($"      Open Ports: {map.Ports.Count}");
            _logger("");

            OnScanComplete?.Invoke(map);
        }
        catch (Exception ex)
        {
            _logger($"  [✗] Scan failed: {ex.Message}");
        }
    }
}
