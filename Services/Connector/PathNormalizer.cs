using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Syncro.Desktop.Services.Connector
{
    /// <summary>
    /// Canonicalizes URL paths so a frontend call and a backend route can be compared regardless of
    /// param-syntax differences (<c>:id</c> vs <c>{id}</c> vs <c>&lt;int:id&gt;</c>), base-URL prefixes,
    /// string interpolation, query strings, and casing. Shared by the frontend extractors and the
    /// matcher so both sides speak the same path language.
    /// </summary>
    internal static class PathNormalizer
    {
        private static readonly Regex SchemeHost = new(@"^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/]+", RegexOptions.Compiled);
        private static readonly Regex InterpBraces = new(@"\$\{[^}]*\}", RegexOptions.Compiled);   // JS/Dart ${expr}
        private static readonly Regex InterpDollar = new(@"\$[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled); // Dart $ident
        private static readonly Regex ColonParam = new(@":([A-Za-z0-9_]+)", RegexOptions.Compiled);   // :id
        private static readonly Regex AngleParam = new(@"<(?:[^:>]+:)?([A-Za-z0-9_]+)>", RegexOptions.Compiled); // <int:id>

        /// <summary>
        /// Turns a raw URL/argument captured from source into a normalized path template:
        /// strips scheme+host, query and hash, collapses interpolation and param syntaxes to
        /// <c>{param}</c>, and drops a leading base-URL segment (e.g. <c>$baseUrl/users</c> → <c>/users</c>).
        /// </summary>
        public static string NormalizeCallPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "/";

            var s = raw.Trim();

            int cut = s.IndexOfAny(new[] { '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);

            s = SchemeHost.Replace(s, "");
            s = InterpBraces.Replace(s, "{param}");
            s = InterpDollar.Replace(s, "{param}");
            s = ColonParam.Replace(s, "{$1}");
            s = AngleParam.Replace(s, "{$1}");

            var segs = s.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

            // A leading interpolated segment is almost always a base-URL variable, not a path param.
            while (segs.Count > 0 && IsParam(segs[0]))
                segs.RemoveAt(0);

            for (int i = 0; i < segs.Count; i++)
                if (IsParam(segs[i])) segs[i] = "{param}";

            return segs.Count == 0 ? "/" : "/" + string.Join("/", segs);
        }

        /// <summary>
        /// Reduces a normalized path template to a comparison key: every param segment becomes
        /// <c>{}</c> and the whole thing is lower-cased, so <c>/api/Users/{id}</c> and
        /// <c>/api/users/{userId}</c> compare equal.
        /// </summary>
        public static string Canonical(string pathTemplate)
        {
            if (string.IsNullOrWhiteSpace(pathTemplate)) return "/";
            var segs = CanonicalSegments(pathTemplate);
            return segs.Length == 0 ? "/" : "/" + string.Join("/", segs);
        }

        /// <summary>Canonical path as an array of segments (param segments are <c>{}</c>, lower-cased).</summary>
        public static string[] CanonicalSegments(string pathTemplate)
        {
            if (string.IsNullOrWhiteSpace(pathTemplate)) return Array.Empty<string>();
            return pathTemplate
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => IsParam(x) ? "{}" : x.ToLowerInvariant())
                .ToArray();
        }

        /// <summary>
        /// True when <paramref name="shorter"/> aligns to the tail of <paramref name="longer"/>
        /// segment-for-segment (params wildcard). Lets a call to <c>/users</c> match a route mounted
        /// at <c>/api/users</c> when only the base prefix differs.
        /// </summary>
        public static bool SuffixAligned(string[] shorter, string[] longer)
        {
            if (shorter.Length == 0 || shorter.Length > longer.Length) return false;

            int offset = longer.Length - shorter.Length;
            for (int i = 0; i < shorter.Length; i++)
            {
                var a = longer[offset + i];
                var b = shorter[i];
                if (a == b) continue;
                if (a == "{}" || b == "{}") continue; // param wildcard
                return false;
            }
            return true;
        }

        private static bool IsParam(string seg) =>
            seg.Length >= 2 && seg[0] == '{' && seg[^1] == '}';
    }
}
