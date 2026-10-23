using System;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// Lets windows without the main router (the IDE window) ask the main window to open
    /// /settings/agent on a given tab. MainLayout subscribes and navigates.
    /// </summary>
    public sealed class AgentSettingsLink
    {
        public event Action<string>? OpenRequested;
        public void Open(string tab = "model") => OpenRequested?.Invoke(tab);
    }
}
