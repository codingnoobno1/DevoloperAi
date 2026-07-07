using System.Collections.Generic;

namespace Syncro.Desktop.Services.projectgenerator.Templates
{
    /// <summary>The parsed YAML front-matter of a ProjectTemplates/*.md file (only the fields the
    /// engine acts on — everything else is ignored on deserialize).</summary>
    public sealed class TemplateManifest
    {
        public string DefaultArchitecture { get; set; } = "";
        public List<string> Architectures { get; set; } = new();
        public List<TemplatePlaceholder> Placeholders { get; set; } = new();
    }

    public sealed class TemplatePlaceholder
    {
        public string Key { get; set; } = "";
        public object? Default { get; set; }
    }

    /// <summary>One file block parsed from a template body, tagged with the section it came from
    /// (<c>__shared__</c> or an architecture name).</summary>
    public sealed class TemplateFileEntry
    {
        public string Architecture { get; set; } = "";
        public string Path { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public sealed class TemplateMaterializeResult
    {
        public bool Success { get; set; }
        public int FilesWritten { get; set; }
        public string? Error { get; set; }
        public string? Architecture { get; set; }
    }
}
