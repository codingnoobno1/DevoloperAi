using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Infrastructure.AgentBridge;

namespace Syncro.Desktop.Services.SyncroCLI
{
    public class AgentOrchestrator
    {
        private readonly AgentBridgeServer _bridgeServer;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };
        private readonly string _syncroDbPath;

        public List<AgentSession> ActiveSessions { get; } = new();
        public List<HindsightRecord> OfflineHindsightMemory { get; } = new();
        public List<AgentAction> LoggedActions { get; } = new();
        public List<LlmActionRequest> LoggedLlmRequests { get; } = new();
        public int TotalTokensTracked { get; set; } = 0;

        public event Action<string>? OnLogUpdated;

        public AgentOrchestrator(AgentBridgeServer bridgeServer)
        {
            _bridgeServer = bridgeServer;
            _bridgeServer.OnPacketReceived += HandlePacket;

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _syncroDbPath = Path.Combine(userProfile, "SyncroWorkspace", ".syncro_db");
            try
            {
                if (!Directory.Exists(_syncroDbPath))
                {
                    Directory.CreateDirectory(_syncroDbPath);
                }
                LoadHindsightMemory();
            }
            catch {}
        }

        private void LoadHindsightMemory()
        {
            try
            {
                string memoryFile = Path.Combine(_syncroDbPath, "hindsight_records.json");
                if (File.Exists(memoryFile))
                {
                    var data = JsonConvert.DeserializeObject<List<HindsightRecord>>(File.ReadAllText(memoryFile));
                    if (data != null)
                    {
                        OfflineHindsightMemory.Clear();
                        OfflineHindsightMemory.AddRange(data);
                    }
                }
                else
                {
                    // Scaffold mock initial records for offline RAG failsafe
                    OfflineHindsightMemory.Add(new HindsightRecord
                    {
                        Problem = "Compile Error: TargetFrameworkAttribute duplicate in net9.0 AssemblyAttributes.cs",
                        RootCause = "Duplicate target framework attributes in auto-generated MSBuild output during simultaneous CLI and Desktop compilation.",
                        Solution = "Excludeobj folder or use <GenerateAssemblyInfo>false</GenerateAssemblyInfo> in project csproj.",
                        Success = true,
                        Confidence = 0.95f
                    });
                    OfflineHindsightMemory.Add(new HindsightRecord
                    {
                        Problem = "Missing package.json npm install command wrapper fail with Win32Exception",
                        RootCause = "Executing npm directly on Windows via Process.Start without cmd.exe shell execution wrapper throws access denied or file not found.",
                        Solution = "Redirect execution through cmd.exe /C 'npm install'.",
                        Success = true,
                        Confidence = 0.99f
                    });
                    SaveHindsightMemory();
                }
            }
            catch {}
        }

        private void SaveHindsightMemory()
        {
            try
            {
                string memoryFile = Path.Combine(_syncroDbPath, "hindsight_records.json");
                File.WriteAllText(memoryFile, JsonConvert.SerializeObject(OfflineHindsightMemory, Formatting.Indented));
            }
            catch {}
        }

        private void HandlePacket(AgentPacket packet)
        {
            try
            {
                string logLine = $"[{packet.PacketType}] Agent: {packet.AgentName} | Session: {packet.SessionId} | Payload: {JsonConvert.SerializeObject(packet.Payload)}";
                OnLogUpdated?.Invoke(logLine);

                // Update session state
                if (!string.IsNullOrEmpty(packet.SessionId))
                {
                    var guid = Guid.Parse(packet.SessionId);
                    AgentSession? session = null;
                    lock (ActiveSessions)
                    {
                        session = ActiveSessions.FirstOrDefault(s => s.Id == guid);
                        if (session == null)
                        {
                            session = new AgentSession
                            {
                                Id = guid,
                                AgentName = packet.AgentName ?? "CLI Agent"
                            };
                            ActiveSessions.Add(session);
                        }
                    }

                    if (packet.PacketType == "Status" && packet.Payload != null)
                    {
                        session.CurrentTask = packet.Payload.ToString() ?? "";
                    }
                }

                // Process structured telemetry
                if (packet.PacketType == "Action" && packet.Payload != null)
                {
                    try
                    {
                        var action = JsonConvert.DeserializeObject<AgentAction>(packet.Payload.ToString()!);
                        if (action != null)
                        {
                            lock (LoggedActions)
                            {
                                LoggedActions.Add(action);
                            }
                        }
                    }
                    catch
                    {
                        lock (LoggedActions)
                        {
                            LoggedActions.Add(new AgentAction { ActionType = packet.Payload.ToString() ?? "Generic Action", Success = true });
                        }
                    }
                }
                else if (packet.PacketType == "LlmRequest" && packet.Payload != null)
                {
                    try
                    {
                        var req = JsonConvert.DeserializeObject<LlmActionRequest>(packet.Payload.ToString()!);
                        if (req != null)
                        {
                            lock (LoggedLlmRequests)
                            {
                                LoggedLlmRequests.Add(req);
                            }
                            TotalTokensTracked += req.Prompt.Length / 4;
                        }
                    }
                    catch
                    {
                        lock (LoggedLlmRequests)
                        {
                            LoggedLlmRequests.Add(new LlmActionRequest { Prompt = packet.Payload.ToString() ?? "", Status = "Sent" });
                        }
                        TotalTokensTracked += (packet.Payload.ToString() ?? "").Length / 4;
                    }
                }
                else if (packet.PacketType == "LlmResponse" && packet.Payload != null)
                {
                    string resultStr = packet.Payload.ToString() ?? "";
                    lock (LoggedLlmRequests)
                    {
                        var lastReq = LoggedLlmRequests.LastOrDefault();
                        if (lastReq != null)
                        {
                            lastReq.Result = resultStr;
                            lastReq.Status = "Completed";
                        }
                    }
                    TotalTokensTracked += resultStr.Length / 4;
                }
                else if (packet.PacketType == "Result" && packet.Payload != null)
                {
                    TotalTokensTracked += (packet.Payload.ToString() ?? "").Length / 4;
                }

                // If error arrives, run the RAG error repair flow
                if (packet.PacketType == "Error" && packet.Payload != null)
                {
                    _ = HandleCompileErrorAsync(packet.SessionId, packet.Payload.ToString() ?? "Unknown error");
                }
            }
            catch {}
        }

