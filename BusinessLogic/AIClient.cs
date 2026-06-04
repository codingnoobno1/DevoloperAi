using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;

namespace DeveloperAI.BusinessLogic
{
    public class AIClient
    {
        // private readonly string _groqApiKey;
        // private readonly string _model = "llama3-8b-8192"; // Using a common Groq model
        private readonly HttpClient _client;

        public AIClient()
        {
            // _groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") ??
            //               throw new InvalidOperationException("GROQ_API_KEY environment variable is not set.");
            _client = new HttpClient();
            // _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _groqApiKey);
        }

        public async Task<(string? script, string? error)> CallLLM(string userPrompt, string selectedAIMode, string targetWorkspacePath, string model = "gemini-2.5-flash")
        {
            try
            {
                var requestBody = new { prompt = userPrompt, aiMode = selectedAIMode, workspacePath = targetWorkspacePath, model = model };
                var jsonContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _client.PostAsync("http://localhost:3020/gemini", jsonContent);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                JObject jsonResponse = JObject.Parse(responseBody);

                if (jsonResponse["success"] != null && (bool?)jsonResponse["success"] == true)
                {
                    // Assuming the Express server returns the script in result.candidates[0].content.parts[0].text
                    string? script = jsonResponse["result"]?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    return (script, null);
                }
                else
                {
                    string? errorMessage = jsonResponse["error"]?.ToString() ?? "Unknown error from Express server.";
                    return (null, errorMessage);
                }
            }
            catch (HttpRequestException e)
            {
                return (null, $"Request error: {e.Message}");
            }
            catch (Exception e)
            {
                return (null, $"An unexpected error occurred: {e.Message}");
            }
        }

        private string GetSystemPrompt() =>
            @"You are an expert Windows batch script generator. Your ONLY task is to generate executable batch scripts.

CRITICAL RULES:
1. Generate ONLY the batch script content - NO explanations, NO markdown, NO comments outside the script
2. Start immediately with @echo off or the first batch command
3. Do NOT include phrases like 'Here is the script:', 'Generated script:', or any explanatory text
4. Do NOT use code blocks, backticks, or markdown formatting
5. The output must be a pure, executable batch file that can be saved directly to a .bat file

Your task is to generate comprehensive, production-ready batch scripts that:

1. **Environment Management**: Properly activate and configure development environments (Python, Node.js, Java, .NET, Go, Rust)
2. **Directory Navigation**: Handle working directory changes safely
3. **Port Management**: Check port availability and handle conflicts
4. **Dependency Installation**: Install required libraries and packages
5. **Error Handling**: Include robust error checking and user feedback
6. **Security**: Follow security best practices
7. **User Experience**: Provide clear status messages and progress indicators

**Script Requirements:**
- Always start with @echo off for clean output
- Include proper error checking for each step
- Use color-coded output (green for success, red for errors, yellow for warnings)
- Provide clear status messages
- Handle edge cases and failures gracefully
- Include comments explaining complex operations
- Make scripts reusable and maintainable

**Environment-Specific Instructions:**
- **Python**: Use virtual environments when possible, check pip availability
- **Node.js**: Use npm/yarn appropriately, handle package.json creation
- **Java**: Set up Maven/Gradle projects, handle classpath issues
- **.NET**: Use dotnet CLI, handle SDK versioning
- **Go**: Use go mod, handle GOPATH/GOROOT
- **Rust**: Use cargo, handle toolchain management

**OUTPUT FORMAT - CRITICAL:**
- Generate ONLY the batch script content
- Start with @echo off
- NO explanations, NO markdown, NO code blocks
- NO phrases like 'Here is the script' or 'Generated script'
- Pure batch script that can be executed immediately
- Include all necessary commands and error handling

Remember: You are generating executable batch files, so focus on reliability and user experience. The output must be a pure batch script with no additional text.";

        private string? CleanBatchScriptResponse(string? response)
        {
            if (string.IsNullOrEmpty(response))
                return null;

            var lines = response.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var cleanedLines = new List<string>();
            bool foundScriptStart = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (!foundScriptStart)
                {
                    if (trimmedLine.StartsWith("@echo") ||
                        trimmedLine.StartsWith("echo") ||
                        trimmedLine.StartsWith("REM") ||
                        trimmedLine.StartsWith("::") ||
                        trimmedLine.StartsWith("cd") ||
                        trimmedLine.StartsWith("if") ||
                        trimmedLine.StartsWith("for") ||
                        trimmedLine.StartsWith("set") ||
                        trimmedLine.StartsWith("call") ||
                        trimmedLine.StartsWith("python") ||
                        trimmedLine.StartsWith("node") ||
                        trimmedLine.StartsWith("java") ||
                        trimmedLine.StartsWith("dotnet") ||
                        trimmedLine.StartsWith("go") ||
                        trimmedLine.StartsWith("cargo") ||
                        trimmedLine.StartsWith("npm") ||
                        trimmedLine.StartsWith("pip") ||
                        trimmedLine.StartsWith("mvn") ||
                        trimmedLine.StartsWith("cargo"))
                    {
                        foundScriptStart = true;
                    }
                    else if (trimmedLine.Contains("script") ||
                             trimmedLine.Contains("here") ||
                             trimmedLine.Contains("generated") ||
                             trimmedLine.StartsWith("```") ||
                             trimmedLine.StartsWith("`") ||
                             trimmedLine.StartsWith("#") ||
                             trimmedLine.StartsWith("*"))
                    {
                        continue; 
                    }
                }

                if (foundScriptStart)
                {
                    cleanedLines.Add(line);
                }
            }

            return string.Join(Environment.NewLine, cleanedLines).Trim();
        }

        public bool VerifyApiKeyAsync()
        {
            // Groq API does not have a separate endpoint for API key verification.
            // We assume if CallLLM works, the key is valid.
            return false; 
        }
    }
}
