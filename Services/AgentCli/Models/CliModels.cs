using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

public record ErrorRecord(string Id, string TaskId, string Type, string Message, string? Source, int Line);

public class ProjectRecord
{
    public string ProjectId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Language { get; set; } = "";
    public string Framework { get; set; } = "";
    public string PackageManager { get; set; } = "";
    public string LastScanned { get; set; } = "";
    public int AstNodeCount { get; set; }
    public int VectorCount { get; set; }
}

public class VectorRecord
{
    public string VectorId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string SymbolName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int LineStart { get; set; }
    public string NodeType { get; set; } = "";
    public string Snippet { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<float> Vector { get; set; } = new();
}
