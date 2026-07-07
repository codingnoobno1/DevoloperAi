using System.Collections.Generic;

namespace Syncro.Desktop.Services.projectgenerator.Models
{
    /// <summary>Which ProjectGenerator entry point a request maps to.</summary>
    public enum CreationKind
    {
        Single,
        Group
    }

    /// <summary>
    /// Everything <see cref="IProjectCreationService"/> needs to scaffold a project. This is the
    /// seam between the UI (manual stepper today, AI prompt flow later) and the generator —
    /// any caller that can fill this out gets the exact same creation behavior.
    /// See safeupgrade.md Phase 1.
    /// </summary>
    public sealed class ProjectCreationRequest
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public CreationKind Kind { get; set; } = CreationKind.Single;

        /// <summary>Framework/stack for a Single request, e.g. "FastAPI", "Flutter", "Express/Node".</summary>
        public string Type { get; set; } = "";

        /// <summary>Member archetypes for a Group request, e.g. ["Next.js", "FastAPI"].</summary>
        public List<string> GroupComponents { get; set; } = new();

        public bool UseNativeCli { get; set; }

        /// <summary>When true, runs AstService scan + tokenize after a successful scaffold.</summary>
        public bool SyncWithAgent { get; set; } = true;
    }
}
