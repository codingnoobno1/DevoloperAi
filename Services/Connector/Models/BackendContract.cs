using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>
    /// The resolved API surface of one backend repo: the detected stack, where the routes came
    /// from, and the full route list. This is what the connector graph maps frontend calls against.
    /// </summary>
    public sealed class BackendContract
    {
        public string BackendPath { get; set; } = "";

        public BackendStack Stack { get; set; } = BackendStack.Unknown;

        /// <summary>How the bulk of <see cref="Routes"/> were obtained.</summary>
        public RouteSource PrimarySource { get; set; } = RouteSource.RouteScan;

        /// <summary>Absolute path to the swagger/openapi file, when one was found.</summary>
        public string? SwaggerPath { get; set; }

        public List<BackendRoute> Routes { get; set; } = new();

        /// <summary>Human-readable diagnostics about the resolution (what was found / skipped / fell back).</summary>
        public List<string> Notes { get; set; } = new();
    }
}
