using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Engine.Core
{
    public interface ISessionManager
    {
        /// <summary>
        /// Starts a new session or loads an existing one for the project.
        /// </summary>
        Task InitializeSessionAsync(string projectId);

        /// <summary>
        /// Records a user request and the LLM's decisions into the active sprint.
        /// </summary>
        Task RecordInteractionAsync(string prompt, string LLMDecision);

        /// <summary>
        /// Retrieves the recent conversation and goals to provide hindsight context.
        /// </summary>
        Task<string> GetSessionContextAsync();
    }
}
