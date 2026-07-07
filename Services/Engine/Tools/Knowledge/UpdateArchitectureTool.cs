using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Knowledge
{
    public class UpdateArchitectureTool : IMcpTool
    {
        public string Name => "UpdateArchitecture";
        public string Description => "Updates or creates a core architectural guideline markdown file so all future AI sessions strictly adhere to design constraints.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"componentName\": \"string\", \"constraints\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new 
                { 
                    workspacePath = "", 
                    componentName = "", 
                    constraints = "" 
                });

                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.componentName))
                {
                    return "{ \"error\": \"workspacePath and componentName are required.\" }";
                }

                string syncroDir = Path.Combine(input.workspacePath, ".syncro", "architecture");
                if (!Directory.Exists(syncroDir)) Directory.CreateDirectory(syncroDir);

                string filePath = Path.Combine(syncroDir, $"{input.componentName.ToLower().Replace(" ", "_")}.md");
                
                string content = $"# {input.componentName} Architecture Constraints\n\n{input.constraints}\n\n*Updated: {DateTime.UtcNow:o}*";
                await File.WriteAllTextAsync(filePath, content);

                return $"{{ \"success\": true, \"message\": \"Architecture constraint saved to {filePath}.\" }}";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
