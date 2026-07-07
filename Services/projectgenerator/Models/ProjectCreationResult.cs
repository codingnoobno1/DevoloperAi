namespace Syncro.Desktop.Services.projectgenerator.Models
{
    /// <summary>Outcome of <see cref="IProjectCreationService.CreateAsync"/>.</summary>
    public sealed class ProjectCreationResult
    {
        public bool Success { get; set; }
        public Syncro.Desktop.Services.ProjectModel? Project { get; set; }
        public string? Error { get; set; }
    }
}
