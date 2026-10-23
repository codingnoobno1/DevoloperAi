namespace Syncro.Desktop.Services.Ide.Host
{
    /// <summary>Which editor backend powers the Syncro IDE surface.</summary>
    public enum IdeHostKind
    {
        /// <summary>The built-in Blazor + Monaco shell (in-process, always available).</summary>
        Monaco,

        /// <summary>Real VS Code OSS (the <c>vscode-main</c> checkout) served over http and shown in a WebView.</summary>
        CodeOss
    }

    /// <summary>How the IDE window should render the active host.</summary>
    public enum IdeHostMode
    {
        /// <summary>Render the in-process Blazor/Monaco shell (no external URL).</summary>
        InProcess,

        /// <summary>Load <see cref="IdeHostSession.Url"/> in a WebView.</summary>
        WebUrl,

        /// <summary>Host could not start; show <see cref="IdeHostSession.Status"/> as guidance.</summary>
        Unavailable
    }

    /// <summary>The result of starting an <see cref="IIdeHost"/> — what the shell should show.</summary>
    public sealed class IdeHostSession
    {
        public IdeHostKind Kind { get; init; }
        public IdeHostMode Mode { get; init; }
        public string? Url { get; init; }
        public bool Ready => Mode != IdeHostMode.Unavailable;
        public string Status { get; init; } = "";
        /// <summary>Actionable next step when not ready (e.g. the build command to run).</summary>
        public string? Remedy { get; init; }

        public static IdeHostSession InProcess(IdeHostKind kind, string status) =>
            new() { Kind = kind, Mode = IdeHostMode.InProcess, Status = status };

        public static IdeHostSession Web(IdeHostKind kind, string url, string status) =>
            new() { Kind = kind, Mode = IdeHostMode.WebUrl, Url = url, Status = status };

        public static IdeHostSession Unavailable(IdeHostKind kind, string status, string? remedy = null) =>
            new() { Kind = kind, Mode = IdeHostMode.Unavailable, Status = status, Remedy = remedy };
    }

    /// <summary>Configuration for the IDE host layer (DI singleton; overridable from settings).</summary>
    public sealed class IdeHostOptions
    {
        /// <summary>Which backend to use. Defaults to <see cref="IdeHostKind.CodeOss"/> (real VS Code OSS)
        /// per the Code-OSS-first architecture; the shell auto-falls back to Monaco if it can't start.
        /// See idearchitecture.md.</summary>
        public IdeHostKind Kind { get; set; } = IdeHostKind.CodeOss;

        /// <summary>Absolute path to the VS Code OSS checkout. If null, the host searches for a
        /// sibling <c>vscode-main</c> folder near the app.</summary>
        public string? CodeOssPath { get; set; }

        /// <summary>Localhost port to serve code-oss web on.</summary>
        public int Port { get; set; } = 9888;

        /// <summary>Seconds to wait for the code-oss web server to accept connections.</summary>
        public int StartTimeoutSeconds { get; set; } = 40;
    }
}
