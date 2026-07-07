using System;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>A persistent agent note: decision / bug / risk / failure / todo (file memory).</summary>
public class MemoryNote
{
    public string Id { get; set; } = "mem_" + Guid.NewGuid().ToString("N")[..8];
    public string ProjectId { get; set; } = "";
    public string Kind { get; set; } = "todo";   // todo | decision | bug | risk | failure
    public string Text { get; set; } = "";
    public double Salience { get; set; } = 0.5;
    public string Status { get; set; } = "open"; // open | done | dismissed
    public string? OriginTaskId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
