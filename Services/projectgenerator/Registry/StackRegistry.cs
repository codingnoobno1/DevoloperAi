using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Syncro.Desktop.Services.projectgenerator.Registry
{
    public class StackRegistry
    {
        private List<StackArchetype> _stacks = new();
        private readonly string _registryPath;

        public StackRegistry()
        {
            // For now, load directly from the json file on disk.
            // If running in MAUI context, we could embed it or use FileSystem.Current.AppDataDirectory.
            // But since this is a library project or run directly, we'll use base directory.
            _registryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Services", "projectgenerator", "Registry", "stacks.json");
            
            // Fallback for development if BaseDirectory is bin/Debug/net9.0
            if (!File.Exists(_registryPath))
            {
                // Traverse up to find the project root
                var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Syncro.Desktop.csproj")))
                {
                    currentDir = currentDir.Parent;
                }
                
                if (currentDir != null)
                {
                    _registryPath = Path.Combine(currentDir.FullName, "Services", "projectgenerator", "Registry", "stacks.json");
                }
            }

            LoadStacks();
        }

        private void LoadStacks()
        {
            if (!File.Exists(_registryPath))
            {
                return; // Will fail gracefully if not found, though it should be shipped
            }

            try
            {
                string json = File.ReadAllText(_registryPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                };
                
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<StackArchetype>>(json, options);
                if (loaded != null)
                {
                    _stacks = loaded;
                }
            }
            catch (Exception ex)
            {
                // In a real app we'd log this via ILogger
                Console.WriteLine($"Failed to load StackRegistry from {_registryPath}: {ex.Message}");
            }
        }

        public IReadOnlyList<StackArchetype> GetAllStacks() => _stacks.AsReadOnly();

        public StackArchetype? GetStackById(string id)
        {
            return _stacks.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        public StackArchetype? GetStackByLabel(string label)
        {
            return _stacks.FirstOrDefault(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        }
    }
}
