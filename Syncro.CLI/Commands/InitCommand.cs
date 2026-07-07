using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Syncro.CLI.Commands;

public static class InitCommand
{
    public static void Run(string[] args)
    {
        string targetPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
        string? name      = GetFlag(args, "--name");

        string fullPath = Path.GetFullPath(targetPath);

        Ui.Header($"Initializing Syncro Workspace → {fullPath}");

        // Detect existing framework or prompt
        var detection = FrameworkDetector.Detect(fullPath);
        Ui.Info($"Detected: {detection.Language} / {detection.Framework} ({detection.Ecosystem})");

        // Ensure DB structure
        SyncroDb.EnsureStructure(fullPath);
        Ui.Ok("Created .syncro_db/ directory structure.");

        // Build project record
        string projectId = $"{Path.GetFileName(fullPath).ToLower().Replace(" ", "-")}-{DateTime.UtcNow:yyyyMMddHHmm}";
        name ??= Path.GetFileName(fullPath);

        var projectRecord = new JObject
        {
            ["project_id"]          = projectId,
            ["name"]                = name,
            ["path"]                = fullPath,
            ["language"]            = detection.Language,
            ["framework"]           = detection.Framework,
            ["ecosystem"]           = detection.Ecosystem,
            ["packageManager"]      = detection.PackageManager,
            ["devCommand"]          = detection.DevCommand,
            ["buildCommand"]        = detection.BuildCommand,
            ["defaultPort"]         = detection.DefaultPort,
            ["requiresVenv"]        = detection.RequiresVenv,
            ["requiresNodeModules"] = detection.RequiresNodeModules,
            ["excludeDirs"]         = new JArray(detection.ExcludeDirs),
            ["sourceRoots"]         = new JArray(detection.SourceRoots),
            ["groupIds"]            = new JArray(),
            ["databases"]           = new JArray(),
            ["lastScanned"]         = (string?)null,
            ["astNodeCount"]        = 0,
            ["vectorCount"]         = 0,
            ["tags"]                = new JArray(),
            ["created"]             = DateTime.UtcNow.ToString("o")
        };

        // Register project
        var projects = SyncroDb.LoadProjects(fullPath);
        if (projects.All(p => p["path"]?.Value<string>() != fullPath))
        {
            projects.Add(projectRecord);
            SyncroDb.SaveProjects(projects, fullPath);
            Ui.Ok($"Registered project '{name}' (ID: {projectId})");
        }
        else
        {
            Ui.Warn("Project already registered. Updating metadata...");
        }

        // Write a local .syncro config shortcut
        string shortcutFile = Path.Combine(fullPath, ".syncro");
        File.WriteAllText(shortcutFile, JsonConvert.SerializeObject(new
        {
            project_id = projectId,
            name,
            framework = detection.Framework,
            language  = detection.Language
        }, Formatting.Indented));

        Ui.Ok($"Created .syncro config file.");

        // Install dependencies hint
        if (detection.RequiresVenv)
            Ui.Info("Python project detected. Run: syncro run setup-venv");
        if (detection.RequiresNodeModules && !Directory.Exists(Path.Combine(fullPath, "node_modules")))
            Ui.Info("Node.js project — no node_modules found. Run: syncro run install-node");

        Console.WriteLine();
        Ui.Ok($"Workspace ready. Run 'syncro scan' to analyse code and 'syncro run' to start the dev server.");
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
