using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Syncro.Desktop.Services.Workspace.Events
{
    /// <summary>
    /// Thread-safe, in-process <see cref="IEventBus"/>. Handlers are invoked synchronously on the
    /// publishing thread but isolated in try/catch so one bad subscriber can't break the others or the
    /// publisher. Dispatch reads a snapshot of the handler list so a subscriber may (un)subscribe from
    /// within a handler without corrupting iteration.
    /// </summary>
    public sealed class InMemoryEventBus : IEventBus
    {
        private readonly object _gate = new();
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();

        public void Publish<T>(T evt) where T : WorkspaceEvent
        {
            if (evt is null) return;

            Delegate[] snapshot;
            lock (_gate)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0) return;
                snapshot = list.ToArray();
            }

            foreach (var d in snapshot)
            {
                try { ((Action<T>)d).Invoke(evt); }
                catch (Exception ex) { Debug.WriteLine($"[EventBus] subscriber of {typeof(T).Name} threw: {ex.Message}"); }
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : WorkspaceEvent
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));

            lock (_gate)
            {
                if (!_handlers.TryGetValue(typeof(T), out var list))
                {
                    list = new List<Delegate>();
                    _handlers[typeof(T)] = list;
                }
                list.Add(handler);
            }
            return new Subscription(() =>
            {
                lock (_gate)
                {
                    if (_handlers.TryGetValue(typeof(T), out var list))
                        list.Remove(handler);
                }
            });
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose()
            {
                var d = _dispose;
                _dispose = null;
                d?.Invoke();
            }
        }
    }
}
