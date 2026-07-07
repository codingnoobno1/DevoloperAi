using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST;
using Syncro.Desktop.Services.SyncroCLI.Core;

namespace Syncro.Desktop.Services.SyncroCLI.Commands;

public class AstCommand : ICliCommand
{
    private readonly AstService _astService;
    private readonly Action<string> _logger;

    public AstCommand(AstService astService, Action<string> logger)
    {
        _astService = astService;
        _logger = logger;
    }

    public string Name => "ast";
    public string Description => "Scan codebases and build AST maps, call/dependency graphs, and reports.";

    public async Task Execute(CommandContext context)
    {
        string projectPath = Directory.GetCurrentDirectory();
        
        // Parse arguments
        var argsList = context.Args;
        if (argsList.Count > 0 && !argsList[0].StartsWith("-"))
        {
            projectPath = Path.GetFullPath(argsList[0]);
        }

        if (!Directory.Exists(projectPath))
        {
            _logger($"[ERROR] Target directory not found: {projectPath}");
            return;
        }

        bool generatePdf = argsList.Contains("--pdf");
        bool generateJson = argsList.Contains("--json");

        _logger($"Syncro AST Engine: Starting scan on {projectPath}...");

        try
        {
            var map = await _astService.ScanProjectAsync(projectPath);

            _logger($"[SUCCESS] Scanning finished.");
            _logger($"  Project Name: {map.ProjectName}");
            _logger($"  Language:     {map.Language}");
            _logger($"  Framework:    {map.Framework}");
            _logger($"  Parsed Nodes: {map.Nodes.Count}");
            _logger($"  API Routes:   {map.Endpoints.Count}");
            _logger($"  DTO Models:   {map.Dtos.Count}");
            _logger($"  Open Ports:   {map.Ports.Count(p => p.State == "Open")}");

            // Export files
            if (generateJson)
            {
                _logger("Exporting JSON Map...");
                string jsonPath = await _astService.ExportJsonAsync(map, projectPath);
                _logger($"[SUCCESS] JSON Map exported to: {jsonPath}");
            }

            if (generatePdf)
            {
                _logger("Exporting QuestPDF Report...");
                string pdfPath = await _astService.GeneratePdfAsync(map, projectPath);
                _logger($"[SUCCESS] PDF Report exported to: {pdfPath}");
            }
        }
        catch (Exception ex)
        {
            _logger($"[ERROR] AST scan execution failed: {ex.Message}");
        }
    }
}
