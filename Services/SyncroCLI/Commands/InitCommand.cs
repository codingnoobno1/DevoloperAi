using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Core;
using Syncro.Desktop.Services.SyncroCLI.Providers;
using DeveloperAI.BusinessLogic;

namespace Syncro.Desktop.Services.SyncroCLI.Commands
{
    public class InitCommand : ICliCommand
    {
        private readonly Dictionary<string, IProjectProvider> _providers;
        private readonly AIClient _aiClient;
        private readonly Action<string> _logger;

        public InitCommand(IEnumerable<IProjectProvider> providers, AIClient aiClient, Action<string> logger)
        {
            _providers = providers.ToDictionary(p => p.StackName.ToLower(), p => p);
            _aiClient = aiClient;
            _logger = logger;
        }

        public string Name => "init";
        public string Description => "Initialize a new project. Usage: syncro init <stack|prompt> [name]";

        public async Task Execute(CommandContext context)
        {
            if (context.Args.Count == 0)
            {
                _logger("Error: Missing stack name or prompt. Example: syncro init mern my-app");
                return;
            }

            string input = context.Args[0].ToLower();
            string name = context.Args.Count > 1 ? context.Args[1] : "new-project";

            // Check if it's a direct stack provider
            if (_providers.TryGetValue(input, out var provider))
            {
                _logger($"Initializing {input} project: {name}...");
                var success = await provider.Create(name, GetWorkspacesPath());
                if (success)
                    _logger($"{input} project '{name}' created successfully.");
                else
                    _logger($"Error: Failed to create {input} project '{name}'.");
            }
            else
            {
                // Smart Init Flow
                _logger($"Analyzing prompt: '{context.RawInput.Replace("init ", "")}'...");
                await HandleSmartInit(context.RawInput.Replace("init ", ""), name);
            }
        }

        private async Task HandleSmartInit(string prompt, string defaultName)
        {
            try
            {
                // Call AI to determine stack and plan
                var (plan, error) = await _aiClient.CallLLM(prompt, "ProjectPlanner", GetWorkspacesPath());
                
                if (plan != null)
                {
                    _logger("AI Project Plan generated. Executing...");
                    // In a real scenario, we'd parse the plan (JSON) and execute steps.
                    // For this demo, we'll try to find the best matching provider.
                    string detectedStack = DetectStackFromPlan(plan);
                    
                    if (_providers.TryGetValue(detectedStack, out var provider))
                    {
                        var success = await provider.Create(defaultName, GetWorkspacesPath());
                        if (success)
                            _logger($"Smart Init: Created {detectedStack} project based on your request.");
                        else
                            _logger($"Smart Init: Failed to create {detectedStack} project.");
                    }
                    else
                    {
                        _logger($"AI suggested a {detectedStack} stack, but no provider is available yet.");
                    }
                }
                else
                {
                    _logger($"AI Planning failed: {error}");
                }
            }
            catch (Exception ex)
            {
                _logger($"Smart Init Error: {ex.Message}");
            }
        }

        private string DetectStackFromPlan(string plan)
        {
            plan = plan.ToLower();
            if (plan.Contains("python") || plan.Contains("fastapi")) return "python";
            if (plan.Contains("mern") || plan.Contains("node") || plan.Contains("react")) return "mern";
            return "python"; // Default fallback
        }

        private string GetWorkspacesPath()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Workspaces");
            if (!System.IO.Directory.Exists(path)) System.IO.Directory.CreateDirectory(path);
            return path;
        }
    }
}
