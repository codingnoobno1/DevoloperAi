using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic; // Added for List

namespace DeveloperAI
{
    public static class AIClient
    {
        private static readonly string groqApiKey = Environment.GetEnvironmentVariable("GROQ_API_KEY") 
                                                    ?? ""; // fallback (unsafe in prod)
        private static readonly string model = "llama3-70b-8192";
        private static readonly HttpClient client = new HttpClient();

        private static string GetSystemPrompt() => 
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

        /// <summary>
        /// Calls the Groq LLM to get a batch script based on the user's prompt and context.
        /// </summary>
        public static async Task<string?> CallLLM(string userPrompt)
        {
            var messages = new[]
            {
                new { role = "system", content = GetSystemPrompt() },
                new { role = "user", content = userPrompt }
            };

            var payload = new
            {
                model = model,
                messages = messages,
                temperature = 0.1, // Very low temperature for consistent output
                top_p = 0.9,
                stream = false,
                max_tokens = 2000
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", groqApiKey);
            request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

            try
            {
                var response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(json);
                    var rawResponse = parsed["choices"]?[0]?["message"]?["content"]?.ToString().Trim();
                    
                    // Clean the response to ensure it's a pure batch script
                    return CleanBatchScriptResponse(rawResponse);
                }
                else
                {
                    Console.WriteLine("HTTP Error: " + response.StatusCode);
                    Console.WriteLine(await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }

            return null;
        }

        private static string? CleanBatchScriptResponse(string? response)
        {
            if (string.IsNullOrEmpty(response))
                return null;

            // Remove common explanatory prefixes
            var lines = response.Split('\n');
            var cleanedLines = new List<string>();
            bool foundScriptStart = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip explanatory text at the beginning
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
                        continue; // Skip explanatory lines
                    }
                }

                if (foundScriptStart)
                {
                    cleanedLines.Add(line);
                }
            }

            return string.Join("\n", cleanedLines).Trim();
        }
    }
}
