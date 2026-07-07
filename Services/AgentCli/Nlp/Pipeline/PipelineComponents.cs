using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;
using Syncro.Desktop.Services.AST;

namespace Syncro.Desktop.Services.AgentCli.Nlp.Pipeline;

public class ProjectAnalyzer : IProjectAnalyzer
{
    public Task AnalyzeAsync(TaskRoutingContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.ProjectPath) || !Directory.Exists(ctx.ProjectPath))
        {
            ctx.Metadata = new ProjectMetadata("Unknown", "Unknown", "Unknown");
            return Task.CompletedTask;
        }

        // Basic heuristic
        string lang = "Unknown";
        string framework = "Unknown";
        string arch = "Unknown";

        if (Directory.GetFiles(ctx.ProjectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
        {
            lang = "C#";
            if (File.Exists(Path.Combine(ctx.ProjectPath, "Program.cs")))
            {
                var content = File.ReadAllText(Path.Combine(ctx.ProjectPath, "Program.cs"));
                if (content.Contains("WebApplication") || content.Contains("AddControllers"))
                {
                    framework = "ASP.NET";
                    arch = "REST API";
                }
                else if (content.Contains("MauiApp"))
                {
                    framework = ".NET MAUI";
                }
                else if (content.Contains("Components"))
                {
                    framework = "Blazor";
                }
            }
        }
        else if (File.Exists(Path.Combine(ctx.ProjectPath, "package.json")))
        {
            lang = "TypeScript/JavaScript";
            var content = File.ReadAllText(Path.Combine(ctx.ProjectPath, "package.json"));
            if (content.Contains("react")) framework = "React";
            else if (content.Contains("vue")) framework = "Vue";
            else if (content.Contains("next")) framework = "Next.js";
        }

        ctx.Metadata = new ProjectMetadata(lang, framework, arch);
        return Task.CompletedTask;
    }
}

public class AstFeatureExtractor : IAstFeatureExtractor
{
    private readonly AstService _ast;

    public AstFeatureExtractor(AstService ast)
    {
        _ast = ast;
    }

    public async Task ExtractAsync(TaskRoutingContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.ProjectPath)) return;

        var map = await _ast.GetCachedMapAsync(ctx.ProjectPath);
        if (map == null) return;

        var existingSymbols = new List<string>();
        foreach (var node in map.Nodes.Where(n => n.Type == Syncro.Desktop.Services.AST.Models.AstNodeType.Class || n.Type == Syncro.Desktop.Services.AST.Models.AstNodeType.Interface))
        {
            existingSymbols.Add(node.Name);
        }

        string guess = "Unknown";
        // Simple guess logic based on goal and existing symbols
        if (ctx.NormalizedGoal.Contains("api") && existingSymbols.Any(s => s.EndsWith("Repository")) && !existingSymbols.Any(s => s.EndsWith("Controller")))
        {
            guess = "Controller Missing";
        }

        ctx.Ast = new AstContext(existingSymbols, guess);
    }
}

public class HindsightRetriever : IHindsightRetriever
{
    public Task RetrieveAsync(TaskRoutingContext ctx)
    {
        // Mocking Vector DB lookup for now
        var knowledge = new List<string>();
        if (ctx.NormalizedGoal.Contains("login") || ctx.NormalizedGoal.Contains("auth"))
        {
            knowledge.Add("Authentication implies JWT, Identity, and Refresh Tokens.");
        }
        ctx.HindsightKnowledge = knowledge;
        return Task.CompletedTask;
    }
}

public class HeuristicIntentClassifier : IIntentClassifier
{
    // Interim scorer before Tiny ONNX is ready
    public Task<(string Intent, double Confidence, string[] Reasons)> ClassifyAsync(TaskRoutingContext ctx)
    {
        string intent = "Explain"; // default fallback
        double confidence = 0.0;
        var reasons = new List<string>();

        if (Regex.IsMatch(ctx.NormalizedGoal, @"\b(generate|add|create|build)\b"))
        {
            intent = "Generate";
            confidence = 0.6;
            reasons.Add("Verb 'generate' detected.");
            
            if (ctx.Metadata.Framework == "ASP.NET")
            {
                confidence += 0.2;
                reasons.Add("ASP.NET context strengthens generation confidence.");
            }
        }
        else if (Regex.IsMatch(ctx.NormalizedGoal, @"\b(fix|patch|repair)\b"))
        {
            intent = "Patch";
            confidence = 0.7;
            reasons.Add("Verb 'fix' detected.");
        }
        else if (Regex.IsMatch(ctx.NormalizedGoal, @"\b(analyse|analyze|scan)\b"))
        {
            intent = "Analyse";
            confidence = 0.8;
            reasons.Add("Verb 'analyse' detected.");
        }
        else
        {
            confidence = 0.4;
            reasons.Add("Fallback intent.");
        }

        if (ctx.HindsightKnowledge.Any())
        {
            confidence = Math.Min(1.0, confidence + 0.1);
            reasons.Add("Hindsight knowledge increased confidence.");
        }

        return Task.FromResult((intent, confidence, reasons.ToArray()));
    }
}

public class TaskPlanner : ITaskPlanner
{
    public Task<TaskPlan> PlanAsync(TaskRoutingContext ctx, string intent, double confidence, string[] reasons)
    {
        var steps = new List<string>();

        if (intent == "Generate" && ctx.Metadata.Framework == "ASP.NET" && ctx.NormalizedGoal.Contains("api"))
        {
            steps.Add("Generate DTO");
            steps.Add("Generate Service");
            steps.Add("Generate Controller");
            steps.Add("Register DI in Program.cs");
        }
        else if (intent == "Generate")
        {
            steps.Add("Analyze Request");
            steps.Add("Generate Target File");
        }
        else if (intent == "Patch")
        {
            steps.Add("Locate Issue");
            steps.Add("Apply AST Diff");
        }
        else
        {
            steps.Add("Evaluate prompt");
            steps.Add("Generate response");
        }

        return Task.FromResult(new TaskPlan(intent, confidence, reasons, steps));
    }
}
