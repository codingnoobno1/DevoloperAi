using System.IO;
using Newtonsoft.Json;

namespace DeveloperAI.BusinessLogic
{
    public static class MetadataManager
    {
        public static void SaveMetadata(ProjectMetadata meta, string projectPath)
        {
            string metaDir = Path.Combine(projectPath, ".projectmeta");
            Directory.CreateDirectory(metaDir);
            string file = Path.Combine(metaDir, "metadata.json");
            File.WriteAllText(file, JsonConvert.SerializeObject(meta, Formatting.Indented));
        }

        public static ProjectMetadata LoadMetadata(string projectPath)
        {
            string file = Path.Combine(projectPath, ".projectmeta", "metadata.json");
            if (!File.Exists(file)) return null;
            return JsonConvert.DeserializeObject<ProjectMetadata>(File.ReadAllText(file));
        }
    }
} 