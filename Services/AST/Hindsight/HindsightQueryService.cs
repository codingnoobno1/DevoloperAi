using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DeveloperAI.BusinessLogic;

namespace Syncro.Desktop.Services.AST.Hindsight;

/// <summary>
/// The hindsight RAG layer. Retrieval is deterministic and offline (lexical match over the real
/// vector-record metadata — symbol names, snippets, tags, files). The "with LLM" path feeds those
/// same retrieved chunks to the local model (port 3020) for a synthesized answer; the "without LLM"
/// path returns the retrieved facts directly. Side by side, this demonstrates the value of hindsight.
/// </summary>
public class HindsightQueryService
{
    private static readonly char[] Sep =
        { ' ', '.', '_', '/', '\\', '(', ')', '{', '}', '<', '>', ',', ';', ':', '-', '"', '\'', '\t', '\n', '\r' };

    private readonly HindsightVectorStore _store;
    private readonly AIClient _ai;
    private readonly HttpClient _probe = new() { Timeout = TimeSpan.FromSeconds(1.5) };

    public HindsightQueryService(HindsightVectorStore store, AIClient ai)
    {
        _store = store;
        _ai = ai;
    }

    public async Task<bool> IsLlmAvailableAsync()
    {
        try
        {
            var r = await _probe.GetAsync("http://localhost:3020/");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Deterministic lexical retrieval over the hindsight index.</summary>
    public async Task<List<RetrievedChunk>> RetrieveAsync(string projectPath, string query, int topK = 6)
    {
        var entries = await _store.LoadAsync(projectPath);
        var qTokens = Tokenize(query);
        if (qTokens.Count == 0) return new List<RetrievedChunk>();

        var scored = new List<RetrievedChunk>();
        foreach (var e in entries)
        {
            var hay = Tokenize($"{e.SymbolName} {e.NodeType} {string.Join(' ', e.Tags)} {e.CodeSnippet} {Path.GetFileName(e.FilePath)}");
            int overlap = qTokens.Count(t => hay.Contains(t));
            if (overlap == 0) continue;

            double score = (double)overlap / qTokens.Count;
            if (!string.IsNullOrEmpty(e.SymbolName) && query.Contains(e.SymbolName, StringComparison.OrdinalIgnoreCase))
                score += 0.5; // exact symbol mention is a strong signal

            scored.Add(new RetrievedChunk { Entry = e, Score = Math.Round(score, 3), Why = $"matched {overlap}/{qTokens.Count} terms" });
        }
        return scored.OrderByDescending(c => c.Score).Take(topK).ToList();
    }

    /// <summary>WITHOUT LLM: return the retrieved project facts directly (instant, offline).</summary>
    public async Task<HindsightAnswer> AnswerWithoutLlmAsync(string projectPath, string query)
    {
        var sw = Stopwatch.StartNew();
        var chunks = await RetrieveAsync(projectPath, query);
        var ans = new HindsightAnswer { Mode = "no-llm", Chunks = chunks, LlmUsed = false, LlmAvailable = false };

        if (chunks.Count == 0)
        {
            ans.Text = "No matching symbols found in the hindsight index. Try other terms, or (re)build the index.";
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Retrieved {chunks.Count} relevant symbol(s) from the project's hindsight index:");
            sb.AppendLine();
            foreach (var c in chunks)
                sb.AppendLine($"• {c.Entry.SymbolName} ({c.Entry.NodeType}) — {Path.GetFileName(c.Entry.FilePath)}:{c.Entry.LineStart}");
            sb.AppendLine();
            sb.AppendLine("These are the project's own definitions, retrieved deterministically from the vector DB — no AI involved.");
            ans.Text = sb.ToString();
        }
        sw.Stop();
        ans.ElapsedMs = sw.ElapsedMilliseconds;
        return ans;
    }

    /// <summary>WITH LLM: ground the local model with the same retrieved chunks. Degrades if offline.</summary>
    public async Task<HindsightAnswer> AnswerWithLlmAsync(string projectPath, string query)
    {
        var sw = Stopwatch.StartNew();
        var chunks = await RetrieveAsync(projectPath, query);
        var ans = new HindsightAnswer { Mode = "llm", Chunks = chunks, LlmAvailable = await IsLlmAvailableAsync() };

        if (!ans.LlmAvailable)
        {
            ans.LlmUsed = false;
            ans.Note = "LLM offline (port 3020).";
            ans.Text = chunks.Count == 0
                ? "LLM offline and no context retrieved."
                : "LLM is offline — start the model on port 3020 to get a synthesized answer. The retrieved hindsight context (left) still works without it.";
            sw.Stop();
            ans.ElapsedMs = sw.ElapsedMilliseconds;
            return ans;
        }

        var ctx = new StringBuilder();
        foreach (var c in chunks)
            ctx.AppendLine($"- {c.Entry.SymbolName} ({c.Entry.NodeType}) in {c.Entry.FilePath}:{c.Entry.LineStart}: {c.Entry.CodeSnippet}");

        var prompt =
            "You are answering a question about a SPECIFIC codebase. Use ONLY the retrieved project context below; " +
            "if it is insufficient, say so. Cite the symbols/files you rely on.\n\n" +
            $"QUESTION:\n{query}\n\n" +
            $"PROJECT CONTEXT (from hindsight vector DB):\n{(chunks.Count == 0 ? "(no context retrieved)" : ctx.ToString())}";

        var (text, err) = await _ai.CallLLM(prompt, "hindsight", projectPath);
        ans.LlmUsed = err == null;
        ans.Text = err == null ? (text ?? "(empty response)") : $"LLM error: {err}";
        sw.Stop();
        ans.ElapsedMs = sw.ElapsedMilliseconds;
        return ans;
    }

    private static List<string> Tokenize(string s) =>
        (s ?? "").ToLowerInvariant()
            .Split(Sep, StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1)
            .Distinct()
            .ToList();
}
