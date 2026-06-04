using Microsoft.Extensions.Logging;
using DeveloperAI.BusinessLogic;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Syncro.Desktop;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
        DotEnv.Load(Path.Combine(AppContext.BaseDirectory, ".env"));
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddMudServices();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Git.FetchProject>();
		builder.Services.AddSingleton<DeveloperAI.BusinessLogic.AIClient>();
		builder.Services.AddSingleton<DeveloperAI.BusinessLogic.EnvironmentManager>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProjectService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.PixelService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.FlutterService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.SyncroCLI.SyncroCLIService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.FolderCreationService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.BatchFileExecutionService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.CommandExecutionService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.MERNProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.SpringBootProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.PhpProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.DotNetProjectGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.INLPServices, Syncro.Desktop.Services.ProgramLogic.SimpleNLPServices>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.DocumentationGeneratorService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.GitCommandService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.ProgramLogic.QuickStartService>();
		builder.Services.AddHttpClient();
		builder.Services.AddSingleton<Syncro.Desktop.Services.Auth.AuthService>();
		builder.Services.AddSingleton<Syncro.Desktop.Services.NetlifyService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif


		return builder.Build();
	}
}
