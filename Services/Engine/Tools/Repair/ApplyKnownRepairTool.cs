using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Repair
{
    public class ApplyKnownRepairTool : IMcpTool
    {
        public string Name => "ApplyKnownRepair";
        public string Description => "Extracts and applies the patch or script from a stored repair artifact.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"framework\": \"string\", \"errorCode\": \"string\" }";
        public bool RequiresAdminApproval => true; // Modifies codebase directly

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", framework = "", errorCode = "" });
                
                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.framework) || string.IsNullOrEmpty(input?.errorCode))
                {
                    return "{ \"error\": \"workspacePath, framework, and errorCode are required.\" }";
                }

                string repairDir = Path.Combine(input.workspacePath, ".syncro", "repairs", input.framework.ToUpper(), input.errorCode.ToUpper());
                
                if (!Directory.Exists(repairDir))
                {
                    return "{ \"error\": \"Repair artifact not found in knowledge base.\" }";
                }

                // In a true implementation, this would either parse and apply the patch.diff file via LibGit2Sharp
                // OR execute the repair.ps1 script natively.
                // For V1 Engine tools, we simulate the reading and return the exact patch content 
                // so the Orchestrator/PatchMcp can apply it securely.

                string resultStr = "{ \"status\": \"Retrieved\"";

                string patchPath = Path.Combine(repairDir, "patch.diff");
                if (File.Exists(patchPath))
                {
                    string diff = await File.ReadAllTextAsync(patchPath);
                    resultStr += $", \"patchContent\": {JsonConvert.ToString(diff)}";
                }

                string scriptPath = Path.Combine(repairDir, "repair.ps1");
                if (File.Exists(scriptPath))
                {
                    string script = await File.ReadAllTextAsync(scriptPath);
                    resultStr += $", \"scriptContent\": {JsonConvert.ToString(script)}";
                }

                resultStr += " }";

                return resultStr;
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
