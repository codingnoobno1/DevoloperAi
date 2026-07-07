using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;
using Syncro.Desktop.Services.Engine.Tools.FileSystem;

namespace Syncro.Desktop.Services.Connector.Mcp
{
    /// <summary>
    /// Backend-scoped twin of <see cref="WriteFileBatchTool"/>: creates/edits/deletes any number
    /// of files under a backend repo root in one atomic call, rolling back the whole batch if any
    /// operation fails. This is the "apply" half of the Connector's parallel backend generation
    /// (see connector.md, GenerateBackendForCall) — once routes are AI-generated for unmatched
    /// frontend calls, this tool writes the resulting folder structure in a single round-trip
    /// instead of one call per file. Creates <c>backendPath</c> itself if it doesn't exist yet,
    /// so it also covers scaffolding a brand-new backend from scratch.
    /// </summary>
    public sealed class WriteBackendFilesTool : IMcpTool
    {
        public string Name => "WriteBackendFiles";

        public string Description =>
            "Creates, edits, or deletes multiple files under a backend repository root in a single " +
            "atomic call. Creates the backend folder itself if it doesn't exist yet. If any file " +
            "operation fails, the entire batch is rolled back. Use this when scaffolding or " +
            "extending a backend (e.g. adding routes generated to match frontend API calls) instead " +
            "of writing files one at a time.";

        public string InputSchema =>
            "{ \"backendPath\": \"string\", \"files\": [ { \"action\": \"create|modify|delete\", " +
            "\"path\": \"string (relative to backendPath)\", \"content\": \"string?\" } ] }";

        public bool RequiresAdminApproval => true;

        private sealed class FileInput
        {
            public string Action { get; set; } = "";
            public string Path { get; set; } = "";
            public string? Content { get; set; }
        }

        private sealed class BackendBatchInput
        {
            public string BackendPath { get; set; } = "";
            public List<FileInput> Files { get; set; } = new();
        }

        public Task<string> ExecuteAsync(string inputJson)
        {
            return Task.Run(() =>
            {
                try
                {
                    var input = JsonConvert.DeserializeObject<BackendBatchInput>(inputJson);
                    if (string.IsNullOrWhiteSpace(input?.BackendPath))
                        return JsonConvert.SerializeObject(new { error = "backendPath is required." });

                    if (input.Files == null || input.Files.Count == 0)
                        return JsonConvert.SerializeObject(new { error = "files must contain at least one entry." });

                    // Scaffolding a brand-new backend is a valid call — create the root if missing.
                    if (!Directory.Exists(input.BackendPath))
                        Directory.CreateDirectory(input.BackendPath);

                    var ops = new List<BatchFileOp>();
                    foreach (var f in input.Files)
                    {
                        if (!TryParseAction(f.Action, out var type))
                            return JsonConvert.SerializeObject(new { error = $"Unknown action '{f.Action}'. Use create, modify, or delete." });

                        ops.Add(new BatchFileOp { Type = type, Path = f.Path, Content = f.Content });
                    }

                    var result = BatchFileOperations.Apply(input.BackendPath, ops);

                    return JsonConvert.SerializeObject(new
                    {
                        success = result.Success,
                        failureReason = result.FailureReason,
                        backendPath = input.BackendPath,
                        appliedCount = result.Applied.Count,
                        applied = result.Applied.Select(a => new { a.Path, action = a.Type.ToString().ToLowerInvariant(), a.Success })
                    });
                }
                catch (Exception ex)
                {
                    return JsonConvert.SerializeObject(new { error = ex.Message });
                }
            });
        }

        private static bool TryParseAction(string raw, out BatchOpType type)
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
