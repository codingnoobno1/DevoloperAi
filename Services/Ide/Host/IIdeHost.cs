using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Ide.Host
{
    /// <summary>
    /// The whole-IDE backend, abstracted. The IDE window asks the active host to start for a workspace
    /// and renders whatever it returns — the in-process Monaco shell, or a WebView pointed at a
    /// code-oss server. This is the seam that lets Syncro swap its custom editor for real VS Code OSS
    /// without the shell knowing which is running.
    /// </summary>
    public interface IIdeHost
    {
        IdeHostKind Kind { get; }

        /// <summary>Start (or reuse) the host for <paramref name="workspacePath"/>.</summary>
        Task<IdeHostSession> StartAsync(string? workspacePath, CancellationToken ct = default);

        /// <summary>Stop the host and release any external process.</summary>
        Task StopAsync();

        /// <summary>
        /// Register one IDE window as a consumer. Hosts are DI singletons shared by every IDE window,
        /// so a window must not tear the host down just because it closed — it releases its claim and
        /// the host stops only when the last window is gone.
        /// </summary>
        void Acquire() { }

        /// <summary>Drop one window's claim; stops the host when no windows remain.</summary>
        Task ReleaseAsync() => Task.CompletedTask;
    }
}
