using System.Threading.Tasks;

namespace Syncro.Desktop.Services.SyncroCLI.Core
{
    public interface ICliCommand
    {
        string Name { get; }
        string Description { get; }
        Task Execute(CommandContext context);
    }
}
