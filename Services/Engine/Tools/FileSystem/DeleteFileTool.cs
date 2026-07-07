using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    public class DeleteFileTool : IMcpTool
    {
        public string Name => "DeleteFile";
        public string Description => "Deletes a specific file from the disk. Requires an absolute path.";
        public string InputSchema => "{ \"absolutePath\": \"string\" }";
        public bool RequiresAdminApproval => false; 

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { absolutePath = "" });
                if (string.IsNullOrEmpty(input?.absolutePath))
                {
                    return Task.FromResult("{ \"error\": \"absolutePath is required.\" }");
                }

                if (!File.Exists(input.absolutePath))
                {
                    return Task.FromResult("{ \"error\": \"File does not exist or was already deleted.\" }");
                }

                // In production, we'd want to inject IWorkspaceManager to verify the path 
                // is within the ActiveWorkspacePath boundary to prevent arbitrary deletion.
                
                File.Delete(input.absolutePath);
                
                var result = new
                {
                    success = true,
                    message = $"File deleted successfully: {input.absolutePath}"
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
