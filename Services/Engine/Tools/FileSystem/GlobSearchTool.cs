using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileSystemGlobbing;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    public class GlobSearchTool : IMcpTool
    {
        public string Name => "GlobSearch";
        public string Description => "Finds files matching a glob pattern (e.g., '**/*.cs') within a directory.";
        public string InputSchema => "{ \"searchDirectory\": \"string\", \"pattern\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { searchDirectory = "", pattern = "" });
                if (string.IsNullOrEmpty(input?.searchDirectory) || !Directory.Exists(input.searchDirectory))
                {
                    return Task.FromResult("{ \"error\": \"Valid searchDirectory is required.\" }");
                }
                if (string.IsNullOrEmpty(input.pattern))
                {
                    return Task.FromResult("{ \"error\": \"Pattern is required.\" }");
                }

                Matcher matcher = new Matcher();
                matcher.AddInclude(input.pattern);

                var matchingFiles = matcher.GetResultsInFullPath(input.searchDirectory).ToList();

                var result = new
                {
                    matchesCount = matchingFiles.Count,
                    files = matchingFiles
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
