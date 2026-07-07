using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Mcp
{
    /// <summary>
    /// Base contract for all MCP Servers (Workspace, Git, FileSystem, etc.).
    /// Each server registers its own deterministic tools with the Orchestrator.
    /// </summary>
    public interface IMcpServer
    {
        /// <summary>
        /// The name of the MCP server (e.g., "GitMcp").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of the server's capabilities for context building.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Returns all tools managed by this server.
        /// </summary>
        IEnumerable<IMcpTool> GetTools();

        /// <summary>
        /// Optional initialization logic (e.g., connecting to SQLite or verifying Git CLI).
        /// </summary>
        Task InitializeAsync();
    }
}
