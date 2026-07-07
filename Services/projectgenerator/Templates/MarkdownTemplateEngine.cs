using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Syncro.Desktop.Services.projectgenerator.Templates
{
    /// <summary>
    /// Turns a <c>ProjectTemplates/*.md</c> file into real files on disk. The template format is:
    /// YAML front-matter between <c>---</c> fences, then a body of <c>## Shared</c> and
    /// <c>## Architecture: name</c> sections, each containing <c>### file: path</c> blocks whose
    /// fenced code is the file content. Placeholders like <c>{{ProjectName}}</c> / <c>{{Port}}</c>
    /// are substituted in both paths and content.
    ///
    /// Parsing note: file contents legitimately contain markdown headers (<c>## Run</c>) and nested
    /// code fences (a ```` ```markdown ```` block wrapping inner ``` fences). So the parser treats
    /// ONLY the specific structural markers (<c>### file:</c>, <c>## Shared</c>,
    /// <c>## Architecture:</c>) as boundaries — never a generic <c>##</c> — and takes the LAST bare
    /// <c>```</c> in a block's region as the closing fence.
    /// </summary>
    public sealed class MarkdownTemplateEngine
    {
        private const string SharedSection = "__shared__";

        private static readonly Regex FileHeaderRx = new(@"^###\s+file:\s*(.+?)\s*$", RegexOptions.Compiled);
        private static readonly Regex SharedHeaderRx = new(@"^##\s+Shared\b", RegexOptions.Compiled);
        private static readonly Regex ArchHeaderRx = new(@"^##\s+Architecture:\s*(.+?)\s*$", RegexOptions.Compiled);

        public TemplateMaterializeResult Materialize(
            string templateId, string? architecture, string projectName, int port, string targetPath, Action<string>? onLog)
        {
            try
            {
                var file = TemplateLocator.FindTemplateFile(templateId);
                if (file == null)
                    return new TemplateMaterializeResult { Success = false, Error = $"No template file '{templateId}.md' found." };

                var fullText = File.ReadAllText(file).Replace("\r\n", "\n");
                var (frontMatter, body) = SplitFrontMatter(fullText);

                var manifest = ParseManifest(frontMatter);
                var entries = ParseFileBlocks(body);

                // Values: placeholder defaults first, then hard overrides from the real context.
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in manifest.Placeholders)
                    if (!string.IsNullOrWhiteSpace(p.Key))
                        values[p.Key] = p.Default?.ToString() ?? "";
                values["ProjectName"] = projectName;
                values["Port"] = port.ToString();

                // Choose an architecture: requested → default → first declared → none.
                string? chosenArch = (architecture ?? manifest.DefaultArchitecture)?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(chosenArch))
                    chosenArch = manifest.Architectures.FirstOrDefault()?.Trim().ToLowerInvariant();

                // Shared files first, then the chosen architecture's files (which override on conflict).
                var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in entries.Where(e => e.Architecture == SharedSection))
                    selected[Substitute(e.Path, values)] = Substitute(e.Content, values);
                if (!string.IsNullOrWhiteSpace(chosenArch))
                    foreach (var e in entries.Where(e => e.Architecture == chosenArch))
                        selected[Substitute(e.Path, values)] = Substitute(e.Content, values);

                int written = 0;
                foreach (var kv in selected)
                {
                    var relPath = kv.Key.Replace('\\', '/').TrimStart('/');
                    if (string.IsNullOrWhiteSpace(relPath)) continue;

                    var destPath = Path.Combine(targetPath, relPath);
                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(destPath, kv.Value);
                    written++;
                }

                return new TemplateMaterializeResult
                {
                    Success = written > 0,
                    FilesWritten = written,
                    Architecture = chosenArch,
                    Error = written == 0 ? "Template produced no files." : null
                };
            }
            catch (Exception ex)
            {
                return new TemplateMaterializeResult { Success = false, Error = ex.Message };
            }
        }

        // ── front-matter ─────────────────────────────────────────────────────────────────
        private static (string frontMatter, string body) SplitFrontMatter(string text)
        {
            var lines = text.Split('\n');
            int start = -1, end = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() != "---") { if (string.IsNullOrWhiteSpace(lines[i])) continue; else break; }
                start = i;
                break;
            }
            if (start < 0) return ("", text);

            for (int i = start + 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---") { end = i; break; }
            }
            if (end < 0) return ("", text);

            var fm = string.Join("\n", lines.Skip(start + 1).Take(end - start - 1));
            var body = string.Join("\n", lines.Skip(end + 1));
            return (fm, body);
        }

        private static TemplateManifest ParseManifest(string frontMatter)
        {
            if (string.IsNullOrWhiteSpace(frontMatter))
                return new TemplateManifest();

            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                return deserializer.Deserialize<TemplateManifest>(frontMatter) ?? new TemplateManifest();
            }
            catch
            {
                return new TemplateManifest();
            }
        }

        // ── body: file blocks ────────────────────────────────────────────────────────────
        private static List<TemplateFileEntry> ParseFileBlocks(string body)
        {
            var entries = new List<TemplateFileEntry>();
            var lines = body.Split('\n');
            string currentArch = SharedSection;

            int i = 0;
            while (i < lines.Length)
            {
                var line = lines[i];

                if (SharedHeaderRx.IsMatch(line)) { currentArch = SharedSection; i++; continue; }

                var archMatch = ArchHeaderRx.Match(line);
                if (archMatch.Success) { currentArch = archMatch.Groups[1].Value.Trim().ToLowerInvariant(); i++; continue; }

                var fileMatch = FileHeaderRx.Match(line);
                if (fileMatch.Success)
                {
                    string path = fileMatch.Groups[1].Value.Trim();

                    // Collect the region up to (but excluding) the next structural boundary.
                    var region = new List<string>();
                    int j = i + 1;
                    while (j < lines.Length && !IsBoundary(lines[j]))
                    {
                        region.Add(lines[j]);
                        j++;
                    }

                    entries.Add(new TemplateFileEntry
                    {
                        Architecture = currentArch,
                        Path = path,
                        Content = ExtractFenced(region)
                    });

                    i = j;
                    continue;
                }

                i++;
            }

            return entries;
        }

        private static bool IsBoundary(string line) =>
            FileHeaderRx.IsMatch(line) || SharedHeaderRx.IsMatch(line) || ArchHeaderRx.IsMatch(line);

        // Content = between the first opening fence and the LAST bare ``` in the region. Taking the
        // last closer lets a file's content contain its own nested ``` fences.
        private static string ExtractFenced(List<string> region)
        {
            int open = region.FindIndex(l => l.TrimStart().StartsWith("```"));
            if (open < 0) return "";

            int close = -1;
            for (int k = region.Count - 1; k > open; k--)
            {
                if (region[k].Trim() == "```") { close = k; break; }
            }

            if (close > open)
                return string.Join("\n", region.GetRange(open + 1, close - open - 1));

            // No closing fence found — take everything after the opener.
            return string.Join("\n", region.GetRange(open + 1, region.Count - open - 1));
        }

        private static string Substitute(string text, Dictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var kv in values)
                text = text.Replace("{{" + kv.Key + "}}", kv.Value);
            return text;
        }
    }
}
