using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Connector.ProjectDomain
{
    /// <summary>
    /// Pure file-system domain detection — no LLM, no network. Reads well-known manifest files
    /// (package.json, pubspec.yaml, requirements.txt, .csproj, docker-compose.yml, pom.xml, etc.)
    /// to classify a folder as Frontend / Backend / Database / Mobile / AIService and infer a
    /// default run command + port. Also reads git branch + remote for a workspace node.
    /// </summary>
    public static class DomainDetector
    {
        // ── Public entry points ──────────────────────────────────────────────

        /// <summary>Detect domain, stack, run command and port for <paramref name="node"/> in-place.</summary>
        public static void Detect(ConnectorWorkspaceNode node)
        {
            var (domain, stack, runCmd, port) = InferDomain(node.Path);
            node.Domain = domain;
            node.Stack = stack;
            node.Wave = GetDefaultWave(domain);
            if (string.IsNullOrWhiteSpace(node.RunCommand)) node.RunCommand = runCmd;
            if (node.Port == 0) node.Port = port;
        }

        /// <summary>Read git branch, remote, and dirty status for <paramref name="node"/> in-place.</summary>
        public static async Task ReadGitInfoAsync(ConnectorWorkspaceNode node)
        {
            if (!Directory.Exists(node.Path)) return;
            try
            {
                var branch = (await RunGit(node.Path, "branch --show-current")).Trim();
                if (string.IsNullOrWhiteSpace(branch))
                    branch = (await RunGit(node.Path, "rev-parse --abbrev-ref HEAD")).Trim();

                var statusOut = (await RunGit(node.Path, "status --short")).Trim();
                var remote    = (await RunGit(node.Path, "remote get-url origin")).Trim();

                node.GitBranch  = branch;
                node.IsGitDirty = !string.IsNullOrEmpty(statusOut);
                node.GitRemote  = remote;
            }
            catch { /* not a git repo or git not installed */ }
        }

        // ── Domain / stack heuristics ────────────────────────────────────────

        private static (DomainKind domain, string stack, string runCmd, int port) InferDomain(string root)
        {
            if (!Directory.Exists(root))
                return (DomainKind.Unknown, "Unknown", "", 0);

            // Flutter
            if (File.Exists(Path.Combine(root, "pubspec.yaml")))
                return (DomainKind.Mobile, "Flutter", "flutter run", 0);

            // package.json — check dependencies first
            var pkg = Path.Combine(root, "package.json");
            if (File.Exists(pkg))
            {
                var t = SafeRead(pkg).ToLowerInvariant();
                if (t.Contains("\"next\""))            return (DomainKind.Frontend,  "Next.js",       "npm run dev",             3000);
                if (t.Contains("\"react-native\""))    return (DomainKind.Mobile,    "React Native",  "npx react-native start",  8081);
                if (t.Contains("\"@angular/core\""))   return (DomainKind.Frontend,  "Angular",       "ng serve",                4200);
                if (t.Contains("\"svelte\""))          return (DomainKind.Frontend,  "Svelte",        "npm run dev",             5173);
                if (t.Contains("\"vue\""))             return (DomainKind.Frontend,  "Vue.js",        "npm run dev",             5173);
                if (t.Contains("\"react\""))           return (DomainKind.Frontend,  "React",         "npm start",               3000);
                if (t.Contains("\"@nestjs/core\""))    return (DomainKind.Backend,   "NestJS",        "npm run start:dev",       3001);
                if (t.Contains("\"express\""))         return (DomainKind.Backend,   "Express",       "npm start",               3000);
                if (t.Contains("\"fastify\""))         return (DomainKind.Backend,   "Fastify",       "npm start",               3000);
                // unknown node project — assume frontend SPA if no obvious server dep
                return (DomainKind.Frontend, "Node.js", "npm start", 3000);
            }

            // Python
            var pyFile = FindAny(root, "requirements.txt", "pyproject.toml", "setup.py");
            if (pyFile != null)
            {
                var t = SafeRead(pyFile).ToLowerInvariant();
                if (t.Contains("torch") || t.Contains("tensorflow") || t.Contains("openai") || t.Contains("transformers") || t.Contains("langchain"))
                    return (DomainKind.AIService, "Python AI", "python main.py", 8000);
                if (t.Contains("fastapi"))  return (DomainKind.Backend, "FastAPI",  "uvicorn main:app --reload", 8000);
                if (t.Contains("django"))   return (DomainKind.Backend, "Django",   "python manage.py runserver", 8000);
                if (t.Contains("flask"))    return (DomainKind.Backend, "Flask",    "flask run", 5000);
                return (DomainKind.Backend, "Python", "python main.py", 8000);
            }

            // .NET — search recursively for .csproj
            var csproj = FindExtRecursive(root, ".csproj").FirstOrDefault();
            if (csproj != null)
            {
                var t = SafeRead(csproj).ToLowerInvariant();
                if (t.Contains("maui") || t.Contains("xamarin"))
                    return (DomainKind.Mobile, "MAUI", "dotnet run", 0);
                if (t.Contains("sdk.web") || t.Contains("aspnetcore") || t.Contains("microsoft.net.sdk.web"))
                    return (DomainKind.Backend, "ASP.NET Core", "dotnet run", 5001);
                return (DomainKind.Backend, "ASP.NET", "dotnet run", 5001);
            }

            // Java (Gradle / Maven)
            var javaBuild = FindAny(root, "build.gradle", "build.gradle.kts", "pom.xml");
            if (javaBuild != null)
            {
                var t = SafeRead(javaBuild).ToLowerInvariant();
                if (t.Contains("spring"))
                    return (DomainKind.Backend, "Spring Boot", "./mvnw spring-boot:run", 8080);
                return (DomainKind.Backend, "Java", "./gradlew run", 8080);
            }

            // docker-compose — treat as DB infra if it only has DB images
            var dc = FindAny(root, "docker-compose.yml", "docker-compose.yaml");
            if (dc != null)
            {
                var t = SafeRead(dc).ToLowerInvariant();
                bool hasDb = t.Contains("postgres") || t.Contains("mysql") || t.Contains("mongodb") || t.Contains("redis");
                if (hasDb) return (DomainKind.Database, "Docker DB", "docker-compose up -d", 5432);
                return (DomainKind.Backend, "Docker", "docker-compose up", 0);
            }

            return (DomainKind.Unknown, "Unknown", "", 0);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        public static int GetDefaultWave(DomainKind d) => d switch
        {
            DomainKind.Database                      => 0,
            DomainKind.Backend or DomainKind.AIService => 1,
            DomainKind.Frontend or DomainKind.Mobile  => 2,
            _                                        => 1
        };

        public static string GetColor(DomainKind d) => d switch
        {
            DomainKind.Frontend  => "#3b82f6",
            DomainKind.Backend   => "#10b981",
            DomainKind.Database  => "#f59e0b",
            DomainKind.Mobile    => "#ec4899",
            DomainKind.AIService => "#8b5cf6",
            _                    => "#64748b"
        };

        // ── File helpers ─────────────────────────────────────────────────────

        private static string? FindAny(string root, params string[] names)
        {
            foreach (var n in names)
            {
                var p = Path.Combine(root, n);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private static string[] FindExtRecursive(string root, string ext)
        {
            try
            {
                return Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("node_modules") &&
                                !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) &&
                                !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                    .Take(5).ToArray();
            }
            catch { return Array.Empty<string>(); }
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return ""; }
        }

        // ── Git ──────────────────────────────────────────────────────────────

        private static async Task<string> RunGit(string workDir, string args)
        {
            try
            {
                var psi = new ProcessStartInfo("git", args)
                {
                    WorkingDirectory    = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute    = false,
                    CreateNoWindow     = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return "";
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return output;
            }
            catch { return ""; }
        }
    }
}
