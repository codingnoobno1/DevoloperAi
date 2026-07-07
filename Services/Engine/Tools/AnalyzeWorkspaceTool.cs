using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools
{
    public class AnalyzeWorkspaceTool : IMcpTool
    {
        public string Name => "AnalyzeWorkspace";
        public string Description => "Scans the project directory to identify the framework, language, and high-level structure.";
        public string InputSchema => "{ \"workspacePath\": \"string\" }";
        public bool RequiresAdminApproval => false; // Read-only

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "" });
                if (string.IsNullOrEmpty(input?.workspacePath) || !Directory.Exists(input.workspacePath))
                {
                    return Task.FromResult("{ \"error\": \"Invalid workspace path.\" }");
                }

                bool hasPubspec = File.Exists(Path.Combine(input.workspacePath, "pubspec.yaml"));
                bool hasPackageJson = File.Exists(Path.Combine(input.workspacePath, "package.json"));
                bool hasCsproj = Directory.GetFiles(input.workspacePath, "*.csproj").Length > 0;

                string framework = "Unknown";
                if (hasPubspec) framework = "Flutter/Dart";
                else if (hasCsproj) framework = ".NET C#";
                else if (hasPackageJson) framework = "Node.js";

                var result = new
                {
                    Framework = framework,
                    FilesScanned = Directory.GetFiles(input.workspacePath, "*.*", SearchOption.AllDirectories).Length,
                    Status = "Success"
                };

                return Task.FromResult(JsonConvert.SerializeObject(result));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"{{ \"error\": \"{ex.Message}\" }}");
            }
        }
    }
}
