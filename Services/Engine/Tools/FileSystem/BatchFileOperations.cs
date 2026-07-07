using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    public enum BatchOpType { Create, Modify, Delete }

    public sealed class BatchFileOp
    {
        public BatchOpType Type { get; set; }

        /// <summary>Path relative to the batch root (an absolute path under root is also accepted).</summary>
        public string Path { get; set; } = "";

        /// <summary>Required for Create/Modify; ignored for Delete.</summary>
        public string? Content { get; set; }
    }

    public sealed class BatchOpResult
    {
        public string Path { get; set; } = "";
        public BatchOpType Type { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public sealed class BatchFileResult
    {
        public bool Success { get; set; }
        public List<BatchOpResult> Applied { get; set; } = new();
        public string? FailureReason { get; set; }
    }

    /// <summary>
    /// Applies a list of create/modify/delete operations under one root as a single unit:
    /// every path is validated (no traversal escape) before anything touches disk, and if any
    /// operation throws mid-batch, everything already applied is rolled back to its prior state.
    /// This is the engine behind both <c>WriteFileBatchTool</c> (general) and the Connector's
    /// backend-scoped batch tool — the LLM gets one call for a whole folder structure instead of
    /// one round-trip per file, with the same all-or-nothing guarantee as a single file write.
    /// </summary>
    public static class BatchFileOperations
    {
        public static BatchFileResult Apply(string rootPath, IReadOnlyList<BatchFileOp> ops)
        {
            if (ops == null || ops.Count == 0)
                return new BatchFileResult { Success = false, FailureReason = "No operations supplied." };

            // 1. Validate every path up front — reject the whole batch before touching disk.
            var resolved = new List<(BatchFileOp op, string fullPath)>();
            foreach (var op in ops)
            {
                if (!PathGuard.TryResolveWithin(rootPath, op.Path, out var fullPath, out var err))
                    return new BatchFileResult { Success = false, FailureReason = $"Rejected: {err}" };

                if (op.Type != BatchOpType.Delete && op.Content == null)
                    return new BatchFileResult { Success = false, FailureReason = $"'{op.Path}': content is required for {op.Type}." };

                resolved.Add((op, fullPath));
            }

            // 2. Apply in order, snapshotting the prior state of each file before mutating it.
            var snapshots = new List<(string fullPath, string? originalContent)>();
            var applied = new List<BatchOpResult>();

            try
            {
                foreach (var (op, fullPath) in resolved)
                {
                    snapshots.Add((fullPath, File.Exists(fullPath) ? File.ReadAllText(fullPath) : null));

                    switch (op.Type)
                    {
                        case BatchOpType.Delete:
                            if (File.Exists(fullPath))
                                File.Delete(fullPath);
                            break;

                        case BatchOpType.Create:
                        case BatchOpType.Modify:
                            var dir = Path.GetDirectoryName(fullPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);
                            File.WriteAllText(fullPath, op.Content ?? "");
                            break;
                    }

                    applied.Add(new BatchOpResult { Path = op.Path, Type = op.Type, Success = true });
                }

                return new BatchFileResult { Success = true, Applied = applied };
            }
            catch (Exception ex)
            {
                RollBack(snapshots);
                return new BatchFileResult
                {
                    Success = false,
                    Applied = applied,
                    FailureReason = $"Operation failed, batch rolled back: {ex.Message}"
                };
            }
        }

        private static void RollBack(List<(string fullPath, string? originalContent)> snapshots)
        {
            foreach (var (fullPath, originalContent) in snapshots.AsEnumerable().Reverse())
            {
                try
                {
                    if (originalContent == null)
                    {
                        if (File.Exists(fullPath))
                            File.Delete(fullPath);
                    }
                    else
                    {
                        File.WriteAllText(fullPath, originalContent);
                    }
                }
                catch
                {
                    // Best-effort rollback — a failure here means a single file is left in the
                    // post-attempt state; the caller still receives Success = false either way.
                }
            }
        }
    }
}
