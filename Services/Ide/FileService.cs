using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Ide.Models;

namespace Syncro.Desktop.Services.Ide;

/// <summary>Filesystem access for the IDE explorer/editor. Honors the AST ignore set.</summary>
public class FileService
{
    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "dist", "build", "__pycache__", "packages", "out"
    };

    public Task<IReadOnlyList<FileNode>> ListAsync(string dir)
    {
        var list = new List<FileNode>();
        try
        {
            foreach (var d in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(d);
                if (name.StartsWith('.') || Ignore.Contains(name)) continue;
                list.Add(new FileNode { Name = name, FullPath = d, IsDirectory = true, HasChildren = true });
            }
            foreach (var f in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith('.') && name != ".env" && name != ".gitignore") continue;
                list.Add(new FileNode { Name = name, FullPath = f, IsDirectory = false });
            }
        }
        catch { /* tolerant: unreadable dir */ }

        IReadOnlyList<FileNode> ordered = list
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Task.FromResult(ordered);
    }

    public async Task<string> ReadAsync(string path)
    {
        try { return await File.ReadAllTextAsync(path); }
        catch (Exception ex) { return $"// Syncro IDE could not read this file:\n// {ex.Message}"; }
    }

    public async Task WriteAsync(string path, string content) => await File.WriteAllTextAsync(path, content);

    public static string DetectLanguage(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".razor" => "razor",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "javascript",
            ".ts" or ".tsx" => "typescript",
            ".json" => "json",
            ".py" => "python",
            ".html" or ".cshtml" => "html",
            ".css" => "css",
            ".md" => "markdown",
            ".xml" or ".csproj" => "xml",
            ".yml" or ".yaml" => "yaml",
            ".java" => "java",
            ".go" => "go",
            ".sh" => "shell",
            ".bat" or ".cmd" => "bat",
            _ => "plaintext"
        };
    }
}
