using DeveloperAI.BusinessLogic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class DocumentationGeneratorService
    {
        private readonly AIClient _aiClient;

        public DocumentationGeneratorService(AIClient aiClient)
        {
            _aiClient = aiClient;
        }

        public async Task<(bool success, string documentation)> GenerateDocumentation(string userPrompt)
        {
            string refinedPrompt = $"Generate comprehensive technical documentation based on the following request: '{userPrompt}'. Include code examples, explanations, and usage instructions where relevant. Format the output in Markdown for readability.";

            var (responseScript, error) = await _aiClient.CallLLM(refinedPrompt, "GeneralChat", ""); // GeneralChat mode, no specific workspace

            if (responseScript == null)
            {
                return (false, $"Failed to generate documentation: {error}");
            }

            return (true, responseScript);
        }
    }
}
