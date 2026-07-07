using Newtonsoft.Json.Linq;

namespace Syncro.CLI.Commands;

public static class ProjectCommand
{
    public static void Run(string[] args)
    {
        string sub = args.FirstOrDefault() ?? "list";

        switch (sub)
        {
            case "list":   List(args.Skip(1).ToArray()); break;
            case "add":    Add(args.Skip(1).ToArray());  break;
            case "remove": Remove(args.Skip(1).ToArray()); break;
            case "info":   Info(args.Skip(1).ToArray()); break;
            case "group":  Group(args.Skip(1).ToArray()); break;
            default: Ui.Usage("syncro project <list|add|remove|info|group>"); break;
        }
    }

    private static void List(string[] _)
    {
        var projects = SyncroDb.LoadProjects();
        if (projects.Count == 0) { Ui.Info("No projects registered. Run: syncro init"); return; }

        Ui.Header($"Registered Projects ({projects.Count})");
        foreach (var p in projects)
        {
            string id  = p["project_id"]?.Value<string>() ?? "?";
            string name = p["name"]?.Value<string>() ?? "?";
            string fw  = p["framework"]?.Value<string>() ?? "?";
            string lang = p["language"]?.Value<string>() ?? "?";
            string path = p["path"]?.Value<string>() ?? "?";
            Console.WriteLine($"  [{id}]  {name}  ({lang} / {fw})");
            Console.WriteLine($"         {path}");
        }
    }

    private static void Add(string[] args)
    {
        string targetPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? ".";
        string? name      = GetFlag(args, "--name");
        InitCommand.Run([".", $"--name", name ?? Path.GetFileName(Path.GetFullPath(targetPath))]);
    }

    private static void Remove(string[] args)
    {
        string? id = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (id == null) { Ui.Usage("syncro project remove <project_id>"); return; }

        var projects = SyncroDb.LoadProjects();
        int before = projects.Count;
        projects.RemoveAll(p => p["project_id"]?.Value<string>() == id);
        if (projects.Count == before) { Ui.Warn($"Project '{id}' not found."); return; }
        SyncroDb.SaveProjects(projects);
        Ui.Ok($"Removed project '{id}'.");
    }

    private static void Info(string[] args)
    {
        string? id = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (id == null) { Ui.Usage("syncro project info <project_id>"); return; }

        var p = SyncroDb.FindProject(id);
        if (p == null) { Ui.Warn($"Project '{id}' not found."); return; }
        Console.WriteLine(p.ToString(Newtonsoft.Json.Formatting.Indented));
    }

    private static void Group(string[] args)
    {
        if (args.Length < 2) { Ui.Usage("syncro project group <\"Group Name\"> <id1> <id2> ..."); return; }

        string groupName = args[0];
        string[] ids     = args.Skip(1).ToArray();

        var groups  = SyncroDb.LoadGroups();
        string gid  = $"grp-{groupName.ToLower().Replace(" ", "-")}-{DateTime.UtcNow:yyyyMMdd}";

        var projectRefs = ids.Select(id =>
        {
            var p = SyncroDb.FindProject(id);
            return p == null ? null : new JObject
            {
                ["project_id"] = id,
                ["role"]       = p["framework"]?.Value<string>()?.ToLower().Contains("next") == true
                    ? "frontend" : p["framework"]?.Value<string>()?.ToLower() == "mongodb" ? "database" : "api",
                ["framework"]  = p["framework"],
                ["port"]       = p["defaultPort"]
            };
        }).Where(x => x != null).ToList();

        var group = new JObject
        {
            ["group_id"]   = gid,
            ["name"]       = groupName,
            ["archetype"]  = "custom",
            ["projects"]   = new JArray(projectRefs),
            ["startOrder"] = new JArray(ids),
            ["created"]    = DateTime.UtcNow.ToString("o")
        };

        groups.Add(group);
        string groupsFile = Path.Combine(SyncroDb.Namespace("Groups"), "groups.json");
        File.WriteAllText(groupsFile, Newtonsoft.Json.JsonConvert.SerializeObject(groups, Newtonsoft.Json.Formatting.Indented));
        Ui.Ok($"Created group '{groupName}' (ID: {gid}) with {projectRefs.Count} projects.");
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
