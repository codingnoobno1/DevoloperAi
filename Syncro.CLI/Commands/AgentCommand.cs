using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Syncro.CLI.Commands;

public static class AgentCommand
{
    public static async Task RunAsync(string[] args, AgentBridge? bridge = null)
    {
        bool planOnly = args.Contains("--plan-only");
        string goal   = string.Join(" ", args.Where(a => !a.StartsWith("--"))).Trim('"', '\'');

        if (string.IsNullOrWhiteSpace(goal)) { Ui.Usage("syncro agent \"<goal>\" [--plan-only]"); return; }

        Ui.Header("Syncro Autonomous Agent");
        Ui.Info($"Goal: {goal}");
        Ui.Info($"Plan only: {planOnly}");

        string cwd = Directory.GetCurrentDirectory();
        string projectId = Path.GetFileName(cwd).ToLower();
        SyncroDb.EnsureStructure(cwd);

        await bridge?.SendStatusAsync($"Agent started: {goal[..Math.Min(40, goal.Length)]}", "info")!;

        // ── Step 1: Create initial task ──────────────────────────────────────
        var tasks = SyncroDb.LoadTasks(cwd);
        var activeTask = tasks.FirstOrDefault(t => t["status"]?.ToString() != "done" && t["status"]?.ToString() != "needs_human");

        if (activeTask == null)
        {
            Ui.Info("📋 Creating initial task for goal...");
            string initialScriptId = "scr_" + Guid.NewGuid().ToString("N")[..8];
            var created = SyncroDb.EnqueueTask(projectId, goal, "run_script", initialScriptId, cwd);
            tasks = SyncroDb.LoadTasks(cwd);
            activeTask = tasks.FirstOrDefault(t => t["task_id"]?.ToString() == created["task_id"]?.ToString());
        }

        if (planOnly)
        {
            Ui.Info($"Initial Task Enqueued: {activeTask?["title"]} ({activeTask?["task_id"]})");
            return;
        }

        var llm = new LlmClient();

        // ── Step 2: Loopable Task Execution State Machine ─────────────────────
        Ui.Info("⚙️  Starting autonomous task loop...");
        int loopCount = 0;
        int maxLoops = 25;

        while (activeTask != null && loopCount++ < maxLoops)
        {
            string taskId = activeTask["task_id"]?.ToString() ?? "";
            string status = activeTask["status"]?.ToString() ?? "pending";
            Ui.Info($"\n[Loop {loopCount}] Task: {activeTask["title"]} | Status: {status}");

            if (status == "pending")
            {
                // Transition to running
                UpdateTaskStatus(activeTask, "running", "running script");
                SyncroDb.SaveTasks(tasks, cwd);

                string scriptId = activeTask["script_id"]?.ToString() ?? "";
                if (scriptId == "scr_setup_bat")
                {
                    scriptId = "scr_" + Guid.NewGuid().ToString("N")[..8];
                    activeTask["script_id"] = scriptId;
                }
                Ui.Info($"  Executing script {scriptId}...");

                // Generate execution script dynamically using local LLM server
                Ui.Info("🧠 Generating execution script using LLM...");
                string scriptBody = await llm.GenerateBatchScriptAsync(activeTask["title"]?.ToString() ?? goal, cwd);
                scriptBody = CleanScriptContent(scriptBody);

                string scriptPath = Path.Combine(cwd, $"{scriptId}.bat");
                await File.WriteAllTextAsync(scriptPath, scriptBody);
                Ui.Info($"💾 Saved generated script to: {scriptPath}");

                activeTask["llm_involved"] = true;

                // Run the script
                var result = ShellRunner.RunPowershell(scriptPath, cwd);
                bool scriptSuccess = (result.ExitCode == 0);
                string output = result.Output;

                // Clean up the temporary batch script
                try { File.Delete(scriptPath); } catch {}

                activeTask["template_script_executed"] = true;

                if (scriptSuccess)
                {
                    activeTask["error"] = false;
                    activeTask["source"] = output;
                    UpdateTaskStatus(activeTask, "succeeded", "script exit 0");
                }
                else
                {
                    activeTask["error"] = true;
                    activeTask["error_count"] = (activeTask["error_count"]?.Value<int>() ?? 0) + 1;
                    activeTask["source"] = output;
                    UpdateTaskStatus(activeTask, "failed", $"script failed with exit code {result.ExitCode}");
                }
                SyncroDb.SaveTasks(tasks, cwd);
            }
            else if (status == "running")
            {
                // Crash recovery fallback
                activeTask["error"] = true;
                activeTask["source"] = "interrupted while running";
                UpdateTaskStatus(activeTask, "failed", "interrupted");
                SyncroDb.SaveTasks(tasks, cwd);
            }
            else if (status == "failed")
            {
                // Diagnose error
                string errText = activeTask["source"]?.ToString() ?? "";
                string errorType = "unknown";
                string solution = "human";
                string why = "unknown failure";

                // Try LLM for dynamic diagnosis
                try
                {
                    Ui.Info("🧠 Querying LLM for error diagnosis...");
                    string system = "You are a software debugger and error diagnostician.";
                    string prompt = $"We ran a batch script to achieve: '{activeTask["title"]}' and it failed with this output:\n{errText}\n\n" +
                                     "Diagnose the error. Respond with a JSON object containing:\n" +
                                     "1. \"error_type\": one of [\"missing_dependency\", \"port_conflict\", \"compile_error\", \"runtime_error\", \"permission_denied\", \"unknown\"]\n" +
                                     "2. \"solution\": one of [\"install_dep\", \"change_port\", \"edit_code\", \"run_another\", \"rerun\", \"human\"]\n" +
                                     "3. \"why\": A short description of the root cause and the next action.\n\n" +
                                     "Respond ONLY with the JSON object. No conversational text.";

                    string suggest = await llm.CompleteAsync(system, prompt);
                    var jsonStart = suggest.IndexOf('{');
                    var jsonEnd = suggest.LastIndexOf('}');
                    if (jsonStart >= 0 && jsonEnd > jsonStart)
                    {
                        var jsonStr = suggest[jsonStart..(jsonEnd + 1)];
                        var diag = JObject.Parse(jsonStr);
                        errorType = diag["error_type"]?.ToString() ?? "unknown";
                        solution = diag["solution"]?.ToString() ?? "human";
                        why = diag["why"]?.ToString() ?? "LLM diagnosed failure";
                        activeTask["llm_involved"] = true;
                    }
                    else
                    {
                        // Fallback logic
                        if (suggest.Contains("edit_code"))
                        {
                            solution = "edit_code";
                            why = "LLM suggested code edit solution.";
                            activeTask["llm_involved"] = true;
                        }
                        else if (suggest.Contains("run_another"))
                        {
                            solution = "run_another";
                            why = "LLM suggested running a different script.";
                            activeTask["llm_involved"] = true;
                        }
                    }
                }
                catch
                {
                    // Heuristic fallback
                    if (errText.Contains("missing") || errText.Contains("install") || errText.Contains("requirements"))
                    {
                        errorType = "missing_dependency";
                        solution = "install_dep";
                        why = "Missing package detected. Recommended action: install dependencies.";
                    }
                    else if (errText.Contains("port") || errText.Contains("address already in use"))
                    {
                        errorType = "port_conflict";
                        solution = "change_port";
                        why = "Port conflict detected. Recommended action: rerun on a different port.";
                    }
                }

                activeTask["error_type"] = errorType;
                activeTask["solution"] = solution;
                activeTask["next_action"] = why;

                UpdateTaskStatus(activeTask, "resolving", why);
                SyncroDb.SaveTasks(tasks, cwd);
            }
            else if (status == "resolving")
            {
                string solution = activeTask["solution"]?.ToString() ?? "";
                int attempts = activeTask["attempts"]?.Value<int>() ?? 0;
                int maxAttempts = activeTask["max_attempts"]?.Value<int>() ?? 4;

                if (attempts >= maxAttempts)
                {
                    UpdateTaskStatus(activeTask, "needs_human", "max attempts reached");
                    SyncroDb.SaveTasks(tasks, cwd);
                    break;
                }

                if (solution == "install_dep" || solution == "change_port" || solution == "rerun" || solution == "run_another")
                {
                    // Auto apply safe fixes
                    Ui.Info($"  [Auto Fix] Applying safe solution '{solution}'...");
                    if (solution == "install_dep")
                    {
                        ShellRunner.RunPowershell("npm install", cwd);
                    }
                    activeTask["attempts"] = attempts + 1;
                    UpdateTaskStatus(activeTask, "patched", "patched with safe fix");
                }
                else
                {
                    // Code edits need approval
                    activeTask["needs_approval"] = true;
                    UpdateTaskStatus(activeTask, "needs_human", $"solution '{solution}' requires manual verification");
                    SyncroDb.SaveTasks(tasks, cwd);
                    Ui.Warn($"Task {taskId} requires manual code changes / human handoff.");
                    break;
                }
                SyncroDb.SaveTasks(tasks, cwd);
            }
            else if (status == "patched")
            {
                activeTask["error"] = false;
                UpdateTaskStatus(activeTask, "pending", "looping back to pending");
                SyncroDb.SaveTasks(tasks, cwd);
            }
            else if (status == "succeeded")
            {
                UpdateTaskStatus(activeTask, "done", "completed successfully");
                SyncroDb.SaveTasks(tasks, cwd);
                Ui.Ok($"Task {taskId} has Succeeded!");
                break;
            }
            else
            {
                // done or needs_human (terminal states)
                break;
            }

            // Reload state
            tasks = SyncroDb.LoadTasks(cwd);
            activeTask = tasks.FirstOrDefault(t => t["task_id"]?.ToString() == taskId);
        }

        Ui.Ok("Agent task loop execution ended.");
        await bridge?.SendStatusAsync($"Agent finished loop", "success")!;
    }

