using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Engine.Knowledge;

namespace Syncro.Desktop.Services.Engine.Tools.Repair
{
    public class QueryRepairKnowledgeTool : IMcpTool
    {
        private readonly RepairDatabaseService _dbService;

        public QueryRepairKnowledgeTool(RepairDatabaseService dbService)
        {
            _dbService = dbService;
        }

        public string Name => "QueryRepairKnowledge";
        public string Description => "Searches the local workspace LiteDB for a known fix matching the framework and error symptoms.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"framework\": \"string\", \"errorCode\": \"string?\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", framework = "", errorCode = "" });
                
                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.framework))
                {
                    return "{ \"error\": \"workspacePath and framework are required.\" }";
                }

                var matches = new List<object>();

                // Fast LiteDB Query
                using (var db = _dbService.OpenWorkspaceDb(input.workspacePath))
                {
                    var repairs = db.GetCollection<RepairIndexModel>("Repairs");
                    
                    IEnumerable<RepairIndexModel> queryResult;

                    if (!string.IsNullOrEmpty(input.errorCode))
                    {
                        queryResult = repairs.Find(x => x.Framework == input.framework.ToUpper() && x.ErrorCode == input.errorCode.ToUpper());
                    }
                    else
                    {
                        queryResult = repairs.Find(x => x.Framework == input.framework.ToUpper());
                    }

                    foreach (var record in queryResult)
                    {
                        var repairData = await LoadRepairDataAsync(record.RepairDir);
                        if (repairData != null) matches.Add(repairData);
                    }
                }

                var result = new { totalFound = matches.Count, matches = matches };
                return JsonConvert.SerializeObject(result, Formatting.Indented);
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }

        private async Task<object?> LoadRepairDataAsync(string repairDir)
        {
            string jsonPath = Path.Combine(repairDir, "repair.json");
            if (!File.Exists(jsonPath)) return null;

            try
            {
                string content = await File.ReadAllTextAsync(jsonPath);
                JObject metadata = JObject.Parse(content);
                
                return new
                {
                    directoryName = new DirectoryInfo(repairDir).Name,
                    metadata = metadata,
                    hasPatch = File.Exists(Path.Combine(repairDir, "patch.diff")),
                    hasScript = File.Exists(Path.Combine(repairDir, "repair.ps1"))
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
