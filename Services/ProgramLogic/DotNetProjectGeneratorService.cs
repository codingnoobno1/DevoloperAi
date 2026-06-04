using DeveloperAI.BusinessLogic;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class DotNetProjectGeneratorService : IProjectGenerator
    {
        private readonly AIClient _aiClient;
        private readonly FolderCreationService _folderCreationService;
        private readonly CommandExecutionService _commandExecutionService;

        public DotNetProjectGeneratorService(AIClient aiClient, FolderCreationService folderCreationService, CommandExecutionService commandExecutionService)
        {
            _aiClient = aiClient;
            _folderCreationService = folderCreationService;
            _commandExecutionService = commandExecutionService;
        }

        public async Task<(bool success, string message)> GenerateProject(string userPrompt, string targetWorkspacePath)
        {
            if (string.IsNullOrWhiteSpace(targetWorkspacePath))
            {
                return (false, "Target Workspace Path cannot be empty for .NET project generation.");
            }

            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(targetWorkspacePath);
            if (!folderSuccess)
            {
                return (false, $"Failed to create base project directory: {folderMessage}");
            }

            // Refine the prompt for .NET project
            string refinedPrompt = $"Generate a complete .NET project setup (e.g., console app, web API, Blazor, MAUI based on user prompt) including the necessary SDK commands (e.g., dotnet new, dotnet add package), and common project structure. Provide specific commands for initializing the project, installing dependencies, and creating basic file structures. The project should be set up in the directory: {targetWorkspacePath}. User request: {userPrompt}";

            var (script, error) = await _aiClient.CallLLM(refinedPrompt, "EnvironmentSetup", targetWorkspacePath);

            if (script == null)
            {
                return (false, $"Failed to get .NET setup script from AI: {error}");
            }

            // Execute the generated script/commands
            var (execSuccess, execOutput, execError) = await _commandExecutionService.ExecuteCommand(script, targetWorkspacePath);

            if (!execSuccess)
            {
                return (false, $".NET project setup failed:\nOutput: {execOutput}\nError: {execError}");
            }

            return (true, $".NET project '{Path.GetFileName(targetWorkspacePath)}' generated and set up successfully.\nOutput:\n{execOutput}");
        }
    }
}
