namespace Syncro.Desktop;

public partial class IdePage : ContentPage
{
    public IdePage(string token)
    {
        InitializeComponent();
        Bvw.RootComponents.Clear();
        Bvw.RootComponents.Add(new Microsoft.AspNetCore.Components.WebView.Maui.RootComponent
        {
            Selector = "#app",
            ComponentType = typeof(Syncro.Desktop.Components.IDE.IdeRoot),
            Parameters = new System.Collections.Generic.Dictionary<string, object?> { { "Token", token } }
        });
    }
}
