using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Workspace
{
    public class ListProjectsTool : IMcpTool
    {
        public string Name => "ListProjects";
        public string Description => "Scans the active workspace to find all distinct sub-projects (e.g. looking for .csproj or pubspec.yaml inside subfolders).";
        public string InputSchema => "{ \"workspacePath\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "" });
                if (string.IsNullOrEmpty(input?.workspacePath) || !Directory.Exists(input.workspacePath))
                {
                    return Task.FromResult("{ \"error\": \"Valid workspacePath is required.\" }");
                }

                var subProjects = new List<object>();

                // Fast scan up to 2 levels deep
                SearchForProjects(input.workspacePath, subProjects, 0, 2);

                return Task.FromResult(JsonConvert.SerializeObject(new { workspace = input.workspacePath, projects = subProjects }));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"{{ \"error\": \"{ex.Message}\" }}");
            }
        }

        private void SearchForProjects(string currentDir, List<object> subProjects, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth) return;

            try
            {
                bool isProject = false;
                string type = "";

                if (File.Exists(Path.Combine(currentDir, "pubspec.yaml"))) { isProject = true; type = "Flutter"; }
                else if (Directory.GetFiles(currentDir, "*.csproj").Length > 0) { isProject = true; type = ".NET"; }
                else if (File.Exists(Path.Combine(currentDir, "package.json"))) { isProject = true; type = "Node"; }

                if (isProject)
                {
                    subProjects.Add(new { path = currentDir, type = type, name = new DirectoryInfo(currentDir).Name });
                }

                foreach (var dir in Directory.GetDirectories(currentDir))
                {
                    var dirName = new DirectoryInfo(dir).Name.ToLower();
                    if (dirName != "node_modules" && dirName != "bin" && dirName != "obj" && dirName != ".git")
                    {
                        SearchForProjects(dir, subProjects, currentDepth + 1, maxDepth);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
        }
    }
}
