using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Git;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Git
{
    public class CloneRepositoryTool : IMcpTool
    {
        public string Name => "CloneRepository";
        public string Description => "Clones a remote repository locally securely using SSH or HTTPS.";
        public string InputSchema => "{ \"repoUrl\": \"string\", \"targetDirectory\": \"string\", \"sshKeyPath\": \"string?\" }";
        public bool RequiresAdminApproval => true; // Potentially pulling massive external data

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { repoUrl = "", targetDirectory = "", sshKeyPath = "" });
                
                if (string.IsNullOrEmpty(input?.repoUrl) || string.IsNullOrEmpty(input?.targetDirectory))
                {
                    return "{ \"error\": \"repoUrl and targetDirectory are required.\" }";
                }

                var gitService = new GitAutomationService(); // Ideally injected via DI in a production setup
                bool success = await gitService.CloneRepositoryAsync(input.repoUrl, input.targetDirectory, input.sshKeyPath);

                var result = new
                {
                    success = success,
                    message = success ? $"Successfully cloned {input.repoUrl}" : "Failed to clone repository. Check logs."
                };

                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
