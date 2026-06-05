using System.Collections.Generic;
using System.Threading;

namespace Syncro.Desktop.Services.AST.Core;

public class AstContext
{
    public string RootPath { get; init; } = "";
    public string ProjectLanguage { get; set; } = ""; // "csharp" | "typescript" | "python" | "javascript"
    public string Framework { get; set; } = "";       // "aspnet" | "nextjs" | "express" | "fastapi"
    public List<string> IgnorePatterns { get; init; } = new() { "bin", "obj", "node_modules", ".git", "__pycache__" };
    public bool ScanPorts { get; init; } = true;
    public bool ParseSwagger { get; init; } = true;
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;
    public List<string> Errors { get; } = new();
    public Dictionary<string, List<AstNode>> FileNodes { get; } = new(); // filePath -> nodes
}
