using System.Linq;

namespace Syncro.Desktop.Services.SyncroCLI.Core
{
    public class CommandParser
    {
        public CommandContext Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new CommandContext();

            var parts = input.Trim().Split(' ');
            return new CommandContext
            {
                Command = parts[0].ToLower(),
                Args = parts.Skip(1).ToList(),
                RawInput = input
            };
        }
    }
}
