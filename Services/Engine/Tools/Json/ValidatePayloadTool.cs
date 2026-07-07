using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Json
{
    public class ValidatePayloadTool : IMcpTool
    {
        public string Name => "ValidatePayload";
        public string Description => "Checks if a string is a valid JSON object and formats it neatly.";
        public string InputSchema => "{ \"rawString\": \"string\" }";
        public bool RequiresAdminApproval => false;

        public Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new { rawString = "" });
                
                if (string.IsNullOrEmpty(input?.rawString))
                {
                    return Task.FromResult("{ \"error\": \"rawString is required.\" }");
                }

                try
                {
                    JToken parsed = JToken.Parse(input.rawString);
                    string formatted = parsed.ToString(Formatting.Indented);
                    
                    return Task.FromResult(JsonConvert.SerializeObject(new { isValid = true, formattedJson = formatted }));
                }
                catch (JsonReaderException ex)
                {
                    return Task.FromResult(JsonConvert.SerializeObject(new { isValid = false, parseError = ex.Message }));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult($"{{ \"error\": \"{ex.Message}\" }}");
            }
        }
    }
}
