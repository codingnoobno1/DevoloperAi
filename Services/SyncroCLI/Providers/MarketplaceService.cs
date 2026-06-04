using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class MarketplaceService
    {
        private readonly string _manifestPath;
        private readonly string _userScriptsPath;
        private List<MarketplaceScript> _scripts = new();

        public MarketplaceService()
        {
            var baseDir = AppContext.BaseDirectory;
            
            // Try to find the manifest in multiple locations
            string[] possiblePaths = {
                Path.Combine(baseDir, "Marketplace", "manifest.json"),
                Path.Combine(Directory.GetParent(baseDir)?.FullName ?? "", "Marketplace", "manifest.json"),
                Path.Combine(Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.Parent?.FullName ?? "", "Marketplace", "manifest.json")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _manifestPath = path;
                    var marketplaceDir = Path.GetDirectoryName(path)!;
                    _userScriptsPath = Path.Combine(marketplaceDir, "Scripts", "User");
                    break;
                }
            }

            // Fallback
            _manifestPath ??= Path.Combine(baseDir, "Marketplace", "manifest.json");
            _userScriptsPath ??= Path.Combine(baseDir, "Marketplace", "Scripts", "User");

            if (!Directory.Exists(_userScriptsPath))
            {
                Directory.CreateDirectory(_userScriptsPath);
            }
        }

        public async Task LoadMarketplace()
        {
            if (!File.Exists(_manifestPath)) return;

            try
            {
                var json = await File.ReadAllTextAsync(_manifestPath);
                var root = System.Text.Json.JsonSerializer.Deserialize<MarketplaceRoot>(json, new System.Text.Json.JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
                _scripts = root?.Scripts ?? new List<MarketplaceScript>();
            }
            catch
            {
                _scripts = new List<MarketplaceScript>();
            }
        }

        public List<MarketplaceScript> GetAllScripts() => _scripts;

        public MarketplaceScript? GetScript(string id) 
            => _scripts.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        public async Task SaveUserScript(string name, string content, string extension)
        {
            var fileName = $"{name}{extension}";
            var path = Path.Combine(_userScriptsPath, fileName);
            await File.WriteAllTextAsync(path, content);
        }

        public List<string> GetUserScripts()
        {
            if (!Directory.Exists(_userScriptsPath)) return new();
            return Directory.GetFiles(_userScriptsPath).Select(Path.GetFileName).ToList()!;
        }

        public async Task DownloadScript(string id, string content, string platform, string extension)
        {
            var platformDir = Path.Combine(AppContext.BaseDirectory, "Scripts", platform);
            if (!Directory.Exists(platformDir)) Directory.CreateDirectory(platformDir);

            var path = Path.Combine(platformDir, $"{id}{extension}");
            await File.WriteAllTextAsync(path, content);
            
            // Add to manifest if not present
            if (!_scripts.Any(s => s.Id == id))
            {
                _scripts.Add(new MarketplaceScript { Id = id, Name = id, Description = "Downloaded from marketplace" });
                // We should technically save the manifest back to disk here, but for now we update in-memory
            }
        }

        public async Task<string> GetScriptContent(string id, string platform)
        {
            var extension = platform.Equals("Windows", StringComparison.OrdinalIgnoreCase) ? ".ps1" : ".sh";
            var marketplaceDir = Path.GetDirectoryName(_manifestPath)!;
            var path = Path.Combine(marketplaceDir, "Scripts", platform, $"{id}{extension}");

            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }
            
            // Try user scripts
            path = Path.Combine(_userScriptsPath, $"{id}{extension}");
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }

            return $"Error: Script file not found at {path}";
        }
    }

    public class MarketplaceRoot
    {
        public List<MarketplaceScript> Scripts { get; set; } = new();
    }

    public class MarketplaceScript
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<string> Platforms { get; set; } = new();
        public bool RequiresAdmin { get; set; }
    }
}
