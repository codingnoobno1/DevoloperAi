using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls;

namespace Syncro.Desktop.Components.Pages.Connector.DevHub
{
    /// <summary>
    /// MAUI ContentPage hosting a BlazorWebView with the DevHub Blazor app.
    /// The BlazorWebView automatically uses the app's IServiceProvider (same DI container),
    /// so all singletons (StackRunManager, DevHubState, etc.) are shared with the main window.
    /// </summary>
    public sealed class DevHubContentPage : ContentPage
    {
        public DevHubContentPage()
        {
            Title = "DevHub";
            var bwv = new BlazorWebView
            {
                HostPage = "wwwroot/index.html"
            };
            bwv.RootComponents.Add(new RootComponent
            {
                Selector      = "#app",
                ComponentType = typeof(DevHubApp)
            });
            Content = bwv;
        }

        public static void Launch()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Application.Current?.OpenWindow(new Window(new DevHubContentPage())
                {
                    Title  = "DevHub — Live Project Tooling",
                    Width  = 1440,
                    Height = 960
                });
            });
        }
    }
}
