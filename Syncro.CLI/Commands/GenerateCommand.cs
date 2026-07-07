namespace Syncro.CLI.Commands;

public static class GenerateCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        // Delegate to AiCommand.generate sub-command
        var aiArgs = new[] { "generate" }.Concat(args).ToArray();
        await AiCommand.RunAsync(aiArgs, bridge);
    }
}
