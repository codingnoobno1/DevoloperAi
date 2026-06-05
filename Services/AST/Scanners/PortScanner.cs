using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using YamlDotNet.Serialization;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Scanners;

public class PortScanner
{
    private static readonly int[] DevPorts = new[]
    {
        3000, 3001, 3002, 4000, 5000, 5001, 5173,  // Web frontends / API backends
        8000, 8080, 8443, 8888,                       // Django, Flask, Node, Java, Tomcat
        9000, 9229,                                    // PHP, Node debug
        1433, 3306, 5432, 6379, 27017,               // DBs (SQL Server, MySQL, Postgres, Redis, Mongo)
        7071, 7272                                     // Azure Functions, Dapr
    };

    // Scan localhost ports asynchronously
    public async Task<List<PortInfo>> ScanAsync(string host = "localhost", int timeoutMs = 250)
    {
        var openPorts = new List<PortInfo>();
        
        var tasks = new List<Task<PortInfo?>>();
        foreach (int port in DevPorts)
        {
            tasks.Add(CheckPortAsync(host, port, timeoutMs));
        }

        var results = await Task.WhenAll(tasks);
        foreach (var info in results)
        {
            if (info != null) openPorts.Add(info);
        }

        return openPorts;
    }

    private async Task<PortInfo?> CheckPortAsync(string host, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            
            // Wait for connect or timeout
            if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) == connectTask)
            {
                // Completed connectTask
                await connectTask; // Throws if failed
                return new PortInfo
                {
                    Port = port,
                    State = "Open",
                    Service = GuessService(port),
                    Type = "Scanned"
                };
            }
        }
        catch
        {
            // Port is closed or filtered
        }
        return null;
    }

    // Extract ports declared in config files
    public async Task<List<PortInfo>> ExtractDeclaredPortsAsync(string projectPath)
    {
        var declaredPorts = new List<PortInfo>();
        if (!Directory.Exists(projectPath)) return declaredPorts;

        try
        {
            // 1. Scan appsettings.json for Kestrel URL declarations
            var appsettingsFiles = Directory.GetFiles(projectPath, "appsettings.json", SearchOption.AllDirectories);
            foreach (var file in appsettingsFiles)
            {
                string text = await File.ReadAllTextAsync(file);
                // Regex matches port numbers in http://localhost:5000 formats
                var matches = Regex.Matches(text, @":(\d{4,5})\b");
                foreach (Match m in matches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int port))
                    {
                        AddDeclaredPort(declaredPorts, port, "Kestrel (C#)");
                    }
                }
            }

            // 2. Scan package.json scripts for port arguments (e.g. "--port 3000")
            var packageJsonFiles = Directory.GetFiles(projectPath, "package.json", SearchOption.AllDirectories);
            foreach (var file in packageJsonFiles)
            {
                string text = await File.ReadAllTextAsync(file);
                var matches = Regex.Matches(text, @"--port\s+(\d{4,5})\b");
                foreach (Match m in matches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int port))
                    {
                        AddDeclaredPort(declaredPorts, port, "npm script");
                    }
                }
            }

            // 3. Scan docker-compose.yaml for port mappings e.g. "8080:80"
            var dockerComposeFiles = Directory.GetFiles(projectPath, "docker-compose.*", SearchOption.AllDirectories);
            foreach (var file in dockerComposeFiles)
            {
                string text = await File.ReadAllTextAsync(file);
                var matches = Regex.Matches(text, @"['""]?(\d{4,5}):\d+['""]?");
                foreach (Match m in matches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int port))
                    {
                        AddDeclaredPort(declaredPorts, port, "Docker Compose host port");
                    }
                }
            }
        }
        catch
        {
            // Suppress errors during port file extraction
        }

        return declaredPorts;
    }

    private void AddDeclaredPort(List<PortInfo> list, int port, string source)
    {
        if (!list.Exists(p => p.Port == port))
        {
            list.Add(new PortInfo
            {
                Port = port,
                State = "Declared",
                Service = $"{GuessService(port)} ({source})",
                Type = "Declared"
            });
        }
    }

    private string GuessService(int port)
    {
        return port switch
        {
            3000 => "React/Next.js",
            3001 => "Node API / Next.js backend",
            3002 => "Web Service",
            4000 => "Express Server / GraphQL",
            5000 => "ASP.NET Core / Flask",
            5001 => "ASP.NET Core HTTPS",
            5173 => "Vite (React/Vue)",
            8000 => "FastAPI / Django",
            8080 => "Java Spring Boot / Jenkins",
            8443 => "HTTPS Server",
            8888 => "Jupyter Notebook",
            9000 => "PHP-FPM / SonarQube",
            9229 => "Node.js Debugger",
            1433 => "MS SQL Server",
            3306 => "MySQL DB",
            5432 => "PostgreSQL DB",
            6379 => "Redis Cache",
            27017 => "MongoDB",
            7071 => "Azure Functions",
            7272 => "Dapr Placement Service",
            _ => "Web/Local Service"
        };
    }
}
