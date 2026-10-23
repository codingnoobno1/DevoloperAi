using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// What each registered MCP tool actually does. This replaces the old
    /// <c>RequiresAdminApproval</c> bool as the input to the bypass decision — that flag was
    /// <c>false</c> on WriteFile, DeleteFile, ApplyPatch and Commit even though they change files.
    /// Tools not listed fall back to LocalWrite (asks at the default level), never to Read.
    /// </summary>
    public static class ToolCatalog
    {
        private static readonly Dictionary<string, ActionClass> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            // Read-only
            ["GitStatus"] = ActionClass.Read, ["GitLog"] = ActionClass.Read, ["GitContributors"] = ActionClass.Read,
            ["GitBranches"] = ActionClass.Read, ["GitFetch"] = ActionClass.Read, ["DraftCommitMessage"] = ActionClass.Read,
            ["ReadFile"] = ActionClass.Read, ["GlobSearch"] = ActionClass.Read, ["RepositoryTree"] = ActionClass.Read,
            ["ScanRepository"] = ActionClass.Read, ["DetectProjectType"] = ActionClass.Read, ["ListProjects"] = ActionClass.Read,
            ["WorkspaceSummary"] = ActionClass.Read, ["AnalyzeWorkspace"] = ActionClass.Read, ["FindClasses"] = ActionClass.Read,
            ["FindFunctions"] = ActionClass.Read, ["FindImports"] = ActionClass.Read, ["AstContextPack"] = ActionClass.Read,
            ["ParseSwagger"] = ActionClass.Read, ["ExtractSchema"] = ActionClass.Read, ["ValidatePayload"] = ActionClass.Read,
            ["ValidatePatch"] = ActionClass.Read, ["QueryHindsight"] = ActionClass.Read, ["QueryRepairKnowledge"] = ActionClass.Read,
            ["SecurityScan"] = ActionClass.Read, ["GetProblems"] = ActionClass.Read, ["MapFeature"] = ActionClass.Read,
            ["ExtractFrontendApi"] = ActionClass.Read, ["ResolveBackendContract"] = ActionClass.Read, ["MapContracts"] = ActionClass.Read,
            ["VerifyContract"] = ActionClass.Read, ["UniverseGraphStats"] = ActionClass.Read, ["MapUniverse"] = ActionClass.Read,
            ["RunStatus"] = ActionClass.Read, ["GeneratePipeline"] = ActionClass.Read, ["GenerateBackendForCall"] = ActionClass.Read,
            // Conflict & Cherry-Pick MCP (read side)
            ["list_conflicts"] = ActionClass.Read, ["classify_conflicts"] = ActionClass.Read, ["explain_decision"] = ActionClass.Read,

            // Writes only Syncro's own data (knowledge base, task graph, universe index)
            ["StoreHindsight"] = ActionClass.Internal, ["StoreRepairArtifact"] = ActionClass.Internal,
            ["UpdateArchitecture"] = ActionClass.Internal, ["CreateTaskGraph"] = ActionClass.Internal,
            ["UpdateTaskStatus"] = ActionClass.Internal, ["IndexWorkspace"] = ActionClass.Internal,

            // Changes files in the user's projects
            ["WriteFile"] = ActionClass.LocalWrite, ["WriteFileBatch"] = ActionClass.LocalWrite, ["ApplyPatch"] = ActionClass.LocalWrite,
            ["VerifiedPatch"] = ActionClass.LocalWrite, ["RollbackPatch"] = ActionClass.LocalWrite, ["Commit"] = ActionClass.LocalWrite,
            ["WriteBackendFiles"] = ActionClass.LocalWrite, ["WritePipeline"] = ActionClass.LocalWrite,
            ["ApplyKnownRepair"] = ActionClass.LocalWrite, ["SyncConnectorContract"] = ActionClass.LocalWrite,
            // Conflict & Cherry-Pick MCP (write side): both save a backup ref first, so they are undoable
            ["resolve_conflicts"] = ActionClass.LocalWrite, ["cherry_pick"] = ActionClass.LocalWrite,

            // Runs processes
            ["RunBatchScript"] = ActionClass.Execute, ["BuildProject"] = ActionClass.Execute, ["RunTests"] = ActionClass.Execute,
            ["StartStack"] = ActionClass.Execute, ["StopStack"] = ActionClass.Execute, ["RunGroup"] = ActionClass.Execute,
            ["StopGroup"] = ActionClass.Execute, ["BootstrapWorkspace"] = ActionClass.Execute, ["ReconcileDependencies"] = ActionClass.Execute,
            // Platform job: clone/fetch a task repo into the workspace and check out its branch
            ["SetupTaskWorkspace"] = ActionClass.Execute,

            // Leaves the machine
            ["GitPush"] = ActionClass.Outward, ["CloneRepository"] = ActionClass.Outward, ["FetchEndpoint"] = ActionClass.Outward,

            // Loses data
            ["DeleteFile"] = ActionClass.Destructive, ["GitRollback"] = ActionClass.Destructive,
        };

        public static bool IsKnown(string tool) => Map.ContainsKey(tool);

        /// <summary>Base class for a tool, refined by its arguments (e.g. GitRollback revert vs restore).</summary>
        public static ActionClass Classify(string tool, ToolCallFacts facts)
        {
            if (string.Equals(tool, "GitRollback", StringComparison.OrdinalIgnoreCase)
                && string.Equals(facts.Mode, "revert", StringComparison.OrdinalIgnoreCase))
                return ActionClass.LocalWrite;   // revert makes a new commit — undoable
            return Map.TryGetValue(tool, out var c) ? c : ActionClass.LocalWrite;
        }

        public static ActionClass Classify(string tool) => Map.TryGetValue(tool, out var c) ? c : ActionClass.LocalWrite;

        public static int BasePoints(ActionClass c) => c switch
        {
            ActionClass.Read => 0,
            ActionClass.Internal => 0,
            ActionClass.LocalWrite => 10,
            ActionClass.Execute => 20,
            ActionClass.Outward => 35,
            ActionClass.Destructive => 60,
            _ => 10,
        };

        public static string Label(ActionClass c) => c switch
        {
            ActionClass.Read => "Read",
            ActionClass.Internal => "Internal",
            ActionClass.LocalWrite => "Local write",
            ActionClass.Execute => "Execute",
            ActionClass.Outward => "Outward",
            ActionClass.Destructive => "Destructive",
            _ => c.ToString(),
        };
    }
}
