using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Planning
{
    public class CreateTaskGraphTool : IMcpTool
    {
        public string Name => "CreateTaskGraph";
        public string Description => "Generates a structured execution plan (task graph) for a complex goal and saves it to the workspace for tracked execution.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"planName\": \"string\", \"tasks\": [ { \"id\": \"string\", \"description\": \"string\", \"dependencies\": [\"string\"] } ] }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new 
                { 
                    workspacePath = "", 
                    planName = "", 
                    tasks = new[] { new { id = "", description = "", dependencies = new string[0] } } 
                });

                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.planName))
                {
                    return "{ \"error\": \"workspacePath and planName are required.\" }";
                }

                string syncroDir = Path.Combine(input.workspacePath, ".syncro", "plans");
                if (!Directory.Exists(syncroDir)) Directory.CreateDirectory(syncroDir);

                string planPath = Path.Combine(syncroDir, $"{input.planName.ToLower().Replace(" ", "_")}.json");
                
                var planObj = new
                {
                    planId = Guid.NewGuid().ToString(),
                    name = input.planName,
                    createdAt = DateTime.UtcNow.ToString("o"),
                    status = "In Progress",
                    tasks = input.tasks
                };

                await File.WriteAllTextAsync(planPath, JsonConvert.SerializeObject(planObj, Formatting.Indented));

                return $"{{ \"success\": true, \"message\": \"Task graph created successfully at {planPath}.\" }}";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
