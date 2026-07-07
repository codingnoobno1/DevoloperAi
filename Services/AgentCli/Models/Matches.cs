using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>Result of matching an error signature against the mappings table.</summary>
public record ErrorMatch(ErrorMapping Row, double Confidence, string Explanation);

/// <summary>Result of finding a reusable script.</summary>
public record ScriptMatch(ScriptRecord Script, double Confidence, string Explanation);

/// <summary>Result of mapping a natural-language goal to an intent + slots.</summary>
public record TaskIntentResult(string Intent, IReadOnlyDictionary<string, string> Slots, double Confidence);

/// <summary>Output of the script safety scanner.</summary>
public record ScanResult(bool Safe, IReadOnlyList<string> Hits);
