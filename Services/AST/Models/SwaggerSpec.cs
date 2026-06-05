using System.Collections.Generic;

namespace Syncro.Desktop.Services.AST.Models;

public class SwaggerSpec
{
    public string Title { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string FilePath { get; set; } = "";
    public List<AstEndpoint> Endpoints { get; set; } = new();
    public string RawJsonOrYaml { get; set; } = "";
}
