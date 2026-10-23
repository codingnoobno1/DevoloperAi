using System;

namespace Syncro.Desktop.Services.Connector.Contract
{
    /// <summary>Pure path-containment check used to decide whether a changed file belongs to a
    /// project's backend or frontend tree. Separator-agnostic, case-insensitive, prefix-safe (so
    /// <c>/app/back</c> does not match <c>/app/backend</c>). No filesystem access — trivially testable.</summary>
    public static class RelevanceFilter
    {
        public static bool IsUnder(string childPath, string? ancestorPath)
        {
            if (string.IsNullOrWhiteSpace(childPath) || string.IsNullOrWhiteSpace(ancestorPath)) return false;

            var child = Normalize(childPath);
            var ancestor = Normalize(ancestorPath!).TrimEnd('/');
            if (ancestor.Length == 0) return false;

            return child.Equals(ancestor, StringComparison.OrdinalIgnoreCase)
                || child.StartsWith(ancestor + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path) => path.Replace('\\', '/').TrimEnd('/');
    }
}
