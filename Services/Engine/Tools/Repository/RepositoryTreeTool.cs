using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Repository
{
    public class RepositoryTreeTool : IMcpTool
    {
        public string Name => "RepositoryTree";
        public string Description => "Generates a fast, lightweight structural map of the folders and files in the repository.";
        public string InputSchema => "{ \"workspacePath\": \"string\", \"maxDepth\": \"number?\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { workspacePath = "", maxDepth = 3 });
                if (string.IsNullOrEmpty(input?.workspacePath) || !Directory.Exists(input.workspacePath))
                {
                    return Task.FromResult("{ \"error\": \"Invalid workspace path.\" }");
                }

                int depth = input.maxDepth <= 0 ? 3 : input.maxDepth;
                var tree = BuildTree(input.workspacePath, 0, depth);

                var result = new
                {
                    tree = tree
                };

                return Task.FromResult(JsonConvert.SerializeObject(result));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"{{ \"error\": \"{ex.Message}\" }}");
            }
        }

        private List<object> BuildTree(string path, int currentDepth, int maxDepth)
        {
            var nodes = new List<object>();
            if (currentDepth >= maxDepth) return nodes;

            try
            {
                var directories = Directory.GetDirectories(path)
                    .Where(d => !IsNoiseDirectory(new DirectoryInfo(d).Name))
                    .OrderBy(d => d);

                foreach (var dir in directories)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    nodes.Add(new
                    {
                        type = "directory",
                        name = dirInfo.Name,
                        children = BuildTree(dir, currentDepth + 1, maxDepth)
                    });
                }

                var files = Directory.GetFiles(path)
                    .OrderBy(f => f);

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    nodes.Add(new
                    {
                        type = "file",
                        name = fileInfo.Name
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip restricted folders
            }

            return nodes;
        }

        private bool IsNoiseDirectory(string name)
        {
            var noise = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git", "node_modules", "bin", "obj", "build", "dist", ".vs", ".idea", "packages"
            };
            return noise.Contains(name);
        }
    }
}
