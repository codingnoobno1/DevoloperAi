using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Syncro.Desktop.Services.SyncroCLI;

namespace Syncro.Desktop.Services
{
    public class ProjectModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Type { get; set; } = ""; // Python, Node, Go, etc.
        public string Status { get; set; } = "Pending";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastModified { get; set; } = DateTime.Now;
    }

    public class ProjectService
    {
        private readonly string _cacheFilePath;
        private List<ProjectModel> _projects = new();
        private readonly FlutterService _flutterService;
        private readonly SyncroCLIService _syncroCLIService;

        public ProjectService(FlutterService flutterService, SyncroCLIService syncroCLIService)
        {
            _flutterService = flutterService;
            _syncroCLIService = syncroCLIService;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var syncroDir = Path.Combine(appData, "SyncroDesktop");
            if (!Directory.Exists(syncroDir)) Directory.CreateDirectory(syncroDir);
            _cacheFilePath = Path.Combine(syncroDir, "projects_cache.json");
            
            LoadCache();
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    _projects = System.Text.Json.JsonSerializer.Deserialize<List<ProjectModel>>(json) ?? new();
                }
            }
            catch
            {
                _projects = new();
            }
        }

        private void SaveCache()
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(_projects, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_cacheFilePath, json);
            }
            catch { /* Ignore */ }
        }

        public List<ProjectModel> GetProjects() => _projects.OrderByDescending(p => p.CreatedAt).ToList();

        public async Task<ProjectModel?> CreateProject(string name, string path, string type)
        {
            bool success = false;
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);

                string stack = type.ToLower() switch
                {
                    "mobile app" => "flutter",
                    "python" => "python",
                    "node.js" => "mern",
                    "api service" => "python",
                    "react/next/vite" => "mern",
                    _ => type.ToLower()
                };

                var parentDir = Directory.GetParent(path)?.FullName ?? path;
                success = await _syncroCLIService.CreateProject(stack, name.ToLower().Replace(" ", "_"), parentDir);
            }
            catch (Exception ex)
            {
                _syncroCLIService.Log($"ProjectService: Create Error - {ex.Message}");
                success = false;
            }

            if (!success) return null;

            return AddProjectToCache(name, path, type);
        }

        public ProjectModel? ImportProject(string path)
        {
            if (!Directory.Exists(path)) return null;

            var name = Path.GetFileName(path);
            var type = DetectProjectType(path);

            return AddProjectToCache(name, path, type);
        }

        private ProjectModel AddProjectToCache(string name, string path, string type)
        {
            var project = new ProjectModel
            {
                Name = name,
                Path = path,
                Type = type,
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now
            };

            _projects.Add(project);
            SaveCache();
            return project;
        }

        public string DetectProjectType(string path)
        {
            if (!Directory.Exists(path)) return "Unknown";

            // Priority 1: Mobile (Flutter)
            if (File.Exists(Path.Combine(path, "pubspec.yaml"))) return "Mobile App";

            // Priority 2: Web Frameworks
            if (File.Exists(Path.Combine(path, "package.json")))
            {
                var pkgContent = File.ReadAllText(Path.Combine(path, "package.json"));
                if (pkgContent.Contains("\"next\"")) return "Next.js Project";
                if (pkgContent.Contains("\"vite\"")) return "Vite Project";
                if (pkgContent.Contains("\"react\"")) return "React Project";
                return "Node.js Project";
            }

            // Priority 3: Python
            if (File.Exists(Path.Combine(path, "requirements.txt")) || 
                File.Exists(Path.Combine(path, "setup.py")) || 
                File.Exists(Path.Combine(path, "main.py")) ||
                Directory.GetFiles(path, "*.py").Any())
            {
                return "Python";
            }

            // Priority 4: Go
            if (File.Exists(Path.Combine(path, "go.mod"))) return "Go Project";

            // Priority 5: TypeScript/JavaScript Generic
            if (File.Exists(Path.Combine(path, "tsconfig.json"))) return "TypeScript Project";
            if (Directory.GetFiles(path, "*.js").Any() || Directory.GetFiles(path, "*.ts").Any()) return "JS/TS Project";

            return "Generic Project";
        }

        public void DeleteProject(string id)
        {
            var project = _projects.FirstOrDefault(p => p.Id == id);
            if (project != null)
            {
                _projects.Remove(project);
                SaveCache();
            }
        }
    }
}
