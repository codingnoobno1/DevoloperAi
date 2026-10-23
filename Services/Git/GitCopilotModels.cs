using System.Collections.Generic;

namespace Syncro.Desktop.Services.Git
{
    public enum CopilotIntent
    {
        Unknown,
        BasicAction,
        WorkflowScript,
        ReviewerAnalysis
    }

    public class CopilotResponse
    {
        public CopilotIntent Intent { get; set; }
        
        /// <summary>
        /// What the LLM wants to say to the user (e.g., "I've created a script to fetch and prune.").
        /// </summary>
        public string Commentary { get; set; } = string.Empty;

        /// <summary>
        /// Used if Intent is BasicAction. Example: ["commit", "-m", "fix bug"] or ["push"]
        /// </summary>
        public List<string>? GitArgs { get; set; }

        /// <summary>
        /// Used if Intent is WorkflowScript. Each string is a line in the workflow.
        /// e.g. ["fetch --all", "pull --ff-only", "shell: echo Done"]
        /// </summary>
        public List<string>? WorkflowSteps { get; set; }

        /// <summary>
        /// Used if Intent is ReviewerAnalysis. List of suggested reviewer names/emails.
        /// </summary>
        public List<string>? SuggestedReviewers { get; set; }
    }
}
