using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Reporters;

public class JsonReporter : IAstReporter
{
    public async Task<string> ExportAsync(AstProjectMap map, string outputPath)
    {
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        string fullPath = Path.Combine(outputPath, $"{map.ProjectName}_ast_map.json");
        string json = JsonConvert.SerializeObject(map, Formatting.Indented);
        await File.WriteAllTextAsync(fullPath, json);
        return fullPath;
    }
}
