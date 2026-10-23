using System;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>
    /// Drafts a Conventional-Commits message from a repo's pending diff, using the app's LLM (Groq).
    /// Read-only: it only produces text — committing stays a separate, explicit action. The diff is
    /// bounded before it hits the model so a huge change set can't blow the context window.
    /// </summary>
    public sealed class GitCommitDrafter
    {
        private const int MaxDiffChars = 12000;   // ~3k tokens — enough signal, bounded cost

        private readonly IGitEngine _git;
        private readonly ILLMProvider _llm;

        public GitCommitDrafter(IGitEngine git, ILLMProvider llm)
        {
            _git = git;
            _llm = llm;
        }

        /// <summary>Returns a drafted commit message, or an explanatory line if there's nothing/​no LLM.</summary>
        public async Task<string> DraftAsync(string repo, CancellationToken ct = default)
        {
            // What WILL be committed: everything vs HEAD (CommitAsync stages -A).
            var stat = await _git.RawAsync(repo, new[] { "diff", "HEAD", "--stat" }, ct);
            var patch = await _git.RawAsync(repo, new[] { "diff", "HEAD" }, ct);

            var body = patch.Output;
            if (string.IsNullOrWhiteSpace(body))
            {
                // maybe only staged (e.g. after add) — try the index
                var staged = await _git.RawAsync(repo, new[] { "diff", "--staged" }, ct);
                body = staged.Output;
                if (string.IsNullOrWhiteSpace(body)) return "chore: no changes to commit";
            }

            var truncated = body.Length > MaxDiffChars
                ? body.Substring(0, MaxDiffChars) + "\n… (diff truncated)"
                : body;

            const string system =
                "You write git commit messages in the Conventional Commits style. " +
                "Reply with ONLY the message — no code fences, no explanation. " +
                "Format: a `type(scope): summary` subject line (≤72 chars), then, if useful, a blank line " +
                "and 1–4 short bullet points. Types: feat, fix, refactor, chore, docs, test, perf, build.";

            var prompt =
                $"Summary of changed files:\n{stat.Output}\n\nUnified diff:\n{truncated}\n\n" +
                "Write the commit message.";

            try
            {
                var msg = (await _llm.GenerateResponseAsync(system, prompt))?.Trim();
                if (string.IsNullOrWhiteSpace(msg)) return "chore: update";
                // strip accidental code fences
                msg = msg.Trim('`').Trim();
                return msg;
            }
            catch (Exception ex)
            {
                return $"chore: update  (AI draft unavailable: {ex.Message})";
            }
        }
    }
}
