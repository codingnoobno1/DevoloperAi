using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Ide.Host
{
    /// <summary>
    /// The built-in editor: the existing Blazor + Monaco shell (driven by <see cref="Engines.IEditorEngine"/>).
    /// Always available and in-process, so it needs no external server — it just tells the window to
    /// render the shell it already hosts.
    /// </summary>
    public sealed class MonacoIdeHost : IIdeHost
    {
        public IdeHostKind Kind => IdeHostKind.Monaco;

        public Task<IdeHostSession> StartAsync(string? workspacePath, CancellationToken ct = default) =>
            Task.FromResult(IdeHostSession.InProcess(Kind, "Monaco (built-in)"));

        public Task StopAsync() => Task.CompletedTask;
    }
}
