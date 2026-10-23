using System;
using Microsoft.Extensions.DependencyInjection;

namespace Syncro.Desktop.Services.Agent.Policy
{
    public static class AgentPolicyServiceCollectionExtensions
    {
        /// <summary>Agent Settings P0/P1: policy store, key store, bypass decider, approvals, audit.</summary>
        public static IServiceCollection AddAgentPolicy(this IServiceCollection services)
        {
            services.AddSingleton<AgentPolicyStore>();
            services.AddSingleton<ApiKeyStore>();
            services.AddSingleton<TokenUsageTracker>();
            services.AddSingleton<BypassDecider>();
            services.AddSingleton<ApprovalService>();
            services.AddSingleton<AgentAuditLog>();
            services.AddSingleton<AgentGuard>();
            services.AddSingleton<AgentSettingsLink>();
            services.AddSingleton<ConflictResolutionService>();
            services.AddSingleton<IncidentAutoSreService>();
            services.AddSingleton<Syncro.Desktop.Services.Git.GitTaskScopeService>();

            // Platform agent (Phase 2): device registration, heartbeat, job queue worker.
            services.AddSingleton<Platform.DeviceIdentity>();
            services.AddSingleton<Platform.PlatformAgentClient>();
            services.AddSingleton<Platform.PlatformAgentStatus>();
            services.AddSingleton<Platform.SetupTaskJobHandler>();
            services.AddSingleton<Platform.AgentJobWorker>();
            services.AddSingleton<Syncro.Desktop.Services.Universe.Runtime.IRuntimeWorker>(sp => sp.GetRequiredService<Platform.AgentJobWorker>());
            return services;
        }

        /// <summary>
        /// Call once after the app is built: loads the saved key into the LLM provider and pushes
        /// model settings now and on every save.
        /// </summary>
        public static void ApplyAgentPolicy(IServiceProvider sp)
        {
            var store = sp.GetRequiredService<AgentPolicyStore>();
            sp.GetRequiredService<AgentAuditLog>();   // hooks policy-save auditing

            if (sp.GetService<Engine.Core.ILLMProvider>() is Engine.LLM.GroqProvider groq)
            {
                groq.Usage = sp.GetRequiredService<TokenUsageTracker>();
                var key = sp.GetRequiredService<ApiKeyStore>().Get("groq");
                if (!string.IsNullOrWhiteSpace(key)) groq.Initialize(key);   // saved key wins over GROQ_API_KEY

                void Push(AgentPolicy p)
                {
                    groq.AgentModelOverride = p.Model.AgentModel;
                    groq.AgentTemperature = p.Model.AgentTemperature;
                    groq.MaxToolCalls = p.Model.MaxToolCalls;
                    groq.OfflineMode = p.Model.OfflineMode;
                    groq.DailyTokenBudget = p.Model.DailyTokenBudget;
                }
                Push(store.Current);
                store.Changed += Push;
            }
        }
    }
}
