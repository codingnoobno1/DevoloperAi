using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Json
{
    public class ExtractSchemaTool : IMcpTool
    {
        public string Name => "ExtractSchema";
        public string Description => "Infers a C# class or TypeScript interface structure from a raw JSON payload string.";
        public string InputSchema => "{ \"jsonPayload\": \"string\", \"targetLanguage\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { jsonPayload = "", targetLanguage = "C#" });
                
                if (string.IsNullOrEmpty(input?.jsonPayload))
                {
                    return Task.FromResult("{ \"error\": \"jsonPayload is required.\" }");
                }

                JToken parsed;
                try
                {
                    parsed = JToken.Parse(input.jsonPayload);
                }
                catch (JsonReaderException)
                {
                    return Task.FromResult("{ \"error\": \"Invalid JSON payload provided.\" }");
                }

                string schema = GenerateSchema(parsed, input.targetLanguage ?? "C#");
                
                return Task.FromResult(JsonConvert.SerializeObject(new { success = true, schema = schema }));
            }
            catch (Exception ex)
            {
                return Task.FromResult($"{{ \"error\": \"{ex.Message}\" }}");
            }
        }

        private string GenerateSchema(JToken token, string lang)
        {
            if (token is JObject obj)
            {
                var sb = new StringBuilder();
                sb.AppendLine(lang.Equals("C#", StringComparison.OrdinalIgnoreCase) ? "public class GeneratedSchema\n{" : "export interface GeneratedSchema {");

                foreach (var prop in obj.Properties())
                {
                    string typeStr = GetTypeString(prop.Value, lang);
                    if (lang.Equals("C#", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"    public {typeStr} {Capitalize(prop.Name)} {{ get; set; }}");
                    }
                    else
                    {
                        sb.AppendLine($"    {prop.Name}: {typeStr};");
                    }
                }

                sb.AppendLine("}");
                return sb.ToString();
            }
            return "Payload must be a JSON object.";
        }

        private string GetTypeString(JToken token, string lang)
        {
            bool isCSharp = lang.Equals("C#", StringComparison.OrdinalIgnoreCase);
            
            return token.Type switch
            {
                JTokenType.String => isCSharp ? "string" : "string",
                JTokenType.Integer => isCSharp ? "int" : "number",
                JTokenType.Float => isCSharp ? "double" : "number",
                JTokenType.Boolean => isCSharp ? "bool" : "boolean",
                JTokenType.Date => isCSharp ? "DateTime" : "Date",
                JTokenType.Array => isCSharp ? "List<object>" : "any[]",
                JTokenType.Object => isCSharp ? "object" : "any",
                _ => isCSharp ? "object" : "any"
            };
        }

        private string Capitalize(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return char.ToUpper(str[0]) + str.Substring(1);
        }
    }
}
