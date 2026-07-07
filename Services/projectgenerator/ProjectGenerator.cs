using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI;
using Syncro.Desktop.Services.projectgenerator.Registry;
using Syncro.Desktop.Services.projectgenerator.Scaffolders;
using Syncro.Desktop.Services.projectgenerator.Orchestration;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Syncro.Desktop.Services.projectgenerator
{
    public class ProjectGenerator
    {
        private readonly SyncroCLIService _cliService;
        private readonly StackRegistry _registry;
        private readonly GroupOrchestrator _groupOrchestrator;
        private readonly PortAllocator _portAllocator;

        public ProjectGenerator(
            SyncroCLIService cliService, 
            StackRegistry registry, 
            GroupOrchestrator groupOrchestrator,
            PortAllocator portAllocator)
        {
            _cliService = cliService;
            _registry = registry;
            _groupOrchestrator = groupOrchestrator;
            _portAllocator = portAllocator;
        }

        public async Task<bool> ScaffoldProjectAsync(
            string name, 
            string targetPath, 
            string type, 
            bool useNativeCli, 
            Action<string>? onLog = null,
            bool isSubProject = false,
            string? dbRoot = null)
        {
            try
            {
                var stack = _registry.GetStackById(type) ?? _registry.GetStackByLabel(type);
                if (stack == null)
                {
                    onLog?.Invoke($"[Generator ERROR] Unknown stack type: {type}");
                    return false;
                }

                onLog?.Invoke($"[Generator] Scaffolding individual '{name}' project of type: {stack.Label}...");
                Directory.CreateDirectory(targetPath);

                int port = _portAllocator.Allocate(stack.DefaultPort);
                var context = new ScaffolderContext
                {
                    ProjectName = name,
                    TargetPath = targetPath,
                    OnLog = onLog,
                    Archetype = stack,
                    AssignedPort = port
                };

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                
                IStackScaffolder scaffolder;
                if (useNativeCli && !string.IsNullOrEmpty(stack.Cli))
                {
                    scaffolder = new CliScaffolder();
                }
                else
                {
                    onLog?.Invoke("[Generator] Native CLI skipped or unavailable. Scaffolding from local static templates...");
                    scaffolder = new TemplateScaffolder();
                }

                bool success = await scaffolder.ScaffoldAsync(context, cts.Token);
                
                // If native CLI failed, gracefully fallback to TemplateScaffolder
                if (!success && scaffolder is CliScaffolder)
                {
                    onLog?.Invoke("[Generator] Native CLI failed. Falling back to static templates...");
                    scaffolder = new TemplateScaffolder();
                    success = await scaffolder.ScaffoldAsync(context, cts.Token);
                }

                if (!success)
                {
                    onLog?.Invoke($"[Generator ERROR] Scaffolder failed for {stack.Label}");
                    return false;
                }

                // Initialize standard .syncro_db structure inside project if root
                string actualDbRoot = dbRoot ?? Path.Combine(targetPath, ".syncro_db");
                if (!isSubProject)
                {
                    Directory.CreateDirectory(actualDbRoot);
                    Directory.CreateDirectory(Path.Combine(actualDbRoot, "AST"));
                    Directory.CreateDirectory(Path.Combine(actualDbRoot, "Graphs"));
                    Directory.CreateDirectory(Path.Combine(actualDbRoot, "Vectors"));
                    Directory.CreateDirectory(Path.Combine(actualDbRoot, "Projects"));
                    Directory.CreateDirectory(Path.Combine(actualDbRoot, "Groups"));
                }

                // Save basic projects.json registry in local db
                string projectId = name.ToLower().Replace(" ", "_");
                var projectRecord = new JObject
                {
                    ["project_id"] = projectId,
                    ["name"] = name,
                    ["path"] = targetPath,
                    ["language"] = stack.Language,
                    ["framework"] = stack.Id,
                    ["packageManager"] = stack.PackageManager,
                    ["lastScanned"] = DateTime.UtcNow.ToString("o"),
                    ["astNodeCount"] = 0,
                    ["vectorCount"] = 0
                };
                
                string projJsonPath = Path.Combine(actualDbRoot, "Projects", "projects.json");
                var projList = new List<JObject>();
                if (File.Exists(projJsonPath))
                {
                    try {
                        string content = await File.ReadAllTextAsync(projJsonPath);
                        var parsed = JsonConvert.DeserializeObject<List<JObject>>(content);
                        if (parsed != null) projList = parsed;
                    } catch { }
                }
                
                var existing = projList.FirstOrDefault(p => p["project_id"]?.ToString() == projectId);
                if (existing != null) projList.Remove(existing);
                projList.Add(projectRecord);

                await File.WriteAllTextAsync(
                    projJsonPath, 
                    JsonConvert.SerializeObject(projList, Formatting.Indented)
                );

                onLog?.Invoke($"[Generator SUCCESS] Project '{name}' successfully scaffolded at: {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"[Generator ERROR] Scaffolder failed: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> ScaffoldGroupProjectAsync(
            string groupName, 
            string targetPath, 
            List<string> selectedArchetypes, 
            bool useNativeCli, 
            Action<string>? onLog = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                bool success = await _groupOrchestrator.OrchestrateAsync(
                    groupName, 
                    targetPath, 
                    selectedArchetypes, 
                    useNativeCli, 
                    onLog, 
                    cts.Token
                );

                if (!success)
                {
                    return false;
                }

                // Setup the group registry
                string dbRootPath = Path.Combine(targetPath, ".syncro_db");
                Directory.CreateDirectory(dbRootPath);
                Directory.CreateDirectory(Path.Combine(dbRootPath, "Groups"));

                string groupId = $"grp-{groupName.ToLower().Replace(" ", "_")}-{Guid.NewGuid().ToString().Substring(0, 5)}";
                
                var projectsConfig = new List<JObject>();
                foreach (var archId in selectedArchetypes)
                {
                    var stack = _registry.GetStackById(archId);
                    if (stack != null)
                    {
                        projectsConfig.Add(new JObject
                        {
                            ["project_id"] = $"{groupName}_{stack.Subfolder}".ToLower(),
                            ["role"] = stack.Kind.ToString().ToLower(),
                            ["framework"] = stack.Id,
                            ["path"] = Path.Combine(targetPath, stack.Subfolder)
                        });
                    }
                }

                var groupRecord = new JObject
                {
                    ["group_id"] = groupId,
                    ["name"] = groupName,
                    ["projects"] = new JArray(projectsConfig),
                    ["created"] = DateTime.UtcNow.ToString("o")
                };

                string groupJsonPath = Path.Combine(dbRootPath, "Groups", "groups.json");
                var groupList = new List<JObject>();
                if (File.Exists(groupJsonPath))
                {
                    try {
                        string content = await File.ReadAllTextAsync(groupJsonPath);
                        var parsed = JsonConvert.DeserializeObject<List<JObject>>(content);
                        if (parsed != null) groupList = parsed;
                    } catch { }
                }
                
                var existingGroup = groupList.FirstOrDefault(g => g["group_id"]?.ToString() == groupId);
                if (existingGroup != null) groupList.Remove(existingGroup);
                groupList.Add(groupRecord);

                await File.WriteAllTextAsync(
                    groupJsonPath,
                    JsonConvert.SerializeObject(groupList, Formatting.Indented)
                );

                onLog?.Invoke($"\n[Generator SUCCESS] Group stack '{groupName}' configured with {selectedArchetypes.Count} services.");
                return true;
            }
            catch (Exception ex)
            {
                onLog?.Invoke($"[Generator ERROR] Group stack creation failed: {ex.Message}");
                return false;
            }
        }
    }
}
