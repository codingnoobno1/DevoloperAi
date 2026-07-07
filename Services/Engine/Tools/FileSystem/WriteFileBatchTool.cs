using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    /// <summary>
    /// Creates, edits, or deletes any number of files in one call, scoped to <c>rootPath</c>.
    /// All-or-nothing: if any operation fails, every change already applied in the batch is
    /// rolled back. Use this instead of repeated WriteFile/DeleteFile calls whenever more than
    /// one file is involved (scaffolding, restructuring, multi-file edits) — it costs one
    /// tool-call round-trip instead of N.
    /// </summary>
    public class WriteFileBatchTool : IMcpTool
    {
        public string Name => "WriteFileBatch";

        public string Description =>
            "Creates, edits, or deletes multiple files in a single atomic call, scoped to rootPath " +
            "(no path may escape it). If any operation fails the entire batch is rolled back. " +
            "Prefer this over WriteFile/DeleteFile whenever more than one file needs to change.";

        public string InputSchema =>
            "{ \"rootPath\": \"string\", \"operations\": [ { \"type\": \"create|modify|delete\", " +
            "\"path\": \"string (relative to rootPath)\", \"content\": \"string?\" } ] }";

        public bool RequiresAdminApproval => true;

        private sealed class OpInput
        {
            public string Type { get; set; } = "";
            public string Path { get; set; } = "";
            public string? Content { get; set; }
        }

        private sealed class BatchInput
        {
            public string RootPath { get; set; } = "";
            public List<OpInput> Operations { get; set; } = new();
        }

        public Task<string> ExecuteAsync(string inputJson)
        {
            return Task.Run(() =>
            {
                try
                {
                    var input = JsonConvert.DeserializeObject<BatchInput>(inputJson);
                    if (string.IsNullOrWhiteSpace(input?.RootPath))
                        return JsonConvert.SerializeObject(new { error = "rootPath is required." });

                    if (input.Operations == null || input.Operations.Count == 0)
                        return JsonConvert.SerializeObject(new { error = "operations must contain at least one entry." });

                    var ops = new List<BatchFileOp>();
                    foreach (var o in input.Operations)
                    {
                        if (!TryParseType(o.Type, out var type))
                            return JsonConvert.SerializeObject(new { error = $"Unknown operation type '{o.Type}'. Use create, modify, or delete." });

                        ops.Add(new BatchFileOp { Type = type, Path = o.Path, Content = o.Content });
                    }

                    var result = BatchFileOperations.Apply(input.RootPath, ops);

                    return JsonConvert.SerializeObject(new
                    {
                        success = result.Success,
                        failureReason = result.FailureReason,
                        appliedCount = result.Applied.Count,
                        applied = result.Applied.Select(a => new { a.Path, type = a.Type.ToString().ToLowerInvariant(), a.Success })
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { error = ex.Message });
                }
            });
        }

        private static bool TryParseType(string raw, out BatchOpType type)
        {
            switch ((raw ?? "").Trim().ToLowerInvariant())
            {
                case "create": type = BatchOpType.Create; return true;
                case "modify": case "edit": case "update": type = BatchOpType.Modify; return true;
                case "delete": case "remove": type = BatchOpType.Delete; return true;
                default: type = default; return false;
            }
        }
    }
}
