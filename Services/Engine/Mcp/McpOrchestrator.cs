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
        private readonly Syncro.Desktop.Services.Agent.Policy.AgentGuard? _guard;

        public McpOrchestrator(ILLMProvider llmProvider, Syncro.Desktop.Services.Agent.Policy.AgentGuard? guard = null)
        {
            _llmProvider = llmProvider;
            _guard = guard;
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

        /// <summary>All registered tools — the source for MCP <c>tools/list</c> (McpHttpEndpoint).</summary>
        public IReadOnlyCollection<IMcpTool> Tools => _registeredTools.Values;

        public bool TryGetTool(string toolName, out IMcpTool tool)
        {
            if (_registeredTools.TryGetValue(toolName, out var found))
            {
                tool = found;
                return true;
            }
            tool = null!;
            return false;
        }

        /// <summary>Run a tool for the in-app agent. Goes through Agent Settings' bypass decision.</summary>
        public Task<string> ExecuteToolAsync(string toolName, string jsonPayload)
            => ExecuteToolAsync(toolName, jsonPayload, "agent");

        /// <summary>
        /// Run a tool on behalf of <paramref name="origin"/> ("agent", "editor", …). Every call is decided,
        /// possibly approved, and audited by <see cref="Syncro.Desktop.Services.Agent.Policy.AgentGuard"/>.
        /// </summary>
        public async Task<string> ExecuteToolAsync(string toolName, string jsonPayload, string origin)
        {
            if (!_registeredTools.TryGetValue(toolName, out var tool))
                return $"Error: Tool '{toolName}' not found in the MCP registry.";

            if (_guard == null)
            {
                // No guard registered: fail closed for anything the tool itself marks as needing approval.
                if (tool.RequiresAdminApproval)
                    return $"Error: '{toolName}' needs approval, but the approval service is not available.";
                return await tool.ExecuteAsync(jsonPayload);
            }

            return await _guard.ExecuteAsync(toolName, jsonPayload, origin, () => tool.ExecuteAsync(jsonPayload));
        }

        /// <summary>Scope one agent task so "allow for this run" approvals don't leak into the next.</summary>
        public System.IDisposable BeginRun() => _guard?.BeginRun() ?? new NoopScope();

        private sealed class NoopScope : System.IDisposable { public void Dispose() { } }

        /// <summary>
        /// Run the agent loop with all registered MCP tools — the Groq-backed Git agent entry point.
        /// Pass a focused <paramref name="systemContext"/> describing the agent role; all registered
        /// tools (including GitMcp, ShellMcp, etc.) are offered automatically.
        /// </summary>
        public async Task<string> RunAgentAsync(string systemContext, string userGoal)
        {
            using var run = BeginRun();
            return await _llmProvider.RunAgentLoopAsync(
                systemContext,
                userGoal,
                _registeredTools.Values,
                ExecuteToolAsync);
        }

        /// <summary>
        /// Run the agent loop scoped to a specific server's tools (e.g. GitMcp only).
        /// </summary>
        public async Task<string> RunAgentWithServerAsync(string systemContext, string userGoal, IMcpServer server)
        {
            using var run = BeginRun();
            var tools = new System.Collections.Generic.List<IMcpTool>(server.GetTools());
            return await _llmProvider.RunAgentLoopAsync(systemContext, userGoal, tools, ExecuteToolAsync);
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
