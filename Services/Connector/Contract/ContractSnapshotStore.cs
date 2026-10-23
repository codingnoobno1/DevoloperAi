using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>
    /// Persists the baseline <see cref="ContractSnapshot"/> per project at
    /// <c>&lt;projectRoot&gt;/.syncro/contract.baseline.json</c>. The baseline is the "agreed" backend
    /// surface; the Contract Engine diffs the live surface against it to detect breaking drift.
    /// </summary>
    public sealed class ContractSnapshotStore
    {
        private const string Dir = ".syncro";
        private const string File = "contract.baseline.json";

        private static string PathFor(string projectRoot) => Path.Combine(projectRoot, Dir, File);

        /// <summary>Returns the stored baseline, or null when none has been captured yet.</summary>
        public async Task<ContractSnapshot?> LoadAsync(string projectRoot)
        {
            var path = PathFor(projectRoot);
            if (!System.IO.File.Exists(path)) return null;
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(path);
                return JsonConvert.DeserializeObject<ContractSnapshot>(json);
            }
            catch
            {
                return null; // corrupt/unreadable baseline is treated as "no baseline"
            }
        }

        /// <summary>Writes <paramref name="snapshot"/> as the new baseline.</summary>
        public async Task SaveAsync(string projectRoot, ContractSnapshot snapshot)
        {
            var dir = Path.Combine(projectRoot, Dir);
            Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(snapshot, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(Path.Combine(dir, File), json);
        }

        public bool HasBaseline(string projectRoot) => System.IO.File.Exists(PathFor(projectRoot));
    }
}
