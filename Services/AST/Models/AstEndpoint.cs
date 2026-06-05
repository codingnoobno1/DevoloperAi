using System.Collections.Generic;

namespace Syncro.Desktop.Services.AST.Models;

public class AstEndpoint
{
    public string Method { get; set; } = "GET";
    public string Path { get; set; } = "/";
    public List<string> Parameters { get; set; } = new();
    public string? RequestDto { get; set; }
    public string? ResponseDto { get; set; }
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public string ControllerName { get; set; } = "";
    public string HandlerMethod { get; set; } = "";
}
