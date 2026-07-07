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
        return services;
    }
}
