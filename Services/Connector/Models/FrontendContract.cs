using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>
    /// The resolved API-call surface of one frontend repo — the frontend mirror of
    /// <see cref="BackendContract"/>. This is what the matcher pairs against backend routes.
    /// </summary>
    public sealed class FrontendContract
    {
        public string FrontendPath { get; set; } = "";

        public FrontendStack Stack { get; set; } = FrontendStack.Unknown;

        public List<ApiCallSite> Calls { get; set; } = new();

        /// <summary>Human-readable diagnostics about extraction (stack detected, files scanned, ...).</summary>
        public List<string> Notes { get; set; } = new();
    }
}
