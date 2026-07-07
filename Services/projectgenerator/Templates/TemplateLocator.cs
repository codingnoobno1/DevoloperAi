using System;
using System.IO;
using System.Linq;

namespace Syncro.Desktop.Services.projectgenerator.Templates
{
    /// <summary>
    /// Finds the <c>ProjectTemplates</c> directory and a template file within it. Mirrors how
    /// <c>StackRegistry</c> locates stacks.json: check the app base directory (shipped layout),
    /// then walk up to the project root (dev / run-from-source layout).
    /// </summary>
    internal static class TemplateLocator
    {
        public static string? FindTemplateFile(string templateId)
        {
            var dir = FindTemplatesDir();
            if (dir == null) return null;

            var path = Path.Combine(dir, templateId + ".md");
            return File.Exists(path) ? path : null;
        }

        public static string? FindTemplatesDir()
        {
            var baseDirCandidate = Path.Combine(AppContext.BaseDirectory, "ProjectTemplates");
            if (Directory.Exists(baseDirCandidate))
                return baseDirCandidate;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Syncro.Desktop.csproj")))
                {
                    var candidate = Path.Combine(dir.FullName, "ProjectTemplates");
                    return Directory.Exists(candidate) ? candidate : null;
                }
                dir = dir.Parent;
            }

            return null;
        }
    }
}
