using System;
using CliWrap;
using CliWrap.Buffered;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Git
{
    public class CommitTool : IMcpTool
    {
        public string Name => "Commit";
        public string Description => "Stages all tracked and untracked changes, and commits them with a message.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"message\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", message = "" });
                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.message)) 
                {
                    return "{ \"error\": \"workspacePath and message required.\" }";
                }

                // Stage all
                await RunGitCommand(new[] { "add", "-A" }, input.workspacePath);

                // Commit
                string commitOutput = await RunGitCommand(new[] { "commit", "-m", input.message }, input.workspacePath);

                return JsonConvert.SerializeObject(new { success = true, output = commitOutput });
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }

        private async Task<string> RunGitCommand(string[] arguments, string workingDir)
        {
            var result = await Cli.Wrap("git")
                .WithArguments(arguments)
                .WithWorkingDirectory(workingDir)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (result.ExitCode != 0 && !result.StandardOutput.Contains("nothing to commit")) 
                throw new Exception(result.StandardError);

            return result.StandardOutput;
        }
    }
}
