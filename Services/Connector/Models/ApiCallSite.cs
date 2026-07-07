using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>Frontend framework detected for a repo. Drives which extractor runs.</summary>
    public enum FrontendStack
    {
        Unknown,
        Flutter,
        NextJs,
        React,
        Vite,
        Angular,
        Blazor
    }

    /// <summary>
    /// One HTTP call discovered in frontend source — the counterpart of a
    /// <see cref="BackendRoute"/>. Path is normalized to the same <c>{param}</c> convention the
    /// backend side uses so the two can be matched directly.
    /// </summary>
    public sealed class ApiCallSite
    {
        /// <summary>Stable identity = "{METHOD} {PathTemplate}", used for de-dup and matching.</summary>
        public string Id => $"{Method.ToString().ToUpperInvariant()} {PathTemplate}";

        public HttpVerb Method { get; set; }

        /// <summary>Path with params normalized to <c>{param}</c>, base URL stripped, e.g. <c>/api/users/{param}</c>.</summary>
        public string PathTemplate { get; set; } = "";

        public string? FilePath { get; set; }

        public int? Line { get; set; }

        /// <summary>Which client library issued the call: dio | http | fetch | axios | HttpClient.</summary>
        public string Client { get; set; } = "";

        public List<ApiField> RequestFields { get; set; } = new();

        public List<ApiField> ResponseFields { get; set; } = new();
    }
}
