using System;
using CliWrap;
using CliWrap.Buffered;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Git
{
    public class GitStatusTool : IMcpTool
    {
        public string Name => "GitStatus";
        public string Description => "Returns the current working tree status, highlighting modified, untracked, and staged files.";
        public string InputSchema => "{ \"workspacePath\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "" });
                if (string.IsNullOrEmpty(input?.workspacePath)) return "{ \"error\": \"workspacePath required.\" }";

                var result = await Cli.Wrap("git")
                    .WithArguments(new[] { "status", "-s" })
                    .WithWorkingDirectory(input.workspacePath)
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();

                return JsonConvert.SerializeObject(new { status = result.StandardOutput, success = result.ExitCode == 0 });
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
