using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Workspace.Watch
{
    /// <summary>
    /// Pure predicate deciding whether a changed path is worth reacting to. Mirrors the ignore set the
    /// rest of the IDE uses (<see cref="Ide.FileService"/> / the AST scanners) so the watcher never
    /// storms the bus with build-output or VCS churn. Stateless and side-effect-free — trivially testable.
    /// </summary>
    public static class WorkspacePathFilter
    {
        private static readonly HashSet<string> IgnoredDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", "dist", "build", "out", "__pycache__",
            "packages", ".syncro", ".dart_tool", ".next", ".vs", ".idea", ".vscode-server-data"
        };

        /// <summary>True when a change to <paramref name="relativePath"/> (relative to the workspace
        /// root, either slash style) should be surfaced as a <c>FileChanged</c> event.</summary>
        public static bool ShouldWatch(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return false;

            var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            foreach (var segment in parts)
                if (IgnoredDirs.Contains(segment)) return false;

            var name = parts[^1];
            // Editor/office temp + swap + lock artifacts.
            if (name.StartsWith("~$", StringComparison.Ordinal)) return false;
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.EndsWith("~", StringComparison.Ordinal)) return false;
            if (name.EndsWith(".swp", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals(".DS_Store", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }
    }
}
