using Anthropic.SDK.Messaging;

namespace TaskFlow.Api.Services;

public interface IClaudeClient
{
    /// <summary>True when an API key is configured; false lets agents skip a cycle quietly.</summary>
    bool IsConfigured { get; }

    Task<MessageResponse> SendAsync(MessageParameters parameters, CancellationToken ct = default);
}