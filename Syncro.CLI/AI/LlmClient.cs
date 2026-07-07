using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Syncro.CLI;

/// <summary>
/// Lightweight LLM client that supports Ollama (local) and a cloud fallback.
/// Configured via .syncro_db/config.json — defaults to Ollama if not configured.
/// </summary>
public class LlmClient
{
    private readonly LlmConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public LlmClient(string? configPath = null)
    {
        _config = LoadConfig(configPath);
    }

    private async Task<bool> IsLocalExpressOnlineAsync()
    {
        try
        {
            var resp = await _http.GetAsync("http://localhost:3020/").ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> LocalExpressCompleteAsync(string prompt, string? aiMode = null, string? workspacePath = null)
    {
        var payload = new
        {
            prompt = prompt,
            aiMode = aiMode,
            workspacePath = workspacePath ?? Directory.GetCurrentDirectory()
        };

        try
        {
            var resp = await _http.PostAsync(
                "http://localhost:3020/gemini",
                new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return $"[LLM Server Error: {resp.StatusCode}]";
            }

            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var obj = JObject.Parse(body);
            if (obj["success"]?.Value<bool>() == true)
            {
                var result = obj["result"];
                return result?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>() ?? "[empty]";
            }
            return obj["error"]?.ToString() ?? "[error]";
        }
        catch (Exception ex)
        {
            return $"[LLM Server Connection Error: {ex.Message}]";
        }
    }

    public async Task<string> GenerateBatchScriptAsync(string goal, string workspacePath)
    {
        if (await IsLocalExpressOnlineAsync().ConfigureAwait(false))
        {
            return await LocalExpressCompleteAsync(goal, "BatchFileGenerator", workspacePath).ConfigureAwait(false);
        }
        else
        {
            string sysPrompt = "You are an expert Windows batch script generator. Generate ONLY the batch script content. " +
                               "Start with @echo off. Do not include markdown fences, backticks, or conversational text. " +
                               "Absolutely no Linux/macOS commands. Output pure Windows batch code.";
            return await CompleteAsync(sysPrompt, $"Create a batch script for this goal: {goal}").ConfigureAwait(false);
        }
    }

    // ── Complete (full response) ─────────────────────────────────────────────

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, float temperature = 0.2f)
    {
        if (await IsLocalExpressOnlineAsync().ConfigureAwait(false))
        {
            string combinedPrompt = $"{systemPrompt}\n\n{userPrompt}";
            return await LocalExpressCompleteAsync(combinedPrompt).ConfigureAwait(false);
        }

        if (_config.Provider == "ollama")
            return await OllamaCompleteAsync(systemPrompt, userPrompt, temperature);

        if (_config.Provider == "gemini")
            return await GeminiCompleteAsync(systemPrompt, userPrompt, temperature);

        return await OllamaCompleteAsync(systemPrompt, userPrompt, temperature);
    }

    // ── Streaming ────────────────────────────────────────────────────────────

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userPrompt, float temperature = 0.2f)
    {
        if (await IsLocalExpressOnlineAsync().ConfigureAwait(false))
        {
            string combinedPrompt = $"{systemPrompt}\n\n{userPrompt}";
            string result = await LocalExpressCompleteAsync(combinedPrompt).ConfigureAwait(false);
            yield return result;
            yield break;
        }

        if (_config.Provider == "ollama")
        {
            await foreach (var chunk in OllamaStreamAsync(systemPrompt, userPrompt, temperature))
                yield return chunk;
        }
        else
        {
            // Non-streaming fallback: complete then yield
            string result = await CompleteAsync(systemPrompt, userPrompt, temperature);
            yield return result;
        }
    }

    // ── Ollama ───────────────────────────────────────────────────────────────

    private async Task<string> OllamaCompleteAsync(string sys, string user, float temp)
    {
        var payload = new
        {
            model  = _config.Model,
            prompt = $"<system>\n{sys}\n</system>\n\n{user}",
            stream = false,
            options = new { temperature = temp }
        };

        try
        {
            var resp = await _http.PostAsync(
                $"{_config.Endpoint}/api/generate",
                new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json"));

            string body = await resp.Content.ReadAsStringAsync();
            var obj = JObject.Parse(body);
            return obj["response"]?.Value<string>() ?? "[empty response]";
        }
        catch (Exception ex)
        {
            return $"[LLM Error: {ex.Message}]";
        }
    }

    private async IAsyncEnumerable<string> OllamaStreamAsync(string sys, string user, float temp)
    {
        var payload = new
        {
            model  = _config.Model,
            prompt = $"<system>\n{sys}\n</system>\n\n{user}",
            stream = true,
            options = new { temperature = temp }
        };

        HttpResponseMessage? resp = null;
        string? earlyError = null;
        try
        {
            resp = await _http.PostAsync(
                $"{_config.Endpoint}/api/generate",
                new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json"));
        }
        catch (Exception ex) { earlyError = ex.Message; }

        if (earlyError != null) { yield return $"[LLM Error: {earlyError}]"; yield break; }
        if (resp == null)        { yield return "[LLM Error: no response]"; yield break; }

        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var obj = JObject.Parse(line);
            string? chunk = obj["response"]?.Value<string>();
            if (!string.IsNullOrEmpty(chunk)) yield return chunk;
            if (obj["done"]?.Value<bool>() == true) break;
        }
    }

    // ── Gemini ───────────────────────────────────────────────────────────────

    private async Task<string> GeminiCompleteAsync(string sys, string user, float temp)
    {
        string apiKey = _config.ApiKey ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
            return "[LLM Error: GEMINI_API_KEY not set]";

        var payload = new
        {
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = $"{sys}\n\n{user}" } } }
            },
            generationConfig = new { temperature = temp, maxOutputTokens = _config.MaxTokens }
        };

        string model = _config.Model.StartsWith("gemini") ? _config.Model : "gemini-2.5-pro";
        string url   = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        try
        {
            var resp = await _http.PostAsync(url,
                new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json"));
            string body = await resp.Content.ReadAsStringAsync();
            var obj      = JObject.Parse(body);
            return obj["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>() ?? "[empty]";
        }
        catch (Exception ex) { return $"[LLM Error: {ex.Message}]"; }
    }

    // ── Config ───────────────────────────────────────────────────────────────

    private static LlmConfig LoadConfig(string? configPath)
    {
        configPath ??= Path.Combine(SyncroDb.GetDbRoot(), "config.json");
        if (!File.Exists(configPath)) return LlmConfig.Default;
        try
        {
            var obj = JObject.Parse(File.ReadAllText(configPath));
            var llm = obj["llm"];
            return new LlmConfig
            {
                Provider  = llm?["provider"]?.Value<string>()  ?? "ollama",
                Model     = llm?["model"]?.Value<string>()     ?? "codellama:13b",
                Endpoint  = llm?["endpoint"]?.Value<string>()  ?? "http://localhost:11434",
                MaxTokens = llm?["maxTokens"]?.Value<int>()    ?? 4096,
                ApiKey    = llm?["apiKey"]?.Value<string>()
            };
        }
        catch { return LlmConfig.Default; }
    }

    private class LlmConfig
    {
        public string  Provider  { get; init; } = "ollama";
        public string  Model     { get; init; } = "codellama:13b";
        public string  Endpoint  { get; init; } = "http://localhost:11434";
        public int     MaxTokens { get; init; } = 4096;
        public string? ApiKey    { get; init; }

        public static LlmConfig Default => new();
    }
}
