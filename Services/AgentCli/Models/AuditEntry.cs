using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>Append-only audit record for every mutation, script run, and LLM call.</summary>
public class AuditEntry
{
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "cli";   // cli | agent
    public string Action { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public string? LlmState { get; set; }
    public string? Note { get; set; }
}
