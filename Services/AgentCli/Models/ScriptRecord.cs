using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>
/// A reusable script stored in the DB. Found again by purpose + keyword overlap + worked-rate,
/// so the same fix is reused next time instead of regenerated.
/// </summary>
public class ScriptRecord
{
    public string Id { get; set; } = "scr_" + Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Shell { get; set; } = "bat";              // bat | sh | ps1 | py
    public string BodyPath { get; set; } = "";              // relative to the scripts/bodies dir
    public string Hash { get; set; } = "";                  // sha256 of body (dedup + integrity)

    public string Purpose { get; set; } = Solutions.None;   // solution label it serves
    public List<string> Keywords { get; set; } = new();     // error signals it resolves
    public List<string> Platform { get; set; } = new();     // windows | linux | mac
    public List<string> Params { get; set; } = new();       // {{placeholders}} (never bake secrets)

    public bool Safe { get; set; }                          // auto-runnable without approval?
    public bool Flagged { get; set; }                       // safety scanner hit -> always needs human
    public string Source { get; set; } = "seed";            // seed | llm | human

    public int Runs { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsed { get; set; }

    public double Reliability => Runs == 0 ? 0.5 : (double)Succeeded / Runs;
}

/// <summary>A request to create/store a script.</summary>
public record ScriptDraft(
    string Name,
    string Shell,
    string Body,
    string Purpose,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Platform,
    IReadOnlyList<string> Params,
    string Source);
