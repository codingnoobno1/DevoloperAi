namespace Syncro.CLI.Commands;

public static class DaemonCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        string sub = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "start";

        if (sub == "start")
        {
            await StartDaemonAsync(bridge);
        }
        else if (sub == "stop")
        {
            StopDaemon();
        }
        else
        {
            Ui.Error($"Unknown daemon command: {sub}");
        }
    }

    private static async Task StartDaemonAsync(AgentBridge? bridge)
    {
        Ui.Header("Starting Code-OSS Daemon (Web Server Mode)");

        // Assuming CLI is run from Syncro.Desktop root or we locate it
        string cwd = Directory.GetCurrentDirectory();
        
        // Find vscode-main dir
        string vscodeDir = Path.Combine(cwd, "vscode-main");
        if (!Directory.Exists(vscodeDir))
        {
            vscodeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "vscode-main");
            vscodeDir = Path.GetFullPath(vscodeDir);
        }

        string codeWebBat = Path.Combine(vscodeDir, "scripts", "code-web.bat");
        
        if (!File.Exists(codeWebBat))
        {
            Ui.Error($"Could not find Code-OSS web script at: {codeWebBat}");
            Ui.Info("Ensure you are running this from Syncro.Desktop root and vscode-main is fully cloned.");
            return;
        }

        // 1. Generate minimal settings.json for blank UI
        string userDataDir = Path.Combine(vscodeDir, ".vscode-server-data");
        string settingsDir = Path.Combine(userDataDir, "Machine");
        Directory.CreateDirectory(settingsDir);
        string settingsFile = Path.Combine(settingsDir, "settings.json");
        
        string settingsJson = @"{
    ""workbench.activityBar.visible"": false,
    ""workbench.statusBar.visible"": false,
    ""workbench.layoutControl.enabled"": false,
    ""window.titleBarStyle"": ""custom"",
    ""workbench.editor.showTabs"": ""none""
}";
        File.WriteAllText(settingsFile, settingsJson);
        Ui.Info($"Injected minimal settings at {settingsFile}");

        // 2. Launch code-web.bat (with data-dir flag)
        string cmd = $"& '{codeWebBat}' --host 127.0.0.1 --port 8080 --user-data-dir '{userDataDir}'";
        Ui.Info($"Running: {cmd}");
        
        await bridge?.SendStatusAsync($"Starting Code-OSS daemon on port 8080", "info")!;

        // Pipe output indefinitely
        await foreach (var line in ShellRunner.StreamPowershell(cmd, vscodeDir, bridge))
        {
            Console.WriteLine(line);
        }
    }

    private static void StopDaemon()
    {
        Ui.Info("Stopping Code-OSS Daemon...");
        // Fast/dirty node kill (assuming node runs the server)
        var result = ShellRunner.RunPowershell("Get-Process node | Stop-Process -Force", Directory.GetCurrentDirectory());
        if (result.Success)
            Ui.Info("Daemon stopped.");
        else
            Ui.Error("Failed to stop daemon or none running.");
    }
}
