using System.Text;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Engine.Context
{
    public class ContextBuilder
    {
        private readonly IWorkspaceManager _workspaceManager;
        private readonly ISessionManager _sessionManager;
        private readonly IKnowledgeEngine _knowledgeEngine;

        public ContextBuilder(
            IWorkspaceManager workspaceManager,
            ISessionManager sessionManager,
            IKnowledgeEngine knowledgeEngine)
        {
            _workspaceManager = workspaceManager;
            _sessionManager = sessionManager;
            _knowledgeEngine = knowledgeEngine;
        }

        /// <summary>
        /// Assembles the complete context payload for the LLM based on the user prompt.
        /// </summary>
        public async Task<string> BuildSystemPromptAsync(string projectId)
        {
            var sb = new StringBuilder();

            // 1. Identity & System Constraints
            sb.AppendLine("You are Syncro, an expert software engineer and autonomous AI agent.");
            sb.AppendLine("You have access to a suite of MCP tools to read and modify the workspace.");
            sb.AppendLine("You MUST use these tools to gather information and apply patches.");
            
            // 2. Workspace Summary
            var workspaceSummary = await _workspaceManager.GetWorkspaceSummaryAsync();
            sb.AppendLine("\n### CURRENT WORKSPACE ###");
            sb.AppendLine(workspaceSummary);

            // 3. Conversation & Sprint History (Session)
            var sessionContext = await _sessionManager.GetSessionContextAsync();
            if (!string.IsNullOrEmpty(sessionContext))
            {
                sb.AppendLine("\n### CONVERSATION HISTORY & ACTIVE SPRINT ###");
                sb.AppendLine(sessionContext);
            }

            // 4. Hindsight & Architecture (Knowledge Graph)
            var projectKnowledge = await _knowledgeEngine.RetrieveProjectKnowledgeAsync(projectId);
            if (!string.IsNullOrEmpty(projectKnowledge))
            {
                sb.AppendLine("\n### ARCHITECTURE & HINDSIGHT ###");
                sb.AppendLine("Review past decisions to maintain consistency:");
                sb.AppendLine(projectKnowledge);
            }

            sb.AppendLine("\n### INSTRUCTIONS ###");
            sb.AppendLine("Before generating code, explicitly state your plan.");
            sb.AppendLine("Use the 'ApplyContextualPatchTool' or 'ScaffoldComponentTool' to make modifications.");

            return sb.ToString();
        }
    }
}
