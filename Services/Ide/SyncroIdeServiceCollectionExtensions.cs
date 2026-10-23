using Microsoft.Extensions.DependencyInjection;

namespace Syncro.Desktop.Services.Ide;

/// <summary>DI registration for Syncro IDE (Phase 0/1). Call from MauiProgram: <c>AddSyncroIde()</c>.</summary>
public static class SyncroIdeServiceCollectionExtensions
{
    public static IServiceCollection AddSyncroIde(this IServiceCollection services)
    {
        services.AddSingleton<FileService>();
        services.AddSingleton<IdeStateStore>();
        services.AddSingleton<IdeLaunchContext>();
        services.AddSingleton<IdeWindowService>();
        services.AddSingleton<IProjectContextBuilder, ProjectContextBuilder>();
        services.AddScoped<Engines.IEditorEngine, Engines.MonacoEditorEngine>();  // editor surface, §2.4
        services.AddScoped<Engines.ITerminalEngine, Engines.LocalProcessTerminalEngine>();
        services.AddScoped<Engines.IRoslynLanguageService, Engines.RoslynLanguageService>();
        services.AddScoped<IdeWorkspaceState>();   // per BlazorWebView circuit

        // IDE host abstraction — Monaco (built-in) or code-oss (real VS Code OSS via serve-web).
        services.AddSingleton<Host.IdeHostOptions>();
        services.AddSingleton<Host.MonacoIdeHost>();
        services.AddSingleton<Engine.Mcp.Transport.McpHttpEndpoint>();   // syncro-bridge endpoint (E0: /health)
        services.AddSingleton<Host.CodeOssIdeHost>();
        services.AddSingleton<Host.IdeHostResolver>();
        return services;
    }
}
