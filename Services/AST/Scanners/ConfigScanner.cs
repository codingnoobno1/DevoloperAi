using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;

namespace Syncro.Desktop.Services.AST.Scanners;

public class ConfigScanner
{
    // Find all configuration and package manifests in target path
    public Dictionary<string, string> FindConfigFiles(string projectPath)
    {
        var filesMap = new Dictionary<string, string>();
        if (!Directory.Exists(projectPath)) return filesMap;

        string[] targets = { 
            "package.json", 
            "appsettings.json", 
            "appsettings.development.json",
            "docker-compose.yaml", 
            "docker-compose.yml",
            "pyproject.toml",
            "requirements.txt",
            ".env"
        };

        foreach (var file in Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file).ToLower();
            if (targets.Contains(name) || name.EndsWith(".env"))
            {
                string relPath = Path.GetRelativePath(projectPath, file);
                filesMap[relPath] = file;
            }
        }

        return filesMap;
    }

    // Extract declared ports in appsettings.json
    public List<int> ExtractAspNetPorts(string appsettingsPath)
    {
        var ports = new List<int>();
        try
        {
            string content = File.ReadAllText(appsettingsPath);
            var obj = JObject.Parse(content);
            
            // Look for Kestrel/Endpoints config
            var urls = obj["Kestrel"]?["Endpoints"]?["Http"]?["Url"]?.ToString() ?? 
                       obj["Kestrel"]?["Endpoints"]?["Https"]?["Url"]?.ToString();
            
            if (urls != null)
            {
                var match = Regex.Match(urls, @":(\d{4,5})\b");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int port))
                {
                    ports.Add(port);
                }
            }

            // Look for generic "Urls" property
            var genericUrls = obj["Urls"]?.ToString();
            if (genericUrls != null)
            {
                foreach (var urlPart in genericUrls.Split(';'))
                {
                    var match = Regex.Match(urlPart, @":(\d{4,5})\b");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int port))
                    {
                        ports.Add(port);
                    }
                }
            }
        }
        catch
        {
            // Fail silently
        }
        return ports;
    }

    // Extract docker ports: list of (HostPort, ContainerPort)
    public List<(int Host, int Container)> ExtractDockerPorts(string composePath)
    {
        var portPairs = new List<(int Host, int Container)>();
        try
        {
            string text = File.ReadAllText(composePath);
            var deserializer = new DeserializerBuilder().Build();
            var compose = deserializer.Deserialize<Dictionary<object, object>>(text);

            if (compose != null && compose.TryGetValue("services", out var servicesObj) && servicesObj is Dictionary<object, object> services)
            {
                foreach (var serviceEntry in services)
                {
                    var serviceDetails = serviceEntry.Value as Dictionary<object, object>;
                    if (serviceDetails != null && serviceDetails.TryGetValue("ports", out var portsListObj) && portsListObj is List<object> ports)
                    {
                        foreach (var portMapping in ports)
                        {
                            string mapStr = portMapping.ToString() ?? "";
                            // Match HostPort:ContainerPort
                            var match = Regex.Match(mapStr, @"^(\d{1,5}):(\d{1,5})$");
                            if (match.Success && 
                                int.TryParse(match.Groups[1].Value, out int host) && 
                                int.TryParse(match.Groups[2].Value, out int container))
                            {
                                portPairs.Add((host, container));
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Fail silently
        }
        return portPairs;
    }

    // Extract npm scripts (start, test, dev, build)
    public Dictionary<string, string> ExtractNpmScripts(string packageJsonPath)
    {
        var scriptsMap = new Dictionary<string, string>();
        try
        {
            string content = File.ReadAllText(packageJsonPath);
            var obj = JObject.Parse(content);
            var scripts = obj["scripts"] as JObject;
            
            if (scripts != null)
            {
                foreach (var prop in scripts.Properties())
                {
                    scriptsMap[prop.Name] = prop.Value.ToString();
                }
            }
        }
        catch
        {
            // Fail silently
        }
        return scriptsMap;
    }

    // Extract env keys (excluding values for security reasons)
    public List<string> ExtractEnvKeys(string envFilePath)
    {
        var keys = new List<string>();
        try
        {
            foreach (var line in File.ReadLines(envFilePath))
            {
                string cleanLine = line.Trim();
                if (string.IsNullOrEmpty(cleanLine) || cleanLine.StartsWith("#")) continue;

                int eqIndex = cleanLine.IndexOf('=');
                if (eqIndex != -1)
                {
                    string key = cleanLine.Substring(0, eqIndex).Trim();
                    keys.Add(key);
                }
            }
        }
        catch
        {
            // Fail silently
        }
        return keys;
    }
}
