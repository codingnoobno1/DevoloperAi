using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Syncro.Desktop.Services.Ide.Engines;

/// <summary>
/// The editor surface, abstracted (IDE doc §2.4 / W2). The Blazor shell talks to THIS, never to
/// "window.SyncroMonaco" or Monaco directly — so the editor implementation is replaceable without
/// touching the shell. The default implementation is Monaco-backed.
/// </summary>
public interface IEditorEngine
{
    /// <summary>Open (or focus) a file tab in the editor surface.</summary>
    Task OpenAsync(string uri, string content, string language, string fileName);

    /// <summary>Push new content into an already-open editor (e.g. after a revert).</summary>
    Task SetContentAsync(string uri, string content);

    /// <summary>Read the live content of an open editor.</summary>
    Task<string?> GetContentAsync(string uri);

    /// <summary>Render a read-only side-by-side diff into a host element (versioning UI, §6).</summary>
    Task ShowDiffAsync(string hostElementId, string original, string modified, string language);
}

/// <summary>Monaco-backed editor engine. Delegates to the <c>window.SyncroMonaco</c> surface module
/// (and <c>window.SyncroIde</c> for dock-managed tab placement).</summary>
public sealed class MonacoEditorEngine : IEditorEngine
{
    private readonly IJSRuntime _js;
    public MonacoEditorEngine(IJSRuntime js) => _js = js;

    public Task OpenAsync(string uri, string content, string language, string fileName) =>
        _js.InvokeVoidAsync("window.SyncroIde.openFile", uri, content, language, fileName).AsTask();

    public Task SetContentAsync(string uri, string content) =>
        _js.InvokeVoidAsync("window.SyncroMonaco.setContent", uri, content).AsTask();

    public Task<string?> GetContentAsync(string uri) =>
        _js.InvokeAsync<string?>("window.SyncroMonaco.getContent", uri).AsTask();

    public Task ShowDiffAsync(string hostElementId, string original, string modified, string language) =>
        _js.InvokeVoidAsync("window.SyncroMonaco.showDiff", hostElementId, original, modified, language).AsTask();
}
