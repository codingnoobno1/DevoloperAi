using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Knowledge
{
    public class StoreHindsightTool : IMcpTool
    {
        public string Name => "StoreHindsight";
        public string Description => "Records a design decision, fix, or architectural change to the project's permanent memory bank so future agent sessions remember why something was done.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"decision\": \"string\", \"filesChanged\": \"string[]\", \"impact\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new 
                { 
                    workspacePath = "", 
                    decision = "", 
                    filesChanged = new string[0], 
                    impact = "" 
                });

                if (string.IsNullOrEmpty(input?.workspacePath) || string.IsNullOrEmpty(input?.decision))
                {
                    return "{ \"error\": \"workspacePath and decision are required.\" }";
                }

                string syncroDir = Path.Combine(input.workspacePath, ".syncro");
                if (!Directory.Exists(syncroDir)) Directory.CreateDirectory(syncroDir);

                string dbPath = Path.Combine(syncroDir, "hindsight.json");
                
                var records = new List<object>();
                if (File.Exists(dbPath))
                {
                    string existingData = await File.ReadAllTextAsync(dbPath);
                    if (!string.IsNullOrWhiteSpace(existingData))
                    {
                        records = JsonConvert.DeserializeObject<List<object>>(existingData) ?? new List<object>();
                    }
                }

                var newRecord = new
                {
                    id = Guid.NewGuid().ToString(),
                    timestamp = DateTime.UtcNow.ToString("o"),
                    decision = input.decision,
                    filesChanged = input.filesChanged,
                    impact = input.impact
                };

                records.Add(newRecord);

                await File.WriteAllTextAsync(dbPath, JsonConvert.SerializeObject(records, Formatting.Indented));

                return "{ \"success\": true, \"message\": \"Hindsight recorded permanently.\" }";
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
