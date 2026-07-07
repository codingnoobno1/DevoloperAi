using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Repository
{
    public class DetectProjectTypeTool : IMcpTool
    {
        public string Name => "DetectProjectType";
        public string Description => "Scans the root directory for signature configuration files to detect the primary programming language and framework.";
        public string InputSchema => "{ \"workspacePath\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "" });
                if (string.IsNullOrEmpty(input?.workspacePath) || !Directory.Exists(input.workspacePath))
                {
                    return Task.FromResult("{ \"error\": \"Invalid workspace path.\" }");
                }

                string path = input.workspacePath;
                string language = "Unknown";
                string framework = "Unknown";
                int confidence = 0;

                // Deterministic checks
                if (File.Exists(Path.Combine(path, "pubspec.yaml")))
                {
                    language = "Dart";
                    framework = "Flutter";
                    confidence = 100;
                }
                else if (Directory.GetFiles(path, "*.csproj").Any() || Directory.GetFiles(path, "*.sln").Any())
                {
                    language = "C#";
                    framework = ".NET";
                    confidence = 100;
                }
                else if (File.Exists(Path.Combine(path, "package.json")))
                {
                    language = "JavaScript/TypeScript";
                    framework = "Node.js (React/Vue/Angular)";
                    confidence = 90;
                }
                else if (File.Exists(Path.Combine(path, "requirements.txt")) || File.Exists(Path.Combine(path, "Pipfile")))
                {
                    language = "Python";
                    framework = "Python App";
                    confidence = 90;
                }
                else if (File.Exists(Path.Combine(path, "go.mod")))
                {
                    language = "Go";
                    framework = "Go Module";
                    confidence = 100;
                }
                else if (File.Exists(Path.Combine(path, "pom.xml")) || File.Exists(Path.Combine(path, "build.gradle")))
                {
                    language = "Java/Kotlin";
                    framework = "JVM App";
                    confidence = 90;
                }

                var result = new
                {
                    language = language,
                    framework = framework,
                    confidenceScore = confidence
                };

                return Task.FromResult(JsonConvert.SerializeObject(result));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"{{ \"error\": \"{ex.Message}\" }}");
            }
        }
    }
}
