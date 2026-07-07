using System;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.projectgenerator.Registry;

namespace Syncro.Desktop.Services.projectgenerator.Scaffolders
{
    public class ScaffolderContext
    {
        public string ProjectName { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public Action<string>? OnLog { get; set; }
        public StackArchetype Archetype { get; set; } = default!;
        public int AssignedPort { get; set; }
    }

    public interface IStackScaffolder
    {
        Task<bool> ScaffoldAsync(ScaffolderContext context, CancellationToken cancellationToken = default);
    }
}
