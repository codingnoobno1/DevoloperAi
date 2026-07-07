using System;
using CliWrap;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    public class GitAutomationService
    {
        /// <summary>
        /// Validates if an SSH key exists and can be used for cloning.
        /// </summary>
        public bool ValidateSshKey(string sshKeyPath)
        {
            if (string.IsNullOrEmpty(sshKeyPath))
            {
                // Fallback to default id_rsa
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                sshKeyPath = Path.Combine(userProfile, ".ssh", "id_rsa");
            }

            return File.Exists(sshKeyPath);
        }

        /// <summary>
        /// Clones a repository autonomously using the provided SSH credentials.
        /// </summary>
        public async Task<bool> CloneRepositoryAsync(string repoUrl, string targetDirectory, string sshKeyPath = "")
        {
            try
            {
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                // If the directory already has files, we assume it's already cloned
                if (Directory.GetFileSystemEntries(targetDirectory).Length > 0)
                {
                    return true;
                }

                var cmd = Cli.Wrap("git")
                    .WithArguments(new[] { "clone", repoUrl, targetDirectory })
                    .WithValidation(CommandResultValidation.None);

                if (!string.IsNullOrEmpty(sshKeyPath) && File.Exists(sshKeyPath))
                {
                    string sshCommand = $"ssh -i \"{sshKeyPath.Replace("\\", "/")}\" -o StrictHostKeyChecking=no";
                    cmd = cmd.WithEnvironmentVariables(env => env.Set("GIT_SSH_COMMAND", sshCommand));
                }

                var result = await cmd.ExecuteAsync();
                return result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Git clone failed: {ex.Message}");
                return false;
            }
        }
    }
}
