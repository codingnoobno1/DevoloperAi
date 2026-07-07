namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>HTTP verbs the connector understands. Maps 1:1 to both frontend calls and backend routes.</summary>
    public enum HttpVerb
    {
        Get,
        Post,
        Put,
        Patch,
        Delete,
        Head,
        Options
    }

    /// <summary>Where a <see cref="BackendRoute"/> was discovered.</summary>
    public enum RouteSource
    {
        /// <summary>Parsed from a swagger.json / openapi.(yaml|json) contract.</summary>
        Swagger,

        /// <summary>Heuristically scanned from backend source files.</summary>
        RouteScan,

        /// <summary>AI-generated to satisfy an unmatched frontend call (added in a later phase).</summary>
        Generated
    }

    /// <summary>Backend framework detected for a repo. Drives which scanner/template applies.</summary>
    public enum BackendStack
    {
        Unknown,
        Express,
        FastApi,
        Flask,
        Django,
        SpringBoot,
        AspNetCore
    }

    /// <summary>Helpers for translating loose verb strings into <see cref="HttpVerb"/>.</summary>
    public static class HttpVerbExtensions
    {
        public static HttpVerb ToHttpVerb(this string method)
        {
            return (method ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "GET" => HttpVerb.Get,
                "POST" => HttpVerb.Post,
                "PUT" => HttpVerb.Put,
                "PATCH" => HttpVerb.Patch,
                "DELETE" => HttpVerb.Delete,
                "HEAD" => HttpVerb.Head,
                "OPTIONS" => HttpVerb.Options,
                _ => HttpVerb.Get
            };
        }
    }
}
