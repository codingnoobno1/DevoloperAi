using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services
{
    public class NetlifyService
    {
        private readonly HttpClient _httpClient;
        private const string ApiBaseUrl = "https://api.netlify.com/api/v1";
        private string? _token;

        public NetlifyService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public void SetToken(string token)
        {
            _token = token;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        public async Task<NetlifySite?> GetSiteStatus(string siteName)
        {
            try
            {
                // Note: Netlify API allows using the site domain as the ID in many cases
                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/sites/{siteName}.netlify.app");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<NetlifySite>();
                }
                
                // Fallback: search sites if the direct name doesn't work
                var sites = await _httpClient.GetFromJsonAsync<List<NetlifySite>>($"{ApiBaseUrl}/sites");
                return sites?.Find(s => s.Name == siteName);
            }
            catch { return null; }
        }

        public async Task<List<NetlifyDeploy>> GetRecentDeploys(string siteId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{ApiBaseUrl}/sites/{siteId}/deploys");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<NetlifyDeploy>>() ?? new();
                }
                return new();
            }
            catch { return new(); }
        }
    }

    public class NetlifySite
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public string State { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public NetlifyPublishedDeploy? PublishedDeploy { get; set; }
    }

    public class NetlifyPublishedDeploy
    {
        public string Id { get; set; } = "";
        public string State { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string? Summary { get; set; }
    }

    public class NetlifyDeploy
    {
        public string Id { get; set; } = "";
        public string SiteId { get; set; } = "";
        public string State { get; set; } = "";
        public string? Context { get; set; }
        public string? Branch { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CommitRef { get; set; }
        public string? Title { get; set; }
    }
}
