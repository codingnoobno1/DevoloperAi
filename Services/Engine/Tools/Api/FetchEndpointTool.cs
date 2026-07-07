using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Engine.Mcp;

namespace Syncro.Desktop.Services.Engine.Tools.Api
{
    public class FetchEndpointTool : IMcpTool
    {
        public string Name => "FetchEndpoint";
        public string Description => "Tests an API endpoint by sending an HTTP request and returning the raw status code and response body.";
        public string InputSchema => "{ \"url\": \"string\", \"method\": \"string?\", \"body\": \"string?\" }";
        public bool RequiresAdminApproval => true; // Needs approval since it makes external network requests

        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<string> ExecuteAsync(string inputJson)
        {
            try
            {
                var input = JsonConvert.DeserializeAnonymousType(inputJson, new 
                { 
                    url = "", 
                    method = "GET", 
                    body = "" 
                });

                if (string.IsNullOrEmpty(input?.url))
                {
                    return "{ \"error\": \"url is required.\" }";
                }

                var request = new HttpRequestMessage(new HttpMethod(input.method ?? "GET"), input.url);
                
                if (!string.IsNullOrEmpty(input.body))
                {
                    request.Content = new StringContent(input.body, Encoding.UTF8, "application/json");
                }

                var response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                var result = new
                {
                    statusCode = (int)response.StatusCode,
                    isSuccess = response.IsSuccessStatusCode,
                    body = responseContent
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
