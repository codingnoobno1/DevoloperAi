using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>One endpoint the backend is missing and should be generated to satisfy.</summary>
    public sealed class EndpointSpec
    {
        public HttpVerb Method { get; set; }
        public string PathTemplate { get; set; } = "";
        public string? Description { get; set; }
        public List<ApiField> RequestFields { get; set; } = new();
        public List<ApiField> ResponseFields { get; set; } = new();
    }

    /// <summary>One file the AI proposed. Not written to disk by the generator itself —
    /// callers review/approve and apply it via <c>WriteBackendFilesTool</c>.</summary>
    public sealed class GeneratedBackendFile
    {
        public string Path { get; set; } = "";

        public string Content { get; set; } = "";

        /// <summary>"create" or "modify".</summary>
        public string Action { get; set; } = "create";
    }

    public sealed class BackendGenerationRequest
    {
        public string BackendPath { get; set; } = "";
        public BackendStack Stack { get; set; } = BackendStack.Unknown;

        /// <summary>Optional architecture hint matching a ProjectTemplates front-matter value, e.g. "ntier", "clean".</summary>
        public string? Architecture { get; set; }

        public List<EndpointSpec> Endpoints { get; set; } = new();

        /// <summary>Routes the backend already exposes, so generated code doesn't collide with them.</summary>
        public List<BackendRoute> ExistingRoutes { get; set; } = new();

        /// <summary>Optional raw template guidance (e.g. the contents of ProjectTemplates/express.md)
        /// to ground the AI in this stack's real folder conventions.</summary>
        public string? TemplateGuidance { get; set; }
    }

    public sealed class BackendGenerationResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Explanation { get; set; }
        public List<GeneratedBackendFile> Files { get; set; } = new();
    }
}
