using System.Collections.Generic;

namespace Syncro.Desktop.Services.AST.Hindsight;

/// <summary>One record from the project's vector index (.syncro_db/Vectors/&lt;id&gt;/index.json).</summary>
public class VectorEntry
{
    public string VectorId { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SymbolName { get; set; } = "";
    public string NodeType { get; set; } = "";
    public string Language { get; set; } = "";
    public string Framework { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string CodeSnippet { get; set; } = "";
    public int LineStart { get; set; }
    public int LineEnd { get; set; }
}

/// <summary>A retrieved entry plus its lexical relevance score.</summary>
public class RetrievedChunk
{
    public VectorEntry Entry { get; set; } = new();
    public double Score { get; set; }
    public string Why { get; set; } = "";
}

/// <summary>Analysis of the hindsight index for a project.</summary>
public class HindsightStats
{
    public int VectorCount { get; set; }
    public int FileCount { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
    public bool Indexed => VectorCount > 0;
}

/// <summary>An answer in one mode (no-llm or llm), with the chunks that grounded it.</summary>
public class HindsightAnswer
{
    public string Mode { get; set; } = "";          // "no-llm" | "llm"
    public string Text { get; set; } = "";
    public List<RetrievedChunk> Chunks { get; set; } = new();
    public bool LlmUsed { get; set; }
    public bool LlmAvailable { get; set; }
    public long ElapsedMs { get; set; }
    public string? Note { get; set; }
}
