using System;
using System.Collections.Generic;
using Syncro.Desktop.Services.Connector.ProjectDomain;

namespace Syncro.Desktop.Services.Connector.DevHub
{
    /// <summary>
    /// Singleton shared between UniverseConnector and the DevHub secondary window.
    /// UniverseConnector pushes project mutations here; DevHub reads and subscribes.
    /// </summary>
    public sealed class DevHubState
    {
        private readonly List<ConnectorWorkspaceNode> _projects = new();

        public IReadOnlyList<ConnectorWorkspaceNode> Projects => _projects;

        public event Action? Changed;

        public void SetProjects(IEnumerable<ConnectorWorkspaceNode> projects)
        {
            _projects.Clear();
            _projects.AddRange(projects);
            Changed?.Invoke();
        }

        public void NotifyChanged() => Changed?.Invoke();
    }
}
