using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.projectgenerator.Registry;
using Syncro.Desktop.Services.projectgenerator.Scaffolders;

namespace Syncro.Desktop.Services.projectgenerator.Orchestration
{
    public class GroupOrchestrator
    {
        private readonly StackRegistry _registry;
        private readonly PortAllocator _portAllocator;
        private readonly ProjectWiringService _wiringService;

        public GroupOrchestrator(StackRegistry registry, PortAllocator portAllocator, ProjectWiringService wiringService)
        {
            _registry = registry;
            _portAllocator = portAllocator;
            _wiringService = wiringService;
        }

        public async Task<bool> OrchestrateAsync(
            string projectName, 
            string rootPath, 
            List<string> archetypeIds, 
            bool useNativeCli, 
            Action<string>? onLog,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            // Create root group.json tracking file
            var groupInfo = new 
            {
                Name = projectName,
                CreatedAt = DateTime.UtcNow,
                Stacks = new List<object>()
            };

            int? primaryApiPort = null;
            var completedStacks = new List<ScaffolderContext>();

            // Scaffold Backends/Databases first so we know their ports
            var sortedStacks = archetypeIds
                .Select(id => _registry.GetStackById(id))
                .Where(s => s != null)
                .OrderByDescending(s => s!.Kind == StackKind.Database)
                .ThenByDescending(s => s!.Kind == StackKind.Backend)
                .ToList();

            foreach (var stack in sortedStacks)
            {
                if (cancellationToken.IsCancellationRequested) break;

                int port = _portAllocator.Allocate(stack!.DefaultPort);
                if (stack.Kind == StackKind.Backend && primaryApiPort == null)
                {
                    primaryApiPort = port;
                }

                string subPath = Path.Combine(rootPath, stack.Subfolder);
                var context = new ScaffolderContext
                {
                    ProjectName = $"{projectName}_{stack.Subfolder}",
                    TargetPath = subPath,
                    OnLog = onLog,
                    Archetype = stack,
                    AssignedPort = port
                };

                IStackScaffolder scaffolder;
                if (useNativeCli && !string.IsNullOrEmpty(stack.Cli))
                {
                    scaffolder = new CliScaffolder();
                }
                else
                {
                    scaffolder = new TemplateScaffolder();
                }

                bool success = await scaffolder.ScaffoldAsync(context, cancellationToken);
                if (success)
                {
                    completedStacks.Add(context);
                    await _wiringService.WireProjectAsync(stack, subPath, port, primaryApiPort);
                }
                else
                {
                    onLog?.Invoke($"[Orchestrator] Failed to scaffold {stack.Label}.");
                }
            }

            return completedStacks.Any();
        }
    }
}
