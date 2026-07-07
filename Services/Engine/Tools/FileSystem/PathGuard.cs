using System;
using System.IO;

namespace Syncro.Desktop.Services.Engine.Tools.FileSystem
{
    /// <summary>
    /// Resolves a path against a root and rejects anything that escapes it (via "..", an absolute
    /// path elsewhere, etc.). Shared by every tool that writes/deletes on the LLM's behalf so a
    /// single batch call can't reach outside the folder it was scoped to.
    /// </summary>
    public static class PathGuard
    {
        /// <summary>
        /// Resolves <paramref name="relativeOrAbsolute"/> against <paramref name="root"/>.
        /// Returns false (with <paramref name="error"/> set) if the result would fall outside root.
        /// </summary>
        public static bool TryResolveWithin(string root, string relativeOrAbsolute, out string fullPath, out string? error)
        {
            fullPath = "";
            error = null;

            if (string.IsNullOrWhiteSpace(root))
            {
                error = "root path is empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(relativeOrAbsolute))
            {
                error = "path is empty";
                return false;
            }

            string rootFull;
            try { rootFull = Path.GetFullPath(root); }
            catch (Exception ex) { error = $"invalid root path: {ex.Message}"; return false; }

            string candidate = Path.IsPathRooted(relativeOrAbsolute)
                ? relativeOrAbsolute
                : Path.Combine(rootFull, relativeOrAbsolute);

            string candidateFull;
            try { candidateFull = Path.GetFullPath(candidate); }
            catch (Exception ex) { error = $"invalid path: {ex.Message}"; return false; }

            string rootWithSep = rootFull.EndsWith(Path.DirectorySeparatorChar)
                ? rootFull
                : rootFull + Path.DirectorySeparatorChar;

            bool isRootItself = string.Equals(candidateFull, rootFull, StringComparison.OrdinalIgnoreCase);
            bool isInsideRoot = candidateFull.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);

            if (!isRootItself && !isInsideRoot)
            {
                error = $"path '{relativeOrAbsolute}' escapes root '{root}'";
                return false;
            }

            fullPath = candidateFull;
            return true;
        }
    }
}
