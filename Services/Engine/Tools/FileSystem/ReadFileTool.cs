using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    public class ReadFileTool : IMcpTool
    {
        public string Name => "ReadFile";
        public string Description => "Reads the contents of a specific file from the disk.";
        public string InputSchema => "{ \"absolutePath\": \"string\" }";
        public bool RequiresAdminApproval => false; // Read-only

        // Limit to 1MB to prevent LLM context explosion on huge binaries/bundles.
        private const int MaxFileSizeInBytes = 1 * 1024 * 1024;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { absolutePath = "" });
                if (string.IsNullOrEmpty(input?.absolutePath))
                {
                    return "{ \"error\": \"absolutePath is required.\" }";
                }

                if (!File.Exists(input.absolutePath))
                {
                    return "{ \"error\": \"File does not exist.\" }";
                }

                var fileInfo = new FileInfo(input.absolutePath);
                if (fileInfo.Length > MaxFileSizeInBytes)
                {
                    return $"{{ \"error\": \"File is too large to read ({(fileInfo.Length / 1024.0 / 1024.0):F2} MB). Maximum allowed is 1 MB.\" }}";
                }

                string content = await File.ReadAllTextAsync(input.absolutePath);
                
                var result = new
                {
                    path = input.absolutePath,
                    content = content
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
