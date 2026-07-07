using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Auth;

namespace Syncro.Desktop.Services
{
    public class PixelService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private const string BaseUrl = "https://project-syncroo.netlify.app";

        public PixelService(HttpClient httpClient, AuthService authService)
        {
            _httpClient = httpClient;
            _authService = authService;
        }

        private void SetAuthHeader()
        {
            if (!string.IsNullOrEmpty(_authService.Token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _authService.Token);
            }
        }

        public async Task<UserProfileModel?> GetProfile()
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/user/profile");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    return await response.Content.ReadFromJsonAsync<UserProfileModel>();
                }
                return null;
            }
            catch { return null; }
        }

        public async Task<List<ProposalModel>> GetUserProposals()
        {
            try
            {
                if (string.IsNullOrEmpty(_authService.CurrentUserId)) return new();
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/proposals/user/{_authService.CurrentUserId}");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    return await response.Content.ReadFromJsonAsync<List<ProposalModel>>() ?? new();
                }
                return new();
            }
            catch { return new(); }
        }

        public async Task<FeedResponse?> GetFeed()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/feed");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    return await response.Content.ReadFromJsonAsync<FeedResponse>();
                }
                return null;
            }
            catch { return null; }
        }

        public async Task<bool> CreateProposal(ProposalModel proposal)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/proposals", proposal);
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> DeleteProposal(string id)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.DeleteAsync($"{BaseUrl}/api/proposals?id={id}");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> UpvoteProposal(string proposalId)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/votes", new { proposalId, value = 1 });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> AddComment(string proposalId, string content)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/comments", new { proposalId, content });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<List<MarketplaceScriptModel>> GetMarketplaceScripts()
        {
            try
            {
                // Simulated online marketplace fetch - in production this would be a real API endpoint
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/marketplace/scripts");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    return await response.Content.ReadFromJsonAsync<List<MarketplaceScriptModel>>() ?? new();
                }
                
                // Fallback to static sample if API is not yet ready on backend
                return new List<MarketplaceScriptModel>
                {
                    new MarketplaceScriptModel { Id = "install_docker", Name = "Docker Installer", Description = "Fully automated Docker & Compose setup for development.", Author = "Syncro Team", Stars = 1250, Downloads = 45000 },
                    new MarketplaceScriptModel { Id = "setup_k8s", Name = "K8s Micro-Cluster", Description = "Deploy a lightweight Minikube/K3s environment instantly.", Author = "CloudNative", Stars = 890, Downloads = 12000 },
                    new MarketplaceScriptModel { Id = "clean_system", Name = "Disk Purge Pro", Description = "Cleans build artifacts, temp files, and unused packages across all SDKs.", Author = "OptimizationKing", Stars = 2100, Downloads = 89000 }
                };
            }
            catch { return new(); }
        }

        public async Task<List<AgentTaskModel>> GetAssignedTasksAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_authService.CurrentUserId)) return new();
                SetAuthHeader();
                
                // Attempt to fetch from real API
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/user/tasks");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                    var tasksList = new List<AgentTaskModel>();
                    
                    // Check if tasks are wrapped in a "tasks" object (as seen in the live API)
                    if (doc.RootElement.TryGetProperty("tasks", out var tasksElement) && tasksElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var t in tasksElement.EnumerateArray())
                        {
                            tasksList.Add(new AgentTaskModel
                            {
                                Id = t.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" : 
                                     (t.TryGetProperty("id", out var idProp2) ? idProp2.GetString() ?? "" : ""),
                                Title = t.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                                ScopeDescription = t.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : 
                                                   (t.TryGetProperty("scopeDescription", out var scopeProp) ? scopeProp.GetString() ?? "" : ""),
                                RepoUrl = t.TryGetProperty("repoUrl", out var repoProp) ? repoProp.GetString() ?? "" : "",
                                Status = t.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "pending" : "pending",
                                Deadline = t.TryGetProperty("deadline", out var dlProp) && dlProp.TryGetDateTime(out var dl) ? dl : DateTime.Now
                            });
                        }
                        return tasksList;
                    }
                    
                    // Fallback for direct array response
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        return System.Text.Json.JsonSerializer.Deserialize<List<AgentTaskModel>>(jsonStr, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    }
                }
                
                return new();
            }
            catch { return new(); }
        }
    }

    public class MarketplaceScriptModel
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "Community";
        public int Stars { get; set; }
        public int Downloads { get; set; }
        public List<string> Platforms { get; set; } = new();
    }

    public class AgentTaskModel
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string ScopeDescription { get; set; } = "";
        public string RepoUrl { get; set; } = "";
        public string Status { get; set; } = "pending";
        public DateTime Deadline { get; set; }
    }

    public class UserProfileModel
    {
        public string? Id { get; set; }
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Avatar { get; set; }
        public string? Role { get; set; }
        public string? UniversityName { get; set; }
        public List<string> Skills { get; set; } = new();
    }

    public class ProposalModel
    {
        public string? Id { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public string Type { get; set; } = "idea";
        public string Stage { get; set; } = "proposal";
        public List<string> TechStack { get; set; } = new();
        public int TotalVotes { get; set; }
        public int Upvotes { get; set; }
        public int CommentsCount { get; set; }
        public int TeamSize { get; set; } = 1;
        public DateTime CreatedAt { get; set; }
    }

    public class FeedResponse
    {
        public List<FeedProposal> Proposals { get; set; } = new();
        public List<FeedActivity> Activity { get; set; } = new();
    }

    public class FeedProposal : ProposalModel
    {
        public FeedActor? CreatedBy { get; set; }
    }

    public class FeedActivity
    {
        public string? Id { get; set; }
        public FeedActor? ActorId { get; set; }
        public string Action { get; set; } = "";
        public string? Content { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class FeedActor
    {
        public string Name { get; set; } = "";
        public string? Avatar { get; set; }
        public string? Role { get; set; }
    }
}
