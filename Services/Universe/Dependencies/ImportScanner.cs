using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.Connector.Backend; // SourceWalker (internal, same assembly)

namespace Syncro.Desktop.Services.Universe.Dependencies
{
    /// <summary>
    /// Extracts the modules a project actually imports (runtime.md §7b). Regex-based over real source
    /// — the imports are genuinely present in code, not inferred — which is what makes resolving them
    /// to packages defensible. Returns raw module specs; <see cref="DependencyAliasMap"/> maps and
    /// filters them.
    /// </summary>
    internal static class ImportScanner
    {
        private static readonly Regex PyImport = new(@"^\s*import\s+([a-zA-Z0-9_\.]+)", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex PyFrom = new(@"^\s*from\s+([a-zA-Z0-9_\.]+)\s+import", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex JsImport = new(@"\bimport\b[^'""]*['""]([^'""]+)['""]", RegexOptions.Compiled);
        private static readonly Regex JsRequire = new(@"\brequire\s*\(\s*['""]([^'""]+)['""]\s*\)", RegexOptions.Compiled);
        private static readonly Regex JsDynamic = new(@"\bimport\s*\(\s*['""]([^'""]+)['""]\s*\)", RegexOptions.Compiled);

        public static HashSet<string> ScanPython(string root)
        {
            var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".py" }))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }

                foreach (Match m in PyImport.Matches(text)) Add(modules, TopLevel(m.Groups[1].Value));
                foreach (Match m in PyFrom.Matches(text)) Add(modules, TopLevel(m.Groups[1].Value));
            }
            return modules;
        }

        public static HashSet<string> ScanJs(string root)
        {
            var specs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".js", ".jsx", ".ts", ".tsx", ".mjs", ".vue" }))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }

                foreach (Match m in JsImport.Matches(text)) specs.Add(m.Groups[1].Value);
                foreach (Match m in JsRequire.Matches(text)) specs.Add(m.Groups[1].Value);
                foreach (Match m in JsDynamic.Matches(text)) specs.Add(m.Groups[1].Value);
            }
            return specs;
        }

        private static string TopLevel(string dotted)
        {
            if (string.IsNullOrEmpty(dotted) || dotted.StartsWith('.')) return "";
            int dot = dotted.IndexOf('.');
            return dot > -1 ? dotted.Substring(0, dot) : dotted;
        }

        private static void Add(HashSet<string> set, string module)
        {
            if (!string.IsNullOrWhiteSpace(module)) set.Add(module);
        }
    }
}
