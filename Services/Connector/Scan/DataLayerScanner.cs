using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.Connector.Backend;

namespace Syncro.Desktop.Services.Connector.Scan
{
    /// <summary>
    /// Extracts a project's data layer — models, DTOs and the database schema — with the same
    /// deterministic, dependency-free approach the route scanner uses. Two sources:
    /// <list type="bullet">
    ///   <item><b>C# source</b> (EF Core + MongoDB + folder conventions): <c>DbSet&lt;T&gt;</c> tables,
    ///   <c>GetCollection&lt;T&gt;("name")</c> Mongo collections, migrations, <c>.sql</c> scripts, and
    ///   classes bucketed into models vs DTOs by folder/name.</item>
    ///   <item><b>Reverse-AST scan</b> for Flutter (<c>.thunder/reverse/model.json</c>, <c>dto.json</c>,
    ///   <c>datasource.json</c>) when present.</item>
    /// </list>
    /// </summary>
    internal static class DataLayerScanner
    {
        private static readonly Regex DbSetRx = new(@"DbSet<(\w+)>\s+(\w+)", RegexOptions.Compiled);
        private static readonly Regex MongoNamedRx = new(@"GetCollection<(\w+)>\(\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex MongoTypeRx = new(@"IMongoCollection<(\w+)>", RegexOptions.Compiled);
        private static readonly Regex ClassRx = new(
            @"\b(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*(?:class|record)\s+(\w+)", RegexOptions.Compiled);
        // C# auto-property: `public <Type> <Name> { get; ... }` (captures Type + Name).
        private static readonly Regex PropRx = new(
            @"\bpublic\s+(?:virtual\s+|required\s+|override\s+|static\s+)*([\w<>\?\[\],\.\s]+?)\s+(\w+)\s*\{\s*get",
            RegexOptions.Compiled);

        public static DataLayer Scan(string path)
        {
            var data = new DataLayer();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return data;

            // Flutter: prefer the Reverse-AST scan output when it exists.
            var reverse = Path.Combine(path, ".thunder", "reverse");
            if (Directory.Exists(reverse))
                ReadThunder(reverse, data);

            ScanCSharp(path, data);
            ScanScripts(path, data);
            data.Store = InferStore(data);
            return data;
        }

        // ── C# / EF Core / Mongo ────────────────────────────────────────────────
        private static void ScanCSharp(string root, DataLayer data)
        {
            var tables = new Dictionary<string, string>(StringComparer.Ordinal);       // name → entity
            var collections = new Dictionary<string, string>(StringComparer.Ordinal);  // name → doc
            // name → TypeDef (keep the richest definition when a name appears more than once)
            var modelTypes = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
            var dtoTypes = new Dictionary<string, TypeDef>(StringComparer.Ordinal);

            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".cs" }, maxFiles: 4000))
            {
                if (file.Contains(".Designer.cs", StringComparison.OrdinalIgnoreCase)) continue;
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }

                foreach (Match m in DbSetRx.Matches(text))
                    tables[m.Groups[2].Value] = m.Groups[1].Value;
                foreach (Match m in MongoNamedRx.Matches(text))
                    collections[m.Groups[2].Value] = m.Groups[1].Value;
                foreach (Match m in MongoTypeRx.Matches(text))
                    collections.TryAdd(m.Groups[1].Value, m.Groups[1].Value);

                // Bucket declared types by folder + name convention, WITH their fields.
                var lower = file.Replace('\\', '/').ToLowerInvariant();
                bool inDto = lower.Contains("/dto") || lower.Contains("/contracts") || lower.Contains("/requests") || lower.Contains("/responses");
                bool inModel = lower.Contains("/entities") || lower.Contains("/documents") || lower.Contains("/domain") || lower.Contains("/models");
                var rel = RelPath(root, file);

