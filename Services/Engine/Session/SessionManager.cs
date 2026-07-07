using System;
using System.Text;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Engine.Core;

namespace Syncro.Desktop.Services.Engine.Session
{
    public class SessionManager : ISessionManager
    {
        private string _activeProjectId = string.Empty;
        private readonly StringBuilder _conversationHistory = new StringBuilder();

        public Task InitializeSessionAsync(string projectId)
        {
            _activeProjectId = projectId;
            _conversationHistory.Clear();
            _conversationHistory.AppendLine($"Session started for project: {projectId} at {DateTime.Now}");
            return Task.CompletedTask;
        }

        public Task RecordInteractionAsync(string prompt, string LLMDecision)
        {
            _conversationHistory.AppendLine($"[USER]: {prompt}");
            _conversationHistory.AppendLine($"[SYNCRO]: {LLMDecision}");
            return Task.CompletedTask;
        }

        public Task<string> GetSessionContextAsync()
        {
            return Task.FromResult(_conversationHistory.ToString());
        }
    }
}
