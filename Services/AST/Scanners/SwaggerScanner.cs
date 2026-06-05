using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Scanners;

public class SwaggerScanner
{
    // Find swagger or openapi JSON/YAML spec files in directory recursively
    public IEnumerable<string> FindSwaggerFiles(string projectPath)
    {
        var swaggerFiles = new List<string>();
        if (!Directory.Exists(projectPath)) return swaggerFiles;

        string[] targets = { "swagger.json", "openapi.json", "swagger.yaml", "swagger.yml", "openapi.yaml", "openapi.yml" };
        foreach (var file in Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file).ToLower();
            if (targets.Contains(name))
            {
                swaggerFiles.Add(file);
            }
        }
        return swaggerFiles;
    }

    // Parse spec from file using Microsoft.OpenApi.Readers
    public async Task<SwaggerSpec> ParseAsync(string filePath)
    {
        var spec = new SwaggerSpec
        {
            FilePath = filePath
        };

        try
        {
            string content = await File.ReadAllTextAsync(filePath);
            spec.RawJsonOrYaml = content;

            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            var readResult = new OpenApiStreamReader().Read(stream, out var diagnostic);

            if (readResult != null)
            {
                spec.Title = readResult.Info?.Title ?? "Unknown API";
                spec.Version = readResult.Info?.Version ?? "1.0.0";
                
                foreach (var pathEntry in readResult.Paths)
                {
                    string routePath = pathEntry.Key;
                    foreach (var opEntry in pathEntry.Value.Operations)
                    {
                        var method = opEntry.Key.ToString().ToUpper();
                        var endpoint = new AstEndpoint
                        {
                            Method = method,
                            Path = routePath,
                            FilePath = filePath,
                            ControllerName = "SwaggerImport",
                            HandlerMethod = opEntry.Value.OperationId ?? $"{method}_{routePath.Replace("/", "_").Trim('_')}"
                        };

                        // Map parameters
                        foreach (var parameter in opEntry.Value.Parameters)
                        {
                            string typeName = parameter.Schema?.Type ?? "string";
                            endpoint.Parameters.Add($"{typeName} {parameter.Name}");
                        }

                        spec.Endpoints.Add(endpoint);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Capture errors
            spec.Title = $"Error parsing: {Path.GetFileName(filePath)}";
            spec.RawJsonOrYaml = $"Error: {ex.Message}";
        }

        return spec;
    }

    // Merge multiple swagger specs
    public SwaggerSpec MergeSpecs(IEnumerable<SwaggerSpec> specs)
    {
        var merged = new SwaggerSpec
        {
            Title = "Merged OpenAPI Specification",
            Version = "1.0.0"
        };

        foreach (var spec in specs)
        {
            foreach (var ep in spec.Endpoints)
            {
                if (!merged.Endpoints.Any(e => e.Path == ep.Path && e.Method == ep.Method))
                {
                    merged.Endpoints.Add(ep);
                }
            }
        }

        return merged;
    }

    // Convert spec to generic AstEndpoint list
    public List<AstEndpoint> ToAstEndpoints(SwaggerSpec spec)
    {
        return spec.Endpoints;
    }
}
