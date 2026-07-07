using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Engine.Knowledge;

namespace Syncro.Desktop.Services.Engine.Tools.Repair
{
    public class StoreRepairArtifactTool : IMcpTool
    {
        private readonly RepairDatabaseService _dbService;

        public StoreRepairArtifactTool(RepairDatabaseService dbService)
        {
            _dbService = dbService;
        }

        public string Name => "StoreRepairArtifact";
        public string Description => "Stores a complete engineering artifact for a successful repair and indexes it in LiteDB.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"framework\": \"string\", \"errorCode\": \"string\", \"metadata\": \"object\", \"patchDiff\": \"string?\", \"scriptContent\": \"string?\", \"llmContext\": \"object?\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                dynamic? input = JsonConvert.DeserializeObject(inputJson);
                if (input == null)
                {
                    return "{ \"error\": \"Input JSON was null or invalid.\" }";
                }
                
                string workspacePath = input.workspacePath;
                string framework = input.framework;
                string errorCode = input.errorCode;

                if (string.IsNullOrEmpty(workspacePath) || string.IsNullOrEmpty(framework) || string.IsNullOrEmpty(errorCode))
                {
                    return "{ \"error\": \"workspacePath, framework, and errorCode are required.\" }";
                }

                // .syncro/repairs/{FRAMEWORK}/{ERROR_CODE}/
                string repairDir = Path.Combine(workspacePath, ".syncro", "repairs", framework.ToUpper(), errorCode.ToUpper());
                if (!Directory.Exists(repairDir)) Directory.CreateDirectory(repairDir);

                bool hasPatch = false;
                bool hasScript = false;

                // 1. repair.json (metadata)
                if (input.metadata != null)
                {
                    string metadataPath = Path.Combine(repairDir, "repair.json");
                    await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(input.metadata, Formatting.Indented));
                }

                // 2. patch.diff
                if (input.patchDiff != null)
                {
                    string patchPath = Path.Combine(repairDir, "patch.diff");
                    await File.WriteAllTextAsync(patchPath, (string)input.patchDiff);
                    hasPatch = true;
                }

                // 3. repair.ps1
                if (input.scriptContent != null)
                {
                    string scriptPath = Path.Combine(repairDir, "repair.ps1");
                    await File.WriteAllTextAsync(scriptPath, (string)input.scriptContent);
                    hasScript = true;
                }

                // 4. llm_context.json
                if (input.llmContext != null)
                {
                    string llmPath = Path.Combine(repairDir, "llm_context.json");
                    await File.WriteAllTextAsync(llmPath, JsonConvert.SerializeObject(input.llmContext, Formatting.Indented));
                }

                // 5. Index in LiteDB
                using (var db = _dbService.OpenWorkspaceDb(workspacePath))
                {
                    var repairs = db.GetCollection<RepairIndexModel>("Repairs");
                    
                    var existing = repairs.FindOne(x => x.Framework == framework.ToUpper() && x.ErrorCode == errorCode.ToUpper());
                    if (existing != null)
                    {
                        existing.HasPatch = hasPatch;
                        existing.HasScript = hasScript;
                        existing.RepairDir = repairDir;
                        repairs.Update(existing);
                    }
                    else
                    {
                        repairs.Insert(new RepairIndexModel
                        {
                            Framework = framework.ToUpper(),
                            ErrorCode = errorCode.ToUpper(),
                            RepairDir = repairDir,
                            CreatedAt = DateTime.UtcNow,
                            HasPatch = hasPatch,
                            HasScript = hasScript
                        });
                    }
                }

                return $"{{ \"success\": true, \"message\": \"Repair artifact stored and indexed in LiteDB successfully at {repairDir}.\" }}";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
