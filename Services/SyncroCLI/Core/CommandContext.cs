using System.Collections.Generic;

namespace Syncro.Desktop.Services.SyncroCLI.Core
{
    public class CommandContext
    {
        public string Command { get; set; } = string.Empty;
        public List<string> Args { get; set; } = new();
        public string RawInput { get; set; } = string.Empty;
        public string? Password { get; set; }
    }
}
