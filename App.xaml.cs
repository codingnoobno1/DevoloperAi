namespace Syncro.Desktop;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new LoginPage()) { Title = "Syncro AI" };
		window.Destroying += OnMainWindowDestroying;
		return window;
	}

	private void OnMainWindowDestroying(object? sender, EventArgs e)
	{
		try
		{
			var services = Handler?.MauiContext?.Services;
			if (services != null)
			{
				services.GetService<Syncro.Desktop.Services.SyncroCLI.AgentBridgeServer>()?.Stop();
				services.GetService<Syncro.Desktop.Services.SyncroCLI.AgentMonitorServer>()?.Stop();
				services.GetService<Syncro.Desktop.Services.Universe.Runtime.RuntimeCoordinator>()?.Stop();
				services.GetService<Syncro.Desktop.Services.Git.GitSyncService>()?.Stop();
				services.GetService<Syncro.Desktop.Services.Engine.Mcp.Transport.McpHttpEndpoint>()?.Dispose();
				services.GetService<Syncro.Desktop.Services.Ide.Host.CodeOssIdeHost>()?.StopAsync().GetAwaiter().GetResult();
			}
		}
		catch
		{
			// Ignore cleanup exceptions during shutdown
		}
		finally
		{
			Environment.Exit(0);
		}
	}
}
