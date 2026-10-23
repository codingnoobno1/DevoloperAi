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

        /// <summary>
        /// Sends a prompt to the LLM and requests a structured JSON response matching type T.
        /// </summary>
        Task<T?> GenerateStructuredResponseAsync<T>(string systemContext, string userPrompt) where T : class;

        /// <summary>
        /// Runs an autonomous agent loop that calls MCP tools until a final text response is produced.
        /// </summary>
        Task<string> RunAgentLoopAsync(string systemContext, string userPrompt, System.Collections.Generic.IEnumerable<Syncro.Desktop.Services.Engine.Mcp.IMcpTool> availableTools, System.Func<string, string, Task<string>> toolExecutor);
    }
}
