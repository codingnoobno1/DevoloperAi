namespace Syncro.CLI.Commands;

public static class RunCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        string cwd      = Directory.GetCurrentDirectory();
        string? envFile = GetFlag(args, "--env");
        string? portStr = GetFlag(args, "--port");
        string? sub     = args.FirstOrDefault(a => !a.StartsWith("--"));

        // Built-in sub-scripts
        if (sub == "setup-venv")   { RunSetupVenv(cwd); return; }
        if (sub == "install-node") { RunInstallNode(); return; }
        if (sub != null && (sub.EndsWith(".ps1") || sub.EndsWith(".sh")))
        {
            RunScriptFile(sub, cwd); return;
        }

        // Auto-detect project and run dev server
        var detection = FrameworkDetector.Detect(cwd);
        string cmd = detection.DevCommand;

        if (!string.IsNullOrWhiteSpace(portStr))
            cmd = InjectPort(cmd, detection.Framework, portStr);

        // Load env file if specified
        if (!string.IsNullOrWhiteSpace(envFile) && File.Exists(envFile))
        {
            Ui.Info($"Loading env from: {envFile}");
            cmd = PrependEnvLoad(cmd, envFile);
        }

        Ui.Header($"Starting {detection.Framework} dev server");
        Ui.Info($"Command: {cmd}");
        Ui.Info($"Port:    {(string.IsNullOrEmpty(portStr) ? detection.DefaultPort.ToString() : portStr)}");
        Console.WriteLine();

        await bridge?.SendStatusAsync($"Starting {detection.Framework}", "info")!;

        await foreach (var line in ShellRunner.StreamPowershell(cmd, cwd, bridge))
            Console.WriteLine(line);
    }

    private static void RunSetupVenv(string cwd)
    {
        Ui.Info("Setting up Python virtual environment...");
        string scriptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Scripts", "setup-venv.sh");

        if (File.Exists(scriptPath))
            Console.WriteLine(ShellRunner.RunShFile(scriptPath, null, cwd).Output);
        else
        {
            // Inline fallback
            string cmd = "python -m venv .venv; if ($LASTEXITCODE -eq 0) { " +
                         ".venv\\Scripts\\pip install -r requirements.txt -q }";
            Console.WriteLine(ShellRunner.RunPowershell(cmd, cwd).Output);
        }
    }

    private static void RunInstallNode()
    {
        Ui.Info("Installing / verifying Node.js...");
        string scriptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Scripts", "install-node.ps1");
        if (File.Exists(scriptPath))
            Console.WriteLine(ShellRunner.RunPs1File(scriptPath).Output);
        else
            Console.WriteLine(ShellRunner.RunPowershell("winget install -e --id OpenJS.NodeJS --silent").Output);
    }

    private static void RunScriptFile(string script, string cwd)
    {
        if (!File.Exists(script)) { Ui.Error($"Script not found: {script}"); return; }
        Ui.Info($"Running script: {script}");
        var result = script.EndsWith(".ps1")
            ? ShellRunner.RunPs1File(script, null, cwd)
            : ShellRunner.RunShFile(script, null, cwd);
        Console.WriteLine(result.Output);
        if (!result.Success) Ui.Error($"Script exited with code {result.ExitCode}");
    }

    private static string InjectPort(string cmd, string framework, string port) =>
        framework switch
        {
            "Next.js" or "NestJS" or "Express" or "Fastify"
                => cmd.Replace("npm run dev", $"npm run dev -- --port {port}"),
            "ASP.NET Core" or "Blazor"
                => cmd + $" --urls http://localhost:{port}",
            "Django"
                => cmd + $" {port}",
            _   => cmd
        };

    private static string PrependEnvLoad(string cmd, string envFile) =>
        $"Get-Content '{envFile}' | ForEach-Object {{ " +
        "$parts = $_ -split '=',2; if($parts.Count -eq 2){{ [System.Environment]::SetEnvironmentVariable($parts[0],$parts[1]) }} }}; " +
        cmd;

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
