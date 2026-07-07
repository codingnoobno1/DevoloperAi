using Newtonsoft.Json.Linq;

namespace Syncro.CLI.Commands;

public static class StatusCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        string? msg   = GetFlag(args, "--msg");
        string? level = GetFlag(args, "--level") ?? "info";
        string? taskId = GetFlag(args, "--task");

        if (taskId == "current")
        {
            // Show last few memory or agent records
            var last = SyncroDb.ReadMemory().TakeLast(5).ToList();
            Ui.Header("Recent CLI Tasks");
            foreach (var r in last)
            {
                bool? s = r["success"]?.Value<bool?>();
                string icon = s == true ? "✅" : s == false ? "❌" : "⏳";
                string prompt = (r["prompt"]?.Value<string>() ?? "?").Take(60).Join();
                Console.WriteLine($"  {icon} {prompt}");
            }
            return;
        }

        if (msg == null)
        {
            // Print current workspace status
            string cwd = Directory.GetCurrentDirectory();
            var detection = FrameworkDetector.Detect(cwd);
            var projects  = SyncroDb.LoadProjects();

            Ui.Header("Syncro Workspace Status");
            Ui.Info($"Directory : {cwd}");
            Ui.Info($"Framework : {detection.Framework} ({detection.Language})");
            Ui.Info($"Projects  : {projects.Count} registered");

            string dbRoot = SyncroDb.GetDbRoot();
            Ui.Info($"DB Root   : {dbRoot}");
            Ui.Info($"DB Exists : {Directory.Exists(dbRoot)}");
            return;
        }

        // Push status to Desktop UI
        if (bridge != null && bridge.IsConnected)
        {
            await bridge.SendStatusAsync(msg, level, taskId);
            Ui.Ok($"Status sent to Desktop: [{level}] {msg}");
        }
        else
        {
            Ui.Warn("Desktop not connected. Status not pushed.");
            Ui.Info($"Message: [{level}] {msg}");
        }
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

// Small LINQ helper
file static class LinqExt
{
    public static string Join(this IEnumerable<char> chars) => new(chars.ToArray());
}
