using System;
using System.IO;
using System.Threading.Tasks;

namespace DeveloperAI.BusinessLogic
{
    public static class ScriptManager
    {
        public static async Task<(string StdOut, string StdErr, int ExitCode)> RunScript(string scriptName, string workingDir, bool logOutput = false)
        {
            string scriptPath = Path.Combine(workingDir, ".scripts", scriptName);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script not found: {scriptPath}");

            string extension = Path.GetExtension(scriptPath).ToLower();
            string command = extension switch
            {
                ".bat" => scriptPath,
                ".cmd" => scriptPath,
                ".sh" => $"bash {scriptPath}",
                ".ps1" => $"powershell -ExecutionPolicy Bypass -File {scriptPath}",
                ".py" => $"python {scriptPath}",
                ".java" => $"java {Path.GetFileNameWithoutExtension(scriptPath)}",
                ".cpp" => $"g++ {scriptPath} -o temp && temp",
                _ => scriptPath
            };

            return await CommandRunner.RunCommand(command, workingDir, logOutput);
        }
    }
} 