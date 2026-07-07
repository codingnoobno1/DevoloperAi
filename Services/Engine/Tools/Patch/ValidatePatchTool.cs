using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Patch
{
    public class ValidatePatchTool : IMcpTool
    {
        public string Name => "ValidatePatch";
        public string Description => "Dry-runs a patch to check if the searchTarget string actually exists in the file before committing to a write operation.";
        public string InputSchema => "{ \"absolutePath\": \"string\", \"searchTarget\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { absolutePath = "", searchTarget = "" });
                if (string.IsNullOrEmpty(input?.absolutePath) || !File.Exists(input.absolutePath))
                {
                    return "{ \"error\": \"Target file does not exist.\" }";
                }

                string content = await File.ReadAllTextAsync(input.absolutePath);
                bool isValid = content.Contains(input.searchTarget);

                var result = new
                {
                    isValid = isValid,
                    message = isValid ? "Patch is valid. Search target found." : "Patch is INVALID. Search target not found. Do not apply."
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
