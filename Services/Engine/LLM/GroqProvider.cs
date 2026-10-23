using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Engine.LLM
{
    public class GroqProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private string _apiKey = string.Empty;
        private const string GroqApiUrl = "https://api.groq.com/openai/v1/chat/completions";
        private const string GroqModelsUrl = "https://api.groq.com/openai/v1/models";
        
        // llama-3.1-8b-instant was retired for free/developer accounts on 2026-08-16.
        private const string DefaultModel = "openai/gpt-oss-20b";
        // gpt-oss-120b has the best tool-calling precision on Groq (validated 2026-07-10).
        private const string AgentModel = "openai/gpt-oss-120b";

        // ── Runtime options, pushed from Agent Settings (agentsettings.md §C.4) ──
        /// <summary>Model for the agent loop; null/empty = the validated default above.</summary>
        public string? AgentModelOverride { get; set; }
        public double AgentTemperature { get; set; } = 0.2;
        /// <summary>Max LLM round-trips per agent task (each may call several tools).</summary>
        public int MaxAgentIterations { get; set; } = 10;
        /// <summary>Max tool calls per agent task; 0 = unlimited. Further calls are refused, not run.</summary>
        public int MaxToolCalls { get; set; } = 25;
        /// <summary>When true, no request leaves the machine.</summary>
        public bool OfflineMode { get; set; }
        /// <summary>0 = unlimited.</summary>
        public long DailyTokenBudget { get; set; }
        public Syncro.Desktop.Services.Agent.Policy.TokenUsageTracker? Usage { get; set; }
        public bool HasKey => !string.IsNullOrEmpty(_apiKey);
        public string? LastValidationError { get; private set; }

        /// <summary>Returns a user-facing reason when a request must not be sent, else null.</summary>
        private string? Refusal()
        {
            if (OfflineMode) return "Offline mode is on in Agent Settings — no requests are sent to the model provider.";
            if (DailyTokenBudget > 0 && Usage != null && Usage.UsedToday >= DailyTokenBudget)
                return $"Daily token budget reached ({Usage.UsedToday:N0} / {DailyTokenBudget:N0}). Raise it in Agent Settings.";
            return null;
        }

        private void Track(JObject json) => Usage?.Add((long?)json["usage"]?["total_tokens"] ?? 0);

        public GroqProvider()
        {
            _httpClient = new HttpClient();
            // Groq WAF blocks the default urllib User-Agent with HTTP 403; set an explicit one.
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Syncro.Desktop/1.0");
            var envKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                Initialize(envKey);
            }
        }

        public void Initialize(string apiKey)
        {
            _apiKey = apiKey.Trim();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<bool> ValidateConnectionAsync(string apiKey)
        {
            LastValidationError = null;

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                LastValidationError = "Enter a Groq API key before testing the connection.";
                return false;
            }

            try
            {
                // Authenticate independently of a chat model. A retired or restricted model must not
                // make a valid credential look invalid.
                using var request = new HttpRequestMessage(HttpMethod.Get, GroqModelsUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                using var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                    return true;

                var providerMessage = await ReadProviderErrorAsync(response);
                LastValidationError = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Groq rejected the credential (401). Revoke it and create a new API key.",
                    System.Net.HttpStatusCode.Forbidden => "Groq accepted the credential but denied access (403). Check the key's project and permissions.",
                    System.Net.HttpStatusCode.TooManyRequests => "Groq rate-limited the test (429). Wait briefly and try again; the key is not necessarily invalid.",
                    _ when (int)response.StatusCode >= 500 => $"Groq is temporarily unavailable ({(int)response.StatusCode}). Try again later.",
                    _ => $"Groq connection test failed ({(int)response.StatusCode}){(string.IsNullOrWhiteSpace(providerMessage) ? "." : $": {providerMessage}")}",
                };
                return false;
            }
            catch (TaskCanceledException)
            {
                LastValidationError = "The Groq connection test timed out. Check the network and try again.";
                return false;
            }
            catch (HttpRequestException ex)
            {
                LastValidationError = $"Could not reach Groq: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                LastValidationError = $"Groq connection test failed: {ex.Message}";
                return false;
            }
        }

        private static async Task<string?> ReadProviderErrorAsync(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body)) return null;
                var message = JObject.Parse(body)["error"]?["message"]?.ToString();
                if (string.IsNullOrWhiteSpace(message)) return null;
                return message.Length <= 300 ? message : message[..300] + "…";
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GenerateResponseAsync(string systemContext, string userPrompt)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Groq API Key is not initialized.");
            }
            if (Refusal() is { } refused) return refused;

            var requestBody = new
            {
                model = DefaultModel,
                messages = new[]
                {
                    new { role = "system", content = systemContext },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.2 // Lower temp for more deterministic code/patch generation
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(GroqApiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseString);
                Track(json);

                var reply = json["choices"]?[0]?["message"]?["content"]?.ToString();
                return reply ?? throw new Exception("Empty response from Groq.");
            }
            catch (Exception ex)
            {
                return $"Error connecting to Groq: {ex.Message}";
            }
        }

        public async Task<T?> GenerateStructuredResponseAsync<T>(string systemContext, string userPrompt) where T : class
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Groq API Key is not initialized.");
            }

            if (Refusal() != null) return null;

            // Ensure the system prompt explicitly asks for JSON, as required by Groq's json_object mode.
            if (!systemContext.Contains("JSON", StringComparison.OrdinalIgnoreCase))
            {
                systemContext += " You must respond in valid JSON.";
            }

            var requestBody = new
            {
                model = DefaultModel,
                messages = new[]
                {
                    new { role = "system", content = systemContext },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1, // Very low temperature for structured data
                response_format = new { type = "json_object" }
            };

            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(GroqApiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseString);
                Track(json);

                var reply = json["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(reply))
                    return null;

                return JsonConvert.DeserializeObject<T>(reply);
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> RunAgentLoopAsync(string systemContext, string userPrompt, System.Collections.Generic.IEnumerable<Syncro.Desktop.Services.Engine.Mcp.IMcpTool> availableTools, System.Func<string, string, Task<string>> toolExecutor)
        {
            if (string.IsNullOrEmpty(_apiKey)) throw new InvalidOperationException("Groq API Key is not initialized.");
            if (Refusal() is { } refused) return refused;

            var messages = new System.Collections.Generic.List<object>
            {
                new { role = "system", content = systemContext },
                new { role = "user", content = userPrompt }
            };

            var tools = new System.Collections.Generic.List<object>();
            foreach (var tool in availableTools)
            {
                // Groq expects function names to match ^[a-zA-Z0-9_-]{1,64}$
                var safeName = tool.Name.Replace(" ", "_").Replace("-", "_");

                // A tool's InputSchema is a loose descriptor string, not guaranteed valid JSON — a
                // single malformed schema must NOT crash the whole agent loop. Fall back to a
                // permissive object so the tool is still offered (the model can pass any args).
                JObject parameters;
                try { parameters = JObject.Parse(tool.InputSchema); }
                catch { parameters = new JObject { ["type"] = "object" }; }

                tools.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = safeName,
                        description = tool.Description,
                        parameters
                    }
                });
            }

            int loopCount = 0;
            int toolCalls = 0;
            int maxLoops = MaxAgentIterations > 0 ? MaxAgentIterations : 10;
            while (loopCount++ < maxLoops)
            {
                if (loopCount > 1 && Refusal() is { } stop) return stop;
                var requestBody = new
                {
                    model = string.IsNullOrWhiteSpace(AgentModelOverride) ? AgentModel : AgentModelOverride,
                    messages = messages,
                    tools = tools.Count > 0 ? tools : null,
                    tool_choice = tools.Count > 0 ? "auto" : null,
                    temperature = AgentTemperature
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(GroqApiUrl, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    return $"Groq API Error: {response.StatusCode} - {err}";
                }

                var responseString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(responseString);
                Track(json);
                var message = json["choices"]?[0]?["message"];
                if (message == null) return "Error: Invalid response from Groq.";

                var assistantMessage = new JObject
                {
                    ["role"] = "assistant",
                    ["content"] = message["content"]
                };
                if (message["tool_calls"] != null)
                {
                    assistantMessage["tool_calls"] = message["tool_calls"];
                }
                messages.Add(assistantMessage);

                if (message["tool_calls"] != null && message["tool_calls"].HasValues)
                {
                    foreach (var toolCall in message["tool_calls"])
                    {
                        var callId = toolCall["id"]?.ToString();
                        var name = toolCall["function"]?["name"]?.ToString();
                        var args = toolCall["function"]?["arguments"]?.ToString();

                        string result;
                        try
                        {
                            // Map safeName back to tool.Name or let the orchestrator handle it.
                            // Better: pass safe name down to toolExecutor mapping.
                            // To keep it simple, we assume toolExecutor can map or we match the tool list here.
                            var actualTool = System.Linq.Enumerable.FirstOrDefault(availableTools, t => t.Name.Replace(" ", "_").Replace("-", "_") == name);
                            if (MaxToolCalls > 0 && ++toolCalls > MaxToolCalls)
                            {
                                result = $"Error: tool call limit ({MaxToolCalls}) for this task reached (Agent Settings). Finish with what you have.";
                            }
                            else if (actualTool != null)
                            {
                                result = await toolExecutor(actualTool.Name, args);
                            }
                            else
                            {
                                result = $"Error: Tool {name} not found.";
                            }
                        }
                        catch (Exception ex)
                        {
                            result = $"Error executing tool {name}: {ex.Message}";
                        }

                        messages.Add(new
                        {
                            role = "tool",
                            tool_call_id = callId,
                            name = name,
                            content = result
                        });
                    }
                }
                else
                {
                    return message["content"]?.ToString() ?? "";
                }
            }
            return "Error: Agent loop exceeded maximum iterations.";
        }
    }
}
