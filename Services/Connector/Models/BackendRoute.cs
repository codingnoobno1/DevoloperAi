using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>
    /// One HTTP route exposed by the backend repo, normalized so it can be matched against a
    /// frontend <c>ApiCallSite</c> (added in a later phase). Produced by the
    /// <c>BackendContractResolver</c> from either a swagger contract or a source scan.
    /// </summary>
    public sealed class BackendRoute
    {
        /// <summary>Stable identity = "{METHOD} {PathTemplate}", used for de-dup and matching.</summary>
        public string Id => $"{Method.ToString().ToUpperInvariant()} {PathTemplate}";

        public HttpVerb Method { get; set; }

        /// <summary>Path with parameters normalized to <c>{name}</c>, e.g. <c>/api/users/{id}</c>.</summary>
        public string PathTemplate { get; set; } = "";

        /// <summary>Absolute source file the route was found in (null when only present in swagger).</summary>
        public string? FilePath { get; set; }

        /// <summary>1-based line number in <see cref="FilePath"/> (null when from swagger).</summary>
        public int? Line { get; set; }

        public RouteSource Source { get; set; }

        public string? OperationId { get; set; }

        public string? Summary { get; set; }

        public List<ApiField> RequestSchema { get; set; } = new();

        public List<ApiField> ResponseSchema { get; set; } = new();
    }
}
