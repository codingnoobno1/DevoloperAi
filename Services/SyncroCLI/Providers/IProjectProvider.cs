using System.Threading.Tasks;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public interface IProjectProvider
    {
        string StackName { get; }
        Task<bool> Create(string name, string targetPath);
    }
}
