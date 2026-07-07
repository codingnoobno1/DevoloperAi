namespace Syncro.CLI.Commands;

public static class TemplateCommand
{
    public static void Run(string[] args)
    {
        string sub = args.FirstOrDefault() ?? "list";
        switch (sub)
        {
            case "list":   List(); break;
            case "search": Search(args.Skip(1).ToArray()); break;
            case "apply":  Apply(args.Skip(1).ToArray()); break;
            default: Ui.Usage("syncro template <list|search|apply> [args]"); break;
        }
    }

    private static void List()
    {
        string tplRoot = SyncroDb.Namespace("Templates");
        if (!Directory.Exists(tplRoot)) { Ui.Warn("No templates found. Run: syncro init first."); return; }

        Ui.Header("Available Templates");
        foreach (var dir in Directory.EnumerateDirectories(tplRoot))
        {
            string name = Path.GetFileName(dir);
            string meta = Path.Combine(dir, "template.json");
            if (File.Exists(meta))
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(meta));
                string desc = obj["description"]?.ToString() ?? "";
                string tags = obj["tags"] is Newtonsoft.Json.Linq.JArray ta
                    ? string.Join(", ", ta.Select(t => t.ToString()))
                    : "";
                Console.WriteLine($"  {name,-25} {desc}");
                if (!string.IsNullOrWhiteSpace(tags))
                    Console.WriteLine($"  {"",25} Tags: {tags}");
            }
            else Console.WriteLine($"  {name}");
        }
    }

    private static void Search(string[] args)
    {
        string query = string.Join(" ", args);
        string tplRoot = SyncroDb.Namespace("Templates");
        if (!Directory.Exists(tplRoot)) { Ui.Warn("No templates found."); return; }

        Ui.Info($"Searching templates for: {query}");
        int found = 0;
        foreach (var meta in Directory.EnumerateFiles(tplRoot, "template.json", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(meta);
            if (content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(content);
                string name = obj["name"]?.ToString() ?? Path.GetDirectoryName(meta) ?? "";
                string desc = obj["description"]?.ToString() ?? "";
                Ui.Ok($"{name} — {desc}");
                found++;
            }
        }
        if (found == 0) Ui.Warn($"No templates match '{query}'.");
    }

    private static void Apply(string[] args)
    {
        string? tplName = args.FirstOrDefault(a => !a.StartsWith("--"));
        string? dest    = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? ".";

        if (tplName == null) { Ui.Usage("syncro template apply <template-name> [destination]"); return; }

        string tplDir = Path.Combine(SyncroDb.Namespace("Templates"), tplName);
        if (!Directory.Exists(tplDir)) { Ui.Error($"Template '{tplName}' not found."); return; }

        string filesDir = Path.Combine(tplDir, "files");
        if (!Directory.Exists(filesDir)) { Ui.Error($"Template '{tplName}' has no files/ directory."); return; }

        string destPath = Path.GetFullPath(dest);
        Directory.CreateDirectory(destPath);

        int count = 0;
        foreach (var file in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
        {
            string rel  = Path.GetRelativePath(filesDir, file);
            string dstF = Path.Combine(destPath, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dstF)!);
            File.Copy(file, dstF, overwrite: true);
            Ui.Ok($"  → {rel}");
            count++;
        }

        Ui.Ok($"\nApplied template '{tplName}' ({count} files) to {destPath}");
    }
}
