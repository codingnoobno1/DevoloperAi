namespace Syncro.CLI.Commands;

public static class DeployCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        string? target = GetFlag(args, "--target") ?? args.FirstOrDefault(a => !a.StartsWith("--"));
        bool preview   = args.Contains("--preview");

        string cwd      = Directory.GetCurrentDirectory();
        var detection   = FrameworkDetector.Detect(cwd);

        Ui.Header($"Syncro Deploy — {detection.Framework}");
        Ui.Info($"Target: {target ?? "auto-detect"}  |  Preview: {preview}");

        await bridge?.SendStatusAsync($"Deploying to {target}", "info")!;

        switch (target?.ToLower())
        {
            case "cloudrun":
                await DeployCloudRun(cwd, detection, preview, bridge); break;
            case "vercel":
                await DeployVercel(cwd, preview, bridge); break;
            case "azure":
                Ui.Warn("Azure deploy not yet configured. Set up .syncro_db/deploy.json first."); break;
            default:
                Ui.Warn($"Unknown target '{target}'. Supported: cloudrun, vercel, azure");
                Ui.Info("Configure targets in .syncro_db/deploy.json");
                break;
        }
    }

    private static async Task DeployCloudRun(string cwd,
        FrameworkDetector.DetectionResult det, bool preview, AgentBridge? bridge)
    {
        string cmd = preview
            ? $"echo 'Preview: gcloud run deploy --source {cwd}'"
            : $"gcloud run deploy --source \"{cwd}\" --allow-unauthenticated";

        Ui.Info($"Command: {cmd}");
        await foreach (var line in ShellRunner.StreamPowershell(cmd, cwd, bridge))
            Console.WriteLine(line);
    }

    private static async Task DeployVercel(string cwd, bool preview, AgentBridge? bridge)
    {
        string cmd = preview ? "vercel --preview" : "vercel --prod";
        Ui.Info($"Command: {cmd}");
        await foreach (var line in ShellRunner.StreamPowershell(cmd, cwd, bridge))
            Console.WriteLine(line);
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
