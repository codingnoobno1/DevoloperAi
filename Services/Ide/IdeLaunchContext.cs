using System;
using System.Collections.Concurrent;

namespace Syncro.Desktop.Services.Ide;

public record IdeLaunchRequest(string? WorkspacePath, string? AnalysisPath, string Mode);

/// <summary>
/// Shared (singleton) hand-off between the launcher and the IDE window. 
/// Uses a token-based mailbox to prevent race conditions when rapidly opening multiple projects.
/// </summary>
public class IdeLaunchContext
{
    private readonly ConcurrentDictionary<string, IdeLaunchRequest> _pending = new();

    public string Stage(IdeLaunchRequest req)
    {
        var token = Guid.NewGuid().ToString("n");
        _pending[token] = req;
        return token;
    }

    public bool TryClaim(string token, out IdeLaunchRequest req)
    {
        if (token == null)
        {
            req = null!;
            return false;
        }
        return _pending.TryRemove(token, out req!);
    }
}
