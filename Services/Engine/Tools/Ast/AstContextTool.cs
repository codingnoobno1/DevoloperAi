using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AST;
using Syncro.Desktop.Services.AST.Context;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Ast
{
    /// <summary>
    /// Returns a token-budgeted architecture context pack for a project (ast-context-engine.md) — the
    /// richest tier (skeleton → signatures → detail) that fits <c>budgetTokens</c>. Lets the LLM reason
    /// about a codebase's shape for ~1k tokens instead of reading files. Read-only; uses the AST cache.
    /// </summary>
    public sealed class AstContextTool : IMcpTool
    {
        private readonly AstService _ast;
        private readonly ContextPackBuilder _builder = new(new HeuristicTokenCounter());

        public AstContextTool(AstService ast) => _ast = ast;

        public string Name => "AstContextPack";
        public string Description => "Returns a token-budgeted architecture summary of a project (endpoints, symbols, DTOs, deps) — the richest tier that fits the budget. Cheap alternative to reading files.";
        public string InputSchema => "{ \"project\": \"string\", \"budgetTokens\": \"number\", \"focus\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { project = "", budgetTokens = 2000, focus = "" });
            if (string.IsNullOrWhiteSpace(i?.project))
                return JsonConvert.SerializeObject(new { error = "project (path) required" });

            try
            {
                var map = await _ast.ScanProjectAsync(i.project);
                var budget = i.budgetTokens <= 0 ? 2000 : i.budgetTokens;
                var pack = _builder.Fit(new[] { map }, budget, string.IsNullOrWhiteSpace(i.focus) ? null : i.focus);
                var pc = pack.Projects.Count > 0 ? pack.Projects[0] : null;
                return JsonConvert.SerializeObject(new
                {
                    project = map.ProjectName,
                    budget,
                    usedTokens = pack.UsedTokens,
                    tier = pc?.Tier.ToString(),
                    notes = pack.Notes,
                    context = pack.Render(),
                });
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
