using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>
/// A row in the DB-based error->action table (mappings.jsonl). No ML — matched by lookup +
/// keyword overlap + worked-rate. "Learning" = inserting rows and bumping counters.
/// </summary>
public class ErrorMapping
{
    public string Id { get; set; } = "map_" + Guid.NewGuid().ToString("N")[..8];
    public string? Code { get; set; }                       // exact error code (strongest signal)
    public List<string> Keywords { get; set; } = new();     // signal set
    public List<string> MatchPhrases { get; set; } = new(); // optional raw substring checks
    public string Label { get; set; } = "";                 // human-readable action label
    public string Solution { get; set; } = Solutions.None;  // canonical solution label
    public string? ScriptId { get; set; }                   // optional reusable script

    public int Hits { get; set; }
    public int Worked { get; set; }
    public int Fails { get; set; }
    public string Source { get; set; } = "seed";            // seed | llm | human | outcome

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>worked / hits, with a neutral prior for unproven rows.</summary>
    public double Reliability => Hits == 0 ? 0.5 : (double)Worked / Hits;
}
