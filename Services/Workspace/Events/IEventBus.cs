using System;

namespace Syncro.Desktop.Services.Workspace.Events
{
    /// <summary>
    /// Typed publish/subscribe bus — the substrate engines use to react to each other without direct
    /// references. Publishing is fire-and-forget; a throwing subscriber never breaks the publisher or
    /// other subscribers.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>Deliver <paramref name="evt"/> to every subscriber of its concrete type.</summary>
        void Publish<T>(T evt) where T : WorkspaceEvent;

        /// <summary>Subscribe to events of type <typeparamref name="T"/>. Dispose to unsubscribe.</summary>
        IDisposable Subscribe<T>(Action<T> handler) where T : WorkspaceEvent;
    }
}
