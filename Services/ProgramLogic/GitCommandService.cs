using System.Threading.Tasks;
using System.IO;
using Syncro.Desktop.Services.ProgramLogic;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class GitCommandService : IGitService
    {
        private readonly CommandExecutionService _commandExecutionService;

        public GitCommandService(CommandExecutionService commandExecutionService)
        {
            _commandExecutionService = commandExecutionService;
        }

        public async Task<(bool success, string output, string error)> RunGitCommand(string arguments, string workingDirectory)
        {
            if (!Directory.Exists(workingDirectory))
            {
                return (false, "", $"Error: Working directory does not exist: {workingDirectory}");
            }

            // Git commands are typically not run in a shell for direct output parsing
            var (success, output, error) = await _commandExecutionService.ExecuteCommand(
                $"git {arguments}", workingDirectory, runInShell: false);

            return (success, output, error);
        }

        public async Task<(bool isRepo, string remoteUrl)> GetRepositoryInfo(string workingDirectory)
        {
            var (success, output, _) = await RunGitCommand("rev-parse --is-inside-work-tree", workingDirectory);
            if (!success || output.Trim() != "true")
            {
                return (false, ""); // Not a Git repository
            }

            var (urlSuccess, urlOutput, _) = await RunGitCommand("config --get remote.origin.url", workingDirectory);
            if (urlSuccess)
            {
                return (true, urlOutput.Trim());
            }
            return (true, ""); // Is a repo, but no remote URL configured
        }

        public async Task<(bool hasChanges, string statusOutput)> GetGitStatus(string workingDirectory)
        {
            var (success, output, error) = await RunGitCommand("status --porcelain", workingDirectory);
            if (success)
            {
                return (!string.IsNullOrWhiteSpace(output), output);
            }
            return (false, $"Error getting Git status: {error}");
        }

        public async Task<Dictionary<string, double>> GetLanguagePercentages(string workingDirectory)
        {
            var languagePercentages = new Dictionary<string, double>();

            var (success, filesOutput, _) = await RunGitCommand("ls-files", workingDirectory);
            if (!success)
            {
                // Log error or return empty dictionary
                return languagePercentages;
            }

            var files = filesOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var fileExtensions = new Dictionary<string, int>();
            int totalLines = 0;

            foreach (var file in files)
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                // rudimentary language detection based on extension
                if (string.IsNullOrEmpty(extension)) continue;

                // count lines of code - this is a simplification, a real LOC counter would be more complex
                try
                {
                    int lines = (await File.ReadAllLinesAsync(Path.Combine(workingDirectory, file))).Length;
                    totalLines += lines;

                    if (fileExtensions.ContainsKey(extension))
                    {
                        fileExtensions[extension] += lines;
                    }
                    else
                    {
                        fileExtensions.Add(extension, lines);
                    }
                }
                catch { /* Ignore files that can't be read */ }
            }

            if (totalLines == 0) return languagePercentages;

            foreach (var entry in fileExtensions)
            {
                languagePercentages.Add(entry.Key, (double)entry.Value / totalLines * 100);
            }

            return languagePercentages;
        }

        public async Task<string> GetCurrentBranch(string workingDirectory)
        {
            var (success, output, error) = await RunGitCommand("rev-parse --abbrev-ref HEAD", workingDirectory);
            if (success)
            {
                return output.Trim();
            }
            return $"Error getting current branch: {error}";
        }

        public async Task<(bool success, string message)> CloneRepository(string repositoryUrl, string targetPath)
        {
            // Git clone is special, it doesn't run inside a working directory.
            // It creates the working directory. We'll run it from the parent of the target path.
            string? parentDirectory = Directory.GetParent(targetPath)?.FullName;
            if (parentDirectory == null)
            {
                return (false, $"Could not determine parent directory of {targetPath}");
            }
            Directory.CreateDirectory(parentDirectory);

            var (success, output, error) = await _commandExecutionService.ExecuteCommand(
                $"git clone \"{repositoryUrl}\" \"{targetPath}\"", parentDirectory, runInShell: false);

            return (success, success ? output : error);
        }

        public async Task<(bool success, string message)> PullRepository(string workingDirectory)
        {
            var (success, output, error) = await RunGitCommand("pull", workingDirectory);
            return (success, success ? output : error);
        }

        public async Task<(bool success, string message)> PushRepository(string workingDirectory)
        {
            var (success, output, error) = await RunGitCommand("push", workingDirectory);
            return (success, success ? output : error);
        }

        public async Task<(bool success, string message)> CommitChanges(string workingDirectory, string commitMessage)
        {
            var (addSuccess, _, addError) = await RunGitCommand("add .", workingDirectory);
            if (!addSuccess) return (false, $"Failed to stage changes: {addError}");

            var (commitSuccess, commitOutput, commitError) = await RunGitCommand($"commit -m \"{commitMessage}\"", workingDirectory);
            return (commitSuccess, commitSuccess ? commitOutput : commitError);
        }
    }
}
