using DeveloperAI.BusinessLogic;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class SpringBootProjectGeneratorService : IProjectGenerator
    {
        private readonly AIClient _aiClient;
        private readonly FolderCreationService _folderCreationService;
        private readonly CommandExecutionService _commandExecutionService;

        public SpringBootProjectGeneratorService(AIClient aiClient, FolderCreationService folderCreationService, CommandExecutionService commandExecutionService)
        {
            _aiClient = aiClient;
            _folderCreationService = folderCreationService;
            _commandExecutionService = commandExecutionService;
        }

        public async Task<(bool success, string message)> GenerateProject(string userPrompt, string targetWorkspacePath)
        {
            if (string.IsNullOrWhiteSpace(targetWorkspacePath))
            {
                return (false, "Target Workspace Path cannot be empty for Spring Boot project generation.");
            }

            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(targetWorkspacePath);
            if (!folderSuccess)
            {
                return (false, $"Failed to create base project directory: {folderMessage}");
            }

            // Refine the prompt for Spring Boot
            string refinedPrompt = $"Generate a complete Spring Boot project setup using Maven/Gradle (prefer Maven unless specified), including a basic application structure, and commands for initializing the project (e.g., Spring Initializr CLI if available, or manual setup steps), and creating basic file structures. The project should be set up in the directory: {targetWorkspacePath}. User request: {userPrompt}";

            var (script, error) = await _aiClient.CallLLM(refinedPrompt, "EnvironmentSetup", targetWorkspacePath);

            if (script == null)
            {
                return (false, $"Failed to get Spring Boot setup script from AI: {error}");
            }

            // Execute the generated script/commands
            var (execSuccess, execOutput, execError) = await _commandExecutionService.ExecuteCommand(script, targetWorkspacePath);

            if (!execSuccess)
            {
                return (false, $"Spring Boot project setup failed:\nOutput: {execOutput}\nError: {execError}");
            }

            return (true, $"Spring Boot project '{Path.GetFileName(targetWorkspacePath)}' generated and set up successfully.\nOutput:\n{execOutput}");
        }
    }
}
