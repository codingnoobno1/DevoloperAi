using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Syncro.Desktop.Services.AST.Storage;

public class AstCacheManager
{
    public string ComputeDirectorySignature(string projectPath, string[] ignoreFolders)
    {
        if (!Directory.Exists(projectPath)) return string.Empty;

        try
        {
            var sb = new StringBuilder();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            WalkDirectorySafe(projectPath, projectPath, ignoreFolders, sb, visited);

            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(bytes);
        }
        catch
        {
            return Guid.NewGuid().ToString(); // Fallback to invalidating cache on error
        }
    }

    private void WalkDirectorySafe(string rootPath, string currentDir, string[] ignoreFolders, StringBuilder sb, HashSet<string> visited)
    {
        string canonical;
        try
        {
            canonical = Path.GetFullPath(currentDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!visited.Add(canonical)) return; // Cycle detected
        }
        catch
        {
            return;
        }

        // Get and record files in current directory
        string[] files;
        try
        {
            files = Directory.GetFiles(currentDir);
        }
        catch
        {
            return; // Access denied or error, skip
        }

        foreach (var file in files.OrderBy(f => f))
        {
            try
            {
                string relPath = Path.GetRelativePath(rootPath, file);
                var info = new FileInfo(file);
                sb.Append(relPath);
                sb.Append(info.Length);
                sb.Append(info.LastWriteTimeUtc.Ticks);
            }
            catch { }
        }

        // Get subdirectories and walk recursively
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(currentDir);
        }
        catch
        {
            return; // Access denied or error, skip
        }

        foreach (var subDir in subDirs.OrderBy(d => d))
        {
            try
            {
                var attrs = File.GetAttributes(subDir);
                if (attrs.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue; // Skip symlinks/junctions
                }
            }
            catch
            {
                continue;
            }

            string folderName = Path.GetFileName(subDir);
            if (ignoreFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
            {
                continue; // Skip ignored folders entirely without visiting!
            }

            WalkDirectorySafe(rootPath, subDir, ignoreFolders, sb, visited);
        }
    }
}
