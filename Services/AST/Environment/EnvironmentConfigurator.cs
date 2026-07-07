using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.AST.Env;

public class EnvironmentConfigurator
{
    public async Task<List<string>> CheckDatabaseRequirementsAsync(string projectPath, string framework)
    {
        var requiredDbs = new List<string>();
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
        {
            return requiredDbs;
        }

        try
        {
            // Search files for connection strings or database driver imports
            var files = Directory.GetFiles(projectPath, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".js") || f.EndsWith(".ts") || f.EndsWith(".py") || f.EndsWith(".cs") || f.EndsWith(".csproj") || f.EndsWith("package.json") || f.EndsWith("requirements.txt"))
                .Take(250) // Limit search to prevent scanning thousands of files
                .ToList();

            bool pgFound = false;
            bool mongoFound = false;
            bool redisFound = false;
            bool mysqlFound = false;
            bool sqliteFound = false;
            bool sqlserverFound = false;

            foreach (var file in files)
            {
                if (file.Contains("node_modules") || file.Contains("venv") || file.Contains(".git") || file.Contains("bin") || file.Contains("obj"))
                {
                    continue;
                }

                string content = await File.ReadAllTextAsync(file);

                // PostgreSQL drivers: pg, psycopg2, asyncpg, Npgsql
                if (content.Contains("pg") || content.Contains("psycopg2") || content.Contains("asyncpg") || content.Contains("npgsql") || content.Contains("postgresql", StringComparison.OrdinalIgnoreCase))
                {
                    pgFound = true;
                }

                // MongoDB drivers: mongoose, mongodb, pymongo, motor
                if (content.Contains("mongoose") || content.Contains("mongodb") || content.Contains("pymongo") || content.Contains("motor"))
                {
                    mongoFound = true;
                }

                // Redis drivers: redis, redis-py, StackExchange.Redis
                if (content.Contains("redis"))
                {
                    redisFound = true;
                }

                // MySQL drivers: mysql, mysql2, pymysql, Pomelo
                if (content.Contains("mysql") || content.Contains("pymysql") || content.Contains("pomelo"))
                {
                    mysqlFound = true;
                }

                // SQLite drivers: sqlite, sqlite3, Microsoft.Data.Sqlite
                if (content.Contains("sqlite") || content.Contains("microsoft.data.sqlite"))
                {
                    sqliteFound = true;
                }

                // SQL Server drivers: mssql, sqlserver, sqlclient, Microsoft.EntityFrameworkCore.SqlServer
                if (content.Contains("sqlserver") || content.Contains("sqlclient") || content.Contains("entityframeworkcore.sqlserver", StringComparison.OrdinalIgnoreCase))
                {
                    sqlserverFound = true;
                }
            }

            if (pgFound) requiredDbs.Add("PostgreSQL");
            if (mongoFound) requiredDbs.Add("MongoDB");
            if (redisFound) requiredDbs.Add("Redis");
            if (mysqlFound) requiredDbs.Add("MySQL");
            if (sqliteFound) requiredDbs.Add("SQLite");
            if (sqlserverFound) requiredDbs.Add("SQL Server");
        }
        catch
        {
            // Fail silently
        }

