using Syncro.CLI.Commands;

namespace Syncro.CLI;

class Program
{
    static async Task Main(string[] args)
    {
        // ── Connect to Desktop Named Pipe (optional) ──────────────────────────
        var bridge = new AgentBridge();
        await bridge.TryConnectAsync();

        // Heartbeat task (every 5s while CLI is running)
        _ = HeartbeatLoopAsync(bridge);

        // ── Route CLI arguments ──────────────────────────────────────────────
        if (args.Length > 0)
        {
            await RouteAsync(args, bridge);
            return;
        }

        // ── Interactive REPL ─────────────────────────────────────────────────
        await RunInteractiveAsync(bridge);
    }

    // ── Router ───────────────────────────────────────────────────────────────

    static async Task RouteAsync(string[] args, AgentBridge bridge)
    {
        string cmd   = args[0].ToLower();
        string[] rest = args.Skip(1).ToArray();

        switch (cmd)
        {
            // ── System / setup ──────────────────────────────────────────────
            case "install":  InstallCommand.Run(rest); break;
            case "init":     InitCommand.Run(rest); break;
            case "doctor":   DoctorCommand.Run(rest); break;

            // ── Dev server & git ────────────────────────────────────────────
            case "run":      await RunCommand.RunAsync(rest, bridge); break;
            case "git":      await GitCommand.RunAsync(rest, bridge); break;

            // ── Code intelligence ───────────────────────────────────────────
            case "scan":     await ScanCommand.RunAsync(rest, bridge); break;
            case "ast":      AstCommand.Run(rest); break;
            case "graph":    GraphCommand.Run(rest); break;

            // ── Knowledge & memory ──────────────────────────────────────────
            case "kb":       KbCommand.Run(rest); break;
            case "memory":   MemoryCommand.Run(rest); break;

            // ── AI pipeline ─────────────────────────────────────────────────
            case "ai":       await AiCommand.RunAsync(rest, bridge); break;
            case "generate": await GenerateCommand.RunAsync(rest, bridge); break;
            case "patch":    await PatchCommand.RunAsync(rest, bridge); break;
            case "agent":    await AgentCommand.RunAsync(rest, bridge); break;

            // ── Project & templates ─────────────────────────────────────────
            case "project":  ProjectCommand.Run(rest); break;
            case "template": TemplateCommand.Run(rest); break;

            // ── Deploy & status ─────────────────────────────────────────────
            case "deploy":   await DeployCommand.RunAsync(rest, bridge); break;
            case "status":   await StatusCommand.RunAsync(rest, bridge); break;
            case "daemon":   await DaemonCommand.RunAsync(rest, bridge); break;

            // ── Meta ────────────────────────────────────────────────────────
            case "help":
            case "--help":
            case "-h":
                ShowHelp(); break;

            case "version":
            case "--version":
            case "-v":
                Console.WriteLine("syncro-cli v2.0.0 | .NET 9 | Syncro AI Desktop");
                break;

            default:
                // Pass unknown commands through to PowerShell (shell passthrough)
                Ui.Info($"Running via PowerShell: {string.Join(" ", args)}");
                Console.WriteLine(ShellRunner.RunPowershell(string.Join(" ", args)).Output);
                break;
        }
    }

    // ── Interactive REPL ─────────────────────────────────────────────────────

    static async Task RunInteractiveAsync(AgentBridge bridge)
    {
        Console.Title = "Syncro AI CLI v2";

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(@"
 __      __ ___  _  __ ___  _  _   ___  ___ 
 \ \    / /|_ _|| |/ /|_ _|| \| | / __|/ __|
  \ \  / /  | | | ' <  | | | .` || (_ |\__ \
   \_\/_/  |___||_|\_\|___||_|\_| \___||___/
                                            ");
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Syncro AI CLI v2 — AI-Powered Dev Tool");
        Console.WriteLine("  Type 'help' for commands, 'exit' to quit.\n");
        Console.ResetColor();

        // Show framework detection
        string cwd = Directory.GetCurrentDirectory();
        var det = FrameworkDetector.Detect(cwd);
        if (det.Framework != "Unknown")
        {
            Ui.Info($"Detected project: {det.Framework} ({det.Language}) in {Path.GetFileName(cwd)}");
            Console.WriteLine();
        }

        while (true)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"syncro [{Path.GetFileName(cwd)}]> ");
            Console.ResetColor();

            string? input = Console.ReadLine();
            if (input == null) break;

            string trimmed = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            if (trimmed is "exit" or "quit" or "q") break;

            // Tokenize respecting quoted strings
            string[] tokens = Tokenize(trimmed);
            if (tokens.Length == 0) continue;

            try
            {
                await RouteAsync(tokens, bridge);
            }
            catch (Exception ex)
            {
                Ui.Error($"Command failed: {ex.Message}");
            }
        }

