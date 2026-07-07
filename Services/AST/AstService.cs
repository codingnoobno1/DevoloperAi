using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;
using Syncro.Desktop.Services.AST.Storage;
using Syncro.Desktop.Services.AST.Reporters;

namespace Syncro.Desktop.Services.AST;

public class AstService
{
    private readonly AstEngine _engine;
    private readonly AstStorageService _storage;
    private readonly AstCacheManager _cache;
    private readonly PdfReporter _pdfReporter;
    private readonly JsonReporter _jsonReporter;

    public AstProjectMap? ActiveProjectMap { get; set; }

    public AstService(
        AstEngine engine,
        AstStorageService storage,
        AstCacheManager cache,
        PdfReporter pdfReporter,
        JsonReporter jsonReporter)
    {
        _engine = engine;
        _storage = storage;
        _cache = cache;
        _pdfReporter = pdfReporter;
        _jsonReporter = jsonReporter;
    }

    // Scan a single project path, utilizing cache if valid
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

        // Compute current signature of the directory structure
        string currentHash = _cache.ComputeDirectorySignature(projectPath, ctx.IgnorePatterns.ToArray());
        
        // Attempt to load from local storage
        var cachedMap = await _storage.LoadMapAsync(projectPath);
        if (cachedMap != null && cachedMap.Errors.Count == 0)
        {
            // Verify signature match
            if (cachedMap.ConfigFiles.TryGetValue("__cache_signature__", out string? storedHash) && storedHash == currentHash)
            {
                ActiveProjectMap = cachedMap;
                return cachedMap;
            }
        }

        // Run full scanner ingestion
        var map = await _engine.RunAsync(projectPath, ctx);
        
        // Cache result with signature
        map.ConfigFiles["__cache_signature__"] = currentHash;
        await _storage.SaveMapAsync(projectPath, map);

        ActiveProjectMap = map;
        return map;
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
                report.ProjectMaps.Add(new AstProjectMap
                {
                    ProjectPath = path,
                    Errors = new List<string> { ex.Message }
                });
            }
        }
        return report;
    }

    // Generate QuestPDF report
    public async Task<string> GeneratePdfAsync(AstProjectMap map, string outputPath)
    {
        return await _pdfReporter.ExportAsync(map, outputPath);
    }

    // Export AST Project Map as a JSON file
    public async Task<string> ExportJsonAsync(AstProjectMap map, string outputPath)
    {
        return await _jsonReporter.ExportAsync(map, outputPath);
    }

    // Returns stored map if available
    public async Task<AstProjectMap?> GetCachedMapAsync(string projectPath)
    {
        return await _storage.LoadMapAsync(projectPath);
    }

    // Tokenizes parsed project AST nodes into the project's vector DB
    public async Task TokenizeProjectAsync(AstProjectMap map)
    {
        if (map == null) return;
        
        string projectId = map.ProjectName.ToLower().Replace(" ", "_");
        string dbRoot = Path.Combine(map.ProjectPath, ".syncro_db");
        string vectorsDir = Path.Combine(dbRoot, "Vectors", projectId);
        Directory.CreateDirectory(vectorsDir);

        var vectorRecords = new List<object>();
        int offset = 0;
        
        // Loop through all parsed nodes (Classes, Methods, Routes, DTOs)
        var sourceNodes = map.Nodes.Where(n => !n.IsExternalOrBoilerplate).ToList();
        foreach (var node in sourceNodes)
        {
            // Simple embedding: generate a mock float array based on symbol name hash
            var mockVector = new float[768];
            int hash = (node.Name ?? "").GetHashCode();
            Random rand = new Random(hash);
            for (int j = 0; j < 768; j++)
            {
                mockVector[j] = (float)rand.NextDouble();
            }

            var record = new {
                vector_id = $"vec-{node.Type.ToString().ToLower()}-{Guid.NewGuid().ToString().Substring(0, 8)}",
                project_id = projectId,
                file_path = node.FilePath,
                symbol_name = node.Name,
                node_type = node.Type.ToString(),
                language = map.Language,
                framework = map.Framework,
                tags = new string[] { node.Type.ToString().ToLower(), map.Framework.ToLower() },
                embedding_offset = offset,
                embedding_dim = 768,
                embedding_hash = hash.ToString("X"),
                code_snippet = node.Summary ?? node.Name,
                line_start = node.LineNumber,
                line_end = node.LineNumber + 5,
                last_indexed = DateTime.UtcNow.ToString("o")
            };
            
            vectorRecords.Add(record);
            offset += 768 * 4; // float is 4 bytes
        }

        string indexPath = Path.Combine(vectorsDir, "index.json");
        await File.WriteAllTextAsync(indexPath, Newtonsoft.Json.JsonConvert.SerializeObject(vectorRecords, Newtonsoft.Json.Formatting.Indented));

        // Update projects.json index
        string projectsFile = Path.Combine(dbRoot, "Projects", "projects.json");
        if (File.Exists(projectsFile))
        {
            try
            {
                var content = await File.ReadAllTextAsync(projectsFile);
                var projects = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Newtonsoft.Json.Linq.JObject>>(content);
                if (projects != null)
                {
                    var proj = projects.FirstOrDefault(p => p["path"]?.ToString() == map.ProjectPath);
                    if (proj == null)
                    {
                        proj = new Newtonsoft.Json.Linq.JObject {
                            ["project_id"] = projectId,
                            ["name"] = map.ProjectName,
                            ["path"] = map.ProjectPath,
                            ["language"] = map.Language,
                            ["framework"] = map.Framework,
                            ["packageManager"] = "npm",
                            ["entryPoint"] = "",
                            ["architecture"] = "Generic",
                            ["databases"] = new Newtonsoft.Json.Linq.JArray(),
                            ["hasVenv"] = false,
                            ["requiresDocker"] = false,
                            ["groupIds"] = new Newtonsoft.Json.Linq.JArray(),
                            ["lastScanned"] = DateTime.UtcNow.ToString("o"),
                            ["astNodeCount"] = map.Nodes.Count
                        };
                        projects.Add(proj);
                    }
                    
                    proj["vectorCount"] = vectorRecords.Count;
                    proj["lastScanned"] = DateTime.UtcNow.ToString("o");
                    await File.WriteAllTextAsync(projectsFile, Newtonsoft.Json.JsonConvert.SerializeObject(projects, Newtonsoft.Json.Formatting.Indented));
                }
            }
            catch {}
        }
    }
}