                foreach (Match m in ClassRx.Matches(text))
                {
                    var name = m.Groups[1].Value;
                    var isDtoName = name.EndsWith("Dto") || name.EndsWith("Request") || name.EndsWith("Response") || name.EndsWith("Payload");
                    var bucket = (inDto || isDtoName) ? dtoTypes : (inModel ? modelTypes : null);
                    if (bucket == null) continue;

                    var def = new TypeDef
                    {
                        Name = name,
                        File = rel,
                        Line = LineAt(text, m.Index),
                        Fields = ExtractFields(text, m.Index),
                    };
                    // keep whichever definition captured the most fields.
                    if (!bucket.TryGetValue(name, out var existing) || def.Fields.Count > existing.Fields.Count)
                        bucket[name] = def;
                }
            }

            foreach (var t in tables.OrderBy(k => k.Key))
                data.Tables.Add(new SqlTable { Name = t.Key, Entity = t.Value });

            // Drop bare `IMongoCollection<T>` hits whose type is already a *named* collection's document,
            // so `pg_listings<PgListing>` doesn't also appear as `PgListing<PgListing>`.
            var namedDocs = collections.Where(kv => kv.Key != kv.Value).Select(kv => kv.Value).ToHashSet(StringComparer.Ordinal);
            foreach (var c in collections.OrderBy(k => k.Key))
            {
                if (c.Key == c.Value && namedDocs.Contains(c.Value)) continue;
                data.Collections.Add(new MongoCollection { Name = c.Key, Document = c.Value });
            }

            // Type definitions with fields (explorable structure).
            data.ModelTypes.AddRange(modelTypes.Values.OrderBy(t => t.Name));
            data.DtoTypes.AddRange(dtoTypes.Values.OrderBy(t => t.Name));

            // Merge names with any thunder-derived names, keep sorted + unique (back-compat lists).
            data.Models = data.Models.Concat(modelTypes.Keys).Distinct().OrderBy(x => x).ToList();
            data.Dtos = data.Dtos.Concat(dtoTypes.Keys).Distinct().OrderBy(x => x).ToList();

            // EF migrations = non-Designer .cs files under a Migrations folder.
            try
            {
                var migDir = Directory.EnumerateDirectories(root, "Migrations", SearchOption.AllDirectories)
                    .FirstOrDefault(d => !d.Contains("obj") && !d.Contains("bin"));
                if (migDir != null)
                    data.Migrations = Directory.EnumerateFiles(migDir, "*.cs")
                        .Count(f => !f.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }

        // ── field extraction ────────────────────────────────────────────────────
        /// <summary>Parse the auto-properties inside the class whose declaration starts at
        /// <paramref name="declIndex"/> — brace-matched to the class body.</summary>
        private static List<TypeField> ExtractFields(string text, int declIndex)
        {
            var fields = new List<TypeField>();
            int open = text.IndexOf('{', declIndex);
            if (open < 0) return fields;

            int depth = 0, i = open, end = -1;
            for (; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}') { depth--; if (depth == 0) { end = i; break; } }
            }
            if (end < 0) end = Math.Min(text.Length, open + 8000);   // unbalanced → bounded window

            var body = text.Substring(open, end - open);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match p in PropRx.Matches(body))
            {
                var name = p.Groups[2].Value;
                if (!seen.Add(name)) continue;
                fields.Add(new TypeField { Name = name, Type = CleanType(p.Groups[1].Value) });
            }
            return fields;
        }

        private static string CleanType(string t) => Regex.Replace(t.Trim(), @"\s+", " ");

        private static int LineAt(string text, int index)
        {
            int line = 1;
            for (int i = 0; i < index && i < text.Length; i++) if (text[i] == '\n') line++;
            return line;
        }

        private static string RelPath(string root, string file)
        {
            try { return Path.GetRelativePath(root, file); } catch { return Path.GetFileName(file); }
        }

        private static void ScanScripts(string root, DataLayer data)
        {
            try
            {
                foreach (var sql in Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories)
                             .Where(f => !f.Contains("node_modules") && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                             .Take(50))
                    data.SqlScripts.Add(Path.GetFileName(sql));
            }
            catch { }
        }

        // ── Flutter reverse-AST output ──────────────────────────────────────────
        private static void ReadThunder(string reverseDir, DataLayer data)
        {
            data.Models.AddRange(Names(Path.Combine(reverseDir, "model.json"), "models"));
            data.Dtos.AddRange(Names(Path.Combine(reverseDir, "dto.json"), "dtos"));

            // datasource.json → local stores (hive/sqflite/prefs) become "tables".
            foreach (var src in Array(Path.Combine(reverseDir, "datasource.json"), "sources"))
            {
                var kind = Str(src, "kind");
                if (kind.Length > 0) data.Collections.Add(new MongoCollection { Name = kind, Document = Str(src, "scope") });
            }
        }

        private static IEnumerable<string> Names(string file, string prop)
        {
            foreach (var e in Array(file, prop))
            {
                var n = Str(e, "name");
                if (n.Length > 0) yield return n;
            }
        }

        private static List<JsonElement> Array(string file, string prop)
        {
            var list = new List<JsonElement>();
            try
            {
                if (!File.Exists(file)) return list;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray()) list.Add(e.Clone());
            }
            catch { }
            return list;
        }

        private static string Str(JsonElement e, string p) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
                ? (v.GetString() ?? "") : "";

        private static string? InferStore(DataLayer d)
        {
            var stores = new List<string>();
            if (d.Tables.Count > 0) stores.Add("SQL");
            if (d.Collections.Any(c => !new[] { "hive", "sqflite", "shared_preferences", "secure_storage" }.Contains(c.Name)))
                stores.Add("MongoDB");
            if (d.Collections.Any(c => c.Name is "hive" or "sqflite")) stores.Add("local");
            return stores.Count == 0 ? null : string.Join(" + ", stores.Distinct());
        }
    }
}
