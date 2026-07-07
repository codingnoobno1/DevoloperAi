using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Syncro.Desktop.Services.AST.Hindsight;

/// <summary>
/// Reads the project's hindsight vector index from <c>.syncro_db/Vectors/*/index.json</c>.
/// Path-based (scans every vector subfolder) so it doesn't depend on the exact project_id.
/// </summary>
public class HindsightVectorStore
{
    private static readonly JsonSerializerSettings Snake = new()
    {
        ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        NullValueHandling = NullValueHandling.Ignore
    };

    public async Task<List<VectorEntry>> LoadAsync(string projectPath)
    {
        var all = new List<VectorEntry>();
        if (string.IsNullOrEmpty(projectPath)) return all;

        var vectorsDir = Path.Combine(projectPath, ".syncro_db", "Vectors");
        if (!Directory.Exists(vectorsDir)) return all;

        foreach (var sub in Directory.GetDirectories(vectorsDir))
        {
            var index = Path.Combine(sub, "index.json");
            if (!File.Exists(index)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(index);
                var list = JsonConvert.DeserializeObject<List<VectorEntry>>(json, Snake);
                if (list != null) all.AddRange(list);
            }
            catch { /* tolerant: skip unreadable index */ }
        }
        return all;
    }

    public async Task<HindsightStats> StatsAsync(string projectPath)
    {
        var entries = await LoadAsync(projectPath);
        var stats = new HindsightStats { VectorCount = entries.Count };
        foreach (var e in entries)
        {
            var key = string.IsNullOrEmpty(e.NodeType) ? "Unknown" : e.NodeType;
            stats.ByType[key] = stats.ByType.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        stats.FileCount = entries.Select(e => e.FilePath).Distinct().Count();
        return stats;
    }
}
