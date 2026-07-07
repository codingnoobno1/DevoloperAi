using Newtonsoft.Json.Linq;

namespace Syncro.CLI.Commands;

public static class AstCommand
{
    public static void Run(string[] args)
    {
        string sub = args.FirstOrDefault() ?? "help";
        switch (sub)
        {
            case "find":   Find(args.Skip(1).ToArray()); break;
            case "usages": Usages(args.Skip(1).ToArray()); break;
            case "list":   List(args.Skip(1).ToArray()); break;
            case "diff":   Diff(); break;
            default: Ui.Usage("syncro ast <find|usages|list|diff> [args]"); break;
        }
    }

    private static void Find(string[] args)
    {
        string? symbol = args.FirstOrDefault();
        if (symbol == null) { Ui.Usage("syncro ast find <symbol>"); return; }

        Ui.Info($"Searching AST for: {symbol}");
        string astRoot = SyncroDb.Namespace("AST");
        if (!Directory.Exists(astRoot)) { Ui.Warn("No AST data. Run: syncro scan"); return; }

        bool found = false;
        foreach (var indexFile in Directory.EnumerateFiles(astRoot, "ast_index.json", SearchOption.AllDirectories))
        {
            try
            {
                var index = JObject.Parse(File.ReadAllText(indexFile));
                foreach (var (key, val) in index)
                {
                    if (!key.Contains(symbol, StringComparison.OrdinalIgnoreCase)) continue;
                    string file = val?["file"]?.Value<string>() ?? "?";
                    int    line = val?["line"]?.Value<int>()    ?? 0;
                    string type = val?["type"]?.Value<string>() ?? "?";
                    Ui.Ok($"{type,-12} {key}");
                    Console.WriteLine($"             {file}:{line}");
                    found = true;
                }
            }
            catch { }
        }
        if (!found) Ui.Warn($"Symbol '{symbol}' not found in AST. Re-run: syncro scan");
    }

    private static void Usages(string[] args)
    {
        string? symbol = args.FirstOrDefault();
        if (symbol == null) { Ui.Usage("syncro ast usages <symbol>"); return; }

        Ui.Info($"Finding usages of: {symbol}");
        string cwd = Directory.GetCurrentDirectory();
        var det    = FrameworkDetector.Detect(cwd);
        var excludes = new HashSet<string>(det.ExcludeDirs, StringComparer.OrdinalIgnoreCase);

        int count = 0;
        foreach (var file in Directory.EnumerateFiles(cwd, "*", SearchOption.AllDirectories))
        {
            if (excludes.Any(e => file.Contains(Path.DirectorySeparatorChar + e + Path.DirectorySeparatorChar))) continue;
            if (!IsSourceExt(file)) continue;
            try
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(symbol, StringComparison.Ordinal))
                    {
                        string rel = Path.GetRelativePath(cwd, file);
                        Console.WriteLine($"  {rel}:{i + 1}  {lines[i].Trim()}");
                        count++;
                    }
                }
            }
            catch { }
        }
        Ui.Info($"Found {count} usages.");
    }

    private static void List(string[] args)
    {
        string? typeFilter = GetFlag(args, "--type");
        string? fwFilter   = GetFlag(args, "--framework");

        string astRoot = SyncroDb.Namespace("AST");
        if (!Directory.Exists(astRoot)) { Ui.Warn("No AST data. Run: syncro scan"); return; }

        Ui.Header("AST Node List");
        int count = 0;
        foreach (var indexFile in Directory.EnumerateFiles(astRoot, "ast_index.json", SearchOption.AllDirectories))
        {
            try
            {
                var index = JObject.Parse(File.ReadAllText(indexFile));
                foreach (var (key, val) in index)
                {
                    string type = val?["type"]?.Value<string>() ?? "";
                    if (typeFilter != null && !type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    string file = val?["file"]?.Value<string>() ?? "?";
                    int    line = val?["line"]?.Value<int>() ?? 0;
                    Console.WriteLine($"  {type,-12} {key,-40} {Path.GetFileName(file)}:{line}");
                    count++;
                }
            }
            catch { }
        }
        Ui.Info($"Total: {count} nodes");
    }

    private static void Diff()
    {
        Ui.Info("AST diff between snapshots is not yet implemented.");
        Ui.Info("Re-run 'syncro scan' to refresh the AST index.");
    }

    private static bool IsSourceExt(string f)
    {
        string[] exts = [".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs",
                         ".java", ".kt", ".dart", ".rb", ".php"];
        return exts.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
