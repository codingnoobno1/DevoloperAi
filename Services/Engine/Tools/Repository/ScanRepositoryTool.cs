using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Repository
{
    public class ScanRepositoryTool : IMcpTool
    {
        public string Name => "ScanRepository";
        public string Description => "A composite tool that runs DetectProjectType and RepositoryTree to build a holistic macro-level summary of the entire workspace.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"maxDepth\": \"number?\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", maxDepth = 3 });
                if (string.IsNullOrEmpty(input?.workspacePath) || !Directory.Exists(input.workspacePath))
                {
                    return "{ \"error\": \"Invalid workspace path.\" }";
                }

                // Call DetectProjectType
                var detectTool = new DetectProjectTypeTool();
                string projectTypeJson = await detectTool.ExecuteAsync(JsonConvert.SerializeObject(new { workspacePath = input.workspacePath }));
                var projectTypeObj = JObject.Parse(projectTypeJson);

                // Call RepositoryTree
                var treeTool = new RepositoryTreeTool();
                string treeJson = await treeTool.ExecuteAsync(JsonConvert.SerializeObject(new { workspacePath = input.workspacePath, maxDepth = input.maxDepth }));
                var treeObj = JObject.Parse(treeJson);

                var result = new
                {
                    status = "Scanned successfully",
                    language = projectTypeObj["language"]?.ToString(),
                    framework = projectTypeObj["framework"]?.ToString(),
                    confidence = projectTypeObj["confidenceScore"]?.ToObject<int>(),
                    structure = treeObj["tree"]
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
