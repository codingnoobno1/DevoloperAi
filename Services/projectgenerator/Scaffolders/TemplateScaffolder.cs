using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.projectgenerator.Templates;

namespace Syncro.Desktop.Services.projectgenerator.Scaffolders
{
    /// <summary>
    /// Materializes a stack from its <c>ProjectTemplates/*.md</c> template (front-matter + Shared /
    /// Architecture file blocks) via <see cref="MarkdownTemplateEngine"/>. Only when no template
    /// exists for the archetype does it fall back to a minimal, clearly-labeled starter so a stack
    /// never produces an empty folder.
    /// </summary>
    public class TemplateScaffolder : IStackScaffolder
    {
        private readonly MarkdownTemplateEngine _engine = new();

        public async Task<bool> ScaffoldAsync(ScaffolderContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(context.TargetPath);

                string? templateId = context.Archetype.Template;
                if (!string.IsNullOrWhiteSpace(templateId))
                {
                    context.OnLog?.Invoke($"[Templates] Scaffolding {context.Archetype.Label} from template '{templateId}'...");

                    var result = await Task.Run(
                        () => _engine.Materialize(
                            templateId,
                            architecture: null,
                            projectName: context.ProjectName,
                            port: context.AssignedPort,
                            targetPath: context.TargetPath,
                            onLog: context.OnLog),
                        cancellationToken);

                    if (result.Success && result.FilesWritten > 0)
                    {
                        string archNote = string.IsNullOrWhiteSpace(result.Architecture) ? "" : $" ({result.Architecture})";
                        context.OnLog?.Invoke($"[Templates] {context.Archetype.Label}: wrote {result.FilesWritten} file(s) from template{archNote}.");
                        return true;
                    }

                    context.OnLog?.Invoke($"[Templates] Template '{templateId}' unavailable ({result.Error}); writing a minimal starter instead.");
                }

                WriteMinimalStarter(context);
                context.OnLog?.Invoke($"[Templates] {context.Archetype.Label} minimal starter created.");
                return true;
            }
            catch (Exception ex)
            {
                context.OnLog?.Invoke($"[Templates Error] Failed to generate from templates: {ex.Message}");
                return false;
            }
        }

        // Last-resort content when an archetype has no .md template. Kept intentionally small — its
        // job is only to leave a runnable, self-explanatory placeholder, not to be a real starter.
        private static void WriteMinimalStarter(ScaffolderContext context)
        {
            File.WriteAllText(
                Path.Combine(context.TargetPath, "README.md"),
                $"# {context.ProjectName}\n\nScaffolded by Syncro ({context.Archetype.Label}).\n" +
                $"No detailed template exists for this stack yet — this is a minimal starter.\n");

            switch (context.Archetype.Language)
            {
                case "Python":
                    File.WriteAllText(Path.Combine(context.TargetPath, "requirements.txt"), "");
                    File.WriteAllText(Path.Combine(context.TargetPath, "main.py"), "# Scaffolded Python entry point\n");
                    break;
                case "JavaScript":
                case "TypeScript":
                    File.WriteAllText(Path.Combine(context.TargetPath, "package.json"),
                        "{\n  \"name\": \"app\",\n  \"version\": \"1.0.0\"\n}\n");
                    File.WriteAllText(Path.Combine(context.TargetPath, "index.js"), "// Scaffolded JS entry point\n");
                    break;
            }
        }
    }
}