    private static void UpdateTaskStatus(JObject task, string to, string note)
    {
        task["status"] = to;
        task["updated_at"] = DateTime.UtcNow.ToString("o");
        var history = task["history"] as JArray ?? new JArray();
        history.Add(new JObject
        {
            ["status"] = to,
            ["at"] = DateTime.UtcNow.ToString("o"),
            ["note"] = note
        });
        task["history"] = history;
    }

    private static string CleanScriptContent(string script)
    {
        var lines = script.Split('\n').Select(l => l.Trim('\r', ' ', '\t')).ToList();
        
        // Remove code fences
        var cleaned = new List<string>();
        bool inFence = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("```"))
            {
                inFence = !inFence;
                continue;
            }
            if (!inFence || string.IsNullOrWhiteSpace(line))
            {
                if (line.StartsWith("```")) continue;
            }
            cleaned.Add(line);
        }

        var finalLines = new List<string>();
        foreach (var line in cleaned)
        {
            if (line.StartsWith("Here is the script:") || 
                line.StartsWith("Here is the Windows batch script:") || 
                line.StartsWith("Here's the batch script:") ||
                line.StartsWith("Generated script:"))
            {
                continue;
            }
            finalLines.Add(line);
        }

        return string.Join(Environment.NewLine, finalLines);
    }
}
