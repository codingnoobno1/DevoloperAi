using DeveloperAI.BusinessLogic;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class PhpProjectGeneratorService : IProjectGenerator
    {
        private readonly AIClient _aiClient;
        private readonly FolderCreationService _folderCreationService;
        private readonly CommandExecutionService _commandExecutionService;

        public PhpProjectGeneratorService(AIClient aiClient, FolderCreationService folderCreationService, CommandExecutionService commandExecutionService)
        {
            _aiClient = aiClient;
            _folderCreationService = folderCreationService;
            _commandExecutionService = commandExecutionService;
        }

        public async Task<(bool success, string message)> GenerateProject(string userPrompt, string targetWorkspacePath)
        {
            if (string.IsNullOrWhiteSpace(targetWorkspacePath))
            {
                return (false, "Target Workspace Path cannot be empty for PHP project generation.");
            }

            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(targetWorkspacePath);
            if (!folderSuccess)
            {
                return (false, $"Failed to create base project directory: {folderMessage}");
            }

            // Refine the prompt for PHP project
            string refinedPrompt = $"Generate a complete PHP project setup, including Composer for dependency management, a basic web server configuration (e.g., Apache/Nginx placeholder or simple built-in PHP server command), and common project structure (e.g., public/, src/, vendor/). Provide specific commands for initializing the project, installing dependencies (e.g., composer install), and creating basic file structures. The project should be set up in the directory: {targetWorkspacePath}. User request: {userPrompt}";

            var (script, error) = await _aiClient.CallLLM(refinedPrompt, "EnvironmentSetup", targetWorkspacePath);

            if (script == null)
            {
                return (false, $"Failed to get PHP setup script from AI: {error}");
            }

            // Execute the generated script/commands
            var (execSuccess, execOutput, execError) = await _commandExecutionService.ExecuteCommand(script, targetWorkspacePath);

            if (!execSuccess)
            {
                return (false, $"PHP project setup failed:\nOutput: {execOutput}\nError: {execError}");
            }

            return (true, $"PHP project '{Path.GetFileName(targetWorkspacePath)}' generated and set up successfully.\nOutput:\n{execOutput}");
        }
    }
}
