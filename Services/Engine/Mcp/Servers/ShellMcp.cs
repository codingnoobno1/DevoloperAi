using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.ProgramLogic;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class ShellMcp : IMcpServer
    {
        public string Name => "Shell MCP";
        public string Description => "Executes shell scripts (.bat files) in a visible command prompt window.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new RunBatchScriptTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    public class RunBatchScriptTool : IMcpTool
    {
        public string Name => "RunBatchScript";
        public string Description => "Executes a provided batch script in a visible command window. Use this for git pushes, npm installs, or any long-running command where the user wants to see the output live.";
        
        public string InputSchema => @"{
  ""type"": ""object"",
  ""properties"": {
    ""targetDirectory"": { ""type"": ""string"", ""description"": ""The absolute path to the directory where the script should run."" },
    ""scriptContent"": { ""type"": ""string"", ""description"": ""The content of the .bat file."" }
  },
  ""required"": [""targetDirectory"", ""scriptContent""]
}";

        public bool RequiresAdminApproval => true;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var json = JObject.Parse(inputJson);
            var targetDirectory = json["targetDirectory"]?.ToString();
            var scriptContent = json["scriptContent"]?.ToString();

            if (string.IsNullOrWhiteSpace(targetDirectory) || string.IsNullOrWhiteSpace(scriptContent))
            {
                return "Error: targetDirectory and scriptContent are required.";
            }
            
            try
            {
                string scriptFilePath = System.IO.Path.Combine(targetDirectory, $"generated_script_{Guid.NewGuid().ToString().Substring(0, 8)}.bat");
                await System.IO.File.WriteAllTextAsync(scriptFilePath, scriptContent);

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C \"{scriptFilePath}\" & pause", // add pause so the user can see the output before it closes
                    WorkingDirectory = targetDirectory,
                    UseShellExecute = true, // Must be true to pop a window
                    CreateNoWindow = false
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    return $"Script executed in a new window with exit code {process.ExitCode}.";
                }
                
                return "Error: Failed to start process.";
            }
            catch (Exception ex)
            {
                return $"Error executing script: {ex.Message}";
            }
        }
    }
}
