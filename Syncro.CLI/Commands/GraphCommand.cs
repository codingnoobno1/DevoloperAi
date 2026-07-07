namespace Syncro.CLI.Commands;

public static class GraphCommand
{
    public static void Run(string[] args)
    {
        string format    = GetFlag(args, "--format") ?? "mermaid";
        string? outFile  = GetFlag(args, "--output");
        string? symbol   = GetFlag(args, "--symbol");
        string? groupId  = GetFlag(args, "--group");

        string graphRoot = SyncroDb.Namespace("Graphs");
        if (!Directory.Exists(graphRoot)) { Ui.Warn("No graph data. Run: syncro scan first."); return; }

        if (format == "mermaid")
        {
            string? mermaid = FindGraph(graphRoot, "deps.mermaid");
            if (mermaid == null) { Ui.Warn("No Mermaid graph found. Run: syncro scan"); return; }
            if (outFile != null) { File.WriteAllText(outFile, mermaid); Ui.Ok($"Saved to {outFile}"); }
            else Console.WriteLine(mermaid);
        }
        else if (format == "dot")
        {
            string? dot = FindGraph(graphRoot, "calls.dot");
            if (dot == null) { Ui.Warn("No DOT graph found. Run: syncro scan"); return; }
            if (outFile != null) { File.WriteAllText(outFile, dot); Ui.Ok($"Saved to {outFile}"); }
            else Console.WriteLine(dot);
        }
        else
        {
            // Generate a simple Mermaid graph from AST index
            string mermaid = GenerateFromAst();
            if (outFile != null) { File.WriteAllText(outFile, mermaid); Ui.Ok($"Saved to {outFile}"); }
            else Console.WriteLine(mermaid);
        }
    }

    private static string? FindGraph(string root, string filename)
    {
        foreach (var f in Directory.EnumerateFiles(root, filename, SearchOption.AllDirectories))
            return File.ReadAllText(f);
        return null;
    }

    private static string GenerateFromAst()
    {
        string astRoot = SyncroDb.Namespace("AST");
        if (!Directory.Exists(astRoot)) return "graph TD\n  NoData[No AST data — run syncro scan]";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("graph TD");

        foreach (var indexFile in Directory.EnumerateFiles(astRoot, "ast_index.json", SearchOption.AllDirectories))
        {
            try
            {
                var index = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(indexFile));
                foreach (var (sym, val) in index)
                {
                    string type = val?["type"]?.ToString() ?? "Symbol";
                    string safe = sym.Replace(" ", "_").Replace("-", "_").Replace(".", "_");
                    string shape = type == "Class" ? $"[{sym}]" :
                                   type == "Method" ? $"({sym})" :
                                   type == "Interface" ? $"{{{{  {sym}  }}}}" : $"[{sym}]";
                    sb.AppendLine($"  {safe}{shape}");
                }
            }
            catch { }
        }

        return sb.ToString();
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