        public async Task<string> HandleCompileErrorAsync(string sessionId, string errorMessage)
        {
            OnLogUpdated?.Invoke($"[Orchestrator] Error reported: '{errorMessage}'. Running RAG repair flow...");

            // 1. Check if LLM (3020) is fetchable
            bool isLlmOnline = false;
            try
            {
                var response = await _http.GetAsync("http://localhost:3020/");
                isLlmOnline = response.IsSuccessStatusCode;
            }
            catch {}

            if (isLlmOnline)
            {
                OnLogUpdated?.Invoke("[Orchestrator] LLM service is online at port 3020. Requesting AI code patch...");
                try
                {
                    var requestBody = new
                    {
                        prompt = $"Identify root cause and generate a fix for this compiler error: {errorMessage}",
                        aiMode = "BatchFileGenerator", // triggers script solver
                        workspacePath = _syncroDbPath
                    };
                    var jsonContent = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                    var resp = await _http.PostAsync("http://localhost:3020/gemini", jsonContent);
                    if (resp.IsSuccessStatusCode)
                    {
                        string body = await resp.Content.ReadAsStringAsync();
                        var obj = Newtonsoft.Json.Linq.JObject.Parse(body);
                        string? patchScript = obj["result"]?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.Value<string>();
                        if (!string.IsNullOrEmpty(patchScript))
                        {
                            OnLogUpdated?.Invoke($"[Orchestrator] LLM returned fix: {patchScript.Trim()}");
                            return patchScript.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLogUpdated?.Invoke($"[Orchestrator Error] LLM request failed: {ex.Message}. Engaging offline RAG memory failsafe.");
                }
            }

            // 2. Failsafe Fallback: RAG Hindsight Vector Search (offline matching)
            OnLogUpdated?.Invoke("[Orchestrator] LLM is offline. Retrieving matching solution candidates from Hindsight Memory...");
            
            HindsightRecord? bestMatch = null;
            double highestSimilarity = 0.0;

            // Simple lexical overlap coefficient as a vector-similarity proxy for offline usage
            foreach (var record in OfflineHindsightMemory)
            {
                double similarity = CalculateOverlap(errorMessage, record.Problem);
                if (similarity > highestSimilarity && similarity > 0.2)
                {
                    highestSimilarity = similarity;
                    bestMatch = record;
                }
            }

            if (bestMatch != null)
            {
                string solutionText = $"[RAG Hindsight Match (Confidence: {bestMatch.Confidence:P0})] Problem: {bestMatch.Problem}\nRoot Cause: {bestMatch.RootCause}\nSolution: {bestMatch.Solution}";
                OnLogUpdated?.Invoke(solutionText);
                return bestMatch.Solution;
            }
            else
            {
                string fallbackText = "[Orchestrator] No matching hindsight solutions found. Suggesting standard clean and rebuild.";
                OnLogUpdated?.Invoke(fallbackText);
                return "dotnet clean; dotnet build";
            }
        }

        private double CalculateOverlap(string s1, string s2)
        {
            var words1 = s1.ToLower().Split(new[] { ' ', '.', '_', '/', '\\', ':', '-' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var words2 = s2.ToLower().Split(new[] { ' ', '.', '_', '/', '\\', ':', '-' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            
            int intersection = words1.Intersect(words2).Count();
            int union = words1.Union(words2).Count();
            
            return union == 0 ? 0.0 : (double)intersection / union;
        }
    }
}
