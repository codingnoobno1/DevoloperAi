using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;
using Newtonsoft.Json;

namespace Syncro.Desktop.Services.AST;

public class AstService
{
    private readonly AstEngine _engine;
    // Caching/Storage will be implemented fully in Phase 1d.
    // For now, we stub them to make the Service fully functional.

    public AstService(AstEngine engine)
    {
        _engine = engine;
    }

    // Scan a single project path, return full project map
    public async Task<AstProjectMap> ScanProjectAsync(string projectPath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Target project directory not found: {projectPath}");
        }

        var ctx = new AstContext
        {
            RootPath = projectPath,
            CancellationToken = ct
        };

        return await _engine.RunAsync(projectPath, ctx);
    }

    // Scan multiple projects, returning an aggregated report
    public async Task<AstReport> ScanAllProjectsAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var report = new AstReport();
        foreach (var path in paths)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var map = await ScanProjectAsync(path, ct);
                report.ProjectMaps.Add(map);
            }
            catch (Exception ex)
            {
                // Add empty project map with logged error
                report.ProjectMaps.Add(new AstProjectMap
                {
                    ProjectPath = path,
                    Errors = new List<string> { ex.Message }
                });
            }
        }
        return report;
    }

    // Stub for PDF generation (QuestPDF implemented in Phase 1d)
    public Task<string> GeneratePdfAsync(AstProjectMap map, string outputPath)
    {
        // Placeholder file output
        string fullPath = Path.Combine(outputPath, $"{map.ProjectName}_AST_Report.pdf");
        File.WriteAllText(fullPath, "QuestPDF placeholder content for: " + map.ProjectName);
        return Task.FromResult(fullPath);
    }

    // Export AST Project Map as a JSON file
    public async Task<string> ExportJsonAsync(AstProjectMap map, string outputPath)
    {
        string fullPath = Path.Combine(outputPath, $"{map.ProjectName}_AST_Map.json");
        string json = JsonConvert.SerializeObject(map, Formatting.Indented);
        await File.WriteAllTextAsync(fullPath, json);
        return fullPath;
    }

    // Stub for Caching (Cache manager implemented in Phase 1d)
    public Task<AstProjectMap?> GetCachedMapAsync(string projectPath)
    {
        return Task.FromResult<AstProjectMap?>(null);
    }
}
