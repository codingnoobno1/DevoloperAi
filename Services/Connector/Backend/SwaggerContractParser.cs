using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Parses an OpenAPI / Swagger document (2.0 or 3.0, JSON or YAML) into normalized
    /// <see cref="BackendRoute"/>s, including shallow request/response field shapes with
    /// <c>$ref</c> resolution. Deterministic — no AI, no network.
    /// </summary>
    internal static class SwaggerContractParser
    {
        private static readonly string[] HttpMethods =
            { "get", "post", "put", "patch", "delete", "head", "options" };

        public static List<BackendRoute> Parse(string filePath, out List<string> notes)
        {
            notes = new List<string>();
            var routes = new List<BackendRoute>();

            string raw;
            try { raw = File.ReadAllText(filePath); }
            catch (Exception ex)
            {
                notes.Add($"Failed to read swagger file: {ex.Message}");
                return routes;
            }

            JObject root;
            try
            {
                root = LooksLikeYaml(filePath, raw) ? YamlToJObject(raw) : JObject.Parse(raw);
            }
            catch (Exception ex)
            {
                notes.Add($"Failed to parse swagger document: {ex.Message}");
                return routes;
            }

            var paths = root["paths"] as JObject;
            if (paths == null)
            {
                notes.Add("Swagger document has no 'paths' section.");
                return routes;
            }

            foreach (var pathProp in paths.Properties())
            {
                if (pathProp.Value is not JObject pathItem)
                    continue;

                foreach (var methodProp in pathItem.Properties())
                {
                    var method = methodProp.Name.ToLowerInvariant();
                    if (!HttpMethods.Contains(method))
                        continue;

                    if (methodProp.Value is not JObject op)
                        continue;

                    var route = new BackendRoute
                    {
                        Method = method.ToHttpVerb(),
                        PathTemplate = NormalizePath(pathProp.Name),
                        Source = RouteSource.Swagger,
                        OperationId = op["operationId"]?.ToString(),
                        Summary = op["summary"]?.ToString() ?? op["description"]?.ToString(),
                        RequestSchema = ExtractRequestFields(root, op),
                        ResponseSchema = ExtractResponseFields(root, op)
                    };

                    routes.Add(route);
                }
            }

            notes.Add($"Parsed {routes.Count} route(s) from {Path.GetFileName(filePath)}.");
            return routes;
        }

        // ── request shape ──────────────────────────────────────────────────────────────

        private static List<ApiField> ExtractRequestFields(JObject root, JObject op)
        {
            // OpenAPI 3.0: requestBody.content["application/json"].schema
            var schema = op["requestBody"]?["content"]?["application/json"]?["schema"]
                         ?? op["requestBody"]?["content"]?.Children<JProperty>().FirstOrDefault()?.Value?["schema"];

            if (schema != null)
                return ResolveSchemaFields(root, schema);

            // Swagger 2.0: a body parameter carries the schema.
            if (op["parameters"] is JArray parameters)
            {
                var body = parameters.FirstOrDefault(p => p["in"]?.ToString() == "body");
                if (body?["schema"] != null)
                    return ResolveSchemaFields(root, body["schema"]!);

                // Otherwise expose simple query/path params as request fields.
                var simple = parameters
                    .Where(p => p["in"] != null && p["in"]!.ToString() != "header")
                    .Select(p => new ApiField
                    {
                        Name = p["name"]?.ToString() ?? "",
                        Type = p["type"]?.ToString() ?? p["schema"]?["type"]?.ToString() ?? "string",
                        Required = p["required"]?.ToObject<bool>() ?? false
                    })
                    .Where(f => !string.IsNullOrEmpty(f.Name))
                    .ToList();
                if (simple.Count > 0)
                    return simple;
            }

            return new List<ApiField>();
        }

        // ── response shape ─────────────────────────────────────────────────────────────

        private static List<ApiField> ExtractResponseFields(JObject root, JObject op)
        {
            var responses = op["responses"] as JObject;
            if (responses == null)
                return new List<ApiField>();

            // Prefer a 2xx success response.
            var success = responses.Properties()
                              .FirstOrDefault(p => p.Name.StartsWith("2"))
                          ?? responses.Properties().FirstOrDefault();

            if (success?.Value is not JObject resp)
                return new List<ApiField>();

            // OpenAPI 3.0 nests under content; 2.0 has schema directly.
            var schema = resp["content"]?["application/json"]?["schema"]
                         ?? resp["content"]?.Children<JProperty>().FirstOrDefault()?.Value?["schema"]
                         ?? resp["schema"];

            return schema != null ? ResolveSchemaFields(root, schema) : new List<ApiField>();
        }

        // ── schema → fields (top-level, with $ref + array-item resolution) ──────────────

        private static List<ApiField> ResolveSchemaFields(JObject root, JToken schema, int depth = 0)
        {
            var fields = new List<ApiField>();
            if (depth > 4)
                return fields;

            schema = ResolveRef(root, schema);
            if (schema is not JObject)
                return fields;

            // Unwrap arrays to their item schema so list endpoints expose the element shape.
            if (schema["type"]?.ToString() == "array" && schema["items"] != null)
                schema = ResolveRef(root, schema["items"]!);

            var properties = (schema as JObject)?["properties"] as JObject;
            if (properties == null)
                return fields;

            var required = ((schema as JObject)?["required"] as JArray)?.Select(r => r.ToString()).ToHashSet()
                           ?? new HashSet<string>();

            foreach (var prop in properties.Properties())
            {
                var propSchema = ResolveRef(root, prop.Value) as JObject;
                fields.Add(new ApiField
                {
                    Name = prop.Name,
                    Type = propSchema?["type"]?.ToString() ?? (propSchema?["$ref"] != null ? "object" : "any"),
                    Required = required.Contains(prop.Name)
                });
            }

            return fields;
        }

        private static JToken ResolveRef(JObject root, JToken token)
        {
            if (token is not JObject)
                return token;

            var refPath = token["$ref"]?.ToString();
            if (string.IsNullOrEmpty(refPath))
                return token;

            // "#/components/schemas/User" (3.0) or "#/definitions/User" (2.0).
            var segments = refPath.TrimStart('#', '/').Split('/');
            JToken? cursor = root;
            foreach (var seg in segments)
            {
                if (cursor == null) break;
                cursor = cursor[seg];
            }

            return cursor ?? token;
        }

        // ── format helpers ──────────────────────────────────────────────────────────────

        private static string NormalizePath(string path)
        {
            // Express/Flask style ":id" → "{id}" so swagger + scanned routes use one form.
            return System.Text.RegularExpressions.Regex.Replace(path, @":([A-Za-z0-9_]+)", "{$1}");
        }

        private static bool LooksLikeYaml(string filePath, string raw)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".yaml" or ".yml") return true;
            if (ext == ".json") return false;
            return !raw.TrimStart().StartsWith("{");
        }

        private static JObject YamlToJObject(string yaml)
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder().Build();
            var graph = deserializer.Deserialize<object?>(new StringReader(yaml));
            var json = JsonConvert.SerializeObject(graph ?? new object());
            return JObject.Parse(json);
        }
    }
}
