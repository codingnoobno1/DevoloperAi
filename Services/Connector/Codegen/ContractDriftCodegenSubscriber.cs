using System;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Contract;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Connector.Codegen
{
    /// <summary>
    /// Phase 3 loop closure — subscribes to <see cref="ContractDriftDetected"/> and triggers
    /// <see cref="DartBlocCodegen"/> to regenerate the Flutter-side Dart files for every
    /// breaking change.
    ///
    /// Dependency chain: ContractDriftDetected → locate FE/BE roots → resolve BackendContract
    /// → DartBlocCodegen.GenerateAsync → writes files under {frontendRoot}/lib/features/.
    ///
    /// Runs fully async on a background thread so the EventBus subscriber returns immediately
    /// and does not block the watcher pipeline.
    /// </summary>
    public sealed class ContractDriftCodegenSubscriber : IDisposable
    {
        private readonly IBackendContractResolver _backend;
        private readonly IContractProjectLocator _locator;
        private readonly DartBlocCodegen _codegen;
        private readonly IDisposable _sub;

        public ContractDriftCodegenSubscriber(
            IEventBus bus,
            IBackendContractResolver backend,
            IContractProjectLocator locator,
            DartBlocCodegen codegen)
        {
            _backend = backend;
            _locator = locator;
            _codegen = codegen;
            _sub = bus.Subscribe<ContractDriftDetected>(e => _ = Task.Run(() => HandleAsync(e)));
        }

        private async Task HandleAsync(ContractDriftDetected e)
        {
            try
            {
                var (backendPath, frontendPath) = await _locator.LocateAsync(e.WorkspacePath);
                if (string.IsNullOrWhiteSpace(backendPath) || string.IsNullOrWhiteSpace(frontendPath))
                    return;

                var contract = await _backend.ResolveAsync(backendPath);
                var result = await _codegen.GenerateAsync(contract, frontendPath);

                System.Diagnostics.Debug.WriteLine(
                    $"[ContractCodegen] {e.WorkspacePath}: {result.FilesWritten} files for " +
                    $"[{string.Join(", ", result.FeaturesGenerated)}] " +
                    $"(triggered by {e.BreakingCount} breaking change(s))");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ContractCodegen] Failed: {ex.Message}");
            }
        }

        public void Dispose() => _sub.Dispose();
    }
}
