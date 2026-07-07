using System;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services;
using Syncro.Desktop.Services.AST;
using Syncro.Desktop.Services.projectgenerator.Models;

namespace Syncro.Desktop.Services.projectgenerator
{
    /// <summary>
    /// Default <see cref="IProjectCreationService"/>. A behavior-preserving extraction of the
    /// orchestration that previously lived inline in <c>CreateProjectDialog.Submit()</c> —
    /// scaffold, import into the project registry, then (optionally) AST-index. Per
    /// safeupgrade.md Phase 1: no logic changes here, only where it lives. UI concerns (the
    /// Flutter-setup precondition dialog, form validation, snackbars, closing the dialog) stay in
    /// the calling component — this service only orchestrates a fully-prepared request.
    /// </summary>
    public sealed class DefaultProjectCreationService : IProjectCreationService
    {
        private readonly ProjectGenerator _generator;
        private readonly ProjectService _projectService;
        private readonly AstService _astService;

        public DefaultProjectCreationService(ProjectGenerator generator, ProjectService projectService, AstService astService)
        {
            _generator = generator;
            _projectService = projectService;
            _astService = astService;
        }

        public async Task<ProjectCreationResult> CreateAsync(ProjectCreationRequest request, IProgress<string> log, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            log.Report("> Starting project initialization...");

            bool success = request.Kind == CreationKind.Group
                ? await _generator.ScaffoldGroupProjectAsync(
                    request.Name, request.Path, request.GroupComponents, request.UseNativeCli,
                    line => log.Report(line))
                : await _generator.ScaffoldProjectAsync(
                    request.Name, request.Path, request.Type, request.UseNativeCli,
                    line => log.Report(line));

            if (!success)
                return new ProjectCreationResult { Success = false, Error = "Failed to initialize project. Please check the logs." };

            var project = _projectService.ImportProject(request.Path);
            if (project == null)
                return new ProjectCreationResult { Success = false, Error = "Failed to initialize project. Please check the logs." };

            if (request.SyncWithAgent)
            {
                try
                {
                    log.Report("> Scanning project for AST indexing...");
                    var map = await _astService.ScanProjectAsync(request.Path, ct);
                    log.Report($"> Tokenizing project ({map.Nodes.Count} symbols detected)...");
                    await _astService.TokenizeProjectAsync(map);
                    log.Report("> AST Indexing and tokenization completed successfully.");
                }
                catch (Exception ex)
                {
                    log.Report($"> Warning: AST Scan/Tokenization failed: {ex.Message}");
                }
            }

            return new ProjectCreationResult { Success = true, Project = project };
        }
    }
}
