namespace Syncro.Desktop.Services.Ide.Host
{
    /// <summary>
    /// Picks the active <see cref="IIdeHost"/> from <see cref="IdeHostOptions.Kind"/>. The IDE window
    /// asks the resolver for the active host rather than depending on a concrete backend — flip
    /// <c>Kind</c> to <see cref="IdeHostKind.CodeOss"/> and the same shell drives VS Code OSS instead.
    /// </summary>
    public sealed class IdeHostResolver
    {
        private readonly IdeHostOptions _opts;
        private readonly MonacoIdeHost _monaco;
        private readonly CodeOssIdeHost _codeOss;

        public IdeHostResolver(IdeHostOptions opts, MonacoIdeHost monaco, CodeOssIdeHost codeOss)
        {
            _opts = opts;
            _monaco = monaco;
            _codeOss = codeOss;
        }

        public IdeHostKind ActiveKind => _opts.Kind;

        public IIdeHost Active => Get(_opts.Kind);

        public IIdeHost Get(IdeHostKind kind) => kind == IdeHostKind.CodeOss ? _codeOss : _monaco;

        /// <summary>The always-available fallback, used when the active host reports Unavailable.</summary>
        public IIdeHost Fallback => _monaco;
    }
}
