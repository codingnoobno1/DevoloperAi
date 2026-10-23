using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Universe.Indexing;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Workspace.Index
{
    /// <summary>
    /// Phase 2 — watcher-driven IndexEngine subscriber (idearchitecture.md §Phase 2).
    ///
    /// WorkspaceOpened → full <see cref="WorkspaceIndexer.IndexAsync"/> in the background.
    /// FileChanged     → 2-second debounce per workspace → incremental re-index → <see cref="WorkspaceIndexed"/>.
    ///
    /// The debounce cancels any pending re-index when new changes arrive, so a burst of
    /// rapid saves (auto-format, code-gen) results in a single index pass, not N.
    ///
    /// AstCacheManager's signature hash handles invalidation automatically: if any file's
    /// mtime changed, the hash differs from the stored one and WorkspaceIndexer runs the
    /// full AST engine. No manual cache busting needed.
    /// </summary>
    public sealed class IndexEngineSubscriber : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly WorkspaceIndexer _indexer;
        private readonly CrossLanguageMapper _mapper;
        private readonly IDisposable _openedSub;
        private readonly IDisposable _changedSub;

        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending
            = new(StringComparer.OrdinalIgnoreCase);

        public IndexEngineSubscriber(IEventBus bus, WorkspaceIndexer indexer, CrossLanguageMapper mapper)
        {
            _bus = bus;
            _indexer = indexer;
            _mapper = mapper;
            _openedSub = bus.Subscribe<WorkspaceOpened>(OnWorkspaceOpened);
            _changedSub = bus.Subscribe<FileChanged>(OnFileChanged);
        }

        private void OnWorkspaceOpened(WorkspaceOpened e)
            => _ = Task.Run(() => RunIndexAsync(e.WorkspacePath, CancellationToken.None));

        private void OnFileChanged(FileChanged e)
        {
            var cts = _pending.AddOrUpdate(
                e.WorkspacePath,
                _ => new CancellationTokenSource(),
                (_, old) => { old.Cancel(); return new CancellationTokenSource(); });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2_000, cts.Token);
                    await RunIndexAsync(e.WorkspacePath, cts.Token);
                }
                catch (OperationCanceledException) { }
            });
        }

        private async Task RunIndexAsync(string workspacePath, CancellationToken ct)
        {
            try
            {
                var result = await _indexer.IndexAsync(workspacePath, ct);

                // Cross-language mapping runs over the symbols the indexer just wrote, so it must
                // follow IndexAsync. A mapping failure must not suppress the WorkspaceIndexed event.
                try
                {
                    var map = _mapper.Map(result.WorkspaceId);
                    if (map.Clusters.Count > 0)
                        System.Diagnostics.Debug.WriteLine($"[CrossLanguage] {workspacePath}: {map.Summary}");
                }
                catch (Exception mapEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[CrossLanguage] Mapping failed: {mapEx.Message}");
                }

                _bus.Publish(new WorkspaceIndexed(workspacePath, result.Files));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IndexEngine] {workspacePath}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _openedSub.Dispose();
            _changedSub.Dispose();
            foreach (var cts in _pending.Values) cts.Cancel();
        }
    }
}