        bridge.Dispose();
        Console.WriteLine("\n  Goodbye.");
    }

    // ── Help ─────────────────────────────────────────────────────────────────

    static void ShowHelp()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("  Syncro AI CLI v2 — Command Reference");
        Console.ResetColor();
        Console.WriteLine();

        void Row(string cmd, string desc)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"  {cmd,-30}");
            Console.ResetColor();
            Console.WriteLine(desc);
        }

        Console.WriteLine("  ── System ──────────────────────────────────────────────────────");
        Row("install [--check|--uninstall]", "Register syncro in Windows System PATH (UAC)");
        Row("init [path] [--name <name>]",   "Bootstrap workspace, detect framework");
        Row("doctor [--fix] [--json]",        "Check dev environment health");
        Row("status [--msg x] [--level y]",   "Show workspace info or push status to Desktop");
        Row("version",                         "Show CLI version");

        Console.WriteLine("\n  ── Dev ─────────────────────────────────────────────────────────");
        Row("run [--port x] [--env file]",    "Auto-detect and launch dev server");
        Row("git clone|pull|push|status",      "Safe Git wrapper with crash protection");
        Row("deploy --target cloudrun|vercel", "Push project to cloud target");
        Row("daemon start|stop",               "Manage Code-OSS background daemon");

        Console.WriteLine("\n  ── Code Intelligence ───────────────────────────────────────────");
        Row("scan [--incremental] [--depth n]","Run AST scan, build symbol index");
        Row("ast find|usages|list|diff",       "Query AST node store");
        Row("graph [--format mermaid|dot]",    "Render dependency graph");

        Console.WriteLine("\n  ── Knowledge ───────────────────────────────────────────────────");
        Row("kb search|add|list|export",       "Manage knowledge base");
        Row("memory list|view|purge|compact",  "Manage agent memory (hindsight)");
        Row("template list|search|apply",      "Apply code scaffolding templates");

        Console.WriteLine("\n  ── AI Pipeline ─────────────────────────────────────────────────");
        Row("ai explain <symbol>",             "Explain a code symbol with RAG context");
        Row("ai generate <type> <name>",       "Generate code matching project conventions");
        Row("ai find-auth-flow",               "Trace authentication flow across codebase");
        Row("ai review <file>",                "AI code review: security, performance, arch");
        Row("ai ask \"<question>\"",           "Ask any natural-language question about code");
        Row("generate <type> <name>",          "Alias for: syncro ai generate");
        Row("patch [--last-error]",            "AI auto-fix last compiler error (3 iterations)");
        Row("agent \"<goal>\" [--plan-only]",  "Multi-step autonomous AI task runner");

        Console.WriteLine("\n  ── Project ─────────────────────────────────────────────────────");
        Row("project list|add|remove|info",    "Manage registered projects");
        Row("project group <name> <ids...>",   "Create multi-project combo stack");

        Console.WriteLine();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static string[] Tokenize(string input)
    {
        var tokens = new List<string>();
        bool inQuote = false;
        var current  = new System.Text.StringBuilder();

        foreach (char c in input)
        {
            if (c == '"')   { inQuote = !inQuote; continue; }
            if (c == ' ' && !inQuote)
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return [.. tokens];
    }

    static async Task HeartbeatLoopAsync(AgentBridge bridge)
    {
        while (true)
        {
            await Task.Delay(5000);
            await bridge.SendHeartbeatAsync();
        }
    }
}
