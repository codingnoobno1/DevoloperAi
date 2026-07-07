using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Knowledge
{
    public class QueryHindsightTool : IMcpTool
    {
        public string Name => "QueryHindsight";
        public string Description => "Retrieves the historical record of architectural decisions and fixes made in this workspace.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"limit\": \"number?\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", limit = 10 });
                if (string.IsNullOrEmpty(input?.workspacePath))
                {
                    return "{ \"error\": \"workspacePath is required.\" }";
                }

                string dbPath = Path.Combine(input.workspacePath, ".syncro", "hindsight.json");
                
                if (!File.Exists(dbPath))
                {
                    return "{ \"hindsight\": [], \"message\": \"No historical decisions recorded yet.\" }";
                }

                string content = await File.ReadAllTextAsync(dbPath);
                
                // In a production SQL/Vector setup, we would run a semantic search here.
                // For V1 JSON, we just return the full array (or slice it if limit is provided).
                
                return $"{{ \"hindsight\": {content} }}";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
