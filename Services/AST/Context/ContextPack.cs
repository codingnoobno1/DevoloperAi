using System.Collections.Generic;

namespace Syncro.Desktop.Services.AST.Context
{
    /// <summary>Detail tiers for a project projection — each strictly larger than the last.</summary>
    public enum ContextTier
    {
        Skeleton = 0,   // L0: name, stack, counts, endpoints, modules (~hundreds of tokens)
        Symbols = 1,    // L1: class/method/route/dto signatures with file:line
        Detail = 2      // L2: L1 + summaries/docstrings + dependencies
    }

    /// <summary>One project rendered at a tier, with its token cost.</summary>
    public sealed class ProjectContext
    {
        public string Project { get; init; } = "";
        public ContextTier Tier { get; init; }
        public string Text { get; init; } = "";
        public int Tokens { get; init; }
    }

    /// <summary>A budgeted, LLM-ready projection of one or more projects.</summary>
    public sealed class ContextPack
    {
        public int BudgetTokens { get; init; }
        public int UsedTokens { get; set; }
        public List<ProjectContext> Projects { get; init; } = new();
        public List<string> Notes { get; init; } = new();   // truncation / omission notes

        /// <summary>The concatenated markdown the model actually receives.</summary>
        public string Render()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in Projects)
            {
                sb.Append(p.Text);
                if (!p.Text.EndsWith("\n")) sb.Append('\n');
                sb.Append('\n');
            }
            foreach (var n in Notes) sb.Append("> ").Append(n).Append('\n');
            return sb.ToString();
        }
    }
}
