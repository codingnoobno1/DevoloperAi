using System.Collections.Generic;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    public class AstMcp : IMcpServer
    {
        private readonly IEnumerable<IAstParser> _parsers;

        public AstMcp(IEnumerable<IAstParser> parsers)
        {
            _parsers = parsers;
        }

        public string Name => "AST MCP";
        public string Description => "Parses raw text into syntax trees to extract symbols and logic.";

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new Syncro.Desktop.Services.Engine.Tools.Ast.FindClassesTool(_parsers);
            yield return new Syncro.Desktop.Services.Engine.Tools.Ast.FindFunctionsTool(_parsers);
            yield return new Syncro.Desktop.Services.Engine.Tools.Ast.FindImportsTool(_parsers);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }
}
