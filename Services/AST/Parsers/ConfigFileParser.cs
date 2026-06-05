using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Parsers;

public class ConfigFileParser : IAstParser
{
    public IReadOnlyList<string> SupportedExtensions => new[] { ".json", ".yaml", ".yml", ".toml", ".txt", ".env" };

    public async Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx)
    {
        if (ctx.CancellationToken.IsCancellationRequested) return Array.Empty<AstNode>();

        var nodes = new List<AstNode>();
        string fileName = Path.GetFileName(filePath).ToLower();

        try
        {
            if (fileName == "package.json")
            {
                await ParsePackageJson(filePath, nodes, ctx);
            }
            else if (fileName == "appsettings.json" || fileName == "appsettings.development.json")
            {
                await ParseAppSettingsJson(filePath, nodes, ctx);
            }
            else if (fileName == "docker-compose.yaml" || fileName == "docker-compose.yml")
            {
                await ParseDockerCompose(filePath, nodes, ctx);
            }
            else if (fileName == "requirements.txt")
            {
                await ParseRequirementsTxt(filePath, nodes, ctx);
            }
            else if (fileName == ".env" || fileName.EndsWith(".env"))
            {
                await ParseEnvFile(filePath, nodes, ctx);
            }
        }
        catch (Exception ex)
        {
            ctx.Errors.Add($"Error parsing configuration file {filePath}: {ex.Message}");
        }

        return nodes;
    }

    private async Task ParsePackageJson(string filePath, List<AstNode> nodes, AstContext ctx)
    {
        string text = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);
        var obj = JObject.Parse(text);

        // Project Node
        string name = obj["name"]?.ToString() ?? Path.GetFileName(Path.GetDirectoryName(filePath)) ?? "Unknown";
        string version = obj["version"]?.ToString() ?? "1.0.0";
        
        nodes.Add(new AstNode
        {
            Id = $"{filePath}::project",
            Name = name,
            Type = AstNodeType.Project,
            FilePath = filePath,
            LineNumber = 1,
            Summary = $"npm Package: {name} v{version}",
            Metadata = new Dictionary<string, object> { { "version", version } }
        });

        // Dependencies
        var deps = obj["dependencies"] as JObject;
        if (deps != null)
        {
            foreach (var prop in deps.Properties())
            {
                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::dependency::{prop.Name}",
                    Name = prop.Name,
                    Type = AstNodeType.Module,
                    FilePath = filePath,
                    LineNumber = 1,
                    Summary = $"npm dependency: {prop.Name} ({prop.Value})",
                    Metadata = new Dictionary<string, object> { { "version", prop.Value.ToString() }, { "dependencyType", "npm" } }
                });
            }
        }

        // Scripts
        var scripts = obj["scripts"] as JObject;
        if (scripts != null)
        {
            foreach (var prop in scripts.Properties())
            {
                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::script::{prop.Name}",
                    Name = prop.Name,
                    Type = AstNodeType.Config,
                    FilePath = filePath,
                    LineNumber = 1,
                    Summary = $"npm script command: {prop.Name} -> {prop.Value}",
                    Metadata = new Dictionary<string, object> { { "command", prop.Value.ToString() } }
                });
            }
        }
    }

    private async Task ParseAppSettingsJson(string filePath, List<AstNode> nodes, AstContext ctx)
    {
        string text = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);
        var obj = JObject.Parse(text);

        nodes.Add(new AstNode
        {
            Id = $"{filePath}::settings",
            Name = Path.GetFileName(filePath),
            Type = AstNodeType.Config,
            FilePath = filePath,
            LineNumber = 1,
            Summary = "ASP.NET Core AppSettings Config File"
        });

        // Try extracting connection strings keys
        var connStrings = obj["ConnectionStrings"] as JObject;
        if (connStrings != null)
        {
            foreach (var prop in connStrings.Properties())
            {
                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::connectionstring::{prop.Name}",
                    Name = prop.Name,
                    Type = AstNodeType.Config,
                    FilePath = filePath,
                    LineNumber = 1,
                    Summary = $"DB Connection Key: {prop.Name}"
                });
            }
        }
    }

    private async Task ParseDockerCompose(string filePath, List<AstNode> nodes, AstContext ctx)
    {
        string text = await File.ReadAllTextAsync(filePath, ctx.CancellationToken);
        var deserializer = new DeserializerBuilder().Build();
        var compose = deserializer.Deserialize<Dictionary<object, object>>(text);

        nodes.Add(new AstNode
        {
            Id = $"{filePath}::docker",
            Name = Path.GetFileName(filePath),
            Type = AstNodeType.Config,
            FilePath = filePath,
            LineNumber = 1,
            Summary = "Docker Compose Configuration"
        });

        if (compose != null && compose.TryGetValue("services", out var servicesObj) && servicesObj is Dictionary<object, object> services)
        {
            foreach (var serviceEntry in services)
            {
                string serviceName = serviceEntry.Key.ToString() ?? "UnknownService";
                var serviceDetails = serviceEntry.Value as Dictionary<object, object>;
                
                string portsDesc = "";
                if (serviceDetails != null && serviceDetails.TryGetValue("ports", out var portsListObj) && portsListObj is List<object> ports)
                {
                    portsDesc = " exposing ports: " + string.Join(", ", ports);
                }

                nodes.Add(new AstNode
                {
                    Id = $"{filePath}::service::{serviceName}",
                    Name = serviceName,
                    Type = AstNodeType.Module,
                    FilePath = filePath,
                    LineNumber = 1,
                    Summary = $"Docker Service: {serviceName}{portsDesc}"
                });
            }
        }
    }

    private async Task ParseRequirementsTxt(string filePath, List<AstNode> nodes, AstContext ctx)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, ctx.CancellationToken);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            // Matches package==version, package>=version, etc.
            var match = Regex.Match(line, @"^([a-zA-Z0-9_\-\[\]]+)\s*([<>=!~]+)\s*([a-zA-Z0-9\.\-]+)");
            string pkgName = match.Success ? match.Groups[1].Value : line;
            string pkgVersion = match.Success ? match.Groups[3].Value : "Latest";

            nodes.Add(new AstNode
            {
                Id = $"{filePath}::pip::{pkgName}",
                Name = pkgName,
                Type = AstNodeType.Module,
                FilePath = filePath,
                LineNumber = i + 1,
                Summary = $"Python pip dependency: {pkgName} ({pkgVersion})",
                Metadata = new Dictionary<string, object> { { "version", pkgVersion }, { "dependencyType", "pip" } }
            });
        }
    }

    private async Task ParseEnvFile(string filePath, List<AstNode> nodes, AstContext ctx)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, ctx.CancellationToken);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

            int eqIndex = line.IndexOf('=');
            if (eqIndex == -1) continue;

            string key = line.Substring(0, eqIndex).Trim();
            // Crucial: we do NOT store the value for security and privacy. We only map keys.

            nodes.Add(new AstNode
            {
                Id = $"{filePath}::env::{key}",
                Name = key,
                Type = AstNodeType.Config,
                FilePath = filePath,
                LineNumber = i + 1,
                Summary = $"Environment Variable Key: {key}"
            });
        }
    }

    public async Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx)
    {
        var allNodes = new List<AstNode>();
        // Only walk known config files explicitly to save search time
        string[] configFilesToFind = {
            "package.json",
            "appsettings.json",
            "appsettings.development.json",
            "docker-compose.yaml",
            "docker-compose.yml",
            "requirements.txt",
            ".env"
        };

        foreach (var name in configFilesToFind)
        {
            if (ctx.CancellationToken.IsCancellationRequested) break;
            
            var files = Directory.GetFiles(rootPath, name, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string rel = Path.GetRelativePath(rootPath, file);
                if (ctx.IgnorePatterns.Any(p => rel.Contains(Path.DirectorySeparatorChar + p + Path.DirectorySeparatorChar) || rel.StartsWith(p + Path.DirectorySeparatorChar)))
                {
                    continue;
                }
                
                var nodes = await ParseFileAsync(file, ctx);
                allNodes.AddRange(nodes);
            }
        }

        return allNodes;
    }
}
