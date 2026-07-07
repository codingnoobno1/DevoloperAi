namespace Syncro.CLI.Commands;

public static class DoctorCommand
{
    private record Tool(string Name, string VersionArg, string WingetId, bool Required);

    private static readonly Tool[] Tools =
    [
        new("git",      "--version", "Git.Git",                true),
        new("node",     "--version", "OpenJS.NodeJS",          false),
        new("npm",      "--version", "",                       false),
        new("python",   "--version", "Python.Python.3",        false),
        new("pip",      "--version", "",                       false),
        new("dotnet",   "--version", "Microsoft.DotNet.SDK.9", false),
        new("flutter",  "--version", "",                       false),
        new("go",       "version",   "",                       false),
        new("cargo",    "--version", "",                       false),
        new("rustc",    "--version", "",                       false),
        new("java",     "--version", "",                       false),
        new("docker",   "--version", "Docker.DockerDesktop",   false),
        new("mvn",      "--version", "",                       false),
        new("gradle",   "--version", "",                       false),
    ];

    public static void Run(string[] args)
    {
        bool autoFix = args.Contains("--fix");
        bool jsonOut = args.Contains("--json");

        Ui.Header("Syncro Environment Health Check");

        int found = 0, missing = 0;

        foreach (var tool in Tools)
        {
            var (ok, version) = ShellRunner.CheckTool(tool.Name, tool.VersionArg);

            if (jsonOut)
            {
                string status = ok ? "found" : "missing";
                Console.WriteLine($"{{\"tool\":\"{tool.Name}\",\"status\":\"{status}\",\"version\":\"{version}\"}}");
                continue;
            }

            if (ok)
            {
                Ui.Ok($"{tool.Name,-14} {version.Split('\n')[0].Trim()}");
                found++;
            }
            else
            {
                string label = tool.Required ? "[REQUIRED]" : "[OPTIONAL]";
                Ui.Warn($"{tool.Name,-14} Not found {label}");
                missing++;

                if (autoFix && !string.IsNullOrWhiteSpace(tool.WingetId))
                {
                    Ui.Info($"  → Attempting: winget install {tool.WingetId}");
                    var result = ShellRunner.RunPowershell($"winget install -e --id {tool.WingetId} --silent");
                    Console.WriteLine(result.Success ? "     ✅ Installed" : $"     ❌ {result.Stderr}");
                }
            }
        }

        if (!jsonOut)
        {
            Console.WriteLine();
            Ui.Info($"Found: {found}  Missing: {missing}");
            if (missing > 0 && !autoFix)
                Ui.Info("Run 'syncro doctor --fix' to auto-install missing tools via winget.");
        }
    }
}
