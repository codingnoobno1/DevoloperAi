using DeveloperAI.BusinessLogic;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class MERNProjectGeneratorService : IProjectGenerator
    {
        private readonly AIClient _aiClient;
        private readonly FolderCreationService _folderCreationService;
        private readonly CommandExecutionService _commandExecutionService;

        public MERNProjectGeneratorService(AIClient aiClient, FolderCreationService folderCreationService, CommandExecutionService commandExecutionService)
        {
            _aiClient = aiClient;
            _folderCreationService = folderCreationService;
            _commandExecutionService = commandExecutionService;
        }

        public async Task<(bool success, string message)> GenerateProject(string userPrompt, string targetWorkspacePath)
        {
            if (string.IsNullOrWhiteSpace(targetWorkspacePath))
            {
                return (false, "Target Workspace Path cannot be empty for MERN project generation.");
            }

            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(targetWorkspacePath);
            if (!folderSuccess)
            {
                return (false, $"Failed to create base project directory: {folderMessage}");
            }

            // Refine the prompt for MERN stack
            string refinedPrompt = $"Generate a complete MERN stack project setup including Node.js (Express), React, MongoDB connection (placeholder), and common project structure. Provide specific commands for initializing the project, installing dependencies for both frontend and backend (e.g., npm init, npm install express, create-react-app), and creating basic file structures (e.g., /server, /client, server.js, App.js). The project should be set up in the directory: {targetWorkspacePath}. User request: {userPrompt}";

            var (script, error) = await _aiClient.CallLLM(refinedPrompt, "EnvironmentSetup", targetWorkspacePath);

            if (script == null)
            {
                return (false, $"Failed to get MERN setup script from AI: {error}");
            }

            // Execute the generated script/commands
            var (execSuccess, execOutput, execError) = await _commandExecutionService.ExecuteCommand(script, targetWorkspacePath);

            if (!execSuccess)
            {
                return (false, $"MERN project setup failed:\nOutput: {execOutput}\nError: {execError}");
            }

            return (true, $"MERN project '{Path.GetFileName(targetWorkspacePath)}' generated and set up successfully.\nOutput:\n{execOutput}");
        }
    }
}
