using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Core
{
    public interface IKnowledgeEngine
    {
        /// <summary>
        /// Stores the Hindsight (Decisions, Architecture Impact) after a patch is applied.
        /// </summary>
        Task StoreHindsightAsync(string projectId, string decision, string filesChanged, string architectureImpact);

        /// <summary>
        /// Retrieves the architectural graph and hindsight for context building.
        /// </summary>
        Task<string> RetrieveProjectKnowledgeAsync(string projectId);
    }
}
