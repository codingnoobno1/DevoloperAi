namespace Syncro.CLI.Commands;

/// <summary>AiCommand — RAG-augmented LLM pipeline for explain, generate, find-auth-flow, review, ask, patch.</summary>
public static class AiCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        string sub = args.FirstOrDefault() ?? "help";
        string[] rest = args.Skip(1).ToArray();

        var llm = new LlmClient();
        string cwd = Directory.GetCurrentDirectory();
        var detection = FrameworkDetector.Detect(cwd);

        switch (sub)
        {
            case "explain":        await Explain(rest, llm, detection, bridge); break;
            case "generate":       await Generate(rest, llm, detection, bridge); break;
            case "find-auth-flow": await FindAuthFlow(llm, detection, bridge); break;
            case "review":         await Review(rest, llm, detection, bridge); break;
            case "ask":            await Ask(string.Join(" ", rest), llm, detection, bridge); break;
            default:
                Ui.Usage("syncro ai <explain|generate|find-auth-flow|review|ask> [args]");
                break;
        }
    }

    // ── Explain ──────────────────────────────────────────────────────────────

    private static async Task Explain(string[] args, LlmClient llm,
        FrameworkDetector.DetectionResult det, AgentBridge? bridge)
    {
        string symbol = args.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(symbol)) { Ui.Usage("syncro ai explain <symbol>"); return; }

        Ui.Info($"Explaining: {symbol}");
        await bridge?.SendStatusAsync($"AI explaining {symbol}", "info")!;

        // Build context
        string context = BuildSymbolContext(symbol, det);
        string system  = BuildSystemPrompt(det);
        string user    = $"Explain the following code symbol in plain English. Focus on: what it does, " +
                         $"why it exists, how it connects to the rest of the system.\n\nSymbol: {symbol}\n\n{context}";

        Ui.Header($"AI Explanation: {symbol}");
        await foreach (var chunk in llm.StreamAsync(system, user))
            Console.Write(chunk);
        Console.WriteLine();
    }

    // ── Generate ─────────────────────────────────────────────────────────────

    private static async Task Generate(string[] args, LlmClient llm,
        FrameworkDetector.DetectionResult det, AgentBridge? bridge)
    {
        if (args.Length < 2) { Ui.Usage("syncro ai generate <type> <name> [--framework x] [--lang x]"); return; }

        string type = args[0];   // e.g. controller, service, dto, model
        string name = args[1];

        Ui.Info($"Generating {type}: {name}");
        await bridge?.SendStatusAsync($"Generating {type} {name}", "info")!;

        // Fetch memory few-shots
        string fewShots  = BuildFewShots(type, det.Framework);
        string system    = BuildSystemPrompt(det);
        string user      = $"Generate a {type} named '{name}' for a {det.Framework} project " +
                           $"written in {det.Language}.\n\nFollow the naming conventions and patterns " +
                           $"used in this codebase. Output only the code, no explanations.\n\n{fewShots}";

        // Save to memory (pending)
        string memId = $"mem-gen-{DateTime.UtcNow:yyyyMMddHHmmss}";
        string memPrompt = $"Generate {type} {name}";
        var memRecord = new
        {
            memory_id        = memId,
            prompt_hash      = memPrompt.GetHashCode().ToString("x"),
            prompt           = memPrompt,
            generated_code   = (string?)null,
            target_framework = det.Framework,
            language         = det.Language,
            build_command    = det.BuildCommand,
            success          = (bool?)null,
            timestamp        = DateTime.UtcNow.ToString("o")
        };
        SyncroDb.AppendMemory(memRecord);

        var sb = new System.Text.StringBuilder();
        Ui.Header($"Generated {type}: {name}");
        await foreach (var chunk in llm.StreamAsync(system, user))
        {
            Console.Write(chunk);
            sb.Append(chunk);
        }
        Console.WriteLine();

        Ui.Info("\nRun 'syncro patch --last-error' if the build fails.");
    }

    // ── Find Auth Flow ────────────────────────────────────────────────────────

    private static async Task FindAuthFlow(LlmClient llm,
        FrameworkDetector.DetectionResult det, AgentBridge? bridge)
    {
        Ui.Info("Analysing authentication flow...");
        await bridge?.SendStatusAsync("Finding auth flow", "info")!;

        // Scan source for auth-related files
        string cwd = Directory.GetCurrentDirectory();
        var authFiles = FindAuthFiles(cwd, det.ExcludeDirs);

        string filesContext = string.Join("\n---\n", authFiles.Take(5).Select(f =>
            $"File: {Path.GetRelativePath(cwd, f)}\n{TruncateFile(f, 100)}"));

        string system = BuildSystemPrompt(det);
        string user   = $"Analyse the following files and describe the complete authentication flow: " +
                        $"login, token generation, token verification, and protected route guards.\n\n{filesContext}";

        Ui.Header("Auth Flow Analysis");
        await foreach (var chunk in llm.StreamAsync(system, user))
            Console.Write(chunk);
        Console.WriteLine();
    }

    // ── Review ────────────────────────────────────────────────────────────────

    private static async Task Review(string[] args, LlmClient llm,
        FrameworkDetector.DetectionResult det, AgentBridge? bridge)
    {
        string? file = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (file == null || !File.Exists(file)) { Ui.Error($"File not found: {file}"); return; }

        Ui.Info($"Reviewing: {file}");
        string code   = TruncateFile(file, 300);
        string system = BuildSystemPrompt(det);
        string user   = $"Perform a code review of this file. Check for: security issues, " +
                        $"performance anti-patterns, naming violations, and architecture problems.\n\n" +
                        $"File: {file}\n\n```\n{code}\n```";

        Ui.Header($"Code Review: {Path.GetFileName(file)}");
        await foreach (var chunk in llm.StreamAsync(system, user))
            Console.Write(chunk);
        Console.WriteLine();
    }

    // ── Ask ───────────────────────────────────────────────────────────────────

    private static async Task Ask(string question, LlmClient llm,
        FrameworkDetector.DetectionResult det, AgentBridge? bridge)
    {
        if (string.IsNullOrWhiteSpace(question)) { Ui.Usage("syncro ai ask \"<question>\""); return; }

        Ui.Info($"Question: {question}");
        await bridge?.SendStatusAsync($"AI answering: {question[..Math.Min(40, question.Length)]}", "info")!;

        string system = BuildSystemPrompt(det);
        string user   = $"Answer this question about the codebase: {question}";

        Ui.Header("AI Answer");
        await foreach (var chunk in llm.StreamAsync(system, user))
            Console.Write(chunk);
        Console.WriteLine();
    }

    // ── Prompt Builders ───────────────────────────────────────────────────────

    private static string BuildSystemPrompt(FrameworkDetector.DetectionResult det) =>
        $"You are an expert {det.Language} developer working on a {det.Framework} project. " +
        $"Ecosystem: {det.Ecosystem}. " +
        $"Follow the project's existing code conventions exactly. " +
        $"When generating code, output only code, no markdown fences unless asked.";

    private static string BuildSymbolContext(string symbol, FrameworkDetector.DetectionResult det)
    {
        // Try to find the symbol in source files
        string cwd = Directory.GetCurrentDirectory();
        foreach (var root in det.SourceRoots)
        {
            string rootPath = Path.Combine(cwd, root);
            if (!Directory.Exists(rootPath)) continue;
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    string content = File.ReadAllText(file);
                    if (content.Contains(symbol))
                    {
                        int idx = content.IndexOf(symbol);
                        int start = Math.Max(0, idx - 200);
                        int end   = Math.Min(content.Length, idx + 800);
                        return $"Found in: {Path.GetRelativePath(cwd, file)}\n```\n{content[start..end]}\n```";
                    }
                }
                catch { }
            }
        }
        return $"Symbol '{symbol}' — source file not found. Answer based on the project context.";
    }

    private static string BuildFewShots(string type, string framework)
    {
        var successes = SyncroDb.ReadMemory()
            .Where(r => r["success"]?.ToObject<bool?>() == true)
            .Where(r => (r["target_framework"]?.ToString() ?? "")
                .Equals(framework, StringComparison.OrdinalIgnoreCase))
            .TakeLast(2)
            .ToList();

        if (successes.Count == 0) return "";

        return "## Past Successful Generations (use as style reference):\n" +
               string.Join("\n---\n", successes.Select(r =>
                   $"Prompt: {r["prompt"]}\nCode:\n{r["generated_code"]}"));
    }

    private static List<string> FindAuthFiles(string root, string[] excludeDirs)
    {
        string[] authPatterns = ["auth", "login", "token", "jwt", "passport", "session"];
        try
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !excludeDirs.Any(e => f.Contains(Path.DirectorySeparatorChar + e + Path.DirectorySeparatorChar)))
                .Where(f => authPatterns.Any(p => Path.GetFileName(f).Contains(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        catch { return []; }
    }

    private static string TruncateFile(string path, int maxLines)
    {
        try
        {
            var lines = File.ReadLines(path).Take(maxLines).ToArray();
            string result = string.Join("\n", lines);
            if (File.ReadAllLines(path).Length > maxLines)
                result += $"\n... (truncated at {maxLines} lines)";
            return result;
        }
        catch { return "[could not read file]"; }
    }
}
