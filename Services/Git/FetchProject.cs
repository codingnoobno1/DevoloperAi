using Syncro.Desktop.Services.ProgramLogic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    public class FetchProject
    {
        private readonly GitCommandService _gitCommandService;

        public FetchProject(GitCommandService gitCommandService)
        {
            _gitCommandService = gitCommandService;
        }

        public async Task<string> GetGitStatus(string workingDirectory)
        {
            var (hasChanges, statusOutput) = await _gitCommandService.GetGitStatus(workingDirectory);
            if (hasChanges)
            {
                return $"Changes detected:\n{statusOutput}";
            }
            return "No changes (Up to date)";
        }

        public async Task<string> GetRepositoryUrl(string workingDirectory)
        {
            var (isRepo, remoteUrl) = await _gitCommandService.GetRepositoryInfo(workingDirectory);
            if (isRepo && !string.IsNullOrWhiteSpace(remoteUrl))
            {
                return remoteUrl;
            }
            return "N/A";
        }
    }
}