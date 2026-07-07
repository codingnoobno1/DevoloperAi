using Microsoft.Maui.Controls;

namespace Syncro.Desktop.Services.Connector.DevHub
{
    /// <summary>
    /// Opens native MAUI WebView windows for project previews and Swagger docs.
    /// Native WebView sidesteps BlazorWebView iframe cross-origin restrictions.
    /// </summary>
    public static class PreviewWindowHelper
    {
        public static void OpenPreview(string label, int port)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var webView = MakeWebView($"http://localhost:{port}");
                var page    = new ContentPage { Title = $"{label} Preview", Content = webView };
                Application.Current?.OpenWindow(new Window(page)
                {
                    Title  = $"{label} — localhost:{port}",
                    Width  = 1280,
                    Height = 900
                });
            });
        }

        public static void OpenSwagger(string label, int port)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var webView = MakeWebView($"http://localhost:{port}/swagger");
                var page    = new ContentPage { Title = $"{label} API Docs", Content = webView };
                Application.Current?.OpenWindow(new Window(page)
                {
                    Title  = $"{label} Swagger — localhost:{port}/swagger",
                    Width  = 1400,
                    Height = 900
                });
            });
        }

        public static void OpenUrl(string url, string title)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var webView = MakeWebView(url);
                var page    = new ContentPage { Title = title, Content = webView };
                Application.Current?.OpenWindow(new Window(page)
                {
                    Title  = title,
                    Width  = 1280,
                    Height = 900
                });
            });
        }

        private static WebView MakeWebView(string url) => new()
        {
            Source            = new UrlWebViewSource { Url = url },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions   = LayoutOptions.Fill
        };
    }
}
