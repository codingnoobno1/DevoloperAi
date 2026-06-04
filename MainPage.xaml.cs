using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Web.WebView2.Core;

namespace Syncro.Desktop;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

    private void OnBlazorWebViewInitialized(object sender, BlazorWebViewInitializedEventArgs e)
    {
#if WINDOWS
        if (e.WebView.CoreWebView2 != null)
        {
            e.WebView.CoreWebView2.PermissionRequested += (s, args) =>
            {
                // Automatically grant permission for camera and microphone
                if (args.PermissionKind == CoreWebView2PermissionKind.Camera ||
                    args.PermissionKind == CoreWebView2PermissionKind.Microphone ||
                    args.PermissionKind == CoreWebView2PermissionKind.OtherSensors)
                {
                    args.State = CoreWebView2PermissionState.Allow;
                    System.Diagnostics.Debug.WriteLine($"[Syncro Agent] Automatically granted {args.PermissionKind} permission for {args.Uri}");
                }
            };

            // Disable Zoom Control
            e.WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
        }
#endif
    }
}
