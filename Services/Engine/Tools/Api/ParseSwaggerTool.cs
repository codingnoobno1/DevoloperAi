using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Api
{
    public class ParseSwaggerTool : IMcpTool
    {
        public string Name => "ParseSwagger";
        public string Description => "Extracts all API endpoints and schemas deterministically from a local swagger.json file.";
        public string InputSchema => "{ \"swaggerFilePath\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { swaggerFilePath = "" });
                
                if (string.IsNullOrEmpty(input?.swaggerFilePath) || !File.Exists(input.swaggerFilePath))
                {
                    return "{ \"error\": \"Valid swaggerFilePath is required.\" }";
                }

                string content = await File.ReadAllTextAsync(input.swaggerFilePath);
                JObject swagger = JObject.Parse(content);

                var endpoints = new List<object>();

                if (swagger["paths"] != null)
                {
                    foreach (var path in swagger["paths"].Children<JProperty>())
                    {
                        foreach (var method in path.Value.Children<JProperty>())
                        {
                            endpoints.Add(new
                            {
                                path = path.Name,
                                method = method.Name.ToUpper(),
                                operationId = method.Value["operationId"]?.ToString(),
                                summary = method.Value["summary"]?.ToString()
                            });
                        }
                    }
                }

                var schemas = new List<string>();
                var components = swagger["components"]?["schemas"] ?? swagger["definitions"];
                if (components != null)
                {
                    foreach (var schema in components.Children<JProperty>())
                    {
                        schemas.Add(schema.Name);
                    }
                }

                var result = new
                {
                    totalEndpoints = endpoints.Count,
                    totalSchemas = schemas.Count,
                    endpoints = endpoints,
                    schemaNames = schemas
                };

                return JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                return $"{{ \"error\": \"{ex.Message}\" }}";
            }
        }
    }
}
