using System;
using System.IO;
using System.Threading.Tasks;
using DeveloperAI.BusinessLogic;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class AiFeatureMapper
    {
        private readonly AIClient _aiClient;
        private readonly ContractLinker _contractLinker;

        public AiFeatureMapper(AIClient aiClient, ContractLinker contractLinker)
        {
            _aiClient = aiClient;
            _contractLinker = contractLinker;
        }

        public async Task<(bool success, string message)> GenerateFeatureAsync(string featureRequest, string frontendAppPath, string backendAppPath)
        {
            if (string.IsNullOrEmpty(frontendAppPath) || string.IsNullOrEmpty(backendAppPath))
            {
                return (false, "Both frontend and backend paths are required.");
            }

            // Step 1: Generate Frontend Spec
            var frontendPrompt = $"Generate a Flutter BLoC implementation for this feature request: {featureRequest}. Create a Cubit, Repository, and UI. Output ONLY the files mapping the BLoC structure.";
            var frontendTask = _aiClient.CallLLM(frontendPrompt, "FeatureGeneration", frontendAppPath);

            // Step 2: Generate Backend Spec
            var backendPrompt = $"Generate a .NET Web API implementation for this feature request: {featureRequest}. Create the Controller, Model, and Service. Output ONLY the files mapping the API structure.";
            var backendTask = _aiClient.CallLLM(backendPrompt, "FeatureGeneration", backendAppPath);

            // Step 3: Run in parallel (Phase FB4)
            await Task.WhenAll(frontendTask, backendTask);

            var frontendResult = await frontendTask;
            var backendResult = await backendTask;

            if (frontendResult.script == null || backendResult.script == null)
            {
                return (false, "Failed to generate one or both specifications via AI.");
            }

            // Write the generated contents (assuming the LLM returns shell scripts or file content to execute/write)
            // For now, we simulate saving them (in reality, CommandExecutionService would execute them)
            var feFeaturePath = Path.Combine(frontendAppPath, "lib", "features", "generated_feature");
            var beFeaturePath = Path.Combine(backendAppPath, "Controllers");
            
            Directory.CreateDirectory(feFeaturePath);
            Directory.CreateDirectory(beFeaturePath);

            await File.WriteAllTextAsync(Path.Combine(feFeaturePath, "generated_repository.dart"), frontendResult.script);
            await File.WriteAllTextAsync(Path.Combine(beFeaturePath, "GeneratedController.cs"), backendResult.script);

            // Step 4: Link Contracts
            await _contractLinker.LinkAsync(feFeaturePath, beFeaturePath);

            return (true, "Parallel generation and contract linking successful.");
        }
    }
}
