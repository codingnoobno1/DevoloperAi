using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Planning
{
    public class UpdateTaskStatusTool : IMcpTool
    {
        public string Name => "UpdateTaskStatus";
        public string Description => "Updates the status of a specific task within an active execution plan.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"planName\": \"string\", \"taskId\": \"string\", \"status\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new 
                { 
                    workspacePath = "", 
                    planName = "", 
                    taskId = "", 
                    status = "" 
                });

                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.planName) || string.IsNullOrEmpty(input?.taskId))
                {
                    return "{ \"error\": \"workspacePath, planName, and taskId are required.\" }";
                }

                string planPath = Path.Combine(input.workspacePath, ".syncro", "plans", $"{input.planName.ToLower().Replace(" ", "_")}.json");
                
                if (!File.Exists(planPath))
                {
                    return "{ \"error\": \"Plan file not found.\" }";
                }

                string content = await File.ReadAllTextAsync(planPath);
                JObject plan = JObject.Parse(content);

                var tasks = plan["tasks"] as JArray;
                if (tasks != null)
                {
                    var task = tasks.FirstOrDefault(t => t["id"]?.ToString() == input.taskId);
                    if (task != null)
                    {
                        task["status"] = input.status;
                        task["updatedAt"] = DateTime.UtcNow.ToString("o");
                        await File.WriteAllTextAsync(planPath, plan.ToString(Formatting.Indented));
                        return "{ \"success\": true, \"message\": \"Task status updated.\" }";
                    }
                }

                return "{ \"error\": \"Task ID not found in plan.\" }";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
