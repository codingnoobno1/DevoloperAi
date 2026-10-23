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

/// <summary>
/// Real gateway backed by the platform's <see cref="Syncro.Desktop.Services.Engine.Core.ILLMProvider"/>
/// (GroqProvider configured via Agent Settings / Windows Credential Manager).
/// </summary>
public sealed class ProviderLlmGateway : ILlmGateway
{
    private readonly Syncro.Desktop.Services.Engine.Core.ILLMProvider _provider;

    public ProviderLlmGateway(Syncro.Desktop.Services.Engine.Core.ILLMProvider provider)
    {
        _provider = provider;
    }

    public bool IsAvailable =>
        _provider is Syncro.Desktop.Services.Engine.LLM.GroqProvider g
            ? (g.HasKey && !g.OfflineMode)
            : true;

    public LlmState State => IsAvailable ? LlmState.Available : LlmState.Offline;

    public Task<LlmState> ProbeAsync(CancellationToken ct = default) =>
        Task.FromResult(State);

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return new LlmResponse(false, "", null, "Groq API key not configured or Offline Mode enabled in Agent Settings.");

        try
        {
            var sys = string.IsNullOrWhiteSpace(request.System)
                ? "You are Syncro AI CLI assistant."
                : request.System;
            var text = await _provider.GenerateResponseAsync(sys, request.User);
            string? matchedLabel = null;
            if (request.CandidateLabels is { Count: > 0 } && !string.IsNullOrWhiteSpace(text))
            {
                foreach (var lbl in request.CandidateLabels)
                {
                    if (text.Contains(lbl, System.StringComparison.OrdinalIgnoreCase))
                    {
                        matchedLabel = lbl;
                        break;
                    }
                }
            }
            return new LlmResponse(true, text ?? "", matchedLabel, null);
        }
        catch (System.Exception ex)
        {
            return new LlmResponse(false, "", null, ex.Message);
        }
    }
}
