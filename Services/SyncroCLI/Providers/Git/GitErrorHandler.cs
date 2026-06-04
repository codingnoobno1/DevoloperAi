using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.SyncroCLI.Providers.Git
{
    public class GitErrorHandler
    {
        public string AnalyzeError(string errorOutput)
        {
            if (string.IsNullOrEmpty(errorOutput)) return "Unknown Git error.";

            if (errorOutput.Contains("not a git repository"))
                return "The current directory is not a Git repository. Run 'syncro git init' first.";

            if (errorOutput.Contains("Permission denied") || errorOutput.Contains("Authentication failed"))
                return "Git Authentication failed. Check your SSH keys or credentials.";

            if (errorOutput.Contains("Already up to date"))
                return "No changes to pull. Everything is up to date.";

            if (errorOutput.Contains("Automatic merge failed"))
                return "Merge conflicts detected. Please resolve conflicts manually.";

            if (errorOutput.Contains("branch is ahead of"))
                return "Your local branch is ahead of the remote. You need to push.";

            return $"Git Error: {errorOutput}";
        }
    }
}
