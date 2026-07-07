using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class FileSystemMcp : IMcpServer
    {
        public string Name => "File System MCP";
        public string Description => "The lowest-level read/write handler. All disk modifications route through here.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.FileSystem.ReadFileTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.FileSystem.WriteFileTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.FileSystem.DeleteFileTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.FileSystem.GlobSearchTool();
            yield return new Syncro.Desktop.Services.Engine.Tools.FileSystem.WriteFileBatchTool();
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
