using System;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.projectgenerator.Models;

namespace Syncro.Desktop.Services.projectgenerator
{
    /// <summary>
    /// The seam between project-creation UI (the manual stepper today, an AI-prompt flow later —
    /// see safeupgrade.md) and the generator. Every caller that fills out a
    /// <see cref="ProjectCreationRequest"/> gets identical scaffold → import → AST-index behavior,
    /// so the UI layer can change freely without the creation logic ever forking.
    /// </summary>
    public interface IProjectCreationService
    {
        Task<ProjectCreationResult> CreateAsync(ProjectCreationRequest request, IProgress<string> log, CancellationToken ct = default);
    }
}
