using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Engine.Mcp
{
    /// <summary>
    /// Orchestrates multiple MCP tools and exposes them to the LLM.
    /// </summary>
    public class McpOrchestrator
    {
        private readonly Dictionary<string, IMcpTool> _registeredTools = new();
        private readonly ILLMProvider _llmProvider;

        public McpOrchestrator(ILLMProvider llmProvider)
        {
            _llmProvider = llmProvider;
        }

        public void RegisterTool(IMcpTool tool)
        {
            if (!_registeredTools.ContainsKey(tool.Name))
            {
                _registeredTools.Add(tool.Name, tool);
            }
        }

        public async Task RegisterServerAsync(IMcpServer server)
        {
            await server.InitializeAsync();
            foreach (var tool in server.GetTools())
            {
                RegisterTool(tool);
            }
        }

        public async Task<string> ExecuteToolAsync(string toolName, string jsonPayload)
        {
            if (_registeredTools.TryGetValue(toolName, out var tool))
            {
                // In production, validate AdminApproval and Read/Write permissions here.
                return await tool.ExecuteAsync(jsonPayload);
            }

            return $"Error: Tool '{toolName}' not found in the MCP registry.";
        }
    }

    /// <summary>
    /// Strict contract for all Syncro MCP tools.
    /// </summary>
    public interface IMcpTool
    {
        string Name { get; }
        string Description { get; }
        string InputSchema { get; }
        bool RequiresAdminApproval { get; }
        
        Task<string> ExecuteAsync(string inputJson);
    }
}
