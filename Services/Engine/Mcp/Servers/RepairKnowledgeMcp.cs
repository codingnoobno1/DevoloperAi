using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Knowledge;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class RepairKnowledgeMcp : IMcpServer
    {
        private readonly RepairDatabaseService _dbService;

        public RepairKnowledgeMcp(RepairDatabaseService dbService)
        {
            _dbService = dbService;
        }

        public string Name => "RepairKnowledgeMcp";
        public string Description => "The Repair OS. Stores and retrieves full engineering artifacts using LiteDB for automated codebase repairs.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Repair.StoreRepairArtifactTool(_dbService);
            yield return new Syncro.Desktop.Services.Engine.Tools.Repair.QueryRepairKnowledgeTool(_dbService);
            yield return new Syncro.Desktop.Services.Engine.Tools.Repair.ApplyKnownRepairTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
