using Newtonsoft.Json.Linq;

namespace Syncro.CLI.Commands;

public static class PatchCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        bool lastError = args.Contains("--last-error");
        string? file   = GetFlag(args, "--file");
        string? msg    = GetFlag(args, "--error");

        string cwd       = Directory.GetCurrentDirectory();
        var detection    = FrameworkDetector.Detect(cwd);
        var llm          = new LlmClient();

        JObject? error = null;

        if (lastError || (file == null && msg == null))
            error = SyncroDb.LastBuildError(cwd);

        if (error == null && !string.IsNullOrWhiteSpace(msg))
            error = new JObject { ["message"] = msg, ["file"] = file ?? "unknown" };

        if (error == null) { Ui.Warn("No build error found. Run a build first or use --error \"message\"."); return; }

        string errorMsg  = error["message"]?.Value<string>() ?? "Unknown error";
        string errorFile = error["file"]?.Value<string>()    ?? file ?? cwd;
        int    errorLine = error["line"]?.Value<int>()       ?? 0;

        Ui.Header($"AI Auto-Patch");
        Ui.Info($"Error: {errorMsg}");
        Ui.Info($"File:  {errorFile}:{errorLine}");

        string fileContext = File.Exists(errorFile)
            ? string.Join("\n", File.ReadAllLines(errorFile).Select((l, i) =>
                $"{i + 1,4}: {l}").Take(150))
            : "[File not found]";

        string system = $"You are an expert {detection.Language} developer. Fix compiler errors with minimal changes. " +
                        $"Output ONLY the corrected code block. No explanations.";

        for (int iter = 1; iter <= 3; iter++)
        {
            Ui.Info($"\nIteration {iter}/3...");
            await bridge?.SendStatusAsync($"Patching: iteration {iter}", "info")!;

            string user = $"Fix this compiler error:\n\nError: {errorMsg}\nFile: {errorFile} (line {errorLine})\n\n" +
                          $"File contents:\n```\n{fileContext}\n```\n\n" +
                          $"Output only the corrected file, no markdown fences.";

            var sb = new System.Text.StringBuilder();
            await foreach (var chunk in llm.StreamAsync(system, user))
            {
                Console.Write(chunk);
                sb.Append(chunk);
            }
            Console.WriteLine();

            string patch = sb.ToString().Trim();
            // Strip markdown fences if LLM included them
            if (patch.StartsWith("```")) patch = StripFences(patch);

            if (File.Exists(errorFile) && !string.IsNullOrWhiteSpace(patch))
            {
                File.WriteAllText(errorFile, patch);
                Ui.Info($"Patch applied to {errorFile}. Running build...");
            }

            // Re-run build
            var buildResult = ShellRunner.RunPowershell(detection.BuildCommand, cwd);
            if (buildResult.Success)
            {
                Ui.Ok("Build succeeded! Patch applied.");
                await bridge?.SendStatusAsync("Patch successful", "success")!;
                SyncroDb.AppendMemory(new
                {
                    memory_id = $"mem-patch-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    prompt    = $"Patch {errorMsg}",
                    success   = true,
                    iterations_count = iter,
                    timestamp = DateTime.UtcNow.ToString("o")
                }, cwd);
                return;
            }

            // Update error for next iteration
            errorMsg = buildResult.Stderr.Split('\n').FirstOrDefault(l => l.Contains("error")) ?? buildResult.Stderr;
            fileContext = File.Exists(errorFile)
                ? string.Join("\n", File.ReadAllLines(errorFile).Take(150).Select((l, i) => $"{i + 1,4}: {l}"))
                : fileContext;
        }

        Ui.Error("Patch failed after 3 iterations. Manual fix required.");
        await bridge?.SendErrorAsync("PATCH_FAILED", errorMsg, errorFile, errorLine)!;
        SyncroDb.AppendMemory(new
        {
            memory_id = $"mem-patch-{DateTime.UtcNow:yyyyMMddHHmmss}",
            prompt    = $"Patch {errorMsg}",
            success   = false,
            iterations_count = 3,
            timestamp = DateTime.UtcNow.ToString("o")
        }, cwd);
    }

    private static string StripFences(string code)
    {
        var lines = code.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].StartsWith("```")) lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].StartsWith("```")) lines.RemoveAt(lines.Count - 1);
        return string.Join("\n", lines);
    }

    private static string? GetFlag(string[] args, string flag)
    {
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
