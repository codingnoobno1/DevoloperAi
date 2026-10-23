using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Git
{
    public class GitCopilotService
    {
        private readonly Syncro.Desktop.Services.Engine.Mcp.McpOrchestrator _orchestrator;
        private readonly ILLMProvider _llm;

        public GitCopilotService(Syncro.Desktop.Services.Engine.Mcp.McpOrchestrator orchestrator, ILLMProvider llm)
        {
            _orchestrator = orchestrator;
            _llm = llm;
        }

        public async Task<CopilotResponse> ProcessCommandAsync(string repo, string prompt, CancellationToken ct = default)
        {
            var systemPrompt = @"You are Syncro Assist, a highly capable autonomous agent. You have access to various MCP tools (FileSystem, Git, Shell, Patch, etc.). 
Please use these tools to fulfill the user's request. 
IMPORTANT: If you need to run a shell command, git push, or script visibly (so the user can see the output in a terminal window), YOU MUST use the 'RunBatchScript' tool from the Shell MCP.
Provide a concise, friendly summary of the actions you took.";

            try
            {
                using var run = _orchestrator.BeginRun();
                var finalMessage = await _llm.RunAgentLoopAsync(
                    systemPrompt,
                    $"Context Directory: {repo}\nUser Request: {prompt}",
                    _orchestrator.Tools,
                    (toolName, args) => _orchestrator.ExecuteToolAsync(toolName, args)
                );

                return new CopilotResponse 
                { 
                    Intent = CopilotIntent.WorkflowScript, // This forces the UI to refresh its git status after execution
                    Commentary = finalMessage 
                };
            }
            catch (Exception ex)
            {
                return new CopilotResponse 
                { 
                    Intent = CopilotIntent.Unknown, 
                    Commentary = $"Agent loop failed: {ex.Message}" 
                };
            }
        }
    }
}
