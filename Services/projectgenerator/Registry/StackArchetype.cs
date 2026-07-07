using System.Collections.Generic;

namespace Syncro.Desktop.Services.projectgenerator.Registry
{
    public class EnvironmentDetection
    {
        public string Tool { get; set; } = string.Empty;
        public string VersionArg { get; set; } = string.Empty;
    }

    public class StackArchetype
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public StackKind Kind { get; set; }
        public string Language { get; set; } = string.Empty;
        public string PackageManager { get; set; } = string.Empty;
        public string Subfolder { get; set; } = string.Empty;
        public int DefaultPort { get; set; }
        
        public EnvironmentDetection? Detect { get; set; }
        public string? Cli { get; set; }
        public string? Template { get; set; }
        public string? Install { get; set; }
        public string? Run { get; set; }
        public string? Compose { get; set; }

        public List<string> Provides { get; set; } = new();
        public List<string> Needs { get; set; } = new();
    }
}
