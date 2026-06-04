using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public interface IGitService
    {
        Task<(bool isRepo, string remoteUrl)> GetRepositoryInfo(string workingDirectory);
        Task<(bool hasChanges, string statusOutput)> GetGitStatus(string workingDirectory);
        Task<Dictionary<string, double>> GetLanguagePercentages(string workingDirectory);
        Task<string> GetCurrentBranch(string workingDirectory);
        Task<(bool success, string message)> CloneRepository(string repositoryUrl, string targetPath);
        Task<(bool success, string message)> PullRepository(string workingDirectory);
        Task<(bool success, string message)> PushRepository(string workingDirectory);
        Task<(bool success, string message)> CommitChanges(string workingDirectory, string commitMessage);
    }
}