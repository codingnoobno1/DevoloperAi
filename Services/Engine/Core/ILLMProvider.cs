using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Core
{
    public interface ILLMProvider
    {
        /// <summary>
        /// Sends a highly contextualized prompt to the LLM (e.g., Groq) and executes necessary MCP tools.
        /// </summary>
        Task<string> GenerateResponseAsync(string systemContext, string userPrompt);

        /// <summary>
        /// Validates if the provided API key is valid.
        /// </summary>
        Task<bool> ValidateConnectionAsync(string apiKey);
    }
}
