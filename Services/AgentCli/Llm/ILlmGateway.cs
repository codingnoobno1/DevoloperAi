using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.AgentCli.Llm;

/// <summary>Availability of the LLM backend.</summary>
public enum LlmState { Available, Degraded, Offline }

public record LlmRequest(string System, string User, IReadOnlyList<string>? CandidateLabels = null);
public record LlmResponse(bool Ok, string Text, string? Label = null, string? Error = null);

/// <summary>
/// Boundary to the LLM. The whole AgentCli core is designed to run with the LLM Offline;
/// the LLM only enriches. A real adapter (e.g. over localhost:3020) implements this; the
/// default <see cref="NullLlmGateway"/> reports Offline so everything works deterministically.
/// </summary>
public interface ILlmGateway
{
    LlmState State { get; }
    bool IsAvailable { get; }
    Task<LlmState> ProbeAsync(CancellationToken ct = default);
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
}

/// <summary>Default gateway: always Offline. Lets the core build and run with no LLM server.</summary>
public sealed class NullLlmGateway : ILlmGateway
{
    public LlmState State => LlmState.Offline;
    public bool IsAvailable => false;
    public Task<LlmState> ProbeAsync(CancellationToken ct = default) => Task.FromResult(LlmState.Offline);
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default) =>
        Task.FromResult(new LlmResponse(false, "", null, "llm offline"));
}
