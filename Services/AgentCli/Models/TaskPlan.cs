using System.Collections.Generic;

namespace Syncro.Desktop.Services.AgentCli.Models;

/// <summary>
/// A structured execution plan output by the TaskRouter.
/// Replaces the old TaskIntentResult text-matcher output.
/// </summary>
public record TaskPlan(
    string Intent, 
    double Confidence, 
    IReadOnlyList<string> Reasons, 
    IReadOnlyList<string> Steps);
