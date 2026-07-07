namespace Syncro.CLI.Commands;

public static class ScanCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        string cwd         = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Directory.GetCurrentDirectory();
        int depth          = int.TryParse(GetFlag(args, "--depth"), out int d) ? d : 10;
        bool incremental   = args.Contains("--incremental");
        bool filterSource  = args.Contains("--filter");

        var detection = FrameworkDetector.Detect(cwd);
        Ui.Header($"AST Scan — {detection.Framework} ({detection.Language})");
        Ui.Info($"Path:  {cwd}  |  Depth: {depth}  |  Incremental: {incremental}");

        SyncroDb.EnsureStructure(cwd);

        var excludes = new HashSet<string>(detection.ExcludeDirs, StringComparer.OrdinalIgnoreCase);
        excludes.UnionWith([".git", ".syncro_db", ".vs", ".idea"]);

        int total = 0, parsed = 0;
        var index  = new Dictionary<string, object>();
        var errors = new List<object>();

        var files = WalkFiles(cwd, excludes, depth).ToList();
        total = files.Count;
        Ui.Info($"Found {total} source files.");
        await bridge?.SendProgressAsync(0, 0, total)!;

        for (int i = 0; i < files.Count; i++)
        {
            string file = files[i];
            try
            {
                string rel = Path.GetRelativePath(cwd, file);
                var nodes  = ParseFileNodes(file, detection.Language);
                foreach (var (sym, entry) in nodes)
                    index[sym] = entry;
                parsed++;
            }
            catch (Exception ex)
            {
                errors.Add(new { file, error = ex.Message });
                SyncroDb.AppendError(new { file, error = ex.Message, timestamp = DateTime.UtcNow }, "ast_errors", cwd);
            }

            if (i % 20 == 0)
            {
                int pct = (int)((double)i / total * 100);
                await bridge?.SendProgressAsync(pct, i, total)!;
                Console.Write($"\r  Scanning: {i}/{total} files...   ");
            }
        }

        Console.WriteLine();

        // Save AST index
        string projectId = Path.GetFileName(cwd).ToLower();
        string astDir    = Path.Combine(SyncroDb.Namespace("AST", cwd), projectId);
        Directory.CreateDirectory(astDir);
        File.WriteAllText(Path.Combine(astDir, "ast_index.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(index, Newtonsoft.Json.Formatting.Indented));

        // Update project record
        var projects = SyncroDb.LoadProjects(cwd);
        var proj     = projects.FirstOrDefault(p => p["path"]?.ToString() == cwd);
        if (proj != null)
        {
            proj["lastScanned"]  = DateTime.UtcNow.ToString("o");
            proj["astNodeCount"] = index.Count;
            SyncroDb.SaveProjects(projects, cwd);
        }

        Ui.Ok($"Scan complete. Parsed {parsed}/{total} files. Indexed {index.Count} symbols.");
        if (errors.Count > 0) Ui.Warn($"{errors.Count} parse errors logged to Errors/ast_errors.jsonl");

        await bridge?.SendStatusAsync($"Scan complete: {index.Count} symbols", "success")!;
    }

    private static IEnumerable<string> WalkFiles(string root, HashSet<string> excludes, int maxDepth,
        int currentDepth = 0, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (currentDepth > maxDepth) yield break;

        string realPath;
        try { realPath = Path.GetFullPath(root); } catch { yield break; }
        if (!visited.Add(realPath)) yield break;

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(root); }
        catch { yield break; }

        foreach (var entry in entries)
        {
            FileAttributes attrs;
            try { attrs = File.GetAttributes(entry); } catch { continue; }

            if (attrs.HasFlag(FileAttributes.ReparsePoint)) continue;

            string name = Path.GetFileName(entry);
            if (excludes.Contains(name) || name.StartsWith(".")) continue;

            if (attrs.HasFlag(FileAttributes.Directory))
            {
                foreach (var f in WalkFiles(entry, excludes, maxDepth, currentDepth + 1, visited))
                    yield return f;
            }
            else if (IsSourceFile(name))
            {
                yield return entry;
            }
        }
    }

    private static bool IsSourceFile(string name)
    {
        string[] exts = [".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs",
                         ".java", ".kt", ".dart", ".rb", ".php", ".swift", ".cpp",
                         ".c", ".h", ".vue", ".svelte", ".ex", ".exs"];
        return exts.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, object> ParseFileNodes(string file, string language)
    {
        var result = new Dictionary<string, object>();
        string[] lines = File.ReadAllLines(file);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            string? sym = ExtractSymbol(line, language);
            if (sym == null) continue;

            result[sym] = new
            {
                file = file,
                line = i + 1,
                type = GetNodeType(line, language),
                snippet = line
            };
        }
        return result;
    }

    private static string? ExtractSymbol(string line, string language)
    {
        // C# / Java / Kotlin
        if (line.Contains("class ") || line.Contains("interface ") || line.Contains("record "))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:class|interface|record)\s+(\w+)");
            if (match.Success) return match.Groups[1].Value;
        }
        if (line.Contains("public ") || line.Contains("private ") || line.Contains("protected "))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:public|private|protected|static|async|virtual|override)\s+\S+\s+(\w+)\s*\(");
            if (match.Success) return match.Groups[1].Value;
        }
        // TypeScript / JavaScript
        if (line.StartsWith("export") || line.StartsWith("function") || line.Contains("const ") || line.Contains("async "))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:function|const|class|export\s+(?:default\s+)?(?:function|class|async function))\s+(\w+)");
            if (match.Success) return match.Groups[1].Value;
        }
        // Python
        if (line.StartsWith("def ") || line.StartsWith("class ") || line.StartsWith("async def "))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line,
                @"(?:def|class|async def)\s+(\w+)");
            if (match.Success) return match.Groups[1].Value;
        }
        // Go
        if (line.StartsWith("func "))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"func\s+(?:\(\w+\s+\*?\w+\)\s+)?(\w+)");
            if (match.Success) return match.Groups[1].Value;
        }
        return null;
    }

    private static string GetNodeType(string line, string language) =>
        line.Contains("class") || line.Contains("struct") ? "Class" :
        line.Contains("interface") ? "Interface" :
        line.Contains("enum") ? "Enum" :
        line.Contains("func ") || line.Contains("def ") || line.Contains("function") ? "Method" :
        line.Contains("const ") || line.Contains("let ") || line.Contains("var ") ? "Variable" : "Symbol";

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
