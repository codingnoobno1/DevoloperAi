using Newtonsoft.Json.Linq;

namespace Syncro.CLI.Commands;

public static class MemoryCommand
{
    public static void Run(string[] args)
    {
        string sub = args.FirstOrDefault() ?? "list";
        switch (sub)
        {
            case "list":    List(args.Skip(1).ToArray()); break;
            case "view":    View(args.Skip(1).ToArray()); break;
            case "purge":   Purge(args.Skip(1).ToArray()); break;
            case "compact": Compact(); break;
            case "export":  Export(args.Skip(1).ToArray()); break;
            default: Ui.Usage("syncro memory <list|view|purge|compact|export>"); break;
        }
    }

    private static void List(string[] args)
    {
        string? statusFilter = GetFlag(args, "--status");
        string? fwFilter     = GetFlag(args, "--framework");
        int limit            = int.TryParse(GetFlag(args, "--last"), out int l) ? l : 20;

        var records = SyncroDb.ReadMemory()
            .Where(r => statusFilter == null || MatchStatus(r, statusFilter))
            .Where(r => fwFilter == null || r["target_framework"]?.Value<string>()
                ?.Equals(fwFilter, StringComparison.OrdinalIgnoreCase) == true)
            .TakeLast(limit)
            .ToList();

        if (records.Count == 0) { Ui.Info("No memory records found."); return; }

        Ui.Header($"Agent Memory ({records.Count} records)");
        foreach (var r in records)
        {
            string id      = r["memory_id"]?.Value<string>() ?? "?";
            string prompt  = r["prompt"]?.Value<string>() ?? "?";
            bool? success  = r["success"]?.Value<bool?>();
            string fw      = r["target_framework"]?.Value<string>() ?? "?";
            string ts      = r["timestamp"]?.Value<string>() ?? "?";
            string icon    = success == true ? "✅" : success == false ? "❌" : "⏳";

            if (prompt.Length > 70) prompt = prompt[..67] + "...";
            Console.WriteLine($"  {icon} [{id}] {prompt}");
            Console.WriteLine($"       {fw} | {ts}");
        }
    }

    private static void View(string[] args)
    {
        string? id = args.FirstOrDefault();
        if (id == null) { Ui.Usage("syncro memory view <memory_id>"); return; }

        var record = SyncroDb.ReadMemory().FirstOrDefault(r =>
            r["memory_id"]?.Value<string>() == id);
        if (record == null) { Ui.Warn($"Memory record '{id}' not found."); return; }
        Console.WriteLine(record.ToString(Newtonsoft.Json.Formatting.Indented));
    }

    private static void Purge(string[] args)
    {
        string? statusFilter = GetFlag(args, "--status");
        bool hard = args.Contains("--hard");
        string? olderThan = GetFlag(args, "--older-than");

        DateTime? cutoff = null;
        if (olderThan != null)
        {
            int days = int.Parse(olderThan.Replace("d", "").Trim());
            cutoff = DateTime.UtcNow.AddDays(-days);
        }

        int count = 0;
        string memFile = Path.Combine(SyncroDb.Namespace("Memory"), "memory.jsonl");
        if (!File.Exists(memFile)) { Ui.Info("Memory store is empty."); return; }

        var lines = File.ReadAllLines(memFile).ToList();
        var kept  = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JObject? r; try { r = JObject.Parse(line); } catch { kept.Add(line); continue; }

            bool matchStatus = statusFilter == null || MatchStatus(r, statusFilter);
            bool matchTime   = cutoff == null || (
                DateTime.TryParse(r["timestamp"]?.Value<string>(), out var ts) && ts < cutoff);

            if (matchStatus && matchTime)
            {
                count++;
                if (!hard)
                {
                    r["pruned"] = true;
                    kept.Add(r.ToString(Newtonsoft.Json.Formatting.None));
                }
                // hard: don't add to kept
            }
            else kept.Add(line);
        }

        File.WriteAllLines(memFile, kept);
        Ui.Ok($"Purged {count} records ({(hard ? "hard delete" : "soft marked pruned")}).");
    }

    private static void Compact()
    {
        string memFile = Path.Combine(SyncroDb.Namespace("Memory"), "memory.jsonl");
        if (!File.Exists(memFile)) { Ui.Info("Nothing to compact."); return; }

        var lines = File.ReadAllLines(memFile)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Where(l => {
                try { var o = JObject.Parse(l); return o["pruned"]?.Value<bool>() != true; }
                catch { return false; }
            }).ToList();

        File.WriteAllLines(memFile, lines);
        Ui.Ok($"Compacted memory.jsonl — {lines.Count} active records retained.");
    }

    private static void Export(string[] args)
    {
        string outFile = GetFlag(args, "--output") ?? "memory_export.jsonl";
        string memFile = Path.Combine(SyncroDb.Namespace("Memory"), "memory.jsonl");
        if (!File.Exists(memFile)) { Ui.Warn("Memory store is empty."); return; }
        File.Copy(memFile, outFile, overwrite: true);
        Ui.Ok($"Exported memory to {outFile}");
    }

    private static bool MatchStatus(JObject r, string status) =>
        status switch
        {
            "success" => r["success"]?.Value<bool?>() == true,
            "failed"  => r["success"]?.Value<bool?>() == false,
            "pending" => r["success"]?.Value<bool?>() == null,
            _ => true
        };

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
