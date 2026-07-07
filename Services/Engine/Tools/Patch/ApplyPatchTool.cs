using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Patch
{
    public class ApplyPatchTool : IMcpTool
    {
        public string Name => "ApplyPatch";
        public string Description => "Modifies a specific file by replacing a search string with a new replacement string. Operates safely without completely overwriting the file.";
        public string InputSchema => "{ \"absolutePath\": \"string\", \"searchTarget\": \"string\", \"replacement\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new 
                { 
                    absolutePath = "", 
                    searchTarget = "", 
                    replacement = "" 
                });

                if (string.IsNullOrEmpty(input?.absolutePath) || !File.Exists(input.absolutePath))
                {
                    return "{ \"error\": \"Target file does not exist.\" }";
                }
                if (string.IsNullOrEmpty(input.searchTarget))
                {
                    return "{ \"error\": \"Search target cannot be empty.\" }";
                }

                string content = await File.ReadAllTextAsync(input.absolutePath);

                if (!content.Contains(input.searchTarget))
                {
                    return "{ \"error\": \"Search target not found in file. Patch failed. Please ensure exact matching (including whitespace/indentation).\" }";
                }

                string patchedContent = content.Replace(input.searchTarget, input.replacement);
                await File.WriteAllTextAsync(input.absolutePath, patchedContent);

                return "{ \"success\": true, \"message\": \"Patch applied successfully.\" }";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
