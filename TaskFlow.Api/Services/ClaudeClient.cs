using Anthropic.SDK;
using Anthropic.SDK.Messaging;

namespace TaskFlow.Api.Services;

/// <summary>Production IClaudeClient: forwards to the Anthropic SDK. Tolerates a missing key.</summary>
public class ClaudeClient : IClaudeClient
{
    private readonly AnthropicClient? _client;

    public ClaudeClient(IConfiguration config)
    {
        var apiKey = config["Anthropic:ApiKey"];
        _client = string.IsNullOrWhiteSpace(apiKey) ? null : new AnthropicClient(apiKey);
    }

    public bool IsConfigured => _client is not null;

    public Task<MessageResponse> SendAsync(MessageParameters parameters, CancellationToken ct = default) =>
        _client!.Messages.GetClaudeMessageAsync(parameters, ct);
}