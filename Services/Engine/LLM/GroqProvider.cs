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
        
        // As requested by user, default to Llama 3 API model hosted on Groq
        private const string DefaultModel = "llama3-8b-8192"; 

        public GroqProvider()
        {
            _httpClient = new HttpClient();
            var envKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                Initialize(envKey);
            }
        }

        public void Initialize(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        public async Task<bool> ValidateConnectionAsync(string apiKey)
        {
            try
            {
                // A lightweight call to verify the key.
                var requestBody = new
                {
                    model = DefaultModel,
                    messages = new[] { new { role = "user", content = "ping" } },
                    max_tokens = 10
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                var request = new HttpRequestMessage(HttpMethod.Post, GroqApiUrl) { Content = content };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GenerateResponseAsync(string systemContext, string userPrompt)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("Groq API Key is not initialized.");
            }

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
                
                var reply = json["choices"]?[0]?["message"]?["content"]?.ToString();
                return reply ?? throw new Exception("Empty response from Groq.");
            }
            catch (Exception ex)
            {
                return $"Error connecting to Groq: {ex.Message}";
            }
        }
    }
}
