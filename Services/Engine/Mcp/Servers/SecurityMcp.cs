using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Security;

namespace Syncro.Desktop.Services.Engine.Mcp.Servers
{
    /// <summary>MCP server exposing the native SAST scanner to the LLM / editor chat.</summary>
    public sealed class SecurityMcp : IMcpServer
    {
        public string Name => "Security MCP";
        public string Description => "Static security analysis (secrets, weak crypto, injection, insecure TLS) — native, no external tools.";

        private readonly SecurityScanner _scanner = new();

        public IEnumerable<IMcpTool> GetTools()
        {
            yield return new SecurityScanTool(_scanner);
        }

        public Task InitializeAsync() => Task.CompletedTask;
    }

    /// <summary>Scan a folder for security issues. Read-only.</summary>
    public sealed class SecurityScanTool : IMcpTool
    {
        private readonly SecurityScanner _scanner;
        public SecurityScanTool(SecurityScanner scanner) => _scanner = scanner;

        public string Name => "SecurityScan";
        public string Description => "Runs static security analysis over a folder and returns findings (rule, severity, file:line) plus a pass/fail gate. Read-only.";
        public string InputSchema => "{ \"root\": \"string\", \"failOn\": \"Info|Low|Medium|High|Critical\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            var i = JsonConvert.DeserializeAnonymousType(inputJson, new { root = "", failOn = "High" });
            if (string.IsNullOrWhiteSpace(i?.root))
                return JsonConvert.SerializeObject(new { error = "root required" });

            var failOn = Enum.TryParse<Severity>(i.failOn, true, out var s) ? s : Severity.High;
            var r = await _scanner.ScanAsync(i.root, failOn);

            return JsonConvert.SerializeObject(new
            {
                root = r.Root,
                filesScanned = r.FilesScanned,
                gate = r.GatePassed ? "PASS" : "FAIL",
                summary = r.Summary,
                counts = r.Counts.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                findings = r.Findings.Take(200).Select(f => new
                {
                    f.RuleId, f.Title, severity = f.Severity.ToString(), f.Category, f.File, f.Line, f.Snippet, f.Why
                })
            });
        }
    }
}
