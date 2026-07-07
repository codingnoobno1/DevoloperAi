using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    public class WriteFileTool : IMcpTool
    {
        public string Name => "WriteFile";
        public string Description => "Writes content to a file, completely overwriting it if it exists. Creates parent directories automatically.";
        public string InputSchema => "{ \"absolutePath\": \"string\", \"content\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { absolutePath = "", content = "" });
                if (string.IsNullOrEmpty(input?.absolutePath))
                {
                    return "{ \"error\": \"absolutePath is required.\" }";
                }

                // Ensure parent directory exists
                var dir = Path.GetDirectoryName(input.absolutePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(input.absolutePath, input.content ?? string.Empty);
                
                var result = new
                {
                    success = true,
                    message = $"File written successfully to {input.absolutePath}"
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
