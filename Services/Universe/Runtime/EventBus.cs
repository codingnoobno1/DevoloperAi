using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Universe.Runtime
{
    /// <summary>
    /// In-process event bus backed by an unbounded <see cref="Channel{T}"/>. Publish is a
    /// non-blocking channel write; a single dispatch loop drains the channel and fans each event out
    /// to every subscriber, so slow subscribers (a graph upsert) never block fast publishers (a health
    /// probe). One event, many consumers — the graph updater and the dashboard both subscribe.
    /// Durable/cross-process is a later concern (runtime.md §12); in-proc is right for now.
    /// </summary>
    public sealed class EventBus : IEventBus, IDisposable
    {
        private readonly Channel<RuntimeEvent> _channel =
            Channel.CreateUnbounded<RuntimeEvent>(new UnboundedChannelOptions { SingleReader = true });

        private readonly List<Func<RuntimeEvent, Task>> _handlers = new();
        private readonly object _lock = new();

        private CancellationTokenSource? _cts;
        private Task? _loop;
        private int _started;

        public event Action<RuntimeEvent>? OnEvent;

        public void Publish(RuntimeEvent evt)
        {
            if (evt != null) _channel.Writer.TryWrite(evt);
        }

        public IDisposable Subscribe(Func<RuntimeEvent, Task> handler)
        {
            lock (_lock) _handlers.Add(handler);
            return new Unsubscriber(() => { lock (_lock) _handlers.Remove(handler); });
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => DispatchLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _channel.Writer.TryComplete();
        }

        private async Task DispatchLoopAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var evt in _channel.Reader.ReadAllAsync(ct))
                {
                    try { OnEvent?.Invoke(evt); } catch { /* a bad UI handler must not kill the loop */ }

                    Func<RuntimeEvent, Task>[] snapshot;
                    lock (_lock) snapshot = _handlers.ToArray();

                    foreach (var h in snapshot)
                    {
                        try { await h(evt).ConfigureAwait(false); }
                        catch { /* one failing subscriber must not drop the event for others */ }
                    }
                }
            }
            catch (OperationCanceledException) { /* normal shutdown */ }
        }

        public void Dispose()
        {
            Stop();
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action _dispose;
            public Unsubscriber(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }
}
