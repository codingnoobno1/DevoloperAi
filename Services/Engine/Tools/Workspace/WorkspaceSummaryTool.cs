using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Engine.Tools.Workspace
{
    public class WorkspaceSummaryTool : IMcpTool
    {
        public string Name => "WorkspaceSummary";
        public string Description => "Generates a high-level statistical summary of the workspace health and file counts.";
        public string InputSchema => "{ \"workspacePath\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "" });
                if (string.IsNullOrEmpty(input?.workspacePath) || !Directory.Exists(input.workspacePath))
                {
                    return Task.FromResult("{ \"error\": \"Valid workspacePath is required.\" }");
                }

                int totalFiles = 0;
                long totalSize = 0;

                var files = Directory.GetFiles(input.workspacePath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    // Basic filtering to avoid counting massive caches
                    if (!file.Contains("node_modules") && !file.Contains(".git") && !file.Contains("\\bin\\") && !file.Contains("\\obj\\"))
                    {
                        totalFiles++;
                        totalSize += new FileInfo(file).Length;
                    }
                }

                var result = new
                {
                    path = input.workspacePath,
                    trackedFilesCount = totalFiles,
                    workspaceSizeBytes = totalSize,
                    workspaceSizeMb = Math.Round(totalSize / 1024.0 / 1024.0, 2),
                    status = "Healthy"
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
