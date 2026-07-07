namespace Syncro.CLI.Commands;

public static class KbCommand
{
    public static void Run(string[] args)
    {
        string sub = args.FirstOrDefault() ?? "list";
        switch (sub)
        {
            case "search": Search(args.Skip(1).ToArray()); break;
            case "add":    Add(args.Skip(1).ToArray()); break;
            case "list":   List(); break;
            case "export": Export(args.Skip(1).ToArray()); break;
            default: Ui.Usage("syncro kb <search|add|list|export>"); break;
        }
    }

    private static void Search(string[] args)
    {
        string query = string.Join(" ", args.Where(a => !a.StartsWith("--")));
        if (string.IsNullOrWhiteSpace(query)) { Ui.Usage("syncro kb search <query>"); return; }

        Ui.Info($"Searching knowledge base: {query}");
        string kbRoot = SyncroDb.Namespace("Templates");
        int found = 0;

        foreach (var file in Directory.EnumerateFiles(
            SyncroDb.Namespace("Memory"), "*.md", SearchOption.AllDirectories)
            .Concat(Directory.Exists(kbRoot)
                ? Directory.EnumerateFiles(kbRoot, "*.json", SearchOption.AllDirectories)
                : []))
        {
            try
            {
                string content = File.ReadAllText(file);
                if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    Ui.Ok(Path.GetRelativePath(SyncroDb.GetDbRoot(), file));
                    found++;
                }
            }
            catch { }
        }
        Ui.Info($"Found {found} KB entries matching '{query}'.");
    }

    private static void Add(string[] args)
    {
        string? file  = GetFlag(args, "--file");
        string? tag   = GetFlag(args, "--tag");
        string? type  = GetFlag(args, "--type") ?? "note";

        if (file == null || !File.Exists(file)) { Ui.Error($"File not found: {file}"); return; }

        string dest = type == "framework"
            ? SyncroDb.Namespace("Frameworks")
            : SyncroDb.Namespace("Memory");

        string destFile = Path.Combine(dest, Path.GetFileName(file));
        File.Copy(file, destFile, overwrite: true);
        Ui.Ok($"Added to KB ({type}): {destFile}");
    }

    private static void List()
    {
        Ui.Header("Knowledge Base Contents");
        foreach (var ns in new[] { "Templates", "Frameworks", "Memory/scripts" })
        {
            string dir = Path.Combine(SyncroDb.GetDbRoot(), ns);
            if (!Directory.Exists(dir)) continue;
            Console.WriteLine($"\n  [{ns}]");
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                Console.WriteLine($"    {Path.GetFileName(f)}");
        }
    }

    private static void Export(string[] args)
    {
        string outFile = GetFlag(args, "--output") ?? "syncro_kb_export.zip";
        string dbRoot  = SyncroDb.GetDbRoot();
        if (!Directory.Exists(dbRoot)) { Ui.Warn("No SyncroDB found."); return; }
        System.IO.Compression.ZipFile.CreateFromDirectory(dbRoot, outFile);
        Ui.Ok($"Exported KB to {outFile}");
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
