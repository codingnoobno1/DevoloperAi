using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Auth;

namespace Syncro.Desktop.Services
{
    public class PixelService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private const string BaseUrl = "https://project-syncroo.netlify.app";
        private readonly string _localCachePath;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public PixelService(HttpClient httpClient, AuthService authService)
        {
            _httpClient = httpClient;
            _authService = authService;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Syncro");
            Directory.CreateDirectory(dir);
            _localCachePath = Path.Combine(dir, "git_task_distribution_cache.json");
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
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/marketplace/scripts");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    return await response.Content.ReadFromJsonAsync<List<MarketplaceScriptModel>>() ?? new();
                }

                return new List<MarketplaceScriptModel>
                {
                    new MarketplaceScriptModel { Id = "install_docker", Name = "Docker Installer", Description = "Fully automated Docker & Compose setup for development.", Author = "Syncro Team", Stars = 1250, Downloads = 45000 },
                    new MarketplaceScriptModel { Id = "setup_k8s", Name = "K8s Micro-Cluster", Description = "Deploy a lightweight Minikube/K3s environment instantly.", Author = "CloudNative", Stars = 890, Downloads = 12000 },
                    new MarketplaceScriptModel { Id = "clean_system", Name = "Disk Purge Pro", Description = "Cleans build artifacts, temp files, and unused packages across all SDKs.", Author = "OptimizationKing", Stars = 2100, Downloads = 89000 }
                };
            }
            catch { return new(); }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // TEAM MANAGEMENT & GIT TASK DISTRIBUTION (CLOUD API + LOCAL STORE BRIDGE)
        // ═════════════════════════════════════════════════════════════════════════

        private LocalGitTaskStore LoadLocalStore()
        {
            try
            {
                if (File.Exists(_localCachePath))
                {
                    var json = File.ReadAllText(_localCachePath);
                    var store = System.Text.Json.JsonSerializer.Deserialize<LocalGitTaskStore>(json, JsonOpts);
                    if (store != null)
                    {
                        if (PurgeLegacyDemoData(store)) SaveLocalStore(store);
                        return store;
                    }
                }
            }
            catch { }

            var seeded = CreateSeedStore();
            SaveLocalStore(seeded);
            return seeded;
        }

        private void SaveLocalStore(LocalGitTaskStore store)
        {
            try
            {
                File.WriteAllText(_localCachePath, System.Text.Json.JsonSerializer.Serialize(store, JsonOpts));
            }
            catch { }
        }

        /// <summary>
        /// Empty local store used until the platform returns real data. Nothing is invented: no demo
        /// projects, teammates or tasks (those created fake file locks that blocked real commits).
        /// </summary>
        private LocalGitTaskStore CreateSeedStore() => new LocalGitTaskStore();

        /// <summary>Ids of the demo data older builds wrote into the cache; removed on load.</summary>
        private static readonly HashSet<string> LegacyDemoIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "proj-syncro-desktop-core", "proj-pixel-platform-web",
            "task-git-receiver-guard-01", "task-ast-contract-scanner-02", "task-sre-verification-gate-03",
            "dev-rahul-sharma", "dev-sarah-chen", "dev-deepinder-singh",
        };

        private static bool PurgeLegacyDemoData(LocalGitTaskStore store)
        {
            int before = store.Projects.Count + store.Tasks.Count + store.TeamsByProject.Sum(kv => kv.Value.Count);
            store.Projects.RemoveAll(p => LegacyDemoIds.Contains(p.Id));
            store.Tasks.RemoveAll(t => LegacyDemoIds.Contains(t.Id) || LegacyDemoIds.Contains(t.ProjectId) || LegacyDemoIds.Contains(t.AssignedTo));
            foreach (var key in store.TeamsByProject.Keys.ToList())
            {
                if (LegacyDemoIds.Contains(key)) { store.TeamsByProject.Remove(key); continue; }
                store.TeamsByProject[key].RemoveAll(m => LegacyDemoIds.Contains(m.UserId));
            }
            int after = store.Projects.Count + store.Tasks.Count + store.TeamsByProject.Sum(kv => kv.Value.Count);
            return after != before;
        }

        private void RecomputeStoreMetrics(LocalGitTaskStore store)
        {
            string currentUid = !string.IsNullOrEmpty(_authService.CurrentUserId) ? _authService.CurrentUserId : "user-tushar";

            foreach (var proj in store.Projects)
            {
                var projTasks = store.Tasks.Where(t => t.ProjectId == proj.Id).ToList();
                var activeTasks = projTasks.Where(t => !string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToList();

                // Recompute TeammateLockedFiles on every task in this project
                foreach (var task in projTasks)
                {
                    var locks = new List<GitFileLockModel>();
                    foreach (var other in activeTasks.Where(o => o.Id != task.Id && !string.Equals(o.AssignedTo, task.AssignedTo, StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var fp in other.AssignedFiles)
                        {
                            locks.Add(new GitFileLockModel
                            {
                                FilePath = fp,
                                TaskId = other.Id,
                                TaskTitle = other.Title,
                                LockedByUserId = other.AssignedTo,
                                LockedByName = string.IsNullOrWhiteSpace(other.AssignedToName) ? other.AssignedTo : other.AssignedToName,
                                LockMode = other.FileLockMode,
                                TargetBranch = other.TargetBranch
                            });
                        }
                    }
                    task.TeammateLockedFiles = locks;
                }

                // Recompute team member workload stats
                if (store.TeamsByProject.TryGetValue(proj.Id, out var members))
                {
                    foreach (var m in members)
                    {
                        if (m.IsLead && m.UserId == "user-tushar" && !string.IsNullOrEmpty(_authService.CurrentUserId))
                        {
                            m.UserId = currentUid;
                        }
                        var mTasks = projTasks.Where(t =>
                            string.Equals(t.AssignedTo, m.UserId, StringComparison.OrdinalIgnoreCase) ||
                            (m.IsLead && string.Equals(t.AssignedTo, "user-tushar", StringComparison.OrdinalIgnoreCase))).ToList();
                        var mActive = mTasks.Where(t => !string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToList();
                        var mDone = mTasks.Where(t => string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToList();

                        m.ActiveTaskCount = mActive.Count;
                        m.CompletedTaskCount = mDone.Count;
                        m.TotalTaskCount = mTasks.Count;
                        m.AvgProgress = mTasks.Count > 0 ? (int)Math.Round(mTasks.Average(t => t.Progress)) : 0;
                        m.LockedFiles = mActive.SelectMany(t => t.AssignedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    }
                }
            }
        }

        public async Task<List<AgentTaskModel>> GetAssignedTasksAsync()
        {
            var store = LoadLocalStore();
            RecomputeStoreMetrics(store);
            string currentUid = !string.IsNullOrEmpty(_authService.CurrentUserId) ? _authService.CurrentUserId : "user-tushar";

            try
            {
                if (!string.IsNullOrEmpty(_authService.CurrentUserId))
                {
                    SetAuthHeader();
                    var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/user/tasks");
                    if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                    {
                        var jsonStr = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(jsonStr);
                        var remoteTasks = new List<AgentTaskModel>();

                        JsonElement arrEl = default;
                        bool hasArr = false;
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("tasks", out var tasksElement) &&
                            tasksElement.ValueKind == JsonValueKind.Array)
                        {
                            arrEl = tasksElement;
                            hasArr = true;
                        }
                        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            arrEl = doc.RootElement;
                            hasArr = true;
                        }

                        if (hasArr)
                        {
                            foreach (var t in arrEl.EnumerateArray())
                            {
                                remoteTasks.Add(ParseAgentTaskFromJson(t, currentUid));
                            }
                        }

                        // Merge remote tasks into local store so Git file scopes are preserved
                        foreach (var rt in remoteTasks)
                        {
                            var existing = store.Tasks.FirstOrDefault(x => x.Id == rt.Id);
                            if (existing == null)
                            {
                                store.Tasks.Insert(0, rt);
                            }
                            else
                            {
                                existing.Title = rt.Title;
                                existing.ScopeDescription = !string.IsNullOrEmpty(rt.ScopeDescription) ? rt.ScopeDescription : existing.ScopeDescription;
                                existing.Status = rt.Status;
                                existing.Progress = rt.Progress > 0 ? rt.Progress : existing.Progress;
                                if (!string.IsNullOrEmpty(rt.RepoUrl)) existing.RepoUrl = rt.RepoUrl;
                                if (rt.AssignedFiles.Count > 0) existing.AssignedFiles = rt.AssignedFiles;
                                if (rt.AllowedGlobs.Count > 0) existing.AllowedGlobs = rt.AllowedGlobs;
                                if (rt.TeammateLockedFiles.Count > 0) existing.TeammateLockedFiles = rt.TeammateLockedFiles;
                            }
                        }

                        RecomputeStoreMetrics(store);
                        SaveLocalStore(store);
                    }
                }
            }
            catch { }

            return store.Tasks
                .Where(t =>
                    string.Equals(t.AssignedTo, currentUid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.AssignedTo, "user-tushar", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Deadline)
                .ToList();
        }

        public async Task<List<PlatformProjectModel>> GetProjectsAsync()
        {
            var store = LoadLocalStore();
            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/projects");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            string id = el.TryGetProperty("_id", out var idP) ? idP.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(id)) continue;
                            string title = el.TryGetProperty("title", out var tP) ? tP.GetString() ?? "" : "";
                            string desc = el.TryGetProperty("description", out var dP) ? dP.GetString() ?? "" : "";
                            string status = el.TryGetProperty("status", out var sP) ? sP.GetString() ?? "active" : "active";
                            int prog = el.TryGetProperty("progress", out var pP) && pP.TryGetInt32(out var pv) ? pv : 0;
                            string repo = el.TryGetProperty("githubRepo", out var rP) ? rP.GetString() ?? "" : "";

                            var existing = store.Projects.FirstOrDefault(p => p.Id == id);
                            if (existing == null)
                            {
                                store.Projects.Add(new PlatformProjectModel
                                {
                                    Id = id,
                                    Title = title,
                                    Description = desc,
                                    Status = status,
                                    Progress = prog,
                                    GithubRepo = repo
                                });
                            }
                            else
                            {
                                existing.Title = title;
                                if (!string.IsNullOrEmpty(repo)) existing.GithubRepo = repo;
                            }
                        }
                        SaveLocalStore(store);
                    }
                }
            }
            catch { }

            return store.Projects;
        }

        public async Task<ProjectTeamRosterResponse> GetProjectTeamAsync(string projectId)
        {
            var store = LoadLocalStore();
            RecomputeStoreMetrics(store);

            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/projects/{projectId}/team");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var remote = await response.Content.ReadFromJsonAsync<ProjectTeamRosterResponse>(JsonOpts);
                    if (remote != null && remote.Members.Count > 0)
                    {
                        store.TeamsByProject[projectId] = remote.Members;
                        SaveLocalStore(store);
                        remote.FromPlatform = true;
                        return remote;
                    }
                }
            }
            catch { }

            var proj = store.Projects.FirstOrDefault(p => p.Id == projectId);
            if (proj == null)
                return new ProjectTeamRosterResponse { ProjectId = projectId, FromPlatform = false };
            if (!store.TeamsByProject.TryGetValue(proj.Id, out var members))
            {
                members = new List<TeamMemberModel>();
                store.TeamsByProject[proj.Id] = members;
            }

            return new ProjectTeamRosterResponse
            {
                ProjectId = proj.Id,
                Title = proj.Title,
                GithubRepo = proj.GithubRepo,
                Status = proj.Status,
                LeadId = proj.LeadId,
                Members = members
            };
        }

        public async Task<ProjectTeamRosterResponse> AddOrUpdateTeamMemberAsync(
            string projectId,
            TeamMemberModel member)
        {
            var store = LoadLocalStore();
            if (!store.TeamsByProject.TryGetValue(projectId, out var list))
            {
                list = new List<TeamMemberModel>();
                store.TeamsByProject[projectId] = list;
            }

            var existing = list.FirstOrDefault(m =>
                string.Equals(m.UserId, member.UserId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(member.Email) && string.Equals(m.Email, member.Email, StringComparison.OrdinalIgnoreCase)));

            if (existing == null)
            {
                if (string.IsNullOrWhiteSpace(member.UserId))
                {
                    member.UserId = "dev-" + Guid.NewGuid().ToString("N")[..8];
                }
                list.Add(member);
            }
            else
            {
                existing.Name = member.Name;
                existing.Email = member.Email;
                existing.ProjectRole = member.ProjectRole;
                existing.OwnedPaths = member.OwnedPaths;
                existing.MaxActiveTasks = member.MaxActiveTasks;
                existing.Skills = member.Skills;
            }

            RecomputeStoreMetrics(store);
            SaveLocalStore(store);

            bool synced = false;
            try
            {
                SetAuthHeader();
                var resp = await _httpClient.PatchAsJsonAsync($"{BaseUrl}/api/mobile/projects/{projectId}/team", new
                {
                    add = new[] { member.UserId },
                    memberUpdates = new[]
                    {
                        new
                        {
                            userId = member.UserId,
                            projectRole = member.ProjectRole,
                            ownedPaths = member.OwnedPaths,
                            maxActiveTasks = member.MaxActiveTasks
                        }
                    }
                });
                synced = resp.IsSuccessStatusCode;
            }
            catch { }

            var roster = await GetProjectTeamAsync(projectId);
            roster.LastChangeSyncedToPlatform = synced;
            return roster;
        }

        public async Task<ProjectTeamRosterResponse> RemoveTeamMemberAsync(string projectId, string userId)
        {
            var store = LoadLocalStore();
            if (store.TeamsByProject.TryGetValue(projectId, out var list))
            {
                list.RemoveAll(m => !m.IsLead && string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
                RecomputeStoreMetrics(store);
                SaveLocalStore(store);
            }

            bool synced = false;
            try
            {
                SetAuthHeader();
                var resp = await _httpClient.PatchAsJsonAsync($"{BaseUrl}/api/mobile/projects/{projectId}/team", new
                {
                    remove = new[] { userId }
                });
                synced = resp.IsSuccessStatusCode;
            }
            catch { }

            var roster = await GetProjectTeamAsync(projectId);
            roster.LastChangeSyncedToPlatform = synced;
            return roster;
        }

        public async Task<ProjectTasksAndLocksResponse> GetProjectTasksAndLocksAsync(string projectId)
        {
            var store = LoadLocalStore();
            RecomputeStoreMetrics(store);

            try
            {
                SetAuthHeader();
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/mobile/project-progress/tasks/project/{projectId}");
                if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.TryGetProperty("tasks", out var tasksEl) && tasksEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in tasksEl.EnumerateArray())
                        {
                            var rt = ParseAgentTaskFromJson(el, "");
                            if (string.IsNullOrEmpty(rt.ProjectId)) rt.ProjectId = projectId;
                            if (!store.Tasks.Any(t => t.Id == rt.Id))
                            {
                                store.Tasks.Add(rt);
                            }
                        }
                        RecomputeStoreMetrics(store);
                        SaveLocalStore(store);
                    }
                }
            }
            catch { }

            var projTasks = store.Tasks
                .Where(t => t.ProjectId == projectId)
                .OrderByDescending(t => t.Deadline)
                .ToList();

            var fileLocks = new List<GitFileLockModel>();
            foreach (var t in projTasks.Where(t => !string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var fp in t.AssignedFiles)
                {
                    fileLocks.Add(new GitFileLockModel
                    {
                        FilePath = fp,
                        TaskId = t.Id,
                        TaskTitle = t.Title,
                        LockedByUserId = t.AssignedTo,
                        LockedByName = string.IsNullOrWhiteSpace(t.AssignedToName) ? t.AssignedTo : t.AssignedToName,
                        LockMode = t.FileLockMode,
                        TargetBranch = t.TargetBranch
                    });
                }
            }

            return new ProjectTasksAndLocksResponse
            {
                ProjectId = projectId,
                Tasks = projTasks,
                FileLocks = fileLocks
            };
        }

        public async Task<CreateGitTaskResult> CreateGitTaskAsync(CreateGitTaskRequest req)
        {
            var store = LoadLocalStore();
            RecomputeStoreMetrics(store);

            var cleanAssigned = req.AssignedFiles
                .Select(f => f.Replace('\\', '/').Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cleanGlobs = req.AllowedGlobs
                .Select(g => g.Replace('\\', '/').Trim())
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cleanReadOnly = req.ReadOnlyFiles
                .Select(f => f.Replace('\\', '/').Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Check collisions against other active tasks in the same project
            var collisions = new List<GitFileLockModel>();
            var activeOtherTasks = store.Tasks.Where(t =>
                t.ProjectId == req.ProjectId &&
                !string.Equals(t.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(t.AssignedTo, req.AssignedTo, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var other in activeOtherTasks)
            {
                foreach (var ofile in other.AssignedFiles)
                {
                    if (cleanAssigned.Any(f => string.Equals(f, ofile, StringComparison.OrdinalIgnoreCase)))
                    {
                        collisions.Add(new GitFileLockModel
                        {
                            FilePath = ofile,
                            TaskId = other.Id,
                            TaskTitle = other.Title,
                            LockedByUserId = other.AssignedTo,
                            LockedByName = string.IsNullOrWhiteSpace(other.AssignedToName) ? other.AssignedTo : other.AssignedToName,
                            LockMode = other.FileLockMode,
                            TargetBranch = other.TargetBranch
                        });
                    }
                }
            }

            if (collisions.Count > 0 && !req.ForceAssign && !string.Equals(req.FileLockMode, "advisory", StringComparison.OrdinalIgnoreCase))
            {
                return new CreateGitTaskResult
                {
                    Success = false,
                    Error = $"File-lock collision: {collisions.Count} file(s) are already locked by another active teammate task.",
                    Collisions = collisions
                };
            }

            var proj = store.Projects.FirstOrDefault(p => p.Id == req.ProjectId);
            string slugAssignee = Slugify(!string.IsNullOrWhiteSpace(req.AssignedToName) ? req.AssignedToName : req.AssignedTo);
            string slugTitle = Slugify(req.Title);
            string targetBranch = !string.IsNullOrWhiteSpace(req.TargetBranch)
                ? req.TargetBranch.Trim()
                : $"task/{slugAssignee}-{slugTitle}";

            var newTask = new AgentTaskModel
            {
                Id = "task-" + Guid.NewGuid().ToString("N")[..10],
                ProjectId = req.ProjectId,
                ProjectTitle = proj?.Title ?? "Syncro Project",
                Title = req.Title.Trim(),
                ScopeDescription = req.Description.Trim(),
                RepoUrl = !string.IsNullOrWhiteSpace(req.RepoUrl) ? req.RepoUrl.Trim() : (proj?.GithubRepo ?? ""),
                BaseBranch = !string.IsNullOrWhiteSpace(req.BaseBranch) ? req.BaseBranch.Trim() : "main",
                TargetBranch = targetBranch,
                Status = "pending",
                Progress = 0,
                Priority = req.Priority,
                Deadline = req.Deadline,
                AssignedTo = req.AssignedTo,
                AssignedToName = req.AssignedToName,
                AssignedBy = _authService.CurrentUserId ?? "user-tushar",
                AssignedByName = _authService.CurrentUserName ?? "Project Lead",
                AssignedFiles = cleanAssigned,
                AllowedGlobs = cleanGlobs,
                ReadOnlyFiles = cleanReadOnly,
                FileLockMode = req.FileLockMode
            };

            try
            {
                SetAuthHeader();
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/mobile/project-progress/tasks", new
                {
                    projectId = req.ProjectId,
                    title = newTask.Title,
                    description = newTask.ScopeDescription,
                    assignedTo = newTask.AssignedTo,
                    assignedToName = newTask.AssignedToName,
                    priority = newTask.Priority,
                    deadline = newTask.Deadline.ToString("yyyy-MM-dd"),
                    repoUrl = newTask.RepoUrl,
                    baseBranch = newTask.BaseBranch,
                    targetBranch = newTask.TargetBranch,
                    assignedFiles = newTask.AssignedFiles,
                    allowedGlobs = newTask.AllowedGlobs,
                    readOnlyFiles = newTask.ReadOnlyFiles,
                    fileLockMode = newTask.FileLockMode,
                    forceAssign = req.ForceAssign
                });

                if (response.IsSuccessStatusCode)
                {
                    var respJson = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respJson);
                    if (doc.RootElement.TryGetProperty("task", out var taskEl) &&
                        taskEl.TryGetProperty("_id", out var idEl))
                    {
                        newTask.Id = idEl.GetString() ?? newTask.Id;
                    }
                }
            }
            catch { }

            store.Tasks.Insert(0, newTask);
            RecomputeStoreMetrics(store);
            SaveLocalStore(store);

            return new CreateGitTaskResult
            {
                Success = true,
                Task = newTask,
                Collisions = collisions
            };
        }

        public async Task<AgentTaskModel?> UpdateGitTaskAsync(string taskId, UpdateGitTaskRequest req)
        {
            var store = LoadLocalStore();
            var task = store.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task != null)
            {
                if (!string.IsNullOrEmpty(req.Status)) task.Status = req.Status;
                if (req.Progress.HasValue)
                {
                    task.Progress = Math.Clamp(req.Progress.Value, 0, 100);
                    if (task.Progress >= 100 && task.Status != "completed")
                        task.Status = "completed";
                }
                if (req.LatestCommitSha != null) task.LatestCommitSha = req.LatestCommitSha;
                if (req.CommitCount.HasValue) task.CommitCount = req.CommitCount.Value;
                if (req.PullRequestUrl != null) task.PullRequestUrl = req.PullRequestUrl;
                if (req.VerificationGate != null) task.VerificationGate = req.VerificationGate;
                if (req.AssignedFiles != null)
                {
                    task.AssignedFiles = req.AssignedFiles
                        .Select(f => f.Replace('\\', '/').Trim())
                        .Where(f => !string.IsNullOrEmpty(f))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                if (req.AllowedGlobs != null)
                {
                    task.AllowedGlobs = req.AllowedGlobs
                        .Select(g => g.Replace('\\', '/').Trim())
                        .Where(g => !string.IsNullOrEmpty(g))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                if (!string.IsNullOrEmpty(req.TargetBranch)) task.TargetBranch = req.TargetBranch;
                if (!string.IsNullOrEmpty(req.FileLockMode)) task.FileLockMode = req.FileLockMode;

                RecomputeStoreMetrics(store);
                SaveLocalStore(store);
            }

            try
            {
                SetAuthHeader();
                await _httpClient.PatchAsJsonAsync($"{BaseUrl}/api/mobile/project-progress/tasks/{taskId}", new
                {
                    status = req.Status,
                    progress = req.Progress,
                    latestCommitSha = req.LatestCommitSha,
                    commitCount = req.CommitCount,
                    pullRequestUrl = req.PullRequestUrl,
                    verificationGate = req.VerificationGate,
                    assignedFiles = req.AssignedFiles,
                    allowedGlobs = req.AllowedGlobs,
                    targetBranch = req.TargetBranch,
                    fileLockMode = req.FileLockMode
                });
            }
            catch { }

            return task;
        }

        /// <summary>
        /// Asks the platform to open (or return) the pull request for a verified task. GitHub App
        /// credentials stay on the platform server; Desktop only sends its normal member JWT.
        /// </summary>
        public async Task<OpenPullRequestResult> OpenTaskPullRequestAsync(string taskId)
        {
            try
            {
                SetAuthHeader();
                var response = await _httpClient.PostAsJsonAsync(
                    $"{BaseUrl}/api/mobile/tasks/{Uri.EscapeDataString(taskId)}/pr",
                    new { });
                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
                if (!response.IsSuccessStatusCode)
                {
                    var error = doc.RootElement.TryGetProperty("error", out var errorEl)
                        ? errorEl.GetString()
                        : $"Platform returned HTTP {(int)response.StatusCode}.";
                    return new OpenPullRequestResult { Success = false, Error = error ?? "Could not open pull request." };
                }

                if (!doc.RootElement.TryGetProperty("pullRequest", out var pr))
                    return new OpenPullRequestResult { Success = false, Error = "Platform response did not include a pull request." };

                return new OpenPullRequestResult
                {
                    Success = true,
                    Url = pr.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                    Number = pr.TryGetProperty("number", out var number) && number.TryGetInt32(out var n) ? n : 0,
                    Existing = doc.RootElement.TryGetProperty("existing", out var existing) && existing.GetBoolean()
                };
            }
            catch (Exception ex)
            {
                return new OpenPullRequestResult { Success = false, Error = ex.Message };
            }
        }

        public async Task<bool> DeleteGitTaskAsync(string taskId)
        {
            var store = LoadLocalStore();
            int removed = store.Tasks.RemoveAll(t => t.Id == taskId);
            if (removed > 0)
            {
                RecomputeStoreMetrics(store);
                SaveLocalStore(store);
            }

            try
            {
                SetAuthHeader();
                await _httpClient.DeleteAsync($"{BaseUrl}/api/mobile/project-progress/tasks/{taskId}");
            }
            catch { }

            return removed > 0;
        }

        public async Task<bool> SyncRepoFileTreeAsync(
            string projectId,
            string repoUrl,
            string localRepoPath,
            string defaultBranch,
            List<string> branches,
            List<string> fileTree)
        {
            var store = LoadLocalStore();
            var proj = store.Projects.FirstOrDefault(p => p.Id == projectId);
            if (proj != null)
            {
                if (!string.IsNullOrWhiteSpace(repoUrl)) proj.GithubRepo = repoUrl;
                if (!string.IsNullOrWhiteSpace(localRepoPath)) proj.LocalRepoPath = localRepoPath;
                SaveLocalStore(store);
            }

            try
            {
                SetAuthHeader();
                var resp = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/mobile/git/tree", new
                {
                    projectId,
                    repoUrl,
                    defaultBranch,
                    branches,
                    fileTree
                });
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;   // the platform did not receive it
            }
        }

        private static string Slugify(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "item";
            var chars = text.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                .ToArray();
            var raw = new string(chars);
            while (raw.Contains("--")) raw = raw.Replace("--", "-");
            raw = raw.Trim('-');
            return raw.Length > 24 ? raw[..24].Trim('-') : (string.IsNullOrEmpty(raw) ? "item" : raw);
        }

        private static AgentTaskModel ParseAgentTaskFromJson(JsonElement t, string fallbackUserId)
        {
            var model = new AgentTaskModel
            {
                Id = t.TryGetProperty("_id", out var idProp) ? idProp.GetString() ?? "" :
                     (t.TryGetProperty("id", out var idProp2) ? idProp2.GetString() ?? "" : ""),
                ProjectId = t.TryGetProperty("projectId", out var pidProp) ? pidProp.GetString() ?? "" : "",
                ProjectTitle = t.TryGetProperty("projectTitle", out var ptProp) ? ptProp.GetString() ?? "" : "",
                Title = t.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                ScopeDescription = t.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" :
                                   (t.TryGetProperty("scopeDescription", out var scopeProp) ? scopeProp.GetString() ?? "" : ""),
                RepoUrl = t.TryGetProperty("repoUrl", out var repoProp) ? repoProp.GetString() ?? "" : "",
                BaseBranch = t.TryGetProperty("baseBranch", out var bbProp) && !string.IsNullOrWhiteSpace(bbProp.GetString()) ? bbProp.GetString()! : "main",
                TargetBranch = t.TryGetProperty("targetBranch", out var tbProp) ? tbProp.GetString() ?? "" : "",
                Status = t.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "pending" : "pending",
                Progress = t.TryGetProperty("progress", out var progProp) && progProp.TryGetInt32(out var pv) ? pv : 0,
                Priority = t.TryGetProperty("priority", out var prioProp) ? prioProp.GetString() ?? "medium" : "medium",
                Deadline = t.TryGetProperty("deadline", out var dlProp) && dlProp.TryGetDateTime(out var dl) ? dl : DateTime.Now.AddDays(3),
                AssignedTo = t.TryGetProperty("assignedTo", out var atProp) ? atProp.GetString() ?? fallbackUserId : fallbackUserId,
                AssignedToName = t.TryGetProperty("assignedToName", out var atnProp) ? atnProp.GetString() ?? "" : "",
                AssignedBy = t.TryGetProperty("assignedBy", out var abProp) ? abProp.GetString() ?? "" : "",
                AssignedByName = t.TryGetProperty("assignedByName", out var abnProp) ? abnProp.GetString() ?? "Project Lead" : "Project Lead",
                FileLockMode = t.TryGetProperty("fileLockMode", out var flmProp) ? flmProp.GetString() ?? "strict" : "strict",
                LatestCommitSha = t.TryGetProperty("latestCommitSha", out var shaProp) ? shaProp.GetString() ?? "" : "",
                CommitCount = t.TryGetProperty("commitCount", out var ccProp) && ccProp.TryGetInt32(out var ccv) ? ccv : 0,
                PullRequestUrl = t.TryGetProperty("pullRequestUrl", out var prProp) ? prProp.GetString() ?? "" : ""
            };

            if (t.TryGetProperty("assignedFiles", out var afEl) && afEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in afEl.EnumerateArray())
                    if (item.GetString() is string s && !string.IsNullOrWhiteSpace(s))
                        model.AssignedFiles.Add(s);
            }

            if (t.TryGetProperty("allowedGlobs", out var agEl) && agEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in agEl.EnumerateArray())
                    if (item.GetString() is string s && !string.IsNullOrWhiteSpace(s))
                        model.AllowedGlobs.Add(s);
            }

            if (t.TryGetProperty("readOnlyFiles", out var roEl) && roEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in roEl.EnumerateArray())
                    if (item.GetString() is string s && !string.IsNullOrWhiteSpace(s))
                        model.ReadOnlyFiles.Add(s);
            }

            if (t.TryGetProperty("teammateLockedFiles", out var tlEl) && tlEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tlEl.EnumerateArray())
                {
                    model.TeammateLockedFiles.Add(new GitFileLockModel
                    {
                        FilePath = item.TryGetProperty("filePath", out var fp) ? fp.GetString() ?? "" : "",
                        TaskId = item.TryGetProperty("taskId", out var tid) ? tid.GetString() ?? "" : "",
                        TaskTitle = item.TryGetProperty("taskTitle", out var tt) ? tt.GetString() ?? "" : "",
                        LockedByUserId = item.TryGetProperty("lockedByUserId", out var lbu) ? lbu.GetString() ?? "" : "",
                        LockedByName = item.TryGetProperty("lockedByName", out var lbn) ? lbn.GetString() ?? "Teammate" : "Teammate",
                        LockMode = item.TryGetProperty("lockMode", out var lm) ? lm.GetString() ?? "strict" : "strict",
                        TargetBranch = item.TryGetProperty("targetBranch", out var tbr) ? tbr.GetString() ?? "" : ""
                    });
                }
            }

            if (string.IsNullOrWhiteSpace(model.TargetBranch))
            {
                model.TargetBranch = $"feat/{Slugify(model.Title)}";
            }

            return model;
        }
    }

    public class LocalGitTaskStore
    {
        public List<PlatformProjectModel> Projects { get; set; } = new();
        public Dictionary<string, List<TeamMemberModel>> TeamsByProject { get; set; } = new();
        public List<AgentTaskModel> Tasks { get; set; } = new();
    }

    public class PlatformProjectModel
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "active";
        public int Progress { get; set; }
        public string GithubRepo { get; set; } = "";
        public string LocalRepoPath { get; set; } = "";
        public string LeadId { get; set; } = "";
        public string LeadName { get; set; } = "";
    }

    public class ProjectTeamRosterResponse
    {
        public string ProjectId { get; set; } = "";
        public string Title { get; set; } = "";
        public string GithubRepo { get; set; } = "";
        public string Status { get; set; } = "active";
        public string LeadId { get; set; } = "";
        public List<TeamMemberModel> Members { get; set; } = new();
        /// <summary>True only when this roster came from the platform API.</summary>
        public bool FromPlatform { get; set; }
        /// <summary>Result of the last add/remove call to the platform (null = no change made).</summary>
        public bool? LastChangeSyncedToPlatform { get; set; }
    }

    public class TeamMemberModel
    {
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Avatar { get; set; } = "";
        public string PlatformRole { get; set; } = "sankalp_member";
        public string ProjectRole { get; set; } = "contributor";
        public List<string> OwnedPaths { get; set; } = new();
        public int MaxActiveTasks { get; set; } = 5;
        public List<string> Skills { get; set; } = new();
        public bool IsLead { get; set; }
        public int ActiveTaskCount { get; set; }
        public int CompletedTaskCount { get; set; }
        public int TotalTaskCount { get; set; }
        public int AvgProgress { get; set; }
        public List<string> LockedFiles { get; set; } = new();
    }

    public class ProjectTasksAndLocksResponse
    {
        public string ProjectId { get; set; } = "";
        public List<AgentTaskModel> Tasks { get; set; } = new();
        public List<GitFileLockModel> FileLocks { get; set; } = new();
    }

    public class CreateGitTaskRequest
    {
        public string ProjectId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string AssignedTo { get; set; } = "";
        public string AssignedToName { get; set; } = "";
        public string Priority { get; set; } = "medium";
        public DateTime Deadline { get; set; } = DateTime.Now.AddDays(3);
        public string RepoUrl { get; set; } = "";
        public string BaseBranch { get; set; } = "main";
        public string TargetBranch { get; set; } = "";
        public List<string> AssignedFiles { get; set; } = new();
        public List<string> AllowedGlobs { get; set; } = new();
        public List<string> ReadOnlyFiles { get; set; } = new();
        public string FileLockMode { get; set; } = "strict";
        public bool ForceAssign { get; set; }
    }

    public class CreateGitTaskResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public AgentTaskModel? Task { get; set; }
        public List<GitFileLockModel> Collisions { get; set; } = new();
    }

    public class UpdateGitTaskRequest
    {
        public string? Status { get; set; }
        public int? Progress { get; set; }
        public string? LatestCommitSha { get; set; }
        public int? CommitCount { get; set; }
        public string? PullRequestUrl { get; set; }
        public TaskVerificationGateModel? VerificationGate { get; set; }
        public List<string>? AssignedFiles { get; set; }
        public List<string>? AllowedGlobs { get; set; }
        public string? TargetBranch { get; set; }
        public string? FileLockMode { get; set; }
    }

    public class OpenPullRequestResult
    {
        public bool Success { get; set; }
        public string Url { get; set; } = "";
        public int Number { get; set; }
        public bool Existing { get; set; }
        public string Error { get; set; } = "";
    }

    public class GitFileLockModel
    {
        public string FilePath { get; set; } = "";
        public string TaskId { get; set; } = "";
        public string TaskTitle { get; set; } = "";
        public string LockedByUserId { get; set; } = "";
        public string LockedByName { get; set; } = "";
        public string LockMode { get; set; } = "strict";
        public string TargetBranch { get; set; } = "";
    }

    public class TaskVerificationGateModel
    {
        public bool BuildPassed { get; set; }
        public bool TestsPassed { get; set; }
        public bool SastPassed { get; set; }
        public bool ContractsPassed { get; set; }
        /// <summary>"passed" | "failed" | "not run: reason" — empty means never verified.</summary>
        public string BuildStatus { get; set; } = "";
        public string TestsStatus { get; set; } = "";
        public string SastStatus { get; set; } = "";
        public string ContractsStatus { get; set; } = "";
        public DateTime? VerifiedAt { get; set; }
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
        public string ProjectId { get; set; } = "";
        public string ProjectTitle { get; set; } = "";
        public string Title { get; set; } = "";
        public string ScopeDescription { get; set; } = "";
        public string RepoUrl { get; set; } = "";
        public string BaseBranch { get; set; } = "main";
        public string TargetBranch { get; set; } = "";
        public string Status { get; set; } = "pending";
        public int Progress { get; set; }
        public string Priority { get; set; } = "medium";
        public DateTime Deadline { get; set; }
        public string AssignedTo { get; set; } = "";
        public string AssignedToName { get; set; } = "";
        public string AssignedBy { get; set; } = "";
        public string AssignedByName { get; set; } = "Project Lead";
        public List<string> AssignedFiles { get; set; } = new();
        public List<string> AllowedGlobs { get; set; } = new();
        public List<string> ReadOnlyFiles { get; set; } = new();
        public string FileLockMode { get; set; } = "strict";
        public string LatestCommitSha { get; set; } = "";
        public int CommitCount { get; set; }
        public string PullRequestUrl { get; set; } = "";
        public TaskVerificationGateModel VerificationGate { get; set; } = new();
        public List<GitFileLockModel> TeammateLockedFiles { get; set; } = new();
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
