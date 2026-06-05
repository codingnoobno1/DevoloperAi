namespace Syncro.Desktop.Services.AST.Models;

public class AstDependencyInfo
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Type { get; set; } = "NuGet"; // NuGet, npm, pip, maven, etc.
    public bool IsTransitive { get; set; }
}
