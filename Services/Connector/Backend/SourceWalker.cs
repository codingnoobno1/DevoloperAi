using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Enumerates project source files while skipping heavy / generated directories
    /// (node_modules, .git, build output, virtualenvs, the Syncro local DB, etc.).
    /// Shared by the route scanner and the swagger locator.
    /// </summary>
    internal static class SourceWalker
    {
        private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", ".git", ".hg", ".svn", "bin", "obj", "build", "dist", "out",
            ".next", ".nuxt", "target", "venv", ".venv", "env", "__pycache__", ".idea",
            ".vs", ".vscode", "coverage", ".syncro_db", ".dart_tool", "Pods"
        };

        /// <summary>
        /// Yields every file under <paramref name="root"/> whose extension is in
        /// <paramref name="extensions"/> (case-insensitive, leading dot required, e.g. ".js").
        /// Robust to permission errors on individual directories.
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(string root, IReadOnlyCollection<string> extensions, int maxFiles = 5000)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                yield break;

            var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(root);
            int emitted = 0;

            while (stack.Count > 0)
            {
                string dir = stack.Pop();

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); }
                catch { subDirs = Array.Empty<string>(); }

                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    // Skip known heavy / generated dirs; descend into everything else.
                    if (!string.IsNullOrEmpty(name) && !SkipDirs.Contains(name))
                        stack.Push(sub);
                }

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { files = Array.Empty<string>(); }

                foreach (var file in files)
                {
                    if (extSet.Contains(Path.GetExtension(file)))
                    {
                        yield return file;
                        if (++emitted >= maxFiles)
                            yield break;
                    }
                }
            }
        }

        /// <summary>
        /// Finds the first existing file matching any of <paramref name="candidateNames"/>, searching
        /// the root first, then shallow sub-directories. Returns null if none found.
        /// </summary>
        public static string? FindFirst(string root, IReadOnlyCollection<string> candidateNames, int maxDepth = 3)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return null;

            var nameSet = new HashSet<string>(candidateNames, StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<(string dir, int depth)>();
            queue.Enqueue((root, 0));

            while (queue.Count > 0)
            {
                var (dir, depth) = queue.Dequeue();

                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch { files = Array.Empty<string>(); }

                var hit = files.FirstOrDefault(f => nameSet.Contains(Path.GetFileName(f)));
                if (hit != null)
                    return hit;

                if (depth >= maxDepth)
                    continue;

                string[] subDirs;
                try { subDirs = Directory.GetDirectories(dir); }
                catch { subDirs = Array.Empty<string>(); }

                foreach (var sub in subDirs)
                {
                    var name = Path.GetFileName(sub);
                    if (!SkipDirs.Contains(name))
                        queue.Enqueue((sub, depth + 1));
                }
            }

            return null;
        }
    }
}