        return requiredDbs;
    }

    public class SubProject
    {
        public string Path { get; set; } = "";
        public string Framework { get; set; } = "";
    }

    public async Task<List<SubProject>> DetectProjectsAsync(string projectPath, Action<string>? onProgressLog)
    {
        var projects = new List<SubProject>();
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath)) return projects;

        // Try LLM dynamic detection first
        bool useLlm = false;
        using (var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(1.5) })
        {
            try
            {
                var resp = await http.GetAsync("http://localhost:3020/");
                if (resp.IsSuccessStatusCode)
                {
                    useLlm = true;
                }
            }
            catch {}
        }

        if (useLlm)
        {
            onProgressLog?.Invoke("Gemini LLM Express server detected on port 3020. Running AI project layout analyzer...");
            try
            {
                var dirs = Directory.GetDirectories(projectPath).Select(System.IO.Path.GetFileName).ToList();
                var files = Directory.GetFiles(projectPath).Select(System.IO.Path.GetFileName).ToList();
                
                string structureInfo = $"Directories:\n{string.Join("\n", dirs)}\n\nFiles:\n{string.Join("\n", files)}";
                
                var subConfigs = new List<string>();
                foreach (var dir in dirs)
                {
                    if (dir == "node_modules" || dir == ".git" || dir == "venv") continue;
                    string subPath = System.IO.Path.Combine(projectPath, dir!);
                    if (Directory.Exists(subPath))
                    {
                        var subFiles = Directory.GetFiles(subPath).Select(System.IO.Path.GetFileName).ToList();
                        subConfigs.Add($"Subfolder '{dir}': {string.Join(", ", subFiles.Where(f => f != null && (f.EndsWith(".json") || f.EndsWith(".txt") || f.EndsWith(".toml") || f.EndsWith(".mod") || f.EndsWith(".csproj"))))}");
                    }
                }
                structureInfo += "\n\nSubfolder Config Files:\n" + string.Join("\n", subConfigs);

                string prompt = $"Given the following directory structure of a cloned repository, identify all subprojects/subfolders that represent independent services or applications (e.g., frontend, backend, server, landing page, api). Return a JSON array of objects, where each object has 'path' (relative path from root) and 'framework' (one of: 'Next.js', 'NestJS', 'Express/Node', 'Angular', 'React Native', 'SvelteKit', 'Nuxt.js', 'Electron', 'Vite Project', 'Node.js Generic', 'FastAPI', 'Flask', 'Django', 'ASP.NET Core', 'Blazor', 'Spring Boot', 'Go', 'Rust'). Do not include any markdown backticks, conversational text, or code block formatting. Return ONLY a raw JSON array. Example: [ {{\"path\": \"frontend\", \"framework\": \"Next.js\"}} ]\n\nStructure:\n{structureInfo}";

                using (var http = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) })
                {
                    var payload = new { prompt = prompt };
                    var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");
                    var resp = await http.PostAsync("http://localhost:3020/gemini", content);
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync();
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(body);
                        string? rawResult = obj["result"]?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>();
                        if (!string.IsNullOrEmpty(rawResult))
                        {
                            string jsonText = rawResult.Trim();
                            if (jsonText.StartsWith("```"))
                            {
                                int startIdx = jsonText.IndexOf('\n');
                                int endIdx = jsonText.LastIndexOf("```");
                                if (startIdx >= 0 && endIdx > startIdx)
                                {
                                    jsonText = jsonText.Substring(startIdx + 1, endIdx - startIdx - 1).Trim();
                                }
                            }
                            
                            var detected = Newtonsoft.Json.JsonConvert.DeserializeObject<List<SubProject>>(jsonText);
                            if (detected != null && detected.Count > 0)
                            {
                                foreach (var p in detected)
                                {
                                    if (p.Path == "." || p.Path == "") p.Path = "";
                                    projects.Add(p);
                                }
                                onProgressLog?.Invoke($"[AI] Successfully mapped {projects.Count} subprojects using Gemini Flash.");
                                return projects;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onProgressLog?.Invoke($"[AI Error] AI analysis failed: {ex.Message}. Falling back to static detection.");
            }
        }
        else
        {
            onProgressLog?.Invoke("Gemini LLM Express server on port 3020 is offline/unfetchable. Running static heuristics parser...");
        }

        // Static heuristics fallback
        try
        {
            var dirs = Directory.GetDirectories(projectPath);
            foreach (var dir in dirs)
            {
                string dirName = System.IO.Path.GetFileName(dir);
                if (dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("venv", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("exports", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string detectedFramework = DetectFrameworkInFolder(dir);
                if (detectedFramework != "Unknown" && detectedFramework != "Generic")
                {
                    projects.Add(new SubProject { Path = dirName, Framework = detectedFramework });
                }
            }
        }
        catch {}

        if (projects.Count == 0)
        {
            string rootFramework = DetectFrameworkInFolder(projectPath);
            projects.Add(new SubProject { Path = "", Framework = rootFramework });
        }

        onProgressLog?.Invoke($"[Static] Identified {projects.Count} project directories to configure.");
        return projects;
    }

    private string DetectFrameworkInFolder(string folderPath)
    {
        if (File.Exists(System.IO.Path.Combine(folderPath, "package.json")))
        {
            try
            {
                string text = File.ReadAllText(System.IO.Path.Combine(folderPath, "package.json"));
                if (text.Contains("\"next\"")) return "Next.js";
                if (text.Contains("\"@nestjs/core\"")) return "NestJS";
                if (text.Contains("\"express\"")) return "Express/Node";
                if (text.Contains("\"@angular/core\"")) return "Angular";
                if (text.Contains("\"react-native\"")) return "React Native";
                if (text.Contains("\"@sveltejs/kit\"")) return "SvelteKit";
                if (text.Contains("\"nuxt\"")) return "Nuxt.js";
                if (text.Contains("\"electron\"")) return "Electron";
                if (text.Contains("\"vite\"")) return "Vite Project";
                return "Node.js Generic";
            }
            catch {}
            return "Node.js Generic";
        }

        if (File.Exists(System.IO.Path.Combine(folderPath, "requirements.txt")) ||
            File.Exists(System.IO.Path.Combine(folderPath, "pyproject.toml")))
        {
            string reqFile = System.IO.Path.Combine(folderPath, "requirements.txt");
            if (!File.Exists(reqFile)) reqFile = System.IO.Path.Combine(folderPath, "pyproject.toml");
            try
            {
                string text = File.ReadAllText(reqFile);
                if (text.Contains("fastapi", StringComparison.OrdinalIgnoreCase)) return "FastAPI";
                if (text.Contains("flask", StringComparison.OrdinalIgnoreCase)) return "Flask";
                if (text.Contains("django", StringComparison.OrdinalIgnoreCase)) return "Django";
            }
            catch {}
            return "Python Generic";
        }

        if (Directory.GetFiles(folderPath, "*.csproj").Any())
        {
            try
            {
                string csproj = Directory.GetFiles(folderPath, "*.csproj")[0];
                string text = File.ReadAllText(csproj);
                if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) return "ASP.NET Core";
                if (text.Contains("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase)) return "Blazor";
            }
            catch {}
            return "ASP.NET Core";
        }

        if (File.Exists(System.IO.Path.Combine(folderPath, "go.mod"))) return "Go";
        if (File.Exists(System.IO.Path.Combine(folderPath, "Cargo.toml"))) return "Rust";
        if (File.Exists(System.IO.Path.Combine(folderPath, "pubspec.yaml"))) return "Flutter";

        return "Unknown";
    }

    public async Task AutoInstallEnvironmentAsync(string projectPath, string framework, Action<string>? onProgressLog = null)
    {
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Target project directory not found: {projectPath}");
        }

        onProgressLog?.Invoke("Scanning workspace directory structure...");
        var subProjects = await DetectProjectsAsync(projectPath, onProgressLog);

        foreach (var sub in subProjects)
        {
            string subPath = string.IsNullOrEmpty(sub.Path) ? projectPath : System.IO.Path.Combine(projectPath, sub.Path);
            onProgressLog?.Invoke($"\n[Environment Setup] Scanning dependencies in: '{sub.Path}' (Framework: {sub.Framework})");
            await InstallEnvironmentForPathAsync(subPath, sub.Framework, onProgressLog);
        }
    }

    private async Task InstallEnvironmentForPathAsync(string folderPath, string framework, Action<string>? onProgressLog)
    {
        // 1. Python Venv + Pip Installer
        if (framework.Contains("Python") || framework.Equals("FastAPI") || framework.Equals("Flask") || framework.Equals("Django") || framework.Contains("Machine Learning"))
        {
            string reqPath = System.IO.Path.Combine(folderPath, "requirements.txt");
            if (File.Exists(reqPath))
            {
                string venvPath = System.IO.Path.Combine(folderPath, "venv");
                if (!Directory.Exists(venvPath))
                {
                    onProgressLog?.Invoke("Creating virtual environment: venv...");
                    await RunCommandAsync("python", $"-m venv venv", folderPath, onProgressLog);
                }

                onProgressLog?.Invoke("Installing requirements via pip inside venv...");
                string pipPath = System.IO.Path.Combine(venvPath, "Scripts", "pip.exe");
                if (!File.Exists(pipPath))
                {
                    pipPath = System.IO.Path.Combine(venvPath, "bin", "pip");
                }

                if (File.Exists(pipPath))
                {
                    await RunCommandAsync(pipPath, "install -r requirements.txt", folderPath, onProgressLog);
                }
                else
                {
                    await RunCommandAsync("pip", "install -r requirements.txt", folderPath, onProgressLog);
                }
            }
        }

        // 2. Node Modules NPM Installer
        if (framework.Contains("Node") || framework.Equals("Next.js") || framework.Equals("NestJS") || framework.Equals("Express/Node") || framework.Contains("Vite") || framework.Contains("Svelte") || framework.Contains("Nuxt"))
        {
            string pkgPath = System.IO.Path.Combine(folderPath, "package.json");
            if (File.Exists(pkgPath))
            {
                string nodeModulesPath = System.IO.Path.Combine(folderPath, "node_modules");
                if (!Directory.Exists(nodeModulesPath))
                {
                    onProgressLog?.Invoke("Installing node modules dependencies...");
                    await RunCommandAsync("npm", "install", folderPath, onProgressLog);
                }
                else
                {
                    onProgressLog?.Invoke("node_modules directory already exists. Skipping npm install.");
                }
            }
        }
    }

    public async Task InjectDatabaseConnectionStringAsync(string projectPath, string databaseType, string connectionString, Action<string>? onProgressLog = null)
    {
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Target project directory not found: {projectPath}");
        }

        string envFilePath = System.IO.Path.Combine(projectPath, ".env");
        onProgressLog?.Invoke($"Injecting {databaseType} connection string into: {envFilePath}");

        string envKey = databaseType.ToUpper() switch
        {
            "POSTGRESQL" => "DATABASE_URL",
            "MONGODB" => "MONGO_URI",
            "REDIS" => "REDIS_URL",
            "MYSQL" => "DATABASE_URL",
            "SQL SERVER" => "SQLSERVER_CONNECTION_STRING",
            _ => "DATABASE_URL"
        };

        try
        {
            string lineToAdd = $"{envKey}=\"{connectionString}\"";
            List<string> envLines = new List<string>();

            if (File.Exists(envFilePath))
            {
                envLines = (await File.ReadAllLinesAsync(envFilePath)).ToList();
                int existingIdx = envLines.FindIndex(l => l.StartsWith(envKey + "=", StringComparison.OrdinalIgnoreCase));
                if (existingIdx >= 0)
                {
                    envLines[existingIdx] = lineToAdd;
                }
                else
                {
                    envLines.Add(lineToAdd);
                }
            }
            else
            {
                envLines.Add(lineToAdd);
            }

            await File.WriteAllLinesAsync(envFilePath, envLines);
            onProgressLog?.Invoke($"[SUCCESS] Connected! {envKey} successfully mapped in local environment config.");
        }
        catch (Exception ex)
        {
            onProgressLog?.Invoke($"[ERROR] Failed to inject database override: {ex.Message}");
            throw;
        }
    }

    public async Task AutoRunDevServerAsync(string projectPath, string framework, Action<string>? onProgressLog = null)
    {
        if (string.IsNullOrEmpty(projectPath) || !Directory.Exists(projectPath))
        {
            onProgressLog?.Invoke("[ERROR] Cannot start dev server: Project path not found.");
            return;
        }

        onProgressLog?.Invoke("Scanning project configuration for runnable services...");
        var subProjects = await DetectProjectsAsync(projectPath, onProgressLog);

        foreach (var sub in subProjects)
        {
            string subPath = string.IsNullOrEmpty(sub.Path) ? projectPath : System.IO.Path.Combine(projectPath, sub.Path);
            onProgressLog?.Invoke($"\n[Server Startup] Launching dev server for: '{sub.Path}' (Framework: {sub.Framework})");
            await RunDevServerForPathAsync(subPath, sub.Framework, onProgressLog);
        }
    }

    private Task RunDevServerForPathAsync(string folderPath, string framework, Action<string>? onProgressLog)
    {
        string devCommand = "";

        // 1. Detect command
        string pkgPath = System.IO.Path.Combine(folderPath, "package.json");
        if (File.Exists(pkgPath))
        {
            devCommand = "npm run dev";
            try
            {
                string content = File.ReadAllText(pkgPath);
                if (content.Contains("\"start:dev\"")) devCommand = "npm run start:dev";
                else if (content.Contains("\"dev\"")) devCommand = "npm run dev";
                else if (content.Contains("\"start\"")) devCommand = "npm start";
            }
            catch {}
        }
        else if (framework.Contains("Python") || framework.Equals("FastAPI") || framework.Equals("Flask") || framework.Equals("Django"))
        {
            string venvPath = System.IO.Path.Combine(folderPath, "venv");
            string pythonExec = "python";
            string uvicornExec = "uvicorn";
            if (Directory.Exists(venvPath))
            {
                string venvPython = System.IO.Path.Combine(venvPath, "Scripts", "python.exe");
                if (File.Exists(venvPython)) pythonExec = $"\"{venvPython}\"";
                
                string venvUvicorn = System.IO.Path.Combine(venvPath, "Scripts", "uvicorn.exe");
                if (File.Exists(venvUvicorn)) uvicornExec = $"\"{venvUvicorn}\"";
            }

            if (framework.Equals("FastAPI"))
            {
                devCommand = $"{uvicornExec} main:app --reload";
            }
            else if (framework.Equals("Django"))
            {
                devCommand = $"{pythonExec} manage.py runserver";
            }
            else if (framework.Equals("Flask"))
            {
                devCommand = $"{pythonExec} -m flask run";
            }
            else
            {
                if (File.Exists(System.IO.Path.Combine(folderPath, "main.py")))
                    devCommand = $"{pythonExec} main.py";
                else if (File.Exists(System.IO.Path.Combine(folderPath, "app.py")))
                    devCommand = $"{pythonExec} app.py";
            }
        }
        else if (framework.Contains("ASP.NET Core") || framework.Contains("Blazor") || framework.Contains(".NET"))
        {
            devCommand = "dotnet run";
        }
        else if (framework.Contains("Spring Boot"))
        {
            devCommand = File.Exists(System.IO.Path.Combine(folderPath, "mvnw.cmd")) ? "mvnw.cmd spring-boot:run" : "mvn spring-boot:run";
        }
        else if (framework.Contains("Go"))
        {
            devCommand = "go run .";
        }
        else if (framework.Contains("Rust"))
        {
            devCommand = "cargo run";
        }

        if (string.IsNullOrEmpty(devCommand))
        {
            onProgressLog?.Invoke("[INFO] No known dev server configuration found for this folder.");
            return Task.CompletedTask;
        }

        onProgressLog?.Invoke($"[INFO] Auto-launching dev server: {devCommand}");

        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/K cd /d \"{folderPath}\" && {devCommand}",
                    WorkingDirectory = folderPath,
                    UseShellExecute = true, // Open a new interactive terminal window
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(psi);
                onProgressLog?.Invoke("[SUCCESS] Launched dev server in a new terminal window.");
            }
            else
            {
                onProgressLog?.Invoke("[WARNING] Native terminal window launch is only supported on Windows.");
            }
        }
        catch (Exception ex)
        {
            onProgressLog?.Invoke($"[ERROR] Failed to start dev server: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private async Task RunCommandAsync(string cmd, string args, string workingDir, Action<string>? onProgressLog = null)
    {
        string executable = cmd;
        string cmdArgs = args;
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            if (cmd == "npm" || cmd == "pip" || cmd == "python" || cmd == "npx" || cmd == "flutter" || cmd == "dotnet" || cmd == "git")
            {
                executable = "cmd.exe";
                cmdArgs = $"/C \"{cmd}\" {args}";
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = cmdArgs,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null) onProgressLog?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null) onProgressLog?.Invoke(e.Data);
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                onProgressLog?.Invoke($"Warning: command '{cmd} {args}' exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            onProgressLog?.Invoke($"Warning: Execution error running '{cmd} {args}': {ex.Message}");
        }
    }
}
