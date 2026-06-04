using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.SyncroCLI.Core
{
    public class CliEngine
    {
        private readonly Dictionary<string, ICliCommand> _commands = new();
        private readonly CommandParser _parser = new();

        public event Action<string>? OnOutput;

        public void RegisterCommand(ICliCommand command)
        {
            _commands[command.Name.ToLower()] = command;
        }

        public async Task ProcessInput(string input, string? password = null)
        {
            var context = _parser.Parse(input);
            context.Password = password;
            
            if (string.IsNullOrEmpty(context.Command)) return;

            if (_commands.TryGetValue(context.Command, out var command))
            {
                try
                {
                    await command.Execute(context);
                }
                catch (Exception ex)
                {
                    OnOutput?.Invoke($"Error executing command '{context.Command}': {ex.Message}");
                }
            }
            else
            {
                OnOutput?.Invoke($"Unknown command: {context.Command}. Type 'help' for available commands.");
            }
        }

        public IEnumerable<ICliCommand> GetCommands() => _commands.Values;
    }
}
