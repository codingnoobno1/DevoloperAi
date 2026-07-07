using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Patch
{
    public class RollbackPatchTool : IMcpTool
    {
        public string Name => "RollbackPatch";
        public string Description => "Reverts a file back to a previous state if a patch breaks compilation or tests.";
        public string InputSchema => "{ \"absolutePath\": \"string\", \"originalContent\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { absolutePath = "", originalContent = "" });
                if (string.IsNullOrEmpty(input?.absolutePath))
                {
                    return "{ \"error\": \"absolutePath is required.\" }";
                }

                if (input.originalContent == null)
                {
                    return "{ \"error\": \"originalContent must be provided.\" }";
                }

                await File.WriteAllTextAsync(input.absolutePath, input.originalContent);

                return "{ \"success\": true, \"message\": \"Rollback successful. File restored to previous state.\" }";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
