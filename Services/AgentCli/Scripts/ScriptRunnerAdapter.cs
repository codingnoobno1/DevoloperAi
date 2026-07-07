using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Scripts;

/// <summary>
/// Runs a stored script: resolves {{placeholders}} from args at run time (secrets never baked in),
/// re-scans for safety, writes to a temp file, and executes via the process executor.
/// </summary>
public class ScriptRunnerAdapter
{
    private readonly ScriptStore _store;
    private readonly IProcessExecutor _exec;
    private readonly ScriptSafetyScanner _scanner;

    public ScriptRunnerAdapter(ScriptStore store, IProcessExecutor exec, ScriptSafetyScanner scanner)
    {
        _store = store;
        _exec = exec;
        _scanner = scanner;
    }

    public async Task<RunResult> RunAsync(ScriptRecord script, IDictionary<string, string> args,
                                          string workingDir, CancellationToken ct = default)
    {
        var body = await _store.ReadBodyAsync(script.Id);
        if (string.IsNullOrEmpty(body))
            return new RunResult(-1, "", "script body not found");

        // Defense in depth: re-scan the resolved body before running.
        foreach (var (k, v) in args) body = body.Replace("{{" + k + "}}", v);
        var scan = _scanner.Scan(body, script.Shell);
        if (!scan.Safe)
            return new RunResult(-1, "", "blocked by safety scanner: " + string.Join("; ", scan.Hits));

        var tmp = Path.Combine(Path.GetTempPath(), $"syncro_{script.Id}{Ext(script.Shell)}");
        await File.WriteAllTextAsync(tmp, body, ct);

        var (file, argLine) = Invocation(script.Shell, tmp);
        var result = await _exec.RunAsync(file, argLine, workingDir, null, ct);

        try { File.Delete(tmp); } catch { /* best effort */ }
        return result;
    }

    private static (string File, string Args) Invocation(string shell, string path) => shell switch
    {
        "sh" => ("bash", $"\"{path}\""),
        "ps1" => ("powershell", $"-ExecutionPolicy Bypass -File \"{path}\""),
        "py" => ("python", $"\"{path}\""),
        _ => ("cmd.exe", $"/c \"{path}\"")
    };

    private static string Ext(string shell) => shell switch
    {
        "sh" => ".sh",
        "ps1" => ".ps1",
        "py" => ".py",
        _ => ".bat"
    };
}
