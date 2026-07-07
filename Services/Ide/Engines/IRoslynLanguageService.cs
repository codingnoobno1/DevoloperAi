using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

namespace Syncro.Desktop.Services.Ide.Engines
{
    public interface IRoslynLanguageService
    {
        /// <summary>
        /// Loads the workspace for the given project path.
        /// </summary>
        Task LoadWorkspaceAsync(string rootPath);

        /// <summary>
        /// Updates the content of a document in the active workspace.
        /// </summary>
        Task UpdateDocumentAsync(string uri, string content);

        /// <summary>
        /// Gets code completions at the specified position.
        /// </summary>
        Task<IEnumerable<CompletionItemDto>> GetCompletionsAsync(string uri, int line, int column);

        /// <summary>
        /// Gets hover information at the specified position.
        /// </summary>
        Task<HoverDto?> GetHoverAsync(string uri, int line, int column);

        /// <summary>
        /// Gets diagnostics (problems) for a specific document or the whole workspace.
        /// </summary>
        Task<IEnumerable<DiagnosticDto>> GetDiagnosticsAsync(string uri);
    }

    public class CompletionItemDto
    {
        public string Label { get; set; } = "";
        public string InsertText { get; set; } = "";
        public string Detail { get; set; } = "";
        public int Kind { get; set; } // Monaco CompletionItemKind
    }

    public class HoverDto
    {
        public string Content { get; set; } = "";
    }

    public class DiagnosticDto
    {
        public string Message { get; set; } = "";
        public int StartLineNumber { get; set; }
        public int StartColumn { get; set; }
        public int EndLineNumber { get; set; }
        public int EndColumn { get; set; }
        public int Severity { get; set; } // Monaco MarkerSeverity
        public string Id { get; set; } = "";
    }
}
