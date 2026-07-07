namespace Syncro.CLI.Commands;

public static class GitCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        if (args.Length == 0) { Ui.Usage("syncro git <clone|pull|push|status> [args]"); return; }

        string sub = args[0].ToLower();
        string[] rest = args.Skip(1).ToArray();

        switch (sub)
        {
            case "clone": await CloneAsync(rest, bridge); break;
            case "pull":  await GitRunAsync("pull " + string.Join(" ", rest), bridge); break;
            case "push":  await GitRunAsync("push " + string.Join(" ", rest), bridge); break;
            case "status":await GitRunAsync("status", bridge); break;
            case "log":   await GitRunAsync("log --oneline -20", bridge); break;
            default:      await GitRunAsync(string.Join(" ", args), bridge); break;
        }
    }

    private static async Task CloneAsync(string[] args, AgentBridge? bridge)
    {
        if (args.Length == 0) { Ui.Usage("syncro git clone <url> [--dest <path>]"); return; }

        string url  = args[0];
        string dest = GetFlag(args, "--dest") ?? Path.GetFileNameWithoutExtension(url.TrimEnd('/'));

        Ui.Info($"Cloning {url} → {dest}");
        await bridge?.SendStatusAsync($"Cloning {url}", "info")!;

        // Run clone in a safe child process with watchdog
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var cloneTask = Task.Run(async () =>
        {
            await foreach (var line in ShellRunner.StreamPowershell($"git clone \"{url}\" \"{dest}\"",
                bridge: bridge).WithCancellation(cts.Token))
            {
                Console.WriteLine(line);
            }
        }, cts.Token);

        try
        {
            await cloneTask;
            Ui.Ok($"Clone complete → {Path.GetFullPath(dest)}");
            await bridge?.SendStatusAsync("Clone complete", "success")!;
        }
        catch (OperationCanceledException)
        {
            Ui.Error("Clone timed out after 10 minutes.");
            await bridge?.SendErrorAsync("GIT_TIMEOUT", "Clone timed out", url)!;
        }
        catch (Exception ex)
        {
            Ui.Error($"Clone failed: {ex.Message}");
            await bridge?.SendErrorAsync("GIT_CLONE_FAILED", ex.Message, url)!;
        }
    }

    private static async Task GitRunAsync(string gitArgs, AgentBridge? bridge)
    {
        Ui.Info($"git {gitArgs}");
        await bridge?.SendStatusAsync($"Running: git {gitArgs}", "info")!;

        await foreach (var line in ShellRunner.StreamPowershell($"git {gitArgs}", bridge: bridge))
            Console.WriteLine(line);
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int idx = Array.IndexOf(args, flag);
        return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
    }
}
